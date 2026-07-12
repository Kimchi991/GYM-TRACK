using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using GymTrackPro.API.Authentication;
using GymTrackPro.Bootstrap;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class FirebaseBootstrapTokenValidatorTests
{
    private const string ProjectId = "gymtrackpro-production";
    private const string Issuer = "https://securetoken.google.com/gymtrackpro-production";
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_fresh_exact_project_token_derives_uid_and_normalized_verified_email()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "firebase-key" };
        var token = CreateToken(key, Now.AddMinutes(-1), emailVerified: true);
        Uri? requestedMetadata = null;
        var validator = new FirebaseBootstrapTokenValidator(
            (metadata, _) =>
            {
                requestedMetadata = metadata;
                return Task.FromResult(Configuration(key));
            },
            new FixedTimeProvider(Now));

        var identity = await validator.ValidateAsync(token, ProjectId);

        Assert.Equal(
            $"{Issuer}/.well-known/openid-configuration",
            requestedMetadata?.AbsoluteUri);
        Assert.Equal("firebase-owner-uid", identity.FirebaseUid);
        Assert.Equal("Owner@Example.Test", identity.CanonicalEmail);
        Assert.Equal("OWNER@EXAMPLE.TEST", identity.NormalizedEmail);
    }

    [Fact]
    public async Task Wrong_oidc_issuer_is_rejected_before_identity_derivation()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "firebase-key" };
        var configuration = Configuration(key);
        configuration.Issuer = "https://securetoken.google.com/another-project";
        var validator = new FirebaseBootstrapTokenValidator(
            (_, _) => Task.FromResult(configuration),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync(CreateToken(key, Now.AddMinutes(-1), true), ProjectId));
    }

    [Fact]
    public async Task Unverified_or_stale_token_is_rejected_generically()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "firebase-key" };
        var validator = new FirebaseBootstrapTokenValidator(
            (_, _) => Task.FromResult(Configuration(key)),
            new FixedTimeProvider(Now));

        var unverified = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync(CreateToken(key, Now.AddMinutes(-1), false), ProjectId));
        var stale = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync(CreateToken(key, Now.AddMinutes(-6), true), ProjectId));

        Assert.Equal(unverified.Message, stale.Message);
        Assert.DoesNotContain("firebase-owner-uid", unverified.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Owner@Example.Test", stale.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-11, true)]
    [InlineData(3, true)]
    [InlineData(0, false)]
    public async Task Stale_future_or_missing_authentication_time_is_rejected(
        int authenticationOffsetMinutes,
        bool includeAuthenticationTime)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "firebase-key" };
        var validator = new FirebaseBootstrapTokenValidator(
            (_, _) => Task.FromResult(Configuration(key)),
            new FixedTimeProvider(Now));
        var token = CreateToken(
            key,
            Now.AddMinutes(-1),
            emailVerified: true,
            authenticationTime: Now.AddMinutes(authenticationOffsetMinutes),
            includeAuthenticationTime: includeAuthenticationTime);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync(token, ProjectId));
    }

    [Fact]
    public async Task Mismatched_user_id_or_missing_key_id_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var keyed = new RsaSecurityKey(rsa) { KeyId = "firebase-key" };
        var validator = new FirebaseBootstrapTokenValidator(
            (_, _) => Task.FromResult(Configuration(keyed)),
            new FixedTimeProvider(Now));
        var mismatchedUserId = CreateToken(
            keyed,
            Now.AddMinutes(-1),
            emailVerified: true,
            userId: "different-user");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync(mismatchedUserId, ProjectId));

        using var noKidRsa = RSA.Create(2048);
        var noKid = new RsaSecurityKey(noKidRsa);
        var noKidValidator = new FirebaseBootstrapTokenValidator(
            (_, _) => Task.FromResult(Configuration(noKid)),
            new FixedTimeProvider(Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            noKidValidator.ValidateAsync(
                CreateToken(noKid, Now.AddMinutes(-1), emailVerified: true),
                ProjectId));
    }

    private static OpenIdConnectConfiguration Configuration(SecurityKey key)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(key);
        return configuration;
    }

    private static string CreateToken(
        SecurityKey key,
        DateTimeOffset issuedAt,
        bool emailVerified,
        DateTimeOffset? authenticationTime = null,
        bool includeAuthenticationTime = true,
        string? userId = null)
    {
        var claims = new List<Claim>
        {
            new Claim(FirebaseClaimTypes.Subject, "firebase-owner-uid"),
            new Claim(FirebaseClaimTypes.Email, "Owner@Example.Test"),
            new Claim(FirebaseClaimTypes.EmailVerified, emailVerified.ToString()),
            new Claim(
                FirebaseClaimTypes.IssuedAt,
                issuedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };
        if (includeAuthenticationTime)
        {
            claims.Add(new Claim(
                FirebaseClaimTypes.AuthenticationTime,
                (authenticationTime ?? Now.AddMinutes(-2))
                    .ToUnixTimeSeconds()
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64));
        }

        if (userId is not null)
        {
            claims.Add(new Claim(FirebaseClaimTypes.UserId, userId));
        }

        var token = new JwtSecurityToken(
            Issuer,
            ProjectId,
            claims,
            issuedAt.AddMinutes(-1).UtcDateTime,
            Now.AddMinutes(30).UtcDateTime,
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
