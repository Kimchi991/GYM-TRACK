using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.Shared.Events.Payments;

public class RefundProcessedEvent : IDomainEvent
{
    public int PaymentId { get; set; }
    public int MemberId { get; set; }
    public string MemberEmail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
}
