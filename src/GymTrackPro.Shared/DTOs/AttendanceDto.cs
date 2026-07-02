using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class AttendanceDto
{
    public int AttendanceID { get; set; }

    [Required]
    public int MemberID { get; set; }

    public string MemberName { get; set; } = string.Empty;

    [Required]
    public DateTime AttendanceDate { get; set; }

    [Required]
    public DateTime CheckInTime { get; set; }

    public DateTime? CheckOutTime { get; set; }

    public DateTime LastModified { get; set; }
}
