using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("MembershipPauses")]
public class MembershipPause
{
    [Key]
    public int PauseID { get; set; }

    [Required]
    public int SubscriptionID { get; set; }

    [ForeignKey("SubscriptionID")]
    public Subscription? Subscription { get; set; }

    [Required]
    public DateTime PauseStartDate { get; set; }

    public DateTime? PauseEndDate { get; set; }

    [Required]
    [StringLength(255)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}
