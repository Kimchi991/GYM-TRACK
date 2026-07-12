using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Hosting;
using Moq;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class FirebaseJwtConfigurationTests
{
    private const string ProjectId = "test-project";
    private const string Issuer = "https://securetoken.google.com/test-project";

    [Fact]
    public void Missing_project_configuration_fails_validation_in_production()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(Environments.Production);
        var validator = new FirebaseAuthenticationOptionsValidator(environment.Object);

        var result = validator.Validate(null, new FirebaseAuthenticationOptions());

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("project-")]
    [InlineData("google-project")]
    [InlineData("project-ssl")]
    [InlineData("undefined-project")]
    public void Invalid_google_project_identifier_fails_production_validation(string projectId)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(Environments.Production);
        var validator = new FirebaseAuthenticationOptionsValidator(environment.Object);

        var result = validator.Validate(
            null,
            new FirebaseAuthenticationOptions { ProjectId = projectId });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validation_parameters_accept_only_exact_rs256_project_token()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "valid-key" };
        var token = CreateRsaToken(key, Issuer, ProjectId, DateTime.UtcNow.AddMinutes(10));
        var parameters = CreateParameters(key);

        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }
            .ValidateToken(token, parameters, out _);

        Assert.Equal("firebase-user-1", principal.FindFirstValue(FirebaseClaimTypes.Subject));
        Assert.Equal(TimeSpan.FromMinutes(2), parameters.ClockSkew);
        Assert.Equal(new[] { SecurityAlgorithms.RsaSha256 }, parameters.ValidAlgorithms);
    }

    [Theory]
    [InlineData("https://securetoken.google.com/other-project", ProjectId)]
    [InlineData(Issuer, "other-project")]
    public void Wrong_issuer_or_audience_is_rejected(string issuer, string audience)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "valid-key" };
        var token = CreateRsaToken(key, issuer, audience, DateTime.UtcNow.AddMinutes(10));

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, CreateParameters(key), out _));
    }

    [Fact]
    public void Wrong_signature_is_rejected()
    {
        using var signingRsa = RSA.Create(2048);
        using var validationRsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(signingRsa) { KeyId = "signing-key" };
        var validationKey = new RsaSecurityKey(validationRsa) { KeyId = "validation-key" };
        var token = CreateRsaToken(signingKey, Issuer, ProjectId, DateTime.UtcNow.AddMinutes(10));

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, CreateParameters(validationKey), out _));
    }

    [Fact]
    public void Expired_token_outside_clock_skew_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "valid-key" };
        var token = CreateRsaToken(key, Issuer, ProjectId, DateTime.UtcNow.AddMinutes(-3));

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, CreateParameters(key), out _));
    }

    [Fact]
    public void Non_rs256_algorithm_is_rejected()
    {
        var hmacKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ProjectId,
            Subject = new ClaimsIdentity(BaseClaims()),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(hmacKey, SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(handler.CreateToken(descriptor));
        var parameters = FirebaseJwtConfiguration.CreateTokenValidationParameters(ProjectId);
        parameters.IssuerSigningKey = hmacKey;

        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(token, parameters, out _));
    }

    [Fact]
    public void Token_supplied_application_roles_are_removed()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(AppClaimTypes.AppRole, "Administrator"),
            new Claim(ClaimTypes.Role, "Administrator"),
            new Claim("role", "Administrator"),
            new Claim(FirebaseClaimTypes.Subject, "firebase-user-1")
        }, "Bearer");

        FirebaseJwtConfiguration.RemoveUntrustedApplicationClaims(identity);

        Assert.DoesNotContain(identity.Claims, claim =>
            claim.Type == AppClaimTypes.AppRole
            || claim.Type == ClaimTypes.Role
            || claim.Type == "role");
        Assert.Contains(identity.Claims, claim => claim.Type == FirebaseClaimTypes.Subject);
    }

    [Fact]
    public void Firebase_temporal_claims_accept_past_values_with_two_minute_skew()
    {
        var now = DateTimeOffset.UtcNow;
        var principal = TemporalPrincipal(
            now.AddMinutes(-1).ToUnixTimeSeconds(),
            now.AddMinutes(-5).ToUnixTimeSeconds());

        Assert.True(FirebaseJwtConfiguration.HasValidFirebaseTemporalClaims(principal, now));
    }

    [Theory]
    [InlineData(180, 0)]
    [InlineData(0, 180)]
    public void Future_iat_or_auth_time_is_rejected(int issuedAtOffsetSeconds, int authTimeOffsetSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        var principal = TemporalPrincipal(
            now.AddSeconds(issuedAtOffsetSeconds).ToUnixTimeSeconds(),
            now.AddSeconds(authTimeOffsetSeconds).ToUnixTimeSeconds());

        Assert.False(FirebaseJwtConfiguration.HasValidFirebaseTemporalClaims(principal, now));
    }

    [Fact]
    public void Missing_auth_time_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(FirebaseClaimTypes.IssuedAt, now.ToUnixTimeSeconds().ToString())
        }, "Bearer"));

        Assert.False(FirebaseJwtConfiguration.HasValidFirebaseTemporalClaims(principal, now));
    }

    private static TokenValidationParameters CreateParameters(SecurityKey key)
    {
        var parameters = FirebaseJwtConfiguration.CreateTokenValidationParameters(ProjectId);
        parameters.IssuerSigningKey = key;
        return parameters;
    }

    private static string CreateRsaToken(
        RsaSecurityKey key,
        string issuer,
        string audience,
        DateTime expires)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(BaseClaims()),
            NotBefore = expires <= DateTime.UtcNow
                ? expires.AddMinutes(-10)
                : DateTime.UtcNow.AddMinutes(-1),
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static Claim[] BaseClaims() =>
    [
        new Claim(FirebaseClaimTypes.Subject, "firebase-user-1"),
        new Claim(FirebaseClaimTypes.Email, "owner@example.test"),
        new Claim(FirebaseClaimTypes.EmailVerified, bool.TrueString),
        new Claim(
            FirebaseClaimTypes.AuthenticationTime,
            DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString())
    ];

    private static ClaimsPrincipal TemporalPrincipal(long issuedAt, long authTime) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(FirebaseClaimTypes.IssuedAt, issuedAt.ToString()),
            new Claim(FirebaseClaimTypes.AuthenticationTime, authTime.ToString())
        }, "Bearer"));
}
