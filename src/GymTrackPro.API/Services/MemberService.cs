using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

using GymTrackPro.Shared.Events.Members;

namespace GymTrackPro.API.Services;

public class MemberService : IMemberService
{
    private readonly IMemberRepository _memberRepository;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISystemSettingService _settingsService;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IMemberDeletionTransaction _memberDeletionTransaction;

    public MemberService(
        IMemberRepository memberRepository,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        ICurrentUserContext currentUser,
        ISystemSettingService settingsService,
        IDomainEventPublisher eventPublisher,
        IMemberDeletionTransaction memberDeletionTransaction)
    {
        _memberRepository = memberRepository;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
        _settingsService = settingsService;
        _eventPublisher = eventPublisher;
        _memberDeletionTransaction = memberDeletionTransaction;
    }

    private int GetRequiredCurrentUserId() => _currentUser.UserId
        ?? throw new UnauthorizedAccessException("An active application user is required.");

    private string GetClientIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    private string? SaveProfilePicture(string? base64Data, string? existingPath = null)
    {
        if (string.IsNullOrEmpty(base64Data))
            return existingPath;

        try
        {
            var data = base64Data;
            if (data.Contains(","))
            {
                data = data.Split(',')[1];
            }

            var bytes = Convert.FromBase64String(data);
            var uploadsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "uploads", "profiles");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var fileName = $"{Guid.NewGuid()}.jpg";
            var filePath = Path.Combine(uploadsFolder, fileName);
            File.WriteAllBytes(filePath, bytes);

            if (!string.IsNullOrEmpty(existingPath))
            {
                try
                {
                    if (ProfilePicturePathPolicy.TryResolveOwnedFile(
                            AppDomain.CurrentDomain.BaseDirectory,
                            existingPath,
                            out var oldFilePath)
                        && File.Exists(oldFilePath))
                    {
                        File.Delete(oldFilePath);
                    }
                }
                catch { /* ignore deletion failures */ }
            }

            return $"/uploads/profiles/{fileName}";
        }
        catch
        {
            return existingPath;
        }
    }

    public async Task<MemberResponseDto?> GetByIdAsync(int id)
    {
        var member = await _memberRepository.GetByIdAsync(id);
        if (member == null) return null;
        return MapToResponseDto(member);
    }

    public async Task<MemberResponseDto?> GetByQRCodeAsync(string qrCode)
    {
        var member = await _memberRepository.GetByQRCodeAsync(qrCode);
        if (member == null) return null;
        return MapToResponseDto(member);
    }

    public async Task<IEnumerable<MemberResponseDto>> GetAllAsync()
    {
        var members = await _memberRepository.GetAllAsync();
        return members.Select(MapToResponseDto);
    }

    public async Task<PagedResultDto<MemberResponseDto>> GetPagedMembersAsync(string? search, string? status, int page, int pageSize)
    {
        var (items, totalCount) = await _memberRepository.GetPagedAsync(search, status, page, pageSize);
        return new PagedResultDto<MemberResponseDto>
        {
            Items = items.Select(MapToResponseDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    private async Task ValidateProfilePictureAsync(string? base64Data)
    {
        if (string.IsNullOrEmpty(base64Data)) return;
        var data = base64Data;
        if (data.Contains(","))
        {
            data = data.Split(',')[1];
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data);
        }
        catch
        {
            throw new ArgumentException("Invalid profile picture Base64 format.");
        }
        var maxSize = await _settingsService.GetValueIntAsync("MaxUploadSize", 5242880);
        if (bytes.Length > maxSize)
        {
            throw new ArgumentException($"Profile picture size exceeds maximum permitted limit of {maxSize} bytes.");
        }
    }

    public async Task<MemberResponseDto> CreateMemberAsync(CreateMemberDto createDto)
    {
        var actorUserId = GetRequiredCurrentUserId();

        // Enforce unique Phone and Email constraints
        var existingPhone = await _memberRepository.GetByPhoneNumberAsync(createDto.PhoneNumber);
        if (existingPhone != null)
        {
            throw new InvalidOperationException("Phone number is already registered.");
        }

        if (!string.IsNullOrEmpty(createDto.Email))
        {
            var existingEmail = await _memberRepository.GetByEmailAsync(createDto.Email);
            if (existingEmail != null)
            {
                throw new InvalidOperationException("Email address is already registered.");
            }
        }

        // Validate Profile Picture size/format
        await ValidateProfilePictureAsync(createDto.ProfilePictureBase64);

        // Generate unique QRCode check-in token dynamically from settings
        var qrPrefix = await _settingsService.GetValueAsync("QRPrefix", "GTP-");
        string qrCode;
        while (true)
        {
            qrCode = qrPrefix + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper();
            var existingQR = await _memberRepository.GetByQRCodeAsync(qrCode);
            if (existingQR == null) break;
        }

        // Save profile picture if base64 data is present
        var profilePicPath = SaveProfilePicture(createDto.ProfilePictureBase64);

        var member = new Member
        {
            FirstName = createDto.FirstName,
            LastName = createDto.LastName,
            Gender = createDto.Gender,
            BirthDate = createDto.BirthDate,
            PhoneNumber = createDto.PhoneNumber,
            Email = createDto.Email,
            Address = createDto.Address,
            EmergencyContact = createDto.EmergencyContact,
            ProfilePicture = profilePicPath,
            QRCode = qrCode,
            Status = "Active",
            DateRegistered = DateTime.UtcNow,
            LastModified = DateTime.UtcNow,
            IsDeleted = false
        };

        await _memberRepository.AddAsync(member);

        // Audit Logging
        await _auditService.LogActivityAsync(actorUserId, "Member Created", $"Created member {member.FirstName} {member.LastName} (ID: {member.MemberID}).", GetClientIpAddress());

        // Publish Domain Event
        await _eventPublisher.PublishAsync(new MemberRegisteredEvent
        {
            MemberId = member.MemberID,
            FirstName = member.FirstName,
            LastName = member.LastName,
            Email = member.Email ?? string.Empty,
            QRCode = member.QRCode
        });

        return MapToResponseDto(member);
    }

    public async Task<MemberResponseDto> UpdateMemberAsync(int id, UpdateMemberDto updateDto)
    {
        var actorUserId = GetRequiredCurrentUserId();

        var member = await _memberRepository.GetByIdAsync(id);
        if (member == null)
        {
            throw new KeyNotFoundException("Member not found.");
        }

        // Enforce unique Phone and Email constraints for other members
        var existingPhone = await _memberRepository.GetByPhoneNumberAsync(updateDto.PhoneNumber);
        if (existingPhone != null && existingPhone.MemberID != id)
        {
            throw new InvalidOperationException("Phone number is already registered by another member.");
        }

        if (!string.IsNullOrEmpty(updateDto.Email))
        {
            var existingEmail = await _memberRepository.GetByEmailAsync(updateDto.Email);
            if (existingEmail != null && existingEmail.MemberID != id)
            {
                throw new InvalidOperationException("Email address is already registered by another member.");
            }
        }

        // Validate Profile Picture size/format
        await ValidateProfilePictureAsync(updateDto.ProfilePictureBase64);

        // Save new profile picture if provided
        var profilePicPath = SaveProfilePicture(updateDto.ProfilePictureBase64, member.ProfilePicture);

        member.FirstName = updateDto.FirstName;
        member.LastName = updateDto.LastName;
        member.Gender = updateDto.Gender;
        member.BirthDate = updateDto.BirthDate;
        member.PhoneNumber = updateDto.PhoneNumber;
        member.Email = updateDto.Email;
        member.Address = updateDto.Address;
        member.EmergencyContact = updateDto.EmergencyContact;
        member.ProfilePicture = profilePicPath;
        member.Status = updateDto.Status;
        member.LastModified = DateTime.UtcNow;

        await _memberRepository.UpdateAsync(member);

        // Audit Logging
        await _auditService.LogActivityAsync(actorUserId, "Member Updated", $"Updated member profile for {member.FirstName} {member.LastName} (ID: {member.MemberID}).", GetClientIpAddress());

        return MapToResponseDto(member);
    }

    public async Task<bool> DeleteMemberAsync(int id)
    {
        var actorUserId = GetRequiredCurrentUserId();

        return await _memberDeletionTransaction.SoftDeleteAndRevokeAsync(
            id,
            actorUserId,
            GetClientIpAddress());
    }

    private static MemberResponseDto MapToResponseDto(Member member)
    {
        return new MemberResponseDto
        {
            MemberID = member.MemberID,
            FirstName = member.FirstName,
            LastName = member.LastName,
            Gender = member.Gender,
            BirthDate = member.BirthDate,
            PhoneNumber = member.PhoneNumber,
            Email = member.Email,
            Address = member.Address,
            EmergencyContact = member.EmergencyContact,
            ProfilePicture = member.ProfilePicture,
            QRCode = member.QRCode,
            Status = member.Status,
            DateRegistered = member.DateRegistered,
            LastModified = member.LastModified
        };
    }
}
