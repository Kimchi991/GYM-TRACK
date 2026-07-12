using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.AspNetCore.Http;

namespace GymTrackPro.Tests;

public class DashboardServiceTests
{
    [Fact]
    public async Task Ef_backed_dashboard_zero_fills_hours_and_excludes_voided_sessions()
    {
        await using var context = CreateContext();
        var now = Utc(2026, 7, 12, 4, 0);
        var gymDate = new DateOnly(2026, 7, 12);
        context.AttendanceLogs.AddRange(
            CreateAttendance(1, gymDate, now.AddHours(-1), checkOut: null),
            CreateAttendance(2, gymDate, now.AddHours(-17), checkOut: null),
            CreateAttendance(3, gymDate, now.AddHours(-2), checkOut: null, isVoided: true),
            CreateAttendance(4, gymDate, now.AddHours(-3), now.AddHours(-2)));
        await context.SaveChangesAsync();
        var service = CreateService(context, now, gymDate);

        var result = await service.GetDashboardMetricsAsync();

        Assert.Equal(2, result.MembersCheckedInCount);
        Assert.Equal("Open sessions", result.MembersCheckedInLabel);
        Assert.Equal(1, result.StaleOpenSessionCount);
        Assert.Equal(3, result.VisitsTodayCount);
        Assert.Equal(24, result.CheckInsByHour.Count);
        Assert.Equal(24, result.CheckInsByHour.Select(item => item.Hour).Distinct().Count());
        Assert.Equal(2, result.CheckInsByHour.Sum(item => item.Count));
    }

    [Fact]
    public async Task Invalid_stale_session_setting_fails_closed()
    {
        await using var context = CreateContext();
        var service = CreateService(
            context,
            Utc(2026, 7, 12, 4, 0),
            new DateOnly(2026, 7, 12),
            settingFailure: new AppAccessException(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.AttendanceConfigurationInvalid,
                "Attendance dashboard configuration is unavailable."));

        var exception = await Assert.ThrowsAsync<AppAccessException>(
            () => service.GetDashboardMetricsAsync());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal(ErrorCodes.AttendanceConfigurationInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Active_and_expiring_counts_are_distinct_and_use_effective_unpaused_coverage()
    {
        await using var context = CreateContext();
        var now = Utc(2026, 7, 12, 4, 0);
        var today = new DateOnly(2026, 7, 12);
        context.Members.AddRange(
            Member(1),
            Member(2),
            Member(3, deleted: true));
        context.Subscriptions.AddRange(
            Subscription(1, 1, today.AddDays(-10), today.AddDays(6), GymMembershipPolicy.Active),
            Subscription(2, 1, today.AddDays(-10), today.AddDays(30), GymMembershipPolicy.Paused),
            Subscription(3, 2, today.AddDays(-10), today.AddDays(5), GymMembershipPolicy.Active),
            Subscription(4, 3, today.AddDays(-10), today.AddDays(5), GymMembershipPolicy.Active));
        context.MembershipPauses.Add(new MembershipPause
        {
            SubscriptionID = 3,
            PauseStartDate = now,
            Reason = "Open pause",
            DateCreated = now
        });
        await context.SaveChangesAsync();
        var service = CreateService(context, now, today);

        var result = await service.GetDashboardMetricsAsync();

        Assert.Equal(1, result.ActiveMembershipsCount);
        Assert.Equal(1, result.ExpiringMembershipsCount);
    }

    private static DashboardService CreateService(
        GymDbContext context,
        DateTime now,
        DateOnly gymDate,
        AppAccessException? settingFailure = null)
    {
        var timezone = new Mock<ITimezoneService>();
        timezone.Setup(service => service.GetGymDateAsync(
                now,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(gymDate);
        timezone.Setup(service => service.GetUtcRangeForGymDateAsync(
                gymDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UtcDateRange(now.Date.AddHours(-8), now.Date.AddDays(1).AddHours(-8)));
        timezone.Setup(service => service.GetUtcRangeForGymDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                new UtcDateRange(
                    Utc(from.Year, from.Month, from.Day, 0, 0),
                    Utc(to.Year, to.Month, to.Day, 0, 0)));
        timezone.Setup(service => service.GetGymTimeZoneAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
        var clock = new Mock<IClockService>();
        clock.SetupGet(service => service.UtcNow).Returns(now);
        var settings = new Mock<ISystemSettingService>();
        var settingSetup = settings.Setup(service => service.GetValueIntAsync(
            SystemSettingService.StaleSessionHoursKey,
            16));
        if (settingFailure is null)
        {
            settingSetup.ReturnsAsync(16);
        }
        else
        {
            settingSetup.ThrowsAsync(settingFailure);
        }

        return new DashboardService(context, timezone.Object, clock.Object, settings.Object);
    }

    private static Attendance CreateAttendance(
        int id,
        DateOnly date,
        DateTime checkIn,
        DateTime? checkOut,
        bool isVoided = false)
    {
        return new Attendance
        {
            AttendanceID = id,
            MemberID = id,
            AttendanceDate = date,
            CheckInTime = checkIn,
            CheckOutTime = checkOut,
            Source = Attendance.StaffQrSource,
            ActorUserID = 1,
            IsVoided = isVoided,
            LastModified = checkOut ?? checkIn
        };
    }

    private static Member Member(int id, bool deleted = false)
    {
        return new Member
        {
            MemberID = id,
            FirstName = "Test",
            LastName = id.ToString(),
            QRCode = $"qr-{id}",
            Status = GymMembershipPolicy.MemberActive,
            IsDeleted = deleted
        };
    }

    private static Subscription Subscription(
        int id,
        int memberId,
        DateOnly start,
        DateOnly end,
        string status)
    {
        return new Subscription
        {
            SubscriptionID = id,
            MemberID = memberId,
            PlanID = 1,
            StartDate = GymMembershipPolicy.ToStorageDate(start),
            EndDate = GymMembershipPolicy.ToStorageDate(end),
            Status = status
        };
    }

    private static GymDbContext CreateContext()
    {
        return new GymDbContext(new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }
}
