using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("Subscriptions")]
public class Subscription
{
    [Key]
    public int SubscriptionID { get; set; }

    [Required]
    public int MemberID { get; set; }

    [ForeignKey("MemberID")]
    public Member? Member { get; set; }

    [Required]
    public int PlanID { get; set; }

    [ForeignKey("PlanID")]
    public MembershipPlan? Plan { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Active";

    [Required]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
