using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class CheckInRequestDto
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string QrCode { get; set; } = string.Empty;

    [Required]
    public Guid OperationId { get; set; }
}
