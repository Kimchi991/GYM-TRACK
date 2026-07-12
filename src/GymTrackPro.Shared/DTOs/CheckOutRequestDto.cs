using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class CheckOutRequestDto
{
    [Required]
    public Guid OperationId { get; set; }
}
