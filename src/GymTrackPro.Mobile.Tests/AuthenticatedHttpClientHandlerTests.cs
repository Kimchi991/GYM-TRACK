using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile.Tests;

public sealed class AuthenticatedHttpClientHandlerTests
{
    private static readonly ApiEndpointConfiguration Endpoint = ApiEndpointConfiguration.Create(
        "https://api.example.com/api/v1/",
        "api.example.com",
        isProduction: true);

    [Fact]
    public async Task Attaches_session_token_and_overwrites_caller_bearer()
    {
        var session = new FakeSession("session-token", "refreshed-token");
        var transport = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(session, transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "members");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["session-token"], transport.BearerTokens);
    }

    [Fact]
    public async Task Rejects_cross_origin_request_before_reading_session()
    {
        var session = new FakeSession("session-token", "refreshed-token");
        var transport = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(session, transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetAsync("https://evil.example/api/v1/members"));

        Assert.Empty(session.RefreshRequests);
        Assert.Empty(transport.BearerTokens);
    }

    [Fact]
    public async Task Returns_redirect_without_forwarding_authorization()
    {
        var session = new FakeSession("session-token", "refreshed-token");
        var transport = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://evil.example/collect") }
        });
        using var client = CreateClient(session, transport);

        using var response = await client.GetAsync("members");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single(transport.BearerTokens);
        Assert.Equal("api.example.com", transport.RequestHosts.Single());
    }

    [Fact]
    public async Task Retries_safe_get_once_with_forced_refresh()
    {
        var session = new FakeSession("initial-token", "refreshed-token");
        var transport = new RecordingHandler(index => new HttpResponseMessage(
            index == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK));
        using var client = CreateClient(session, transport);

        using var response = await client.GetAsync("members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["initial-token", "refreshed-token"], transport.BearerTokens);
        Assert.Equal([false, true], session.RefreshRequests);
    }

    [Fact]
    public async Task Refreshes_but_does_not_replay_post_after_unauthorized()
    {
        var session = new FakeSession("initial-token", "refreshed-token");
        var transport = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(session, transport);

        using var response = await client.PostAsync("auth/activate", JsonContent.Create(new { code = "invite" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(["initial-token"], transport.BearerTokens);
        Assert.Equal([false, true], session.RefreshRequests);
    }

    private static HttpClient CreateClient(FakeSession session, HttpMessageHandler transport)
    {
        var handler = new AuthenticatedHttpClientHandler(session, Endpoint, transport);
        return new HttpClient(handler) { BaseAddress = Endpoint.BaseUri };
    }

    private sealed class FakeSession(string initialToken, string refreshedToken) : IAuthenticationSession
    {
        public List<bool> RefreshRequests { get; } = [];
        public string? CurrentUserId => "uid-1";

        public Task<string?> GetAccessTokenAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            RefreshRequests.Add(forceRefresh);
            return Task.FromResult<string?>(forceRefresh ? refreshedToken : initialToken);
        }

        public Task<bool> HasSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingHandler(Func<int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int _requestCount;

        public List<string?> BearerTokens { get; } = [];
        public List<string?> RequestHosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            BearerTokens.Add(request.Headers.Authorization?.Parameter);
            RequestHosts.Add(request.RequestUri?.Host);
            var response = responseFactory(Interlocked.Increment(ref _requestCount));
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
