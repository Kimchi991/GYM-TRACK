using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class CorrectCheckoutRequestDto
{
    [Required]
    public Guid OperationId { get; set; }

    [Required]
    public DateTime CorrectedCheckOutTimeUtc { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}
