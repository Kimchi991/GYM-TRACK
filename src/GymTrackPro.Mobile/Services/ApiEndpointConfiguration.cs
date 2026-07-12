using System;
using System.Linq;
using System.Reflection;

namespace GymTrackPro.Mobile.Services;

public sealed class ApiEndpointConfiguration
{
    private const string BaseUrlMetadataKey = "GymTrackPro.ApiBaseUrl";
    private const string ExpectedHostMetadataKey = "GymTrackPro.ApiExpectedHost";
    private const string RequiredPath = "/api/v1/";

    private ApiEndpointConfiguration(Uri baseUri, string expectedHost, bool isProduction)
    {
        BaseUri = baseUri;
        ExpectedHost = expectedHost;
        IsProduction = isProduction;
    }

    public Uri BaseUri { get; }
    public string ExpectedHost { get; }
    public bool IsProduction { get; }

    public static ApiEndpointConfiguration LoadForCurrentBuild()
    {
        var attributes = typeof(ApiEndpointConfiguration).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToArray();
        var rawBaseUrl = attributes
            .LastOrDefault(attribute => attribute.Key == BaseUrlMetadataKey)?.Value;
        var expectedHost = attributes
            .LastOrDefault(attribute => attribute.Key == ExpectedHostMetadataKey)?.Value;

#if DEBUG
        const bool isProduction = false;
#else
        const bool isProduction = true;
#endif

        return Create(rawBaseUrl, expectedHost, isProduction);
    }

    public static ApiEndpointConfiguration Create(
        string? rawBaseUrl,
        string? expectedHost,
        bool isProduction)
    {
        if (string.IsNullOrWhiteSpace(rawBaseUrl) ||
            !Uri.TryCreate(rawBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("A valid absolute GymTrackPro API base URL is required.");
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment) ||
            !string.Equals(baseUri.AbsolutePath, RequiredPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The GymTrackPro API URL must use the exact '{RequiredPath}' path and contain no credentials, query, or fragment.");
        }

        var normalizedExpectedHost = expectedHost?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedExpectedHost) ||
            normalizedExpectedHost.Contains('/') ||
            normalizedExpectedHost.Contains(':') ||
            Uri.CheckHostName(normalizedExpectedHost) is UriHostNameType.Unknown)
        {
            throw new InvalidOperationException("An exact expected GymTrackPro API host is required.");
        }

        if (!string.Equals(baseUri.IdnHost, normalizedExpectedHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The GymTrackPro API URL does not match the configured expected host.");
        }

        if (isProduction)
        {
            if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !baseUri.IsDefaultPort)
            {
                throw new InvalidOperationException("Production GymTrackPro API traffic requires HTTPS on the default port.");
            }

            if (IsDevelopmentHost(baseUri))
            {
                throw new InvalidOperationException("A localhost or emulator API URL is forbidden in production.");
            }
        }
        else if (string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                 !IsDevelopmentHost(baseUri))
        {
            throw new InvalidOperationException("Debug HTTP is restricted to localhost or the Android emulator host.");
        }

        return new ApiEndpointConfiguration(baseUri, normalizedExpectedHost, isProduction);
    }

    public bool Contains(Uri? requestUri)
    {
        if (requestUri is null || !requestUri.IsAbsoluteUri)
        {
            return false;
        }

        return string.Equals(requestUri.Scheme, BaseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(requestUri.IdnHost, BaseUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               requestUri.Port == BaseUri.Port &&
               requestUri.AbsolutePath.StartsWith(BaseUri.AbsolutePath, StringComparison.Ordinal);
    }

    private static bool IsDevelopmentHost(Uri uri) =>
        uri.IsLoopback ||
        string.Equals(uri.IdnHost, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.IdnHost, "10.0.2.2", StringComparison.OrdinalIgnoreCase);
}
