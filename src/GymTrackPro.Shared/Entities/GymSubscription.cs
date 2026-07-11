using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Entities;

[Table("GymSubscriptions")]
public class GymSubscription
{
    [Key]
    public int SubscriptionID { get; set; }

    [Required]
    public int GymID { get; set; }

    [ForeignKey("GymID")]
    public Gym? Gym { get; set; }

    [Required]
    public int PlanID { get; set; }

    [ForeignKey("PlanID")]
    public SubscriptionPlan? Plan { get; set; }

    [Required]
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;

    [Required]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime ExpiresAt { get; set; }

    public DateTime? RenewedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public DateTime? TrialEndsAt { get; set; }
}
