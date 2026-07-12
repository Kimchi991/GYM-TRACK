using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Entities;

[Table("AttendanceOperations")]
public class AttendanceOperation
{
    [Key]
    public Guid OperationID { get; set; }

    [Required]
    public int ActorUserID { get; set; }

    [ForeignKey(nameof(ActorUserID))]
    public User? ActorUser { get; set; }

    [Required]
    public AttendanceOperationType OperationType { get; set; }

    [Required]
    [MaxLength(32)]
    public byte[] RequestFingerprint { get; set; } = Array.Empty<byte>();

    public int? TargetAttendanceID { get; set; }

    [ForeignKey(nameof(TargetAttendanceID))]
    public Attendance? TargetAttendance { get; set; }

    [Required]
    public int OriginalHttpStatus { get; set; }

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string OriginalResultCode { get; set; } = string.Empty;

    [Required]
    public AttendanceOperationState State { get; set; }

    [Required]
    public DateTime CreatedAtUtc { get; set; }

    [Required]
    public DateTime CompletedAtUtc { get; set; }

    public AttendanceAdjustment? Adjustment { get; set; }
}
