using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class PauseSubscriptionDto
{
    [Required]
    [StringLength(255)]
    public string Reason { get; set; } = string.Empty;
}
