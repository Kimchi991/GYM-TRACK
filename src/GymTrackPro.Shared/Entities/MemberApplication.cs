using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Entities;

[Table("MemberApplications")]
public class MemberApplication
{
    [Key]
    public int ApplicationID { get; set; }

    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string ContactNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string EmailAddress { get; set; } = string.Empty;

    [StringLength(100)]
    public string? EmergencyContact { get; set; }

    public int? SelectedPlanID { get; set; }

    [ForeignKey("SelectedPlanID")]
    public MembershipPlan? SelectedPlan { get; set; }

    [Required]
    public bool IsOneDayPass { get; set; } = false;

    [Required]
    public PaymentMethod PaymentMethod { get; set; }

    [StringLength(100)]
    public string? PaymentReferenceNumber { get; set; }

    [Required]
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    [Required]
    public ApplicationStatus ApplicationStatus { get; set; } = ApplicationStatus.Pending;

    [Required]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? VerifiedAtUtc { get; set; }

    public int? VerifiedByUserID { get; set; }

    [ForeignKey("VerifiedByUserID")]
    public User? VerifiedByUser { get; set; }

    [StringLength(255)]
    public string? RejectionReason { get; set; }
}
