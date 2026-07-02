using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.Shared.Events.Membership;

public class MembershipResumedEvent : IDomainEvent
{
    public int SubscriptionId { get; set; }
    public int MemberId { get; set; }
    public string MemberEmail { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
}
