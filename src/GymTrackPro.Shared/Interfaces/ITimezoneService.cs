namespace GymTrackPro.Shared.Interfaces;

public readonly record struct UtcDateRange(DateTime StartUtc, DateTime EndExclusiveUtc);

public interface ITimezoneService
{
    Task<TimeZoneInfo> GetGymTimeZoneAsync(CancellationToken cancellationToken = default);
    Task<TimeZoneInfo> GetGymTimeZoneAsync(
        string authoritativeTimeZoneId,
        CancellationToken cancellationToken = default);
    Task<DateTime> ConvertToGymTimeAsync(DateTime utcDateTime, CancellationToken cancellationToken = default);
    Task<DateOnly> GetGymDateAsync(DateTime utcDateTime, CancellationToken cancellationToken = default);
    Task<DateOnly> GetGymDateAsync(
        DateTime utcDateTime,
        string authoritativeTimeZoneId,
        CancellationToken cancellationToken = default);
    Task<DateTime> ConvertGymDateToUtcStartAsync(DateOnly gymDate, CancellationToken cancellationToken = default);
    Task<UtcDateRange> GetUtcRangeForGymDateAsync(DateOnly gymDate, CancellationToken cancellationToken = default);
    Task<UtcDateRange> GetUtcRangeForGymDateRangeAsync(
        DateOnly startGymDate,
        DateOnly endExclusiveGymDate,
        CancellationToken cancellationToken = default);
    Task<UtcDateRange> GetUtcRangeForGymDateRangeAsync(
        DateOnly startGymDate,
        DateOnly endExclusiveGymDate,
        string authoritativeTimeZoneId,
        CancellationToken cancellationToken = default);
    void InvalidateCache();
}
