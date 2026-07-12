using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace GymTrackPro.Mobile.Services;

public sealed class AuthenticatedHttpClientHandler : DelegatingHandler
{
    private readonly IAuthenticationSession _session;
    private readonly ApiEndpointConfiguration _endpoint;

    public AuthenticatedHttpClientHandler(
        IAuthenticationSession session,
        ApiEndpointConfiguration endpoint)
        : this(
            session,
            endpoint,
            new HttpClientHandler
            {
                // Authentication is never propagated through an HTTP redirect. The API
                // must expose a final HTTPS URI with no redirect dependency.
                AllowAutoRedirect = false
            })
    {
    }

    public AuthenticatedHttpClientHandler(
        IAuthenticationSession session,
        ApiEndpointConfiguration endpoint,
        HttpMessageHandler innerHandler)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        InnerHandler = innerHandler ?? throw new ArgumentNullException(nameof(innerHandler));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureTrustedDestination(request.RequestUri);

        using var retryRequest = CanRetryAfterRefresh(request.Method)
            ? await CloneRequestAsync(request, cancellationToken).ConfigureAwait(false)
            : null;

        await AttachTokenAsync(request, false, cancellationToken).ConfigureAwait(false);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // Refresh even when the request cannot be replayed so the next explicit user
        // action has a current token. POST/PATCH and other unsafe requests are never
        // blindly replayed by this generic handler.
        string? refreshedToken;
        try
        {
            refreshedToken = await _session
                .GetAccessTokenAsync(true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }
        if (retryRequest is null || string.IsNullOrWhiteSpace(refreshedToken))
        {
            return response;
        }

        EnsureTrustedDestination(retryRequest.RequestUri);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedToken);
        response.Dispose();
        return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    private async Task AttachTokenAsync(
        HttpRequestMessage request,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        // Discard any caller-provided bearer value; this client only uses the active
        // Firebase session and only after validating the exact destination.
        request.Headers.Authorization = null;
        var token = await _session
            .GetAccessTokenAsync(forceRefresh, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private void EnsureTrustedDestination(Uri? requestUri)
    {
        if (!_endpoint.Contains(requestUri))
        {
            throw new InvalidOperationException(
                "Authenticated API requests are restricted to the configured GymTrackPro API origin and path.");
        }
    }

    private static bool CanRetryAfterRefresh(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (request.Content is not null)
        {
            var contentBytes = await request.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            clone.Content = new ByteArrayContent(contentBytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
