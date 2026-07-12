using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile.Tests;

public sealed class ApiEndpointConfigurationTests
{
    [Fact]
    public void Production_accepts_exact_https_host_and_path()
    {
        var configuration = ApiEndpointConfiguration.Create(
            "https://api.example.com/api/v1/",
            "api.example.com",
            isProduction: true);

        Assert.Equal("https://api.example.com/api/v1/", configuration.BaseUri.AbsoluteUri);
        Assert.True(configuration.IsProduction);
    }

    [Theory]
    [InlineData(null, "api.example.com")]
    [InlineData("http://api.example.com/api/v1/", "api.example.com")]
    [InlineData("https://localhost/api/v1/", "localhost")]
    [InlineData("https://127.0.0.1/api/v1/", "127.0.0.1")]
    [InlineData("https://api.example.com:8443/api/v1/", "api.example.com")]
    [InlineData("https://api.example.com/api/v2/", "api.example.com")]
    [InlineData("https://api.example.com/api/v1/?next=evil", "api.example.com")]
    [InlineData("https://evil.example/api/v1/", "api.example.com")]
    public void Production_rejects_unsafe_or_unexpected_endpoint(string? url, string expectedHost)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ApiEndpointConfiguration.Create(url, expectedHost, isProduction: true));
    }

    [Fact]
    public void Debug_http_is_limited_to_development_hosts()
    {
        var android = ApiEndpointConfiguration.Create(
            "http://10.0.2.2:5221/api/v1/",
            "10.0.2.2",
            isProduction: false);

        Assert.False(android.IsProduction);
        Assert.Throws<InvalidOperationException>(() =>
            ApiEndpointConfiguration.Create(
                "http://api.example.com/api/v1/",
                "api.example.com",
                isProduction: false));
    }
}
