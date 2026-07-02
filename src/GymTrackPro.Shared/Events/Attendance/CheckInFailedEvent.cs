using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.Shared.Events.Attendance;

public class CheckInFailedEvent : IDomainEvent
{
    public int MemberId { get; set; }
    public string MemberEmail { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
