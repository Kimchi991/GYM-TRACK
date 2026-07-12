using GymTrackPro.API.Authentication;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class TimezoneService : ITimezoneService
{
    public const string TimezoneSettingKey = "Timezone";
    public const string DefaultTimezoneId = "Asia/Manila";

    private readonly ISystemSettingRepository _settingRepository;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private TimeZoneInfo? _cachedTimeZone;

    public TimezoneService(ISystemSettingRepository settingRepository)
    {
        _settingRepository = settingRepository;
    }

    public async Task<TimeZoneInfo> GetGymTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        var cached = Volatile.Read(ref _cachedTimeZone);
        if (cached is not null)
        {
            return cached;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            cached = _cachedTimeZone;
            if (cached is not null)
            {
                return cached;
            }

            var setting = await _settingRepository.GetByKeyAsync(TimezoneSettingKey);
            if (setting is null || string.IsNullOrWhiteSpace(setting.SettingValue))
            {
                throw InvalidTimezoneConfiguration();
            }

            var configuredId = setting.SettingValue;

            try
            {
                cached = FindTimeZoneById(configuredId.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                throw InvalidTimezoneConfiguration();
            }
            catch (InvalidTimeZoneException)
            {
                throw InvalidTimezoneConfiguration();
            }
            catch (ArgumentException)
            {
                throw InvalidTimezoneConfiguration();
            }

            Volatile.Write(ref _cachedTimeZone, cached);
            return cached;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public Task<TimeZoneInfo> GetGymTimeZoneAsync(
        string authoritativeTimeZoneId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ResolveTimeZone(authoritativeTimeZoneId));
    }

    public async Task<DateTime> ConvertToGymTimeAsync(
        DateTime utcDateTime,
        CancellationToken cancellationToken = default)
    {
        ValidateUtc(utcDateTime, nameof(utcDateTime));
        var timeZone = await GetGymTimeZoneAsync(cancellationToken);
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, timeZone);
    }

    public async Task<DateOnly> GetGymDateAsync(
        DateTime utcDateTime,
        CancellationToken cancellationToken = default)
    {
        var gymTime = await ConvertToGymTimeAsync(utcDateTime, cancellationToken);
        return DateOnly.FromDateTime(gymTime);
    }

    public Task<DateOnly> GetGymDateAsync(
        DateTime utcDateTime,
        string authoritativeTimeZoneId,
        CancellationToken cancellationToken = default)
    {
        ValidateUtc(utcDateTime, nameof(utcDateTime));
        cancellationToken.ThrowIfCancellationRequested();
        var timeZone = ResolveTimeZone(authoritativeTimeZoneId);
        var gymTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, timeZone);
        return Task.FromResult(DateOnly.FromDateTime(gymTime));
    }

    public async Task<DateTime> ConvertGymDateToUtcStartAsync(
        DateOnly gymDate,
        CancellationToken cancellationToken = default)
    {
        var timeZone = await GetGymTimeZoneAsync(cancellationToken);
        var localMidnight = DateTime.SpecifyKind(
            gymDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(localMidnight))
        {
            throw InvalidTimezoneConfiguration();
        }

        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, timeZone);
    }

    public async Task<UtcDateRange> GetUtcRangeForGymDateAsync(
        DateOnly gymDate,
        CancellationToken cancellationToken = default)
    {
        return await GetUtcRangeForGymDateRangeAsync(
            gymDate,
            gymDate.AddDays(1),
            cancellationToken);
    }

    public async Task<UtcDateRange> GetUtcRangeForGymDateRangeAsync(
        DateOnly startGymDate,
        DateOnly endExclusiveGymDate,
        CancellationToken cancellationToken = default)
    {
        if (endExclusiveGymDate <= startGymDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endExclusiveGymDate),
                "The end-exclusive gym date must be after the start date.");
        }

        var startUtc = await ConvertGymDateToUtcStartAsync(startGymDate, cancellationToken);
        var endExclusiveUtc = await ConvertGymDateToUtcStartAsync(endExclusiveGymDate, cancellationToken);
        return new UtcDateRange(startUtc, endExclusiveUtc);
    }

    public Task<UtcDateRange> GetUtcRangeForGymDateRangeAsync(
        DateOnly startGymDate,
        DateOnly endExclusiveGymDate,
        string authoritativeTimeZoneId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (endExclusiveGymDate <= startGymDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endExclusiveGymDate),
                "The end-exclusive gym date must be after the start date.");
        }

        var timeZone = ResolveTimeZone(authoritativeTimeZoneId);
        var startUtc = ConvertGymDateToUtcStart(startGymDate, timeZone);
        var endExclusiveUtc = ConvertGymDateToUtcStart(endExclusiveGymDate, timeZone);
        return Task.FromResult(new UtcDateRange(startUtc, endExclusiveUtc));
    }

    public void InvalidateCache()
    {
        Volatile.Write(ref _cachedTimeZone, null);
    }

    protected virtual TimeZoneInfo FindTimeZoneById(string timeZoneId)
    {
        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    private TimeZoneInfo ResolveTimeZone(string authoritativeTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(authoritativeTimeZoneId))
        {
            throw InvalidTimezoneConfiguration();
        }

        try
        {
            return FindTimeZoneById(authoritativeTimeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw InvalidTimezoneConfiguration();
        }
        catch (InvalidTimeZoneException)
        {
            throw InvalidTimezoneConfiguration();
        }
        catch (ArgumentException)
        {
            throw InvalidTimezoneConfiguration();
        }
    }

    private static DateTime ConvertGymDateToUtcStart(DateOnly gymDate, TimeZoneInfo timeZone)
    {
        var localMidnight = DateTime.SpecifyKind(
            gymDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localMidnight))
        {
            throw InvalidTimezoneConfiguration();
        }

        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, timeZone);
    }

    public static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A UTC DateTime value is required.", parameterName);
        }
    }

    private static AppAccessException InvalidTimezoneConfiguration()
    {
        return new AppAccessException(
            StatusCodes.Status503ServiceUnavailable,
            ErrorCodes.GymTimezoneInvalid,
            "The gym timezone configuration is unavailable.");
    }
}
