using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Entities;

[Table("AccountInvites")]
public class AccountInvite
{
    [Key]
    public int AccountInviteID { get; set; }

    public int? TargetMemberID { get; set; }

    [ForeignKey(nameof(TargetMemberID))]
    public Member? TargetMember { get; set; }

    public int? TargetUserID { get; set; }

    [ForeignKey(nameof(TargetUserID))]
    public User? TargetUser { get; set; }

    [Required]
    [MinLength(32)]
    [MaxLength(32)]
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    [Required]
    [StringLength(255)]
    public string NormalizedEmail { get; set; } = string.Empty;

    [Required]
    public UserRole IntendedRole { get; set; }

    [Required]
    [StringLength(100)]
    public string Purpose { get; set; } = string.Empty;

    [Required]
    public int CreatedByUserID { get; set; }

    [ForeignKey(nameof(CreatedByUserID))]
    public User? CreatedByUser { get; set; }

    [Required]
    public DateTime CreatedAtUtc { get; set; }

    [Required]
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    [StringLength(128)]
    public string? UsedByFirebaseUid { get; set; }

    public Guid? RedemptionOperationId { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
