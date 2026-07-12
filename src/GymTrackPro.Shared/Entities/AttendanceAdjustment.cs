using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Entities;

[Table("AttendanceAdjustments")]
public class AttendanceAdjustment
{
    [Key]
    public long AttendanceAdjustmentID { get; set; }

    [Required]
    public int AttendanceID { get; set; }

    [ForeignKey(nameof(AttendanceID))]
    public Attendance? Attendance { get; set; }

    [Required]
    public AttendanceAdjustmentKind Kind { get; set; }

    public DateTime? BeforeCheckOutTimeUtc { get; set; }

    public DateTime? AfterCheckOutTimeUtc { get; set; }

    public bool? BeforeIsVoided { get; set; }

    public bool? AfterIsVoided { get; set; }

    public int? BeforeSupersededByAttendanceID { get; set; }

    public int? AfterSupersededByAttendanceID { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public int ActorUserID { get; set; }

    [ForeignKey(nameof(ActorUserID))]
    public User? ActorUser { get; set; }

    [Required]
    public Guid OperationID { get; set; }

    [ForeignKey(nameof(OperationID))]
    public AttendanceOperation? Operation { get; set; }

    [Required]
    public DateTime CreatedAtUtc { get; set; }
}
