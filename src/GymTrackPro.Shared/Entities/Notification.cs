using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Entities;

[Table("Notifications")]
public class Notification
{
    [Key]
    public int NotificationID { get; set; }

    [Required]
    public int GymID { get; set; }

    [ForeignKey("GymID")]
    public Gym? Gym { get; set; }

    [Required]
    public int MemberID { get; set; }

    [ForeignKey("MemberID")]
    public Member? Member { get; set; }

    [Required]
    [StringLength(100)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    [Required]
    public NotificationStatus Status { get; set; } = NotificationStatus.Unread;

    [Required]
    public DateTime ScheduledTime { get; set; } = DateTime.UtcNow;

    public DateTime? SentTime { get; set; }
}
