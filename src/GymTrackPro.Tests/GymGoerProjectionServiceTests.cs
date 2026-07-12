using GymTrackPro.API.Data;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Repositories;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Moq;

namespace GymTrackPro.Tests;

public class GymGoerProjectionServiceTests
{
    [Fact]
    public async Task Maximum_calendar_month_is_rejected_as_stable_bad_request_before_addmonths()
    {
        await using var fixture = await ProjectionFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Service.GetProgressAsync("9999-12"));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(ErrorCodes.InvalidAttendanceRange, exception.ErrorCode);
    }

    [Fact]
    public void Projection_version_composition_orders_mutations_and_gym_day_rollovers()
    {
        var day = new DateOnly(2026, 7, 12);
        var sameMutationNextDay = ProjectionVersionComposer.Compose(8, day.AddDays(1));
        var current = ProjectionVersionComposer.Compose(8, day);
        var nextMutationEarlierDate = ProjectionVersionComposer.Compose(9, day.AddDays(-30));

        Assert.True(sameMutationNextDay > current);
        Assert.True(nextMutationEarlierDate > sameMutationNextDay);
        Assert.True(ProjectionVersionComposer.Compose(0, DateOnly.MaxValue) > 0);
    }

    [Fact]
    public void Projection_version_composition_rejects_invalid_or_overflowing_mutation_counters()
    {
        var day = new DateOnly(2026, 7, 12);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectionVersionComposer.Compose(-1, day));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectionVersionComposer.Compose(
                ProjectionVersionComposer.MaximumMutationVersion + 1,
                day));
    }

    [Fact]
    public async Task Ef_backed_projection_is_sequential_bounded_and_returns_metadata()
    {
        await using var fixture = await ProjectionFixture.CreateAsync();

        var result = await fixture.Service.GetProgressAsync("2026-07");

        Assert.Equal(3, result.MonthlyVisits);
        Assert.Equal(10_800, result.MonthlyDurationSeconds);
        Assert.Equal(180, result.MonthlyDurationMinutes);
        Assert.Equal(3, result.CurrentStreak);
        Assert.Equal(3, result.LongestStreak);
        Assert.Equal("gym-goer-v1", result.Metadata.SchemaVersion);
        Assert.Equal("UTC", result.Metadata.Timezone);
        Assert.True(result.Metadata.DataVersion > 0);
        Assert.NotEmpty(result.Metadata.ContentETag);
        Assert.Equal(fixture.Now, result.Metadata.GeneratedAtUtc);
        Assert.True(result.Metadata.CacheFreshUntilUtc > result.Metadata.GeneratedAtUtc);
        Assert.True(result.Badges.Single(badge => badge.BadgeId == "streak_3").IsUnlocked);
    }

    [Fact]
    public async Task Open_session_counts_as_visit_but_not_completed_duration()
    {
        await using var fixture = await ProjectionFixture.CreateAsync();
        fixture.Context.AttendanceLogs.Add(new Attendance
        {
            MemberID = 1,
            AttendanceDate = new DateOnly(2026, 7, 12),
            CheckInTime = fixture.Now.AddMinutes(-30),
            Source = Attendance.StaffQrSource,
            ActorUserID = 5,
            LastModified = fixture.Now.AddMinutes(-30)
        });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.GetGoerDashboardAsync();

        Assert.Equal(4, result.VisitCount);
        Assert.Equal(10_800, result.CurrentMonthDurationSeconds);
        Assert.NotNull(result.CurrentSession);
    }

    [Fact]
    public async Task Void_and_correction_recompute_badges_and_duration()
    {
        await using var fixture = await ProjectionFixture.CreateAsync();
        var before = await fixture.Service.GetProgressAsync("2026-07");
        var middle = await fixture.Context.AttendanceLogs.SingleAsync(
            attendance => attendance.AttendanceDate == new DateOnly(2026, 7, 10));
        middle.IsVoided = true;
        var last = await fixture.Context.AttendanceLogs.SingleAsync(
            attendance => attendance.AttendanceDate == new DateOnly(2026, 7, 11));
        last.CheckOutTime = last.CheckInTime.AddHours(2);
        await fixture.Context.SaveChangesAsync();
        fixture.AdvanceMutationVersion();

        var after = await fixture.Service.GetProgressAsync("2026-07");

        Assert.True(before.Badges.Single(badge => badge.BadgeId == "streak_3").IsUnlocked);
        Assert.False(after.Badges.Single(badge => badge.BadgeId == "streak_3").IsUnlocked);
        Assert.Equal(before.MonthlyDurationSeconds, after.MonthlyDurationSeconds);
        Assert.True(after.Metadata.DataVersion > before.Metadata.DataVersion);
        Assert.NotEqual(before.Metadata.ContentETag, after.Metadata.ContentETag);
    }

    [Fact]
    public async Task Status_inactive_member_keeps_self_projection_access_but_reports_inactive_membership()
    {
        await using var fixture = await ProjectionFixture.CreateAsync();
        var member = await fixture.Context.Members.SingleAsync(item => item.MemberID == 1);
        member.Status = "Inactive";
        await fixture.Context.SaveChangesAsync();

        var dashboard = await fixture.Service.GetGoerDashboardAsync();
        var card = await fixture.Service.GetDigitalCardAsync();

        Assert.Equal(AttendanceMembershipState.Inactive.ToString(), dashboard.MembershipStatus);
        Assert.Equal(AttendanceMembershipState.Inactive.ToString(), card.MembershipStatus);
        Assert.Equal(1, card.MemberId);
        Assert.Equal("member-qr", card.QrCodeValue);
    }

    private sealed class ProjectionFixture : IAsyncDisposable
    {
        private ProjectionFixture(
            GymDbContext context,
            GymGoerProjectionService service,
            DateTime now,
            ProjectionVersionState versionState)
        {
            Context = context;
            Service = service;
            Now = now;
            _versionState = versionState;
        }

        private readonly ProjectionVersionState _versionState;

        public GymDbContext Context { get; }
        public GymGoerProjectionService Service { get; }
        public DateTime Now { get; }

        public void AdvanceMutationVersion() => _versionState.Advance();

        public static async Task<ProjectionFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<GymDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            var context = new GymDbContext(options);
            var now = Utc(2026, 7, 11, 12, 0);
            context.Members.Add(new Member
            {
                MemberID = 1,
                FirstName = "Test",
                LastName = "Member",
                QRCode = "member-qr",
                Status = "Active",
                LastModified = now
            });
            context.Subscriptions.Add(new Subscription
            {
                SubscriptionID = 1,
                MemberID = 1,
                PlanID = 1,
                StartDate = Utc(2026, 7, 1, 0, 0),
                EndDate = Utc(2026, 7, 31, 23, 59),
                Status = "Active",
                LastModified = now
            });
            context.SystemSettings.Add(new SystemSetting
            {
                SettingKey = TimezoneService.TimezoneSettingKey,
                SettingValue = "UTC"
            });
            for (var offset = 0; offset < 3; offset++)
            {
                var date = new DateOnly(2026, 7, 9).AddDays(offset);
                var checkIn = Utc(date.Year, date.Month, date.Day, 1, 0);
                context.AttendanceLogs.Add(new Attendance
                {
                    MemberID = 1,
                    AttendanceDate = date,
                    CheckInTime = checkIn,
                    CheckOutTime = checkIn.AddHours(1),
                    Source = Attendance.StaffQrSource,
                    ActorUserID = 5,
                    LastModified = checkIn.AddHours(1)
                });
            }

            await context.SaveChangesAsync();

            var timezone = new Mock<ITimezoneService>();
            timezone.Setup(service => service.GetGymDateAsync(
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DateOnly(2026, 7, 11));
            timezone.Setup(service => service.GetGymDateAsync(
                    It.IsAny<DateTime>(),
                    "UTC",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DateOnly(2026, 7, 11));
            timezone.Setup(service => service.GetGymTimeZoneAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(TimeZoneInfo.Utc);
            timezone.Setup(service => service.GetGymTimeZoneAsync(
                    "UTC",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(TimeZoneInfo.Utc);
            timezone.Setup(service => service.GetUtcRangeForGymDateRangeAsync(
                    It.IsAny<DateOnly>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly start, DateOnly end, CancellationToken _) =>
                    new UtcDateRange(
                        Utc(start.Year, start.Month, start.Day, 0, 0),
                        Utc(end.Year, end.Month, end.Day, 0, 0)));
            timezone.Setup(service => service.GetUtcRangeForGymDateRangeAsync(
                    It.IsAny<DateOnly>(),
                    It.IsAny<DateOnly>(),
                    "UTC",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly start, DateOnly end, string _, CancellationToken _) =>
                    new UtcDateRange(
                        Utc(start.Year, start.Month, start.Day, 0, 0),
                        Utc(end.Year, end.Month, end.Day, 0, 0)));
            var clock = new Mock<IClockService>();
            clock.SetupGet(service => service.UtcNow).Returns(now);
            var currentUser = new Mock<ICurrentUserContext>();
            currentUser.SetupGet(context => context.MemberId).Returns(1);
            var projectionVersion = new ProjectionVersionState(100);
            var versionProvider = new Mock<IProjectionVersionProvider>();
            versionProvider.Setup(provider => provider.GetMutationVersionForMemberAsync(
                    1,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => projectionVersion.Value);

            var service = new GymGoerProjectionService(
                new MemberRepository(context),
                new AttendanceRepository(context),
                timezone.Object,
                clock.Object,
                currentUser.Object,
                versionProvider.Object);
            return new ProjectionFixture(context, service, now, projectionVersion);
        }

        public ValueTask DisposeAsync()
        {
            return Context.DisposeAsync();
        }
    }

    private sealed class ProjectionVersionState
    {
        private long _value;

        public ProjectionVersionState(long value)
        {
            _value = value;
        }

        public long Value => Interlocked.Read(ref _value);

        public void Advance() => Interlocked.Increment(ref _value);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }
}
