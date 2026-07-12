using System.Security.Claims;
using GymTrackPro.API.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace GymTrackPro.API.Authentication;

public static class FirebaseJwtConfiguration
{
    public const string DevelopmentFallbackProjectId = "development-project-not-configured";

    public static void Configure(
        JwtBearerOptions jwtOptions,
        FirebaseAuthenticationOptions firebaseOptions,
        IHostEnvironment environment)
    {
        var projectId = firebaseOptions.HasValidProjectId
            ? firebaseOptions.ProjectId
            : DevelopmentFallbackProjectId;

        if (!firebaseOptions.HasValidProjectId
            && !environment.IsDevelopment()
            && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                $"{FirebaseAuthenticationOptions.SectionName}:ProjectId is required outside Development/Testing.");
        }

        var issuer = $"https://securetoken.google.com/{projectId}";

        jwtOptions.Authority = issuer;
        jwtOptions.Audience = projectId;
        jwtOptions.MapInboundClaims = false;
        jwtOptions.RequireHttpsMetadata = true;
        jwtOptions.RefreshOnIssuerKeyNotFound = true;
        jwtOptions.IncludeErrorDetails = false;
        jwtOptions.TokenValidationParameters = CreateTokenValidationParameters(projectId);
    }

    public static TokenValidationParameters CreateTokenValidationParameters(string projectId)
    {
        var issuer = $"https://securetoken.google.com/{projectId}";

        return new TokenValidationParameters
        {
            AuthenticationType = JwtBearerDefaults.AuthenticationScheme,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            RequireAudience = true,
            ValidAudience = projectId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            TryAllIssuerSigningKeys = false,
            NameClaimType = FirebaseClaimTypes.Subject,
            RoleClaimType = AppClaimTypes.AppRole
        };
    }

    public static Task ValidateRequiredClaimsAsync(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            context.Fail("Firebase identity is unavailable.");
            return Task.CompletedTask;
        }

        RemoveUntrustedApplicationClaims(identity);

        var subject = identity.FindFirst(FirebaseClaimTypes.Subject)?.Value;
        var email = identity.FindFirst(FirebaseClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(subject)
            || subject.Length > 128
            || string.IsNullOrWhiteSpace(email)
            || !HasValidFirebaseTemporalClaims(context.Principal, DateTimeOffset.UtcNow))
        {
            context.Fail("Firebase token is missing required identity claims.");
        }

        return Task.CompletedTask;
    }

    public static bool HasValidFirebaseTemporalClaims(
        ClaimsPrincipal principal,
        DateTimeOffset now)
    {
        var maximumAccepted = now.AddMinutes(2).ToUnixTimeSeconds();
        return TryReadNumericDate(principal, FirebaseClaimTypes.IssuedAt, out var issuedAt)
            && TryReadNumericDate(principal, FirebaseClaimTypes.AuthenticationTime, out var authTime)
            && issuedAt > 0
            && authTime > 0
            && issuedAt <= maximumAccepted
            && authTime <= maximumAccepted;
    }

    private static bool TryReadNumericDate(
        ClaimsPrincipal principal,
        string claimType,
        out long value) => long.TryParse(
            principal.FindFirstValue(claimType),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);

    public static void RemoveUntrustedApplicationClaims(ClaimsIdentity identity)
    {
        var untrustedClaims = identity.Claims
            .Where(claim => AppClaimTypes.IsInternal(claim.Type)
                || claim.Type == ClaimTypes.Role
                || claim.Type.Equals("role", StringComparison.OrdinalIgnoreCase)
                || claim.Type.Equals("roles", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var claim in untrustedClaims)
        {
            identity.RemoveClaim(claim);
        }
    }
}
