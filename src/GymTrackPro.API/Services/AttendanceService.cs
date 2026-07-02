using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

using GymTrackPro.Shared.Events.Attendance;

namespace GymTrackPro.API.Services;

public class AttendanceService : IAttendanceService
{
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IMemberRepository _memberRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDomainEventPublisher _eventPublisher;

    public AttendanceService(
        IAttendanceRepository attendanceRepository,
        IMemberRepository memberRepository,
        ISubscriptionRepository subscriptionRepository,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        IDomainEventPublisher eventPublisher)
    {
        _attendanceRepository = attendanceRepository;
        _memberRepository = memberRepository;
        _subscriptionRepository = subscriptionRepository;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _eventPublisher = eventPublisher;
    }

    private int? GetCurrentUserId()
    {
        var nameIdentifier = _httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(nameIdentifier, out int userId))
        {
            return userId;
        }
        return null;
    }

    private string GetClientIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    public async Task<AttendanceDto?> GetByIdAsync(int id)
    {
        var log = await _attendanceRepository.GetByIdAsync(id);
        if (log == null) return null;
        return MapToDto(log);
    }

    public async Task<IEnumerable<AttendanceDto>> GetByMemberIdAsync(int memberId)
    {
        var logs = await _attendanceRepository.GetByMemberIdAsync(memberId);
        return logs.Select(MapToDto);
    }

    public async Task<AttendanceDto> CheckInAsync(string qrCode)
    {
        // 1. Find the member by QR code
        var member = await _memberRepository.GetByQRCodeAsync(qrCode);
        if (member == null)
        {
            throw new KeyNotFoundException("Invalid check-in code.");
        }

        // 2. Validate membership state / check active subscription (BR-01)
        var subscriptions = await _subscriptionRepository.GetByMemberIdAsync(member.MemberID);
        var activeSub = subscriptions.FirstOrDefault(s => 
            s.Status == "Active" && 
            s.StartDate.Date <= DateTime.UtcNow.Date && 
            s.EndDate.Date >= DateTime.UtcNow.Date);

        if (activeSub == null)
        {
            await _auditService.LogActivityAsync(GetCurrentUserId(), "CheckIn Failure", $"Failed check-in for member {member.FirstName} {member.LastName} (ID: {member.MemberID}) - No active subscription found.", GetClientIpAddress());
            await _eventPublisher.PublishAsync(new CheckInFailedEvent { MemberId = member.MemberID, MemberEmail = member.Email ?? string.Empty, Reason = "No active subscription found." });
            throw new InvalidOperationException("Member does not have an active subscription.");
        }

        // 3. Limit double check-ins / daily check-in limits rules (BR-02)
        var today = DateTime.UtcNow.Date;
        var existingLogs = await _attendanceRepository.GetByMemberIdAsync(member.MemberID);
        
        // Prevent duplicate active sessions
        var activeSession = existingLogs.FirstOrDefault(a => a.CheckOutTime == null);
        if (activeSession != null)
        {
            await _eventPublisher.PublishAsync(new CheckInFailedEvent { MemberId = member.MemberID, MemberEmail = member.Email ?? string.Empty, Reason = "Member is already checked in." });
            throw new InvalidOperationException("Member is already checked in.");
        }

        // Daily check-in limit: 1 check-in per day
        var checkedInToday = existingLogs.Any(a => a.CheckInTime.Date == today);
        if (checkedInToday)
        {
            await _eventPublisher.PublishAsync(new CheckInFailedEvent { MemberId = member.MemberID, MemberEmail = member.Email ?? string.Empty, Reason = "Daily check-in limit reached." });
            throw new InvalidOperationException("Daily check-in limit reached.");
        }

        // 4. Create and save the attendance log
        var log = new Attendance
        {
            MemberID = member.MemberID,
            AttendanceDate = today,
            CheckInTime = DateTime.UtcNow,
            CheckOutTime = null,
            LastModified = DateTime.UtcNow
        };

        await _attendanceRepository.AddAsync(log);

        // Fetch again to include member details
        var savedLog = await _attendanceRepository.GetByIdAsync(log.AttendanceID);
        var result = MapToDto(savedLog ?? log);

        // Log audit log for check-in success
        await _auditService.LogActivityAsync(GetCurrentUserId(), "CheckIn Success", $"Member {member.FirstName} {member.LastName} (ID: {member.MemberID}) checked in successfully.", GetClientIpAddress());

        return result;
    }

    public async Task CheckOutAsync(int attendanceID)
    {
        var log = await _attendanceRepository.GetByIdAsync(attendanceID);
        if (log == null)
        {
            throw new KeyNotFoundException("Attendance log not found.");
        }

        if (log.CheckOutTime != null)
        {
            throw new InvalidOperationException("Member has already checked out.");
        }

        log.CheckOutTime = DateTime.UtcNow;
        log.LastModified = DateTime.UtcNow;

        await _attendanceRepository.UpdateAsync(log);

        // Log audit log for check-out success
        var memberName = log.Member != null ? $"{log.Member.FirstName} {log.Member.LastName}" : $"ID: {log.MemberID}";
        await _auditService.LogActivityAsync(GetCurrentUserId(), "CheckOut Success", $"Member {memberName} checked out successfully.", GetClientIpAddress());
    }

    private static AttendanceDto MapToDto(Attendance log)
    {
        return new AttendanceDto
        {
            AttendanceID = log.AttendanceID,
            MemberID = log.MemberID,
            MemberName = log.Member != null ? $"{log.Member.FirstName} {log.Member.LastName}" : string.Empty,
            AttendanceDate = log.AttendanceDate,
            CheckInTime = log.CheckInTime,
            CheckOutTime = log.CheckOutTime,
            LastModified = log.LastModified
        };
    }
}
