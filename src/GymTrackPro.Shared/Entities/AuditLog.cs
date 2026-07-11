using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("AuditLogs")]
public class AuditLog
{
    [Key]
    public int LogID { get; set; }

    public int? GymID { get; set; }

    public int? UserID { get; set; }

    [ForeignKey("UserID")]
    public User? User { get; set; }

    [Required]
    [StringLength(100)]
    public string Action { get; set; } = string.Empty;

    [Required]
    public string Details { get; set; } = string.Empty;

    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Required]
    [StringLength(50)]
    public string IPAddress { get; set; } = string.Empty;
}
