using System;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.DTOs;

public class UserResponseDto
{
    public int UserID { get; set; }
    public int? MemberID { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public bool EmailVerified { get; set; }
    public string OnboardingState { get; set; } = string.Empty;
    public string[] Capabilities { get; set; } = Array.Empty<string>();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    [Obsolete("API does not mint application tokens. Mobile app sends a refreshed Firebase ID token.")]
    public string Token { get; set; } = string.Empty;
}
