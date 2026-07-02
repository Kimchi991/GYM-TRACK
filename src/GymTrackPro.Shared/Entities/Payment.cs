using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Entities;

[Table("Payments")]
public class Payment
{
    [Key]
    public int PaymentID { get; set; }

    [Required]
    public int MemberID { get; set; }

    [ForeignKey("MemberID")]
    public Member? Member { get; set; }

    [Required]
    public int SubscriptionID { get; set; }

    [ForeignKey("SubscriptionID")]
    public Subscription? Subscription { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public decimal Discount { get; set; } = 0.00m;

    [Required]
    public decimal FinalAmount { get; set; }

    [Required]
    public PaymentMethod PaymentMethod { get; set; }

    [Required]
    public PaymentStatus PaymentStatus { get; set; }

    [Required]
    [StringLength(50)]
    public string ReceiptNumber { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ReferenceNumber { get; set; }

    [Required]
    public DateTime DatePaid { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    [Required]
    public bool IsDeleted { get; set; } = false;
}
