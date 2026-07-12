using GymTrackPro.API.Middleware;
using GymTrackPro.API.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Production_500_contains_correlation_id_but_not_exception_message()
    {
        const string sensitiveMessage = "sensitive database detail";
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "correlation-test-123";
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException(sensitiveMessage),
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            new TestEnvironment { EnvironmentName = Environments.Production });

        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("correlation-test-123", context.Response.Headers["X-Correlation-ID"]);
        Assert.Contains("correlation-test-123", responseBody);
        Assert.DoesNotContain(sensitiveMessage, responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expected_access_denial_logs_only_controlled_category_and_correlation()
    {
        const string sensitiveMessage = "invite-code-or-email-must-never-enter-telemetry";
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "correlation-access-123"
        };
        context.Response.Body = new MemoryStream();
        var logger = new RecordingLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                "INVITE_INVALID",
                sensitiveMessage),
            logger,
            new TestEnvironment { EnvironmentName = Environments.Production });

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("INVITE_INVALID", entry.Message, StringComparison.Ordinal);
        Assert.Contains("correlation-access-123", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveMessage, entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GymTrackPro.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
