using System;
using System.Collections.Generic;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.DTOs;

public class GoerDashboardDto
{
    public ProjectionMetadataDto Metadata { get; set; } = new();
    public string MembershipStatus { get; set; } = string.Empty;
    public AttendanceDto? CurrentSession { get; set; }
    // Compatibility display value; remove after 2027-01-12 in favor of seconds.
    public int CurrentMonthMinutes { get; set; }
    public long CurrentMonthDurationSeconds { get; set; }
    public int VisitCount { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    // Compatibility badge IDs; remove after 2027-01-12 in favor of Badges.
    public List<string> UnlockedBadges { get; set; } = new();
    public List<BadgeEligibilityDto> Badges { get; set; } = new();
    public string Timezone { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

public class GoerDigitalCardDto
{
    public ProjectionMetadataDto Metadata { get; set; } = new();
    public int MemberId { get; set; }
    public string MembershipStatus { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public string QrCodeValue { get; set; } = string.Empty;
}

public class GoerProgressDto
{
    public ProjectionMetadataDto Metadata { get; set; } = new();
    // Compatibility display value; remove after 2027-01-12 in favor of seconds.
    public int MonthlyDurationMinutes { get; set; }
    public long MonthlyDurationSeconds { get; set; }
    public int MonthlyVisits { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    // Compatibility badge IDs; remove after 2027-01-12 in favor of Badges.
    public List<string> EligibleBadges { get; set; } = new();
    public List<BadgeEligibilityDto> Badges { get; set; } = new();
    public string BadgeRuleVersion { get; set; } = "1.0";
}

public class ProjectionMetadataDto
{
    public string SchemaVersion { get; set; } = "gym-goer-v1";
    // Positive ordering key: durable per-member mutation counter in the high bits,
    // effective gym DateOnly.DayNumber in the low 22 bits.
    public long DataVersion { get; set; }
    public string ContentETag { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime CacheFreshUntilUtc { get; set; }
}

public class BadgeEligibilityDto
{
    public string BadgeId { get; set; } = string.Empty;
    public bool IsUnlocked { get; set; }
    public DateOnly? DerivedUnlockDate { get; set; }
}

public class AttendanceTrendPointDto
{
    public DateOnly Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public int VisitCount { get; set; }
}

public class OwnerAttendanceSummaryDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public DateOnly FromGymDate { get; set; }
    public DateOnly EndExclusiveGymDate { get; set; }
    public int PresetDays { get; set; }
    public int TotalVisits { get; set; }
    public double AverageVisits { get; set; }
    public string Timezone { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public Dictionary<string, int> DailyCounts { get; set; } = new();
    // DailyCounts is a one-release compatibility adapter; remove after 2027-01-12.
    public List<AttendanceTrendPointDto> Points { get; set; } = new();
}

public class CheckoutRequestDto
{
    public Guid OperationId { get; set; }
}
