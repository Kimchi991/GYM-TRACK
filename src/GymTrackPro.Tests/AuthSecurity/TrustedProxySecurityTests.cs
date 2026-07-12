using System.Net;
using GymTrackPro.API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class TrustedProxySecurityTests
{
    [Fact]
    public void Trusted_forwarding_cannot_be_enabled_without_exact_proxy_addresses()
    {
        var validator = new TrustedProxyOptionsValidator(CreateEnvironment("Production"));

        var result = validator.Validate(null, new TrustedProxyOptions { Enabled = true });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Production_fails_closed_when_trusted_proxy_configuration_is_disabled()
    {
        var validator = new TrustedProxyOptionsValidator(CreateEnvironment("Production"));

        var result = validator.Validate(null, new TrustedProxyOptions { Enabled = false });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Development_can_run_without_forwarded_headers()
    {
        var validator = new TrustedProxyOptionsValidator(CreateEnvironment("Development"));

        var result = validator.Validate(null, new TrustedProxyOptions { Enabled = false });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Unprocessed_spoofed_forwarded_header_does_not_change_rate_limit_partition()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.77";

        var partition = RequestRateLimitPartition.Create(context);

        Assert.Equal("ip:192.0.2.10", partition);
    }

    [Fact]
    public void Production_allowed_hosts_accepts_only_explicit_well_formed_entries()
    {
        Assert.True(ProductionAllowedHosts.IsValid("api.example.com; admin.example.com"));
        Assert.True(ProductionAllowedHosts.IsValid("[2001:db8::10]"));

        foreach (var invalid in new[]
                 {
                     "*",
                     "*;api.example.com",
                     "api.example.com;",
                     ";api.example.com",
                     "api.example.com;;admin.example.com",
                     "localhost",
                     "api.localhost",
                     "127.0.0.1",
                     "[::1]",
                     "[::]",
                     "[::ffff:127.0.0.1]",
                     "[::ffff:0.0.0.0]",
                     "https://api.example.com",
                     "api.example.com/path",
                     "api.example.com:443"
                 })
        {
            Assert.False(ProductionAllowedHosts.IsValid(invalid), invalid);
        }
    }

    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName);
        return environment.Object;
    }
}
