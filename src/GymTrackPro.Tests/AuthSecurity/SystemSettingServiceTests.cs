using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class SystemSettingServiceTests
{
    [Fact]
    public async Task Missing_stale_session_setting_fails_closed_without_runtime_write()
    {
        await using var context = CreateContext();
        var repository = new Mock<ISystemSettingRepository>();
        var service = CreateService(context, repository);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.GetValueIntAsync(SystemSettingService.StaleSessionHoursKey));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal(ErrorCodes.AttendanceConfigurationInvalid, exception.ErrorCode);
        Assert.Empty(context.SystemSettings);
        repository.Verify(item => item.UpdateAsync(It.IsAny<SystemSetting>()), Times.Never);
    }

    [Fact]
    public async Task Listing_settings_never_seeds_missing_required_configuration()
    {
        await using var context = CreateContext();
        var repository = new Mock<ISystemSettingRepository>();
        repository.Setup(item => item.GetAllAsync())
            .ReturnsAsync(Array.Empty<SystemSetting>());
        var service = CreateService(context, repository);

        var settings = await service.GetAllSettingsAsync();

        Assert.Empty(settings);
        Assert.Empty(context.SystemSettings);
        repository.Verify(item => item.UpdateAsync(It.IsAny<SystemSetting>()), Times.Never);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("169")]
    [InlineData(" 16")]
    [InlineData("+16")]
    [InlineData("1.5")]
    [InlineData("invalid")]
    public async Task Invalid_stale_session_mutation_is_rejected(string value)
    {
        await using var context = CreateContext();
        var repository = CreateRepository(new SystemSetting
        {
            SettingKey = SystemSettingService.StaleSessionHoursKey,
            SettingValue = "16"
        });
        var service = CreateService(context, repository);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.UpdateSettingAsync(SystemSettingService.StaleSessionHoursKey, value));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(ErrorCodes.AttendanceConfigurationInvalid, exception.ErrorCode);
        repository.Verify(item => item.UpdateAsync(It.IsAny<SystemSetting>()), Times.Never);
    }

    [Fact]
    public async Task Invalid_stored_stale_session_value_fails_closed_instead_of_using_default()
    {
        await using var context = CreateContext();
        var invalidSetting = new SystemSetting
        {
            SettingKey = SystemSettingService.StaleSessionHoursKey,
            SettingValue = "not-an-integer"
        };
        context.SystemSettings.Add(invalidSetting);
        await context.SaveChangesAsync();
        var repository = CreateRepository(invalidSetting);
        var service = CreateService(context, repository);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.GetValueIntAsync(SystemSettingService.StaleSessionHoursKey, 16));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal(ErrorCodes.AttendanceConfigurationInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Timezone_change_is_rejected_after_first_attendance_row()
    {
        await using var context = CreateContext();
        context.AttendanceLogs.Add(new Attendance
        {
            AttendanceID = 1,
            MemberID = 10,
            AttendanceDate = new DateOnly(2026, 7, 12),
            CheckInTime = new DateTime(2026, 7, 12, 1, 0, 0, DateTimeKind.Utc),
            LastModified = new DateTime(2026, 7, 12, 1, 0, 0, DateTimeKind.Utc),
            Source = Attendance.StaffQrSource,
            ActorUserID = 1
        });
        context.SystemSettings.Add(new SystemSetting
        {
            SettingKey = TimezoneService.TimezoneSettingKey,
            SettingValue = "Asia/Manila"
        });
        await context.SaveChangesAsync();
        var repository = CreateRepository(new SystemSetting
        {
            SettingKey = TimezoneService.TimezoneSettingKey,
            SettingValue = "Asia/Manila"
        });
        var timezone = new Mock<ITimezoneService>();
        var service = CreateService(context, repository, timezone);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.UpdateSettingAsync(TimezoneService.TimezoneSettingKey, "UTC"));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        repository.Verify(item => item.UpdateAsync(It.IsAny<SystemSetting>()), Times.Never);
        timezone.Verify(item => item.InvalidateCache(), Times.Never);
    }

    [Fact]
    public async Task Valid_timezone_is_saved_then_cache_is_invalidated_without_logging_values()
    {
        await using var context = CreateContext();
        var setting = new SystemSetting
        {
            SettingKey = TimezoneService.TimezoneSettingKey,
            SettingValue = "Asia/Manila"
        };
        context.SystemSettings.Add(setting);
        await context.SaveChangesAsync();
        var repository = CreateRepository(setting);
        var timezone = new Mock<ITimezoneService>();
        var service = CreateService(context, repository, timezone);

        await service.UpdateSettingAsync(TimezoneService.TimezoneSettingKey, "UTC");

        var persisted = await context.SystemSettings.AsNoTracking().SingleAsync(item =>
            item.SettingKey == TimezoneService.TimezoneSettingKey);
        Assert.Equal(TimeZoneInfo.Utc.Id, persisted.SettingValue);
        repository.Verify(item => item.UpdateAsync(It.IsAny<SystemSetting>()), Times.Never);
        timezone.Verify(item => item.InvalidateCache(), Times.Once);
        var auditEntry = await context.AuditLogs.SingleAsync();
        Assert.DoesNotContain("Asia/Manila", auditEntry.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("UTC", auditEntry.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timezone_update_locks_setting_before_attendance_range()
    {
        await using var context = CreateContext();
        var setting = new SystemSetting
        {
            SettingKey = TimezoneService.TimezoneSettingKey,
            SettingValue = "Asia/Manila"
        };
        context.SystemSettings.Add(setting);
        await context.SaveChangesAsync();
        var repository = CreateRepository(setting);
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.SetupGet(item => item.UserId).Returns(1);
        var service = new LockOrderSystemSettingService(
            repository.Object,
            new Mock<IAuditService>().Object,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            currentUser.Object,
            context,
            new Mock<ITimezoneService>().Object);

        await service.UpdateSettingAsync(TimezoneService.TimezoneSettingKey, "UTC");

        Assert.Equal(new[] { "TimezoneSetting", "AttendanceLogs" }, service.LockOrder);
    }

    [Fact]
    public async Task Provider_neutral_audit_save_failure_does_not_commit_timezone_change()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        await using (var seed = new GymDbContext(options))
        {
            seed.SystemSettings.Add(new SystemSetting
            {
                SettingKey = TimezoneService.TimezoneSettingKey,
                SettingValue = "Asia/Manila"
            });
            await seed.SaveChangesAsync();
        }

        await using (var failing = new ThrowingSettingsDbContext(options))
        {
            failing.ThrowOnSave = true;
            var timezone = new Mock<ITimezoneService>();
            var service = CreateService(
                failing,
                new Mock<ISystemSettingRepository>(),
                timezone);

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.UpdateSettingAsync(TimezoneService.TimezoneSettingKey, "UTC"));
            timezone.Verify(item => item.InvalidateCache(), Times.Never);
        }

        await using var verify = new GymDbContext(options);
        var persisted = await verify.SystemSettings.AsNoTracking().SingleAsync();
        Assert.Equal("Asia/Manila", persisted.SettingValue);
        Assert.Empty(verify.AuditLogs);
    }

    private static GymDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GymDbContext(options);
    }

    private static Mock<ISystemSettingRepository> CreateRepository(SystemSetting setting)
    {
        var repository = new Mock<ISystemSettingRepository>();
        repository.Setup(item => item.GetByKeyAsync(setting.SettingKey)).ReturnsAsync(setting);
        repository.Setup(item => item.UpdateAsync(It.IsAny<SystemSetting>())).Returns(Task.CompletedTask);
        return repository;
    }

    private static SystemSettingService CreateService(
        GymDbContext context,
        Mock<ISystemSettingRepository> repository,
        Mock<ITimezoneService>? timezone = null,
        Mock<IAuditService>? audit = null)
    {
        timezone ??= new Mock<ITimezoneService>();
        if (audit is null)
        {
            audit = new Mock<IAuditService>();
            audit.Setup(item => item.LogActivityAsync(
                    It.IsAny<int?>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(Task.CompletedTask);
        }
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.SetupGet(item => item.UserId).Returns(1);
        return new SystemSettingService(
            repository.Object,
            audit.Object,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            currentUser.Object,
            context,
            timezone.Object);
    }

    private sealed class ThrowingSettingsDbContext : GymDbContext
    {
        public ThrowingSettingsDbContext(DbContextOptions<GymDbContext> options)
            : base(options)
        {
        }

        public bool ThrowOnSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new DbUpdateException(
                    "Simulated atomic setting/audit failure.",
                    new TimeoutException("Database unavailable."));
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class LockOrderSystemSettingService : SystemSettingService
    {
        private readonly GymDbContext _context;

        public LockOrderSystemSettingService(
            ISystemSettingRepository repository,
            IAuditService auditService,
            IHttpContextAccessor httpContextAccessor,
            ICurrentUserContext currentUser,
            GymDbContext context,
            ITimezoneService timezoneService)
            : base(
                repository,
                auditService,
                httpContextAccessor,
                currentUser,
                context,
                timezoneService)
        {
            _context = context;
        }

        public List<string> LockOrder { get; } = new();

        protected override Task<SystemSetting?> GetSettingForUpdateAsync(
            string key,
            CancellationToken cancellationToken)
        {
            LockOrder.Add("TimezoneSetting");
            return _context.SystemSettings.SingleOrDefaultAsync(
                setting => setting.SettingKey == key,
                cancellationToken);
        }

        protected override Task<bool> HasAttendanceForUpdateAsync(
            CancellationToken cancellationToken)
        {
            LockOrder.Add("AttendanceLogs");
            return Task.FromResult(false);
        }
    }
}
