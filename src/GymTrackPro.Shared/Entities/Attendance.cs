using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("AttendanceLogs")]
public class Attendance
{
    public const string StaffQrSource = "StaffQr";
    public const string LegacyStaffQrSource = "LegacyStaffQr";
    public const string HistoricalImportSource = "HistoricalImport";
    public const string SelfCheckInSource = "SelfCheckIn";

    [Key]
    public int AttendanceID { get; set; }

    [Required]
    public int MemberID { get; set; }

    [ForeignKey(nameof(MemberID))]
    public Member? Member { get; set; }

    [Required]
    public DateOnly AttendanceDate { get; set; }

    [Required]
    public DateTime CheckInTime { get; set; }

    public DateTime? CheckOutTime { get; set; }

    // Nullable only for staged migration of historical rows. New writes require a value.
    [StringLength(32)]
    public string? Source { get; set; }

    // Nullable only for staged migration of historical rows. New writes require an actor.
    public int? ActorUserID { get; set; }

    [ForeignKey(nameof(ActorUserID))]
    public User? ActorUser { get; set; }

    [Required]
    public bool IsVoided { get; set; }

    public int? VoidActorUserID { get; set; }

    [ForeignKey(nameof(VoidActorUserID))]
    public User? VoidActorUser { get; set; }

    public DateTime? VoidedAtUtc { get; set; }

    [StringLength(255, MinimumLength = 1)]
    public string? VoidReason { get; set; }

    public int? SupersededByAttendanceID { get; set; }

    [ForeignKey(nameof(SupersededByAttendanceID))]
    public Attendance? SupersededByAttendance { get; set; }

    public ICollection<AttendanceAdjustment> Adjustments { get; set; } = new List<AttendanceAdjustment>();

    public ICollection<AttendanceOperation> Operations { get; set; } = new List<AttendanceOperation>();

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    [Required]
    public DateTime LastModified { get; set; }
}
