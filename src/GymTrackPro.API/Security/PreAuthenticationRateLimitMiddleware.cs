using System.Net;
using Microsoft.Extensions.Options;

namespace GymTrackPro.API.Security;

public sealed class PreAuthenticationRateLimitOptions
{
    public const string SectionName = "PreAuthenticationRateLimit";

    public int MaximumTrackedAddresses { get; set; } =
        PreAuthenticationIpLimiter.DefaultMaximumTrackedAddresses;

    public int PermitLimit { get; set; } = PreAuthenticationIpLimiter.DefaultPermitLimit;

    public int WindowSeconds { get; set; } = 60;
}

public sealed class PreAuthenticationRateLimitOptionsValidator
    : IValidateOptions<PreAuthenticationRateLimitOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        PreAuthenticationRateLimitOptions options)
    {
        if (options.MaximumTrackedAddresses is < 256 or > 65536)
        {
            return ValidateOptionsResult.Fail(
                "PreAuthenticationRateLimit:MaximumTrackedAddresses must be between 256 and 65536.");
        }

        if (options.PermitLimit is < 10 or > 2000)
        {
            return ValidateOptionsResult.Fail(
                "PreAuthenticationRateLimit:PermitLimit must be between 10 and 2000.");
        }

        if (options.WindowSeconds is < 30 or > 300)
        {
            return ValidateOptionsResult.Fail(
                "PreAuthenticationRateLimit:WindowSeconds must be between 30 and 300.");
        }

        return ValidateOptionsResult.Success;
    }
}

public enum PreAuthenticationAdmissionResult
{
    Acquired,
    PerIpLimitExceeded,
    CapacityExceeded
}

/// <summary>
/// A fixed-capacity, per-IP admission limiter that runs before bearer-token processing.
/// Entries are discarded at the window boundary, so attacker-controlled addresses cannot
/// create an unbounded in-process partition dictionary.
/// </summary>
public sealed class PreAuthenticationIpLimiter
{
    public const int DefaultMaximumTrackedAddresses = 4096;
    public const int DefaultPermitLimit = 120;

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly int _maximumTrackedAddresses;
    private readonly int _permitLimit;
    private readonly long _windowTicks;
    private readonly TimeProvider _timeProvider;
    private long _windowId = long.MinValue;

    public PreAuthenticationIpLimiter()
        : this(
            DefaultMaximumTrackedAddresses,
            DefaultPermitLimit,
            TimeSpan.FromMinutes(1),
            TimeProvider.System)
    {
    }

    public PreAuthenticationIpLimiter(IOptions<PreAuthenticationRateLimitOptions> options)
        : this(
            options.Value.MaximumTrackedAddresses,
            options.Value.PermitLimit,
            TimeSpan.FromSeconds(options.Value.WindowSeconds),
            TimeProvider.System)
    {
    }

    public PreAuthenticationIpLimiter(
        int maximumTrackedAddresses,
        int permitLimit,
        TimeSpan window,
        TimeProvider timeProvider)
    {
        if (maximumTrackedAddresses < 1 || permitLimit < 1 || window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTrackedAddresses));
        }

        _maximumTrackedAddresses = maximumTrackedAddresses;
        _permitLimit = permitLimit;
        _windowTicks = window.Ticks;
        _timeProvider = timeProvider;
    }

    public int MaximumTrackedAddresses => _maximumTrackedAddresses;
    public int RetryAfterSeconds => checked((int)TimeSpan.FromTicks(_windowTicks).TotalSeconds);

    public PreAuthenticationAdmissionResult TryAcquire(IPAddress? remoteAddress)
    {
        var key = Normalize(remoteAddress);
        var windowId = _timeProvider.GetUtcNow().UtcTicks / _windowTicks;
        lock (_gate)
        {
            if (_windowId != windowId)
            {
                _windowId = windowId;
                _counts.Clear();
            }

            if (!_counts.TryGetValue(key, out var count))
            {
                if (_counts.Count >= _maximumTrackedAddresses)
                {
                    // Fail closed instead of allocating an attacker-controlled partition.
                    return PreAuthenticationAdmissionResult.CapacityExceeded;
                }

                _counts.Add(key, 1);
                return PreAuthenticationAdmissionResult.Acquired;
            }

            if (count >= _permitLimit)
            {
                return PreAuthenticationAdmissionResult.PerIpLimitExceeded;
            }

            _counts[key] = count + 1;
            return PreAuthenticationAdmissionResult.Acquired;
        }
    }

    private static string Normalize(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        return address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : address.ToString();
    }
}

public sealed class PreAuthenticationRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PreAuthenticationIpLimiter _limiter;
    private readonly ILogger<PreAuthenticationRateLimitMiddleware> _logger;

    public PreAuthenticationRateLimitMiddleware(
        RequestDelegate next,
        PreAuthenticationIpLimiter limiter,
        ILogger<PreAuthenticationRateLimitMiddleware> logger)
    {
        _next = next;
        _limiter = limiter;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var admission = _limiter.TryAcquire(context.Connection.RemoteIpAddress);
        if (admission != PreAuthenticationAdmissionResult.Acquired)
        {
            _logger.LogWarning(
                "Pre-authentication admission rejected. ReasonCategory: {ReasonCategory}; CorrelationId: {CorrelationId}",
                admission == PreAuthenticationAdmissionResult.CapacityExceeded
                    ? "IP_CAPACITY_EXCEEDED"
                    : "IP_PER_ADDRESS_LIMIT_EXCEEDED",
                context.TraceIdentifier);
            await RateLimitResponsePayload.WriteAsync(
                context,
                _limiter.RetryAfterSeconds,
                context.RequestAborted);
            return;
        }

        await _next(context);
    }
}
