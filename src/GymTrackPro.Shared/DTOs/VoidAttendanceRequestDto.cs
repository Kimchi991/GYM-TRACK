using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class VoidAttendanceRequestDto
{
    [Required]
    public Guid OperationId { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;

    public int? SupersedingAttendanceId { get; set; }
}
