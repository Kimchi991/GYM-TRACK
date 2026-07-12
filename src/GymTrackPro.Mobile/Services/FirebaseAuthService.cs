using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Auth.Providers;

namespace GymTrackPro.Mobile.Services;

public sealed class FirebaseAuthService : IFirebaseAuthService
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly IFirebaseAuthClient _client;
    private readonly HttpClient _identityToolkitClient;
    private readonly string _apiKey;
    private readonly IAccountSessionInvalidator _sessionInvalidator;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);

    public FirebaseAuthService()
        : this(
            CreateFirebaseClient(),
            CreateIdentityToolkitClient(),
            FirebaseAuthSettings.ApiKey,
            new AccountSessionInvalidator([]))
    {
    }

    public FirebaseAuthService(IAccountSessionInvalidator sessionInvalidator)
        : this(
            CreateFirebaseClient(),
            CreateIdentityToolkitClient(),
            FirebaseAuthSettings.ApiKey,
            sessionInvalidator)
    {
    }

    /// <summary>
    /// Injectable constructor for isolated session tests. Production composition should
    /// continue to use the parameterless constructor until DI is wired by the root owner.
    /// </summary>
    public FirebaseAuthService(
        IFirebaseAuthClient client,
        HttpClient identityToolkitClient,
        string apiKey)
        : this(
            client,
            identityToolkitClient,
            apiKey,
            new AccountSessionInvalidator([]))
    {
    }

    public FirebaseAuthService(
        IFirebaseAuthClient client,
        HttpClient identityToolkitClient,
        string apiKey,
        IAccountSessionInvalidator sessionInvalidator)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _identityToolkitClient = identityToolkitClient ??
            throw new ArgumentNullException(nameof(identityToolkitClient));
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("Firebase API key is required.", nameof(apiKey))
            : apiKey;
        _sessionInvalidator = sessionInvalidator
            ?? throw new ArgumentNullException(nameof(sessionInvalidator));
    }

    public string? CurrentUserId => _client.User?.Uid;

    public async Task<string> LoginAsync(string email, string password)
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var credential = await _client
                .SignInWithEmailAndPasswordAsync(email, password)
                .ConfigureAwait(false);
            return await credential.User.GetIdTokenAsync(false).ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task<string> RegisterAsync(string email, string password)
    {
        string idToken;

        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var credential = await _client
                .CreateUserWithEmailAndPasswordAsync(email, password)
                .ConfigureAwait(false);
            idToken = await credential.User.GetIdTokenAsync(false).ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }

        // FirebaseAuthentication.net 4.1.0 does not expose sendOobCode, so the
        // documented Identity Toolkit endpoint is used for verification mail.
        try
        {
            await SendEmailVerificationCoreAsync(idToken, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Account creation and verification delivery are separate Firebase calls.
            // Preserve the authenticated session and expose only a typed, controlled
            // recovery signal; never include the email, token, or provider response.
            throw new FirebaseRegistrationVerificationPendingException(exception);
        }
        return idToken;
    }

    public Task ResetPasswordAsync(string email) => _client.ResetEmailPasswordAsync(email);

    public async Task<string> LoginWithGoogleAsync(string oauthToken)
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var googleCredential = GoogleProvider.GetCredential(
                oauthToken,
                OAuthCredentialTokenType.IdToken);
            var credential = await _client
                .SignInWithCredentialAsync(googleCredential)
                .ConfigureAwait(false);
            return await credential.User.GetIdTokenAsync(false).ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task SendEmailVerificationAsync(CancellationToken cancellationToken = default)
    {
        var idToken = await GetAccessTokenAsync(true, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(idToken))
        {
            throw new InvalidOperationException("A Firebase session is required to verify email.");
        }

        await SendEmailVerificationCoreAsync(idToken, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsEmailVerifiedAsync(CancellationToken cancellationToken = default)
    {
        var idToken = await GetAccessTokenAsync(true, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return false;
        }

        return ReadEmailVerifiedClaim(idToken, CurrentUserId);
    }

    public async Task<string?> GetAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var user = _client.User;
            if (user is null)
            {
                return null;
            }

            var refresh = forceRefresh || NeedsProactiveRefresh(user.Credential);
            try
            {
                // FirebaseAuthentication.net has no cancellation-token overload. Once
                // refresh begins, keep the single-flight gate until it completes so a
                // cancelled caller cannot start a second refresh with an older token.
                return await user.GetIdTokenAsync(refresh).ConfigureAwait(false);
            }
            catch (FirebaseAuthException exception) when (IsTerminalSessionFailure(exception))
            {
                // Capture the account before sign-out removes it. The callback uses the
                // Firebase client primitive directly because this method already owns
                // _sessionGate; calling SignOutAsync here would deadlock recursively.
                var firebaseUid = user.Uid;
                await _sessionInvalidator
                    .InvalidateAsync(
                        firebaseUid,
                        () =>
                        {
                            _client.SignOut();
                            return Task.CompletedTask;
                        },
                        CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public Task<bool> HasSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _client.User;
        return Task.FromResult(
            user is not null &&
            !string.IsNullOrWhiteSpace(user.Uid) &&
            !string.IsNullOrWhiteSpace(user.Credential?.RefreshToken));
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _client.SignOut();
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public Task LogoutAsync() => SignOutAsync();

    public Task<string?> GetFreshTokenAsync() => GetAccessTokenAsync();

    public Task<bool> HasValidSessionAsync() => HasSessionAsync();

    public string? GetCurrentUid() => CurrentUserId;

    private async Task SendEmailVerificationCoreAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(
            "https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key=" +
            Uri.EscapeDataString(_apiKey),
            UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new SendVerificationEmailRequest
            {
                RequestType = "VERIFY_EMAIL",
                IdToken = idToken
            })
        };
        using var response = await _identityToolkitClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static bool NeedsProactiveRefresh(FirebaseCredential? credential)
    {
        if (credential is null || string.IsNullOrWhiteSpace(credential.RefreshToken) ||
            credential.Created == default || credential.ExpiresIn <= 0)
        {
            return true;
        }

        var expiresAt = credential.Created.ToUniversalTime().AddSeconds(credential.ExpiresIn);
        return DateTime.UtcNow >= expiresAt.Subtract(RefreshSkew);
    }

    private static bool IsTerminalSessionFailure(FirebaseAuthException exception)
    {
        if (exception.Reason is
            AuthErrorReason.InvalidIDToken or
            AuthErrorReason.InvalidAccessToken or
            AuthErrorReason.LoginCredentialsTooOld or
            AuthErrorReason.UserDisabled or
            AuthErrorReason.UserNotFound)
        {
            return true;
        }

        // FirebaseAuthentication.net 4.1.0 maps some Secure Token refresh failures
        // to Unknown. Inspect only the machine error payload to distinguish revoked,
        // expired, or invalid refresh credentials from transient network failures.
        if (exception is not FirebaseAuthHttpException httpException ||
            string.IsNullOrWhiteSpace(httpException.ResponseData))
        {
            return false;
        }

        return ContainsTerminalErrorCode(httpException.ResponseData);
    }

    private static bool ContainsTerminalErrorCode(string responseData)
    {
        string[] terminalCodes =
        [
            "INVALID_REFRESH_TOKEN",
            "INVALID_GRANT",
            "TOKEN_EXPIRED",
            "USER_DISABLED",
            "USER_NOT_FOUND",
            "INVALID_ID_TOKEN"
        ];

        return terminalCodes.Any(code =>
            responseData.Contains(code, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ReadEmailVerifiedClaim(string idToken, string? expectedUid)
    {
        var segments = idToken.Split('.');
        if (segments.Length != 3)
        {
            throw new InvalidOperationException("Firebase returned a malformed ID token.");
        }

        try
        {
            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = document.RootElement;

            if (!root.TryGetProperty("sub", out var subject) ||
                string.IsNullOrWhiteSpace(subject.GetString()) ||
                (!string.IsNullOrWhiteSpace(expectedUid) &&
                 !string.Equals(subject.GetString(), expectedUid, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Firebase token subject does not match the session.");
            }

            return root.TryGetProperty("email_verified", out var verified) &&
                   verified.ValueKind is JsonValueKind.True;
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Firebase returned a malformed ID token.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Firebase returned a malformed ID token.", exception);
        }
    }

    private static IFirebaseAuthClient CreateFirebaseClient()
    {
        var secureStore = new MauiSecureKeyValueStore();
        foreach (var legacyKey in FirebaseAuthSettings.LegacyStorageKeys)
        {
            secureStore.Remove(legacyKey);
        }

        var config = new FirebaseAuthConfig
        {
            ApiKey = FirebaseAuthSettings.ApiKey,
            AuthDomain = FirebaseAuthSettings.AuthDomain,
            Providers =
            [
                new EmailProvider(),
                new GoogleProvider()
            ],
            UserRepository = new SecureStorageUserRepository(
                secureStore,
                FirebaseAuthSettings.UserStorageKey)
        };

        return new FirebaseAuthClient(config);
    }

    private static HttpClient CreateIdentityToolkitClient() => new(
        new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private sealed class SendVerificationEmailRequest
    {
        [JsonPropertyName("requestType")]
        public required string RequestType { get; init; }

        [JsonPropertyName("idToken")]
        public required string IdToken { get; init; }
    }
}
