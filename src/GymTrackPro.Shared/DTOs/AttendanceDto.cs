using System;
using System.ComponentModel.DataAnnotations;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.DTOs;

public class AttendanceDto
{
    public int AttendanceID { get; set; }

    [Required]
    public int MemberID { get; set; }

    public string MemberName { get; set; } = string.Empty;

    [Required]
    public DateOnly AttendanceDate { get; set; }

    [Required]
    public DateTime CheckInTime { get; set; }

    public DateTime? CheckOutTime { get; set; }

    public string Source { get; set; } = string.Empty;

    public bool IsVoided { get; set; }

    public int? SupersededByAttendanceID { get; set; }

    public DateTime LastModified { get; set; }
}

public class CurrentAttendanceStateDto
{
    // CheckedOut means there is no non-void open session; no automatic checkout is implied.
    public AttendanceSessionState State { get; set; } = AttendanceSessionState.CheckedOut;
    // One-release compatibility adapter; remove after 2027-01-12.
    public bool IsCheckedIn => State == AttendanceSessionState.CheckedIn;
    public AttendanceDto? Session { get; set; }
}

public class AttendanceHistoryPageDto
{
    public ProjectionMetadataDto Metadata { get; set; } = new();
    public List<AttendanceDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public DateOnly FromGymDate { get; set; }
    public DateOnly EndExclusiveGymDate { get; set; }
}

public class EmergencyManifestItemDto
{
    public int AttendanceID { get; set; }
    public int MemberID { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string EmergencyContactName { get; set; } = string.Empty;
    public string EmergencyContactPhone { get; set; } = string.Empty;
    public DateTime CheckInTime { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class EmergencyEvacuationManifestDto
{
    public DateTime ExportedAtUtc { get; set; }
    public int TotalCheckedInOccupants { get; set; }
    public List<EmergencyManifestItemDto> Occupants { get; set; } = new();
}
