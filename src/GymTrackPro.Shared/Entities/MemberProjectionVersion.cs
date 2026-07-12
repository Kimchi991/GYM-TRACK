using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

/// <summary>
/// Durable monotonic version for one member's client-visible projection.
/// </summary>
[Table("MemberProjectionVersions")]
public sealed class MemberProjectionVersion
{
    public const long MaximumVersion = long.MaxValue >> 22;

    [Key]
    [ForeignKey(nameof(Member))]
    public int MemberID { get; set; }

    public Member? Member { get; set; }

    [Required]
    public long Version { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
