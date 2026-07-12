using System.Net;
using System.Text.Json;
using GymTrackPro.API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class PreAuthenticationRateLimitMiddlewareTests
{
    [Fact]
    public void Address_partitions_and_memory_are_bounded_and_reset_each_window()
    {
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero));
        var limiter = new PreAuthenticationIpLimiter(
            maximumTrackedAddresses: 2,
            permitLimit: 2,
            window: TimeSpan.FromMinutes(1),
            time);

        Assert.Equal(PreAuthenticationAdmissionResult.Acquired,
            limiter.TryAcquire(IPAddress.Parse("192.0.2.1")));
        Assert.Equal(PreAuthenticationAdmissionResult.Acquired,
            limiter.TryAcquire(IPAddress.Parse("192.0.2.1")));
        Assert.Equal(PreAuthenticationAdmissionResult.PerIpLimitExceeded,
            limiter.TryAcquire(IPAddress.Parse("192.0.2.1")));
        Assert.Equal(PreAuthenticationAdmissionResult.Acquired,
            limiter.TryAcquire(IPAddress.Parse("192.0.2.2")));
        Assert.Equal(PreAuthenticationAdmissionResult.CapacityExceeded,
            limiter.TryAcquire(IPAddress.Parse("192.0.2.3")));

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(PreAuthenticationAdmissionResult.Acquired,
            limiter.TryAcquire(IPAddress.Parse("192.0.2.3")));
        Assert.Equal(2, limiter.MaximumTrackedAddresses);
    }

    [Fact]
    public async Task Rejection_is_generic_and_supplies_retry_after_without_invoking_authentication_path()
    {
        var limiter = new PreAuthenticationIpLimiter(
            maximumTrackedAddresses: 1,
            permitLimit: 1,
            window: TimeSpan.FromMinutes(1),
            TimeProvider.System);
        var downstreamCalls = 0;
        var logger = new RecordingLogger<PreAuthenticationRateLimitMiddleware>();
        var middleware = new PreAuthenticationRateLimitMiddleware(
            _ =>
            {
                downstreamCalls++;
                return Task.CompletedTask;
            },
            limiter,
            logger);
        var first = CreateContext();
        var second = CreateContext();

        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(second);

        Assert.Equal(1, downstreamCalls);
        Assert.Equal(StatusCodes.Status429TooManyRequests, second.Response.StatusCode);
        Assert.Equal("60", second.Response.Headers.RetryAfter);
        second.Response.Body.Position = 0;
        using var reader = new StreamReader(second.Response.Body);
        var response = await reader.ReadToEndAsync();
        Assert.DoesNotContain("192.0.2.10", response, StringComparison.Ordinal);
        var log = Assert.Single(logger.Entries);
        Assert.Contains("IP_PER_ADDRESS_LIMIT_EXCEEDED", log, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.10", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capacity_rejection_has_distinct_secret_free_category()
    {
        var limiter = new PreAuthenticationIpLimiter(
            maximumTrackedAddresses: 1,
            permitLimit: 10,
            window: TimeSpan.FromMinutes(1),
            TimeProvider.System);
        var logger = new RecordingLogger<PreAuthenticationRateLimitMiddleware>();
        var middleware = new PreAuthenticationRateLimitMiddleware(
            _ => Task.CompletedTask,
            limiter,
            logger);
        var admitted = CreateContext();
        var rejected = CreateContext();
        rejected.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.11");

        await middleware.InvokeAsync(admitted);
        await middleware.InvokeAsync(rejected);

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        var log = Assert.Single(logger.Entries);
        Assert.Contains("IP_CAPACITY_EXCEEDED", log, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.10", log, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.11", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_has_conservative_validated_bounds()
    {
        var validator = new PreAuthenticationRateLimitOptionsValidator();
        Assert.True(validator.Validate(null, new PreAuthenticationRateLimitOptions()).Succeeded);

        Assert.True(validator.Validate(null, new PreAuthenticationRateLimitOptions
        {
            MaximumTrackedAddresses = 255
        }).Failed);
        Assert.True(validator.Validate(null, new PreAuthenticationRateLimitOptions
        {
            PermitLimit = 2001
        }).Failed);
        Assert.True(validator.Validate(null, new PreAuthenticationRateLimitOptions
        {
            WindowSeconds = 301
        }).Failed);
    }

    [Fact]
    public void Production_example_declares_a_valid_bounded_pre_auth_configuration()
    {
        var path = Path.Combine(
            TestWorkspace.FindRoot(),
            "src",
            "GymTrackPro.API",
            "appsettings.Production.example.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var section = document.RootElement.GetProperty(
            PreAuthenticationRateLimitOptions.SectionName);
        var options = JsonSerializer.Deserialize<PreAuthenticationRateLimitOptions>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(options);
        Assert.True(new PreAuthenticationRateLimitOptionsValidator()
            .Validate(null, options!)
            .Succeeded);
        var plan = File.ReadAllText(Path.Combine(
            TestWorkspace.FindRoot(),
            "docs",
            "22_GymGoerExperienceAndAnalytics_ImplementationPlan.md"));
        Assert.Contains("provider/edge throttling", plan, StringComparison.OrdinalIgnoreCase);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow += value;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception));
    }
}
