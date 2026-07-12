using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public sealed class QrCodeLookupRequestDto
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string QrCode { get; set; } = string.Empty;
}
