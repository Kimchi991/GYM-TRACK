using System.Security.Claims;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Enums;
using Microsoft.AspNetCore.Http;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class CurrentUserContextTests
{
    [Fact]
    public void Numeric_firebase_subject_is_never_treated_as_sql_user_id()
    {
        var context = CreateContext(new Claim(FirebaseClaimTypes.Subject, "123"));

        Assert.Equal("123", context.FirebaseUid);
        Assert.Null(context.UserId);
        Assert.Null(context.Role);
        Assert.Null(context.MemberId);
    }

    [Fact]
    public void Only_positive_internally_issued_identity_claims_are_authoritative()
    {
        var context = CreateContext(
            AppClaimTypes.CreateInternal(AppClaimTypes.AppUserId, "42"),
            AppClaimTypes.CreateInternal(AppClaimTypes.AppMemberId, "12"),
            AppClaimTypes.CreateInternal(AppClaimTypes.AppRole, nameof(UserRole.GymGoer)),
            new Claim(AppClaimTypes.AppUserId, "999", ClaimValueTypes.String, "attacker"),
            new Claim(ClaimTypes.Role, nameof(UserRole.Administrator)));

        Assert.Equal(42, context.UserId);
        Assert.Equal(12, context.MemberId);
        Assert.Equal(UserRole.GymGoer, context.Role);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-an-int")]
    public void Invalid_internal_user_id_is_rejected(string value)
    {
        var context = CreateContext(AppClaimTypes.CreateInternal(AppClaimTypes.AppUserId, value));

        Assert.Null(context.UserId);
    }

    private static CurrentUserContext CreateContext(params Claim[] additionalClaims)
    {
        var claims = new List<Claim>
        {
            new(FirebaseClaimTypes.Email, "person@example.test"),
            new(FirebaseClaimTypes.EmailVerified, bool.TrueString)
        };
        if (!additionalClaims.Any(claim => claim.Type == FirebaseClaimTypes.Subject))
        {
            claims.Add(new Claim(FirebaseClaimTypes.Subject, "uid"));
        }
        claims.AddRange(additionalClaims);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };
        return new CurrentUserContext(new HttpContextAccessor { HttpContext = httpContext });
    }
}
