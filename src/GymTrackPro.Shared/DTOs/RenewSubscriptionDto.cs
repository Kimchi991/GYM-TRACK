using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class RenewSubscriptionDto
{
    [Required]
    public int MemberID { get; set; }

    [Required]
    public int PlanID { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Discount { get; set; }

    [Required]
    public string PaymentMethod { get; set; } = "Cash";

    public string? ReferenceNumber { get; set; }
}
