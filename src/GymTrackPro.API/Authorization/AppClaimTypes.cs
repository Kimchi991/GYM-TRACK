using System.Security.Claims;

namespace GymTrackPro.API.Authorization;

public static class AppClaimTypes
{
    public const string InternalIssuer = "GymTrackPro.API";
    public const string AppUserId = "urn:gymtrackpro:claim:user-id";
    public const string AppMemberId = "urn:gymtrackpro:claim:member-id";
    public const string AppRole = "urn:gymtrackpro:claim:role";
    public const string IdentityResolved = "urn:gymtrackpro:claim:identity-resolved";
    public const string IdentityResolutionAttempted = "urn:gymtrackpro:claim:identity-resolution-attempted";

    private static readonly HashSet<string> InternalTypes = new(StringComparer.Ordinal)
    {
        AppUserId,
        AppMemberId,
        AppRole,
        IdentityResolved,
        IdentityResolutionAttempted
    };

    public static bool IsInternal(string claimType) => InternalTypes.Contains(claimType);

    public static Claim CreateInternal(string type, string value) =>
        new(type, value, ClaimValueTypes.String, InternalIssuer, InternalIssuer);
}
