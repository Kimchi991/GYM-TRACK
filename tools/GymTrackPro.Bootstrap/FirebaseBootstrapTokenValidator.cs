using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GymTrackPro.API.Authentication;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GymTrackPro.Bootstrap;

public sealed record BootstrapFirebaseIdentity(
    string FirebaseUid,
    string CanonicalEmail,
    string NormalizedEmail);

public sealed class FirebaseBootstrapTokenValidator
{
    public const int MaximumTokenCharacters = 16 * 1024;
    public static readonly TimeSpan MaximumIssuedAtAge = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumAuthenticationAge = TimeSpan.FromMinutes(10);

    private readonly Func<Uri, CancellationToken, Task<OpenIdConnectConfiguration>> _configurationResolver;
    private readonly TimeProvider _timeProvider;

    public FirebaseBootstrapTokenValidator(HttpClient httpClient)
        : this(CreateConfigurationResolver(httpClient), TimeProvider.System)
    {
    }

    public FirebaseBootstrapTokenValidator(
        Func<Uri, CancellationToken, Task<OpenIdConnectConfiguration>> configurationResolver,
        TimeProvider timeProvider)
    {
        _configurationResolver = configurationResolver;
        _timeProvider = timeProvider;
    }

    public async Task<BootstrapFirebaseIdentity> ValidateAsync(
        string firebaseIdToken,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var firebaseOptions = new FirebaseAuthenticationOptions { ProjectId = projectId };
        if (!firebaseOptions.HasValidProjectId
            || string.IsNullOrWhiteSpace(firebaseIdToken)
            || firebaseIdToken.Length > MaximumTokenCharacters)
        {
            throw ValidationFailed();
        }

        var issuer = firebaseOptions.Issuer;
        var metadataAddress = new Uri(
            $"{issuer}/.well-known/openid-configuration",
            UriKind.Absolute);

        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await _configurationResolver(metadataAddress, cancellationToken);
        }
        catch
        {
            throw ValidationFailed();
        }

        if (!string.Equals(configuration.Issuer, issuer, StringComparison.Ordinal)
            || configuration.SigningKeys.Count == 0)
        {
            throw ValidationFailed();
        }

        ClaimsPrincipal principal;
        JwtSecurityToken validatedJwt;
        try
        {
            var validationParameters = FirebaseJwtConfiguration
                .CreateTokenValidationParameters(projectId);
            validationParameters.IssuerSigningKeys = configuration.SigningKeys;
            validationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
            {
                if (!expires.HasValue)
                {
                    return false;
                }

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                return (!notBefore.HasValue || notBefore.Value <= nowUtc + parameters.ClockSkew)
                    && expires.Value > nowUtc - parameters.ClockSkew;
            };
            var handler = new JwtSecurityTokenHandler
            {
                MapInboundClaims = false,
                MaximumTokenSizeInBytes = MaximumTokenCharacters
            };
            principal = handler.ValidateToken(
                firebaseIdToken,
                validationParameters,
                out var validatedToken);
            validatedJwt = validatedToken as JwtSecurityToken
                ?? throw ValidationFailed();
        }
        catch
        {
            throw ValidationFailed();
        }

        FirebaseJwtConfiguration.RemoveUntrustedApplicationClaims(
            (ClaimsIdentity)principal.Identity!);

        var now = _timeProvider.GetUtcNow();
        if (!FirebaseJwtConfiguration.HasValidFirebaseTemporalClaims(principal, now)
            || !HasFreshIssuedAt(principal, now)
            || !HasRecentAuthentication(principal, now)
            || !string.Equals(
                validatedJwt.Header.Alg,
                SecurityAlgorithms.RsaSha256,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(validatedJwt.Header.Kid)
            || validatedJwt.Audiences.Count() != 1
            || !string.Equals(
                validatedJwt.Audiences.Single(),
                projectId,
                StringComparison.Ordinal)
            || !FirebaseClaimTypes.TryGetVerifiedIdentity(
                principal,
                out var firebaseUid,
                out var email)
            || (principal.FindFirstValue(FirebaseClaimTypes.UserId) is { Length: > 0 } userId
                && !string.Equals(userId, firebaseUid, StringComparison.Ordinal))
            || !EmailNormalization.TryCanonicalize(
                email,
                out var canonicalEmail,
                out var normalizedEmail))
        {
            throw ValidationFailed();
        }

        return new BootstrapFirebaseIdentity(
            firebaseUid,
            canonicalEmail,
            normalizedEmail);
    }

    private static bool HasFreshIssuedAt(ClaimsPrincipal principal, DateTimeOffset now)
    {
        if (!long.TryParse(
                principal.FindFirstValue(FirebaseClaimTypes.IssuedAt),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var issuedAt))
        {
            return false;
        }

        var issuedAtTime = DateTimeOffset.FromUnixTimeSeconds(issuedAt);
        return issuedAtTime <= now.AddMinutes(2)
            && now - issuedAtTime <= MaximumIssuedAtAge;
    }

    private static bool HasRecentAuthentication(ClaimsPrincipal principal, DateTimeOffset now)
    {
        if (!long.TryParse(
                principal.FindFirstValue(FirebaseClaimTypes.AuthenticationTime),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var authenticationTime))
        {
            return false;
        }

        var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(authenticationTime);
        return authenticatedAt <= now.AddMinutes(2)
            && now - authenticatedAt <= MaximumAuthenticationAge;
    }

    private static Func<Uri, CancellationToken, Task<OpenIdConnectConfiguration>>
        CreateConfigurationResolver(HttpClient httpClient) =>
        async (metadataAddress, cancellationToken) =>
        {
            var documentRetriever = new HttpDocumentRetriever(httpClient)
            {
                RequireHttps = true
            };
            var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress.AbsoluteUri,
                new OpenIdConnectConfigurationRetriever(),
                documentRetriever);
            return await manager.GetConfigurationAsync(cancellationToken);
        };

    private static InvalidOperationException ValidationFailed() =>
        new("Firebase bootstrap token validation failed.");
}
