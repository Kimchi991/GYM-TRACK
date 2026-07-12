using System.Security.Claims;
using System.Globalization;
using GymTrackPro.API.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace GymTrackPro.API.Authentication;

public sealed class FirebaseAppClaimsTransformation : IClaimsTransformation
{
    private readonly IUidAppUserResolver _resolver;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FirebaseAppClaimsTransformation(
        IUidAppUserResolver resolver,
        IHttpContextAccessor httpContextAccessor)
    {
        _resolver = resolver;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return principal;
        }

        if (identity.Claims.Any(claim =>
            claim.Type == AppClaimTypes.IdentityResolutionAttempted
            && claim.Value == bool.TrueString
            && claim.Issuer == AppClaimTypes.InternalIssuer
            && claim.OriginalIssuer == AppClaimTypes.InternalIssuer))
        {
            return principal;
        }

        FirebaseJwtConfiguration.RemoveUntrustedApplicationClaims(identity);

        // Onboarding actions intentionally authorize only the verified Firebase identity.
        // Their stores perform the required transactional identity lookup themselves, so a
        // preliminary SQL resolver call would add cost before endpoint rate limiting without
        // granting any additional authority.
        if (IsFirebaseOnboardingOnlyEndpoint(_httpContextAccessor.HttpContext?.GetEndpoint()))
        {
            return principal;
        }

        identity.AddClaim(AppClaimTypes.CreateInternal(
            AppClaimTypes.IdentityResolutionAttempted,
            bool.TrueString));

        if (!FirebaseClaimTypes.TryGetVerifiedIdentity(principal, out var uid, out var email))
        {
            return principal;
        }

        var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var resolution = await _resolver.ResolveAsync(uid, email, cancellationToken);
        if (resolution.Status != AppUserResolutionStatus.Resolved || resolution.User is null)
        {
            return principal;
        }

        identity.AddClaim(AppClaimTypes.CreateInternal(
            AppClaimTypes.AppUserId,
            resolution.User.UserId.ToString(CultureInfo.InvariantCulture)));
        identity.AddClaim(AppClaimTypes.CreateInternal(
            AppClaimTypes.AppRole,
            resolution.User.Role.ToString()));
        if (resolution.User.MemberId.HasValue)
        {
            identity.AddClaim(AppClaimTypes.CreateInternal(
                AppClaimTypes.AppMemberId,
                resolution.User.MemberId.Value.ToString(CultureInfo.InvariantCulture)));
        }
        identity.AddClaim(AppClaimTypes.CreateInternal(
            AppClaimTypes.IdentityResolved,
            bool.TrueString));

        return principal;
    }

    private static bool IsFirebaseOnboardingOnlyEndpoint(Endpoint? endpoint)
    {
        var authorizeData = endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>();
        return authorizeData is { Count: > 0 }
            && authorizeData.All(data =>
                string.Equals(data.Policy, Policies.FirebaseOnboarding, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(data.Roles));
    }
}
