using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

/// <summary>
/// Identifies an idempotent attendance operation whose member is resolved from
/// the authenticated gym-goer identity rather than from a QR code.
/// </summary>
public sealed class AttendanceOperationRequestDto
{
    [Required]
    public Guid OperationId { get; set; }
}
