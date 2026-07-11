using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("MembershipPlans")]
public class MembershipPlan
{
    [Key]
    public int PlanID { get; set; }

    [Required]
    public int GymID { get; set; }

    [ForeignKey("GymID")]
    public Gym? Gym { get; set; }

    [Required]
    [StringLength(50)]
    public string PlanName { get; set; } = string.Empty;

    [Required]
    public int DurationDays { get; set; }

    [Required]
    public decimal Price { get; set; }

    [StringLength(255)]
    public string? Description { get; set; }

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Active";

    [Required]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    [Required]
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public int? DeletedBy { get; set; }
}
