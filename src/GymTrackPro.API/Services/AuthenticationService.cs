using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthenticationService(
        IUserRepository userRepository,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor)
    {
        _userRepository = userRepository;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
    }

    private string GetClientIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    public async Task<UserResponseDto> SyncUserAsync(string firebaseUid, string email)
    {
        var user = await _userRepository.GetByEmailAsync(email);
        
        if (user == null)
        {
            var username = email.Split('@')[0];
            
            // Check if username already exists to avoid collision
            var baseUsername = username;
            int counter = 1;
            while (await _userRepository.UsernameExistsAsync(username))
            {
                username = $"{baseUsername}{counter++}";
            }

            user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = string.Empty, // Firebase handles auth
                FirstName = "New",
                LastName = "User",
                Role = UserRole.Receptionist,
                IsActive = true,
                EmailVerified = true, // Trust Firebase
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _userRepository.AddAsync(user);
            await _auditService.LogActivityAsync(user.UserID, "SyncUser", $"New user {user.Username} synchronized from Firebase.", GetClientIpAddress());
        }
        else
        {
            await _auditService.LogActivityAsync(user.UserID, "SyncUser", $"User {user.Username} synchronized from Firebase.", GetClientIpAddress());
        }

        await _userRepository.UpdateLastLoginAsync(user.UserID);

        return new UserResponseDto
        {
            UserID = user.UserID,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            IsActive = user.IsActive,
            EmailVerified = user.EmailVerified,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastLoginAt = user.LastLoginAt,
            Token = string.Empty
        };
    }
}
