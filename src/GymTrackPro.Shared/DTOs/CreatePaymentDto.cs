using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class CreatePaymentDto
{
    [Required]
    public int MemberID { get; set; }

    [Required]
    public int SubscriptionID { get; set; }

    [Required]
    [Range(0.01, 1000000.00)]
    public decimal Amount { get; set; }

    [Range(0.00, 1000000.00)]
    public decimal Discount { get; set; } = 0.00m;

    [Required]
    [StringLength(20)]
    public string PaymentMethod { get; set; } = string.Empty; // Cash, GCash, Card, BankTransfer

    [Required]
    [StringLength(20)]
    public string PaymentStatus { get; set; } = string.Empty; // Paid, Partial, Pending

    [StringLength(100)]
    public string? ReferenceNumber { get; set; }
}
