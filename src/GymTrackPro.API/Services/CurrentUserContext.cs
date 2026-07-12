using System.Security.Claims;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Authorization;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.API.Services;

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    string? FirebaseUid { get; }
    int? UserId { get; }
    int? MemberId { get; }
    UserRole? Role { get; }
    string? Email { get; }
    bool IsEmailVerified { get; }
    ClaimsPrincipal User { get; }
}

public class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClaimsPrincipal User => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => User.Identity?.IsAuthenticated ?? false;

    public string? FirebaseUid
    {
        get
        {
            var uid = User.FindFirst(FirebaseClaimTypes.Subject)?.Value;
            return FirebaseIdentityValidation.TryValidateUid(uid) ? uid : null;
        }
    }

    public string? Email => User.FindFirst(FirebaseClaimTypes.Email)?.Value;

    public bool IsEmailVerified
    {
        get
        {
            var verifiedClaim = User.FindFirst(FirebaseClaimTypes.EmailVerified)?.Value;
            if (bool.TryParse(verifiedClaim, out bool isVerified))
            {
                return isVerified;
            }
            return false;
        }
    }

    // These claims are expected to be populated by the application logic or policy requirements
    // upon mapping the FirebaseUid to the SQL User.
    public int? UserId
    {
        get
        {
            var idClaim = FindInternalClaim(AppClaimTypes.AppUserId)?.Value;
            if (int.TryParse(idClaim, out int id) && id > 0) return id;
            return null;
        }
    }

    public int? MemberId
    {
        get
        {
            var idClaim = FindInternalClaim(AppClaimTypes.AppMemberId)?.Value;
            if (int.TryParse(idClaim, out int id) && id > 0) return id;
            return null;
        }
    }

    public UserRole? Role
    {
        get
        {
            // Only the SQL-derived internal role claim is authoritative.
            var roleClaim = FindInternalClaim(AppClaimTypes.AppRole)?.Value;

            if (Enum.TryParse<UserRole>(roleClaim, ignoreCase: false, out var role)
                && Enum.IsDefined(role)) return role;
            return null;
        }
    }

    private Claim? FindInternalClaim(string type) => User.Claims.FirstOrDefault(claim =>
        claim.Type == type
        && claim.Issuer == AppClaimTypes.InternalIssuer
        && claim.OriginalIssuer == AppClaimTypes.InternalIssuer);
}
