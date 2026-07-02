using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("AttendanceLogs")]
public class Attendance
{
    [Key]
    public int AttendanceID { get; set; }

    [Required]
    public int MemberID { get; set; }

    [ForeignKey("MemberID")]
    public Member? Member { get; set; }

    [Required]
    public DateTime AttendanceDate { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime CheckInTime { get; set; } = DateTime.UtcNow;

    public DateTime? CheckOutTime { get; set; }

    [Required]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
