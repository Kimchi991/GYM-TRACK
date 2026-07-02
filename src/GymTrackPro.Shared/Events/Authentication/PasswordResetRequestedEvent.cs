using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.Shared.Events.Authentication;

public class PasswordResetRequestedEvent : IDomainEvent
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ResetToken { get; set; } = string.Empty;
}
