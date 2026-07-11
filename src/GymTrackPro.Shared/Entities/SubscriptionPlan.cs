using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("SubscriptionPlans")]
public class SubscriptionPlan
{
    [Key]
    public int PlanID { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public decimal Price { get; set; }

    [Required]
    public int MaxMembers { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    public int BillingCycleMonths { get; set; } = 1;
}
