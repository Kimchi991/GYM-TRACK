using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.Shared.Events.Members;

public class MemberRegisteredEvent : IDomainEvent
{
    public int MemberId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string QRCode { get; set; } = string.Empty;
}
