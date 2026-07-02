using System;

namespace GymTrackPro.Shared.DTOs;

public class PaymentResponseDto
{
    public int PaymentID { get; set; }
    public int MemberID { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public int SubscriptionID { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Discount { get; set; }
    public decimal FinalAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string ReceiptNumber { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public DateTime DatePaid { get; set; }
    public DateTime LastModified { get; set; }
}
