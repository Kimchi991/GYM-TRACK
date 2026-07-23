using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("WalkInVisitors")]
public class WalkInVisitor
{
    [Key]
    public int VisitorID { get; set; }

    [Required]
    [StringLength(100)]
    public string VisitorName { get; set; } = string.Empty;

    [Required]
    public DateTime VisitDate { get; set; } = DateTime.UtcNow;

    [Required]
    public decimal FeePaid { get; set; }

    [StringLength(255)]
    public string? Purpose { get; set; }

    [StringLength(100)]
    public string? TemporaryQRCode { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }
}
