using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class CreateMembershipPlanDto
{
    [Required]
    [StringLength(50)]
    public string PlanName { get; set; } = string.Empty;

    [Required]
    [Range(1, 3650)]
    public int DurationDays { get; set; }

    [Required]
    [Range(0.01, 1000000.00)]
    public decimal Price { get; set; }

    [StringLength(255)]
    public string? Description { get; set; }
}
