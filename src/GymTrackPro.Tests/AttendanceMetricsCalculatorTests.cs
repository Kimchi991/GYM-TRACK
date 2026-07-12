using GymTrackPro.API.Services;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.Tests;

public class AttendanceMetricsCalculatorTests
{
    [Fact]
    public void Duplicate_visit_dates_do_not_inflate_streaks_or_visits()
    {
        var today = new DateOnly(2026, 7, 12);
        var dates = new[]
        {
            today.AddDays(-2),
            today.AddDays(-1),
            today.AddDays(-1),
            today
        };

        var metrics = Calculate(dates, Array.Empty<Attendance>(), today);

        Assert.Equal(3, metrics.PeriodVisitCount);
        Assert.Equal(3, metrics.CurrentStreak);
        Assert.Equal(3, metrics.LongestStreak);
    }

    [Fact]
    public void Current_streak_may_end_yesterday_but_not_earlier()
    {
        var today = new DateOnly(2026, 7, 12);
        var yesterday = Calculate(
            new[] { today.AddDays(-3), today.AddDays(-2), today.AddDays(-1) },
            Array.Empty<Attendance>(),
            today);
        var stale = Calculate(
            new[] { today.AddDays(-4), today.AddDays(-3), today.AddDays(-2) },
            Array.Empty<Attendance>(),
            today);

        Assert.Equal(3, yesterday.CurrentStreak);
        Assert.Equal(0, stale.CurrentStreak);
        Assert.Equal(3, stale.LongestStreak);
    }

    [Fact]
    public void Duration_is_exact_period_overlap_and_excludes_open_or_voided_sessions()
    {
        var period = new UtcDateRange(
            Utc(2026, 2, 1, 0, 0),
            Utc(2026, 3, 1, 0, 0));
        var sessions = new[]
        {
            Session(Utc(2026, 1, 31, 23, 30), Utc(2026, 2, 1, 0, 30)),
            Session(Utc(2026, 2, 28, 23, 30), Utc(2026, 3, 1, 0, 30)),
            Session(Utc(2026, 2, 15, 10, 0), null),
            Session(Utc(2026, 2, 15, 10, 0), Utc(2026, 2, 15, 12, 0), isVoided: true)
        };

        var metrics = AttendanceMetricsCalculator.Calculate(
            new[] { new DateOnly(2026, 2, 1) },
            sessions,
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 3, 1),
            period,
            new DateOnly(2026, 2, 15));

        Assert.Equal(3600, metrics.CompletedDurationSeconds);
    }

    [Fact]
    public void Exact_v1_badges_include_derived_unlock_dates()
    {
        var dates = new[]
        {
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 2),
            new DateOnly(2026, 7, 3),
            new DateOnly(2026, 7, 4),
            new DateOnly(2026, 7, 5)
        };

        var metrics = Calculate(dates, Array.Empty<Attendance>(), new DateOnly(2026, 7, 5));

        Assert.Collection(
            metrics.Badges,
            badge =>
            {
                Assert.Equal("first_visit", badge.BadgeId);
                Assert.Equal(new DateOnly(2026, 7, 1), badge.DerivedUnlockDate);
            },
            badge =>
            {
                Assert.Equal("streak_3", badge.BadgeId);
                Assert.Equal(new DateOnly(2026, 7, 3), badge.DerivedUnlockDate);
            },
            badge =>
            {
                Assert.Equal("weekend_warrior", badge.BadgeId);
                Assert.Equal(new DateOnly(2026, 7, 5), badge.DerivedUnlockDate);
            });
    }

    [Fact]
    public void Correction_recomputes_duration_without_changing_visit_badges()
    {
        var date = new DateOnly(2026, 7, 12);
        var original = Calculate(
            new[] { date },
            new[] { Session(Utc(2026, 7, 12, 1, 0), Utc(2026, 7, 12, 2, 0)) },
            date);
        var corrected = Calculate(
            new[] { date },
            new[] { Session(Utc(2026, 7, 12, 1, 0), Utc(2026, 7, 12, 3, 0)) },
            date);

        Assert.Equal(3600, original.CompletedDurationSeconds);
        Assert.Equal(7200, corrected.CompletedDurationSeconds);
        Assert.Equal(original.Badges.Select(item => item.BadgeId), corrected.Badges.Select(item => item.BadgeId));
    }

    [Fact]
    public void Voiding_a_middle_visit_reverses_streak_badge_eligibility()
    {
        var first = new DateOnly(2026, 7, 10);
        var beforeVoid = Calculate(
            new[] { first, first.AddDays(1), first.AddDays(2) },
            Array.Empty<Attendance>(),
            first.AddDays(2));
        var afterVoid = Calculate(
            new[] { first, first.AddDays(2) },
            Array.Empty<Attendance>(),
            first.AddDays(2));

        Assert.True(beforeVoid.Badges.Single(item => item.BadgeId == "streak_3").IsUnlocked);
        Assert.False(afterVoid.Badges.Single(item => item.BadgeId == "streak_3").IsUnlocked);
        Assert.Equal(1, afterVoid.LongestStreak);
    }

    private static AttendanceMetrics Calculate(
        IReadOnlyList<DateOnly> dates,
        IReadOnlyList<Attendance> sessions,
        DateOnly today)
    {
        return AttendanceMetricsCalculator.Calculate(
            dates,
            sessions,
            new DateOnly(today.Year, today.Month, 1),
            new DateOnly(today.Year, today.Month, 1).AddMonths(1),
            new UtcDateRange(
                Utc(today.Year, today.Month, 1, 0, 0),
                Utc(today.Year, today.Month, 1, 0, 0).AddMonths(1)),
            today);
    }

    private static Attendance Session(DateTime checkIn, DateTime? checkOut, bool isVoided = false)
    {
        return new Attendance
        {
            CheckInTime = checkIn,
            CheckOutTime = checkOut,
            IsVoided = isVoided
        };
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }
}
