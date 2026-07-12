using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GymTrackPro.Tests;

public class ReportsServiceTests
{
    [Fact]
    public async Task Inclusive_maximum_report_end_is_rejected_as_stable_bad_request()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new DateOnly(2026, 10, 5));
        var maximumDate = new DateTime(9999, 12, 31);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.GetAttendanceReportAsync(maximumDate, maximumDate));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(ErrorCodes.InvalidAttendanceRange, exception.ErrorCode);
    }

    [Fact]
    public async Task Owner_summary_defaults_to_seven_days_zero_fills_and_excludes_voided()
    {
        await using var context = CreateContext();
        context.AttendanceLogs.AddRange(
            CreateAttendance(1, new DateOnly(2026, 10, 1), isVoided: false),
            CreateAttendance(2, new DateOnly(2026, 10, 1), isVoided: false),
            CreateAttendance(3, new DateOnly(2026, 10, 4), isVoided: false),
            CreateAttendance(4, new DateOnly(2026, 10, 4), isVoided: true));
        await context.SaveChangesAsync();
        var service = CreateService(context, new DateOnly(2026, 10, 5));

        var result = await service.GetAttendanceSummaryAsync(null, null, "day");

        Assert.Equal(7, result.Points.Count);
        Assert.Equal(7, result.DailyCounts.Count);
        Assert.Equal(3, result.TotalVisits);
        Assert.Equal(result.TotalVisits, result.Points.Sum(point => point.VisitCount));
        Assert.Equal(2, result.DailyCounts["2026-10-01"]);
        Assert.Equal(0, result.DailyCounts["2026-10-02"]);
        Assert.Equal(1, result.DailyCounts["2026-10-04"]);
        Assert.Equal("Asia/Manila", result.Timezone);
    }

    [Fact]
    public async Task Owner_summary_accepts_bounded_end_exclusive_range_and_rejects_invalid_contracts()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new DateOnly(2026, 10, 5));

        var result = await service.GetAttendanceSummaryAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            "day");
        var tooLarge = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.GetAttendanceSummaryAsync(
                new DateOnly(2025, 1, 1),
                new DateOnly(2026, 1, 3),
                "day"));
        var badBucket = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.GetAttendanceSummaryAsync(null, null, "month"));

        Assert.Equal(30, result.Points.Count);
        Assert.Equal(ErrorCodes.InvalidAttendanceRange, tooLarge.ErrorCode);
        Assert.Equal(ErrorCodes.UnsupportedAttendancePreset, badBucket.ErrorCode);
    }

    [Fact]
    public async Task Legacy_attendance_report_accepts_date_only_inclusive_dates_and_uses_half_open_utc_query()
    {
        await using var context = CreateContext();
        var start = Utc(2026, 7, 1, 0, 0);
        var nextDay = Utc(2026, 7, 2, 0, 0);
        context.Members.AddRange(Enumerable.Range(1, 4).Select(id => new Member
        {
            MemberID = id,
            FirstName = "Member",
            LastName = id.ToString(),
            QRCode = $"qr-{id}",
            Status = "Active"
        }));
        context.AttendanceLogs.AddRange(
            AttendanceAt(1, start, isVoided: false),
            AttendanceAt(2, nextDay.AddTicks(-1), isVoided: false),
            AttendanceAt(3, nextDay, isVoided: false),
            AttendanceAt(4, start.AddHours(1), isVoided: true));
        await context.SaveChangesAsync();
        var service = CreateService(context, new DateOnly(2026, 7, 1));

        var rows = (await service.GetAttendanceReportAsync(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1))).ToList();

        Assert.Equal(new[] { 1, 2 }, rows.Select(row => row.AttendanceID));
    }

    [Fact]
    public async Task Attendance_plan_attribution_uses_same_effective_subscription_record()
    {
        await using var context = CreateContext();
        var date = new DateOnly(2026, 7, 12);
        context.Members.Add(new Member
        {
            MemberID = 1,
            FirstName = "Plan",
            LastName = "Member",
            QRCode = "plan-member",
            Status = GymMembershipPolicy.MemberActive
        });
        context.MembershipPlans.AddRange(
            new MembershipPlan { PlanID = 1, PlanName = "Effective Plan", DurationDays = 30, Status = GymMembershipPolicy.PlanActive },
            new MembershipPlan { PlanID = 2, PlanName = "Paused Longer Plan", DurationDays = 60, Status = GymMembershipPolicy.PlanActive });
        context.Subscriptions.AddRange(
            Subscription(1, 1, 1, date.AddDays(-5), date.AddDays(5), GymMembershipPolicy.Active),
            Subscription(2, 1, 2, date.AddDays(-5), date.AddDays(30), GymMembershipPolicy.Paused));
        context.AttendanceLogs.Add(AttendanceAt(
            1,
            Utc(2026, 7, 12, 1, 0),
            isVoided: false,
            date));
        await context.SaveChangesAsync();
        var service = CreateService(context, date);

        var row = Assert.Single(await service.GetAttendanceReportAsync(
            new DateTime(2026, 7, 12),
            new DateTime(2026, 7, 12)));

        Assert.Equal("Effective Plan", row.PlanName);
    }

    [Fact]
    public async Task Expiring_report_returns_distinct_effective_unpaused_active_membership()
    {
        await using var context = CreateContext();
        var today = new DateOnly(2026, 7, 12);
        context.Members.AddRange(Member(1), Member(2));
        context.MembershipPlans.Add(new MembershipPlan
        {
            PlanID = 1,
            PlanName = "Standard",
            DurationDays = 30,
            Status = GymMembershipPolicy.PlanActive
        });
        context.Subscriptions.AddRange(
            Subscription(1, 1, 1, today.AddDays(-5), today.AddDays(5), GymMembershipPolicy.Active),
            Subscription(2, 1, 1, today.AddDays(-5), today.AddDays(30), GymMembershipPolicy.Paused),
            Subscription(3, 2, 1, today.AddDays(-5), today.AddDays(5), GymMembershipPolicy.Active));
        context.MembershipPauses.Add(new MembershipPause
        {
            SubscriptionID = 3,
            PauseStartDate = Utc(2026, 7, 12, 4, 0),
            Reason = "Open pause"
        });
        await context.SaveChangesAsync();
        var service = CreateService(context, today);

        var rows = (await service.GetExpiringMembershipsReportAsync(7)).ToList();

        var row = Assert.Single(rows);
        Assert.Equal("Member 1", row.MemberName);
        Assert.Equal(new DateTime(2026, 7, 17), row.EndDate);
    }

    private static ReportsService CreateService(GymDbContext context, DateOnly today)
    {
        var now = Utc(today.Year, today.Month, today.Day, 4, 0);
        var timezone = new Mock<ITimezoneService>();
        timezone.Setup(service => service.GetGymDateAsync(
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(today);
        timezone.Setup(service => service.GetGymTimeZoneAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
        timezone.Setup(service => service.GetUtcRangeForGymDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                new UtcDateRange(
                    Utc(from.Year, from.Month, from.Day, 0, 0),
                    Utc(to.Year, to.Month, to.Day, 0, 0)));
        var clock = new Mock<IClockService>();
        clock.SetupGet(service => service.UtcNow).Returns(now);
        return new ReportsService(context, timezone.Object, clock.Object);
    }

    private static GymDbContext CreateContext()
    {
        return new GymDbContext(new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
    }

    private static Attendance CreateAttendance(int id, DateOnly date, bool isVoided)
    {
        return AttendanceAt(id, Utc(date.Year, date.Month, date.Day, 1, 0), isVoided, date);
    }

    private static Attendance AttendanceAt(
        int id,
        DateTime checkIn,
        bool isVoided,
        DateOnly? date = null)
    {
        return new Attendance
        {
            AttendanceID = id,
            MemberID = id,
            AttendanceDate = date ?? DateOnly.FromDateTime(checkIn),
            CheckInTime = checkIn,
            Source = Attendance.StaffQrSource,
            ActorUserID = 1,
            IsVoided = isVoided,
            LastModified = checkIn
        };
    }

    private static Member Member(int id)
    {
        return new Member
        {
            MemberID = id,
            FirstName = "Member",
            LastName = id.ToString(),
            QRCode = $"member-{id}",
            Status = GymMembershipPolicy.MemberActive
        };
    }

    private static Subscription Subscription(
        int id,
        int memberId,
        int planId,
        DateOnly start,
        DateOnly end,
        string status)
    {
        return new Subscription
        {
            SubscriptionID = id,
            MemberID = memberId,
            PlanID = planId,
            StartDate = GymMembershipPolicy.ToStorageDate(start),
            EndDate = GymMembershipPolicy.ToStorageDate(end),
            Status = status
        };
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }
}
