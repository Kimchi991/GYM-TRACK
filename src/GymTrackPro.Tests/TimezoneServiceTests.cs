using GymTrackPro.API.Authentication;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;
using Moq;
using Microsoft.AspNetCore.Http;

namespace GymTrackPro.Tests;

public class TimezoneServiceTests
{
    [Theory]
    [InlineData(2026, 1, 1, 15, 59, 59, 2026, 1, 1)]
    [InlineData(2026, 1, 1, 16, 0, 0, 2026, 1, 2)]
    [InlineData(2024, 2, 28, 16, 0, 0, 2024, 2, 29)]
    [InlineData(2024, 2, 29, 16, 0, 0, 2024, 3, 1)]
    [InlineData(2025, 12, 31, 16, 0, 0, 2026, 1, 1)]
    public async Task Manila_boundaries_produce_authoritative_local_date(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var service = CreateService("Asia/Manila");
        var utc = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);

        var result = await service.GetGymDateAsync(utc);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result);
    }

    [Fact]
    public async Task Manila_date_range_is_half_open_at_1600_utc()
    {
        var service = CreateService("Asia/Manila");

        var range = await service.GetUtcRangeForGymDateAsync(new DateOnly(2026, 7, 12));

        Assert.Equal(new DateTime(2026, 7, 11, 16, 0, 0, DateTimeKind.Utc), range.StartUtc);
        Assert.Equal(new DateTime(2026, 7, 12, 16, 0, 0, DateTimeKind.Utc), range.EndExclusiveUtc);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public async Task Non_utc_inputs_are_rejected(DateTimeKind kind)
    {
        var service = CreateService("Asia/Manila");
        var value = DateTime.SpecifyKind(new DateTime(2026, 7, 12, 0, 0, 0), kind);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetGymDateAsync(value));
    }

    [Fact]
    public async Task Missing_timezone_setting_fails_closed()
    {
        var repository = new Mock<ISystemSettingRepository>();
        repository.Setup(item => item.GetByKeyAsync(TimezoneService.TimezoneSettingKey))
            .ReturnsAsync((SystemSetting?)null);
        var service = new TimezoneService(repository.Object);

        var exception = await Assert.ThrowsAsync<AppAccessException>(
            () => service.GetGymTimeZoneAsync());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal(ErrorCodes.GymTimezoneInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Blank_timezone_setting_fails_closed()
    {
        var service = CreateService("   ");

        var exception = await Assert.ThrowsAsync<AppAccessException>(
            () => service.GetGymTimeZoneAsync());

        Assert.Equal(ErrorCodes.GymTimezoneInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Unknown_timezone_setting_fails_closed()
    {
        var service = CreateService("Etc/Definitely-Missing-GymTrackPro");

        var exception = await Assert.ThrowsAsync<AppAccessException>(
            () => service.GetGymTimeZoneAsync());

        Assert.Equal(ErrorCodes.GymTimezoneInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Invalid_timezone_data_fails_closed()
    {
        var repository = CreateRepository("Asia/Manila");
        var service = new InvalidResolverTimezoneService(repository.Object);

        var exception = await Assert.ThrowsAsync<AppAccessException>(
            () => service.GetGymTimeZoneAsync());

        Assert.Equal(ErrorCodes.GymTimezoneInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Cache_is_stable_until_explicit_invalidation()
    {
        var configuredId = "Asia/Manila";
        var repository = new Mock<ISystemSettingRepository>();
        repository.Setup(item => item.GetByKeyAsync(TimezoneService.TimezoneSettingKey))
            .ReturnsAsync(() => new SystemSetting
            {
                SettingKey = TimezoneService.TimezoneSettingKey,
                SettingValue = configuredId
            });
        var service = new TimezoneService(repository.Object);

        var first = await service.GetGymTimeZoneAsync();
        configuredId = "UTC";
        var stillCached = await service.GetGymTimeZoneAsync();
        service.InvalidateCache();
        var refreshed = await service.GetGymTimeZoneAsync();

        Assert.Equal(first.Id, stillCached.Id);
        Assert.Equal("UTC", refreshed.Id);
        repository.Verify(
            item => item.GetByKeyAsync(TimezoneService.TimezoneSettingKey),
            Times.Exactly(2));
    }

    private static TimezoneService CreateService(string value)
    {
        return new TimezoneService(CreateRepository(value).Object);
    }

    private static Mock<ISystemSettingRepository> CreateRepository(string value)
    {
        var repository = new Mock<ISystemSettingRepository>();
        repository.Setup(item => item.GetByKeyAsync(TimezoneService.TimezoneSettingKey))
            .ReturnsAsync(new SystemSetting
            {
                SettingKey = TimezoneService.TimezoneSettingKey,
                SettingValue = value
            });
        return repository;
    }

    private sealed class InvalidResolverTimezoneService : TimezoneService
    {
        public InvalidResolverTimezoneService(ISystemSettingRepository repository)
            : base(repository)
        {
        }

        protected override TimeZoneInfo FindTimeZoneById(string timeZoneId)
        {
            throw new InvalidTimeZoneException("Invalid test timezone data.");
        }
    }
}
