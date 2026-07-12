using System.ComponentModel.DataAnnotations;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.DTOs;

public class ActivateInviteDto : IValidatableObject
{
    [Required]
    [StringLength(43, MinimumLength = 43)]
    [RegularExpression("^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$")]
    public string InviteCode { get; set; } = string.Empty;

    public Guid OperationId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OperationId == Guid.Empty)
        {
            yield return new ValidationResult(
                "A non-empty operationId is required.",
                new[] { nameof(OperationId) });
        }
    }
}

[Obsolete("Use ActivateInviteDto. This compatibility subtype has no independent authority fields.")]
public sealed class ActivateAppRequestDto : ActivateInviteDto
{
}

public class AppInviteResponseDto
{
    public int? TargetMemberID { get; set; }
    public int? TargetUserID { get; set; }
    public UserRole IntendedRole { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AppInviteCodeResponseDto
{
    public string InviteCode { get; set; } = string.Empty;
    public AppInviteResponseDto Details { get; set; } = new();
}

public class CreateAppInviteDto
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Purpose { get; set; } = string.Empty;
}

public sealed class CreateStaffInviteDto
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Purpose { get; set; } = string.Empty;
}

public sealed class StaffInviteProvisioningResponseDto
{
    public UserResponseDto User { get; set; } = new();
    public AppInviteCodeResponseDto Invite { get; set; } = new();
}
