using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class WalkInVisitorDto
{
    public int VisitorID { get; set; }

    [Required]
    [StringLength(100)]
    public string VisitorName { get; set; } = string.Empty;

    [Required]
    public DateTime VisitDate { get; set; } = DateTime.UtcNow;

    [Required]
    [Range(0.01, 10000.00)]
    public decimal FeePaid { get; set; }

    [StringLength(255)]
    public string? Purpose { get; set; }
}
