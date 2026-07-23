using System;
using System.ComponentModel.DataAnnotations;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.DTOs;

public class SubmitApplicationDto
{
    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string ContactNumber { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string EmailAddress { get; set; } = string.Empty;

    [StringLength(100)]
    public string? EmergencyContact { get; set; }

    public int? SelectedPlanID { get; set; }

    [Required]
    public bool IsOneDayPass { get; set; }

    [Required]
    public PaymentMethod PaymentMethod { get; set; }

    [StringLength(100)]
    public string? PaymentReferenceNumber { get; set; }
}

public class ApplicationListItemDto
{
    public int ApplicationID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string? EmergencyContact { get; set; }
    public int? SelectedPlanID { get; set; }
    public string? SelectedPlanName { get; set; }
    public decimal Price { get; set; }
    public bool IsOneDayPass { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? PaymentReferenceNumber { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public ApplicationStatus ApplicationStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public string? VerifiedByUsername { get; set; }
    public string? RejectionReason { get; set; }
    public string? TemporaryQRCode { get; set; }
}

public class VerifyApplicationDto
{
    [Required]
    public ApplicationStatus Status { get; set; }

    [StringLength(255)]
    public string? RejectionReason { get; set; }
}
