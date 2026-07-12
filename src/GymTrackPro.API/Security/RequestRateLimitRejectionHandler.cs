using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace GymTrackPro.API.Security;

public static class RateLimitResponsePayload
{
    public const string Json =
        "{\"success\":false,\"message\":\"Too many requests.\",\"errorCode\":\"RATE_LIMITED\"}";

    public static async Task WriteAsync(
        HttpContext context,
        int retryAfterSeconds,
        CancellationToken cancellationToken)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        context.Response.Headers.RetryAfter = Math.Max(1, retryAfterSeconds).ToString(
            CultureInfo.InvariantCulture);
        await context.Response.WriteAsync(Json, cancellationToken);
    }
}

public static class RequestRateLimitRejectionHandler
{
    public static async ValueTask HandleAsync(
        OnRejectedContext rejectionContext,
        CancellationToken cancellationToken)
    {
        var httpContext = rejectionContext.HttpContext;
        var configuredPolicy = httpContext.GetEndpoint()?
            .Metadata.GetMetadata<EnableRateLimitingAttribute>()?
            .PolicyName;
        var policyCategory = configuredPolicy is "Auth" or "Activation"
            ? configuredPolicy
            : "Global";
        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RequestRateLimiting");
        logger.LogWarning(
            "Request rate limit rejected. PolicyCategory: {PolicyCategory}; CorrelationId: {CorrelationId}",
            policyCategory,
            httpContext.TraceIdentifier);

        var retryAfterSeconds = rejectionContext.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;
        await RateLimitResponsePayload.WriteAsync(
            httpContext,
            retryAfterSeconds,
            cancellationToken);
    }
}
