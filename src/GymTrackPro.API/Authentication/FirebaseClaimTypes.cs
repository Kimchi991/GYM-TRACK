using System.Security.Claims;

namespace GymTrackPro.API.Authentication;

public static class FirebaseClaimTypes
{
    public const string Subject = "sub";
    public const string UserId = "user_id";
    public const string Email = "email";
    public const string EmailVerified = "email_verified";
    public const string IssuedAt = "iat";
    public const string AuthenticationTime = "auth_time";

    public static bool TryGetVerifiedIdentity(
        ClaimsPrincipal principal,
        out string firebaseUid,
        out string email)
    {
        firebaseUid = principal.FindFirstValue(Subject) ?? string.Empty;
        email = principal.FindFirstValue(Email) ?? string.Empty;

        var verifiedValue = principal.FindFirstValue(EmailVerified);
        return FirebaseIdentityValidation.TryValidateUid(firebaseUid)
            && EmailNormalization.TryNormalize(email, out _)
            && bool.TryParse(verifiedValue, out var verified)
            && verified;
    }
}
