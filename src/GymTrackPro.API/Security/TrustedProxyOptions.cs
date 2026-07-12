using System.Net;
using Microsoft.Extensions.Options;

namespace GymTrackPro.API.Security;

public sealed class TrustedProxyOptions
{
    public const string SectionName = "TrustedProxy";

    public bool Enabled { get; set; }
    public int ForwardLimit { get; set; } = 1;
    public string[] KnownProxies { get; set; } = Array.Empty<string>();
}

public sealed class TrustedProxyOptionsValidator : IValidateOptions<TrustedProxyOptions>
{
    private readonly IHostEnvironment _environment;

    public TrustedProxyOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, TrustedProxyOptions options)
    {
        if (!options.Enabled)
        {
            return _environment.IsProduction()
                ? ValidateOptionsResult.Fail(
                    "TrustedProxy must be enabled with exact MonsterASP proxy IP addresses in production.")
                : ValidateOptionsResult.Success;
        }

        if (options.ForwardLimit is < 1 or > 2)
        {
            return ValidateOptionsResult.Fail("TrustedProxy:ForwardLimit must be 1 or 2.");
        }

        if (options.KnownProxies.Length == 0)
        {
            return ValidateOptionsResult.Fail(
                "TrustedProxy is enabled but no exact proxy IP addresses are configured.");
        }

        return options.KnownProxies.All(value => IPAddress.TryParse(value, out _))
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("TrustedProxy:KnownProxies contains an invalid IP address.");
    }
}

public static class RequestRateLimitPartition
{
    public static string Create(HttpContext context)
    {
        var uid = context.User.FindFirst(Authentication.FirebaseClaimTypes.Subject)?.Value;
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return string.IsNullOrWhiteSpace(uid)
            ? $"ip:{address}"
            : $"uid:{uid}:ip:{address}";
    }
}
