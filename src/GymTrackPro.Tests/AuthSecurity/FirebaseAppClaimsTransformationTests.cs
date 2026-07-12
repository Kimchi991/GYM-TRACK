using System.Security.Claims;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Authorization;
using GymTrackPro.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Moq;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class FirebaseAppClaimsTransformationTests
{
    [Fact]
    public async Task Reviewed_active_sql_user_receives_internal_role_and_id_claims()
    {
        var resolver = new Mock<IUidAppUserResolver>();
        resolver.Setup(value => value.ResolveAsync("uid", "owner@example.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUserResolution(
                AppUserResolutionStatus.Resolved,
                new AppUserIdentity(
                    42,
                    "uid",
                    null,
                    "owner",
                    "owner@example.test",
                    "Gym",
                    "Owner",
                    UserRole.Administrator,
                    true,
                    true,
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    null)));
        var context = new DefaultHttpContext();
        var transformation = new FirebaseAppClaimsTransformation(
            resolver.Object,
            new HttpContextAccessor { HttpContext = context });
        var principal = CreatePrincipal(
            "owner@example.test",
            new Claim(ClaimTypes.Role, "GymGoer"));

        await transformation.TransformAsync(principal);

        Assert.DoesNotContain(principal.Claims, claim => claim.Type == ClaimTypes.Role);
        Assert.Contains(principal.Claims, claim =>
            claim.Type == AppClaimTypes.AppUserId
            && claim.Value == "42"
            && claim.Issuer == AppClaimTypes.InternalIssuer);
        Assert.Contains(principal.Claims, claim =>
            claim.Type == AppClaimTypes.AppRole
            && claim.Value == "Administrator"
            && claim.Issuer == AppClaimTypes.InternalIssuer);
        Assert.True(principal.IsInRole("Administrator"));
    }

    [Fact]
    public async Task Linked_gym_goer_receives_internal_member_claim()
    {
        var resolver = new Mock<IUidAppUserResolver>();
        resolver.Setup(value => value.ResolveAsync("uid", "goer@example.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUserResolution(
                AppUserResolutionStatus.Resolved,
                new AppUserIdentity(
                    52,
                    "uid",
                    12,
                    "member-12",
                    "goer@example.test",
                    "Gym",
                    "Goer",
                    UserRole.GymGoer,
                    true,
                    true,
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    null)));
        var transformation = new FirebaseAppClaimsTransformation(
            resolver.Object,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var principal = CreatePrincipal("goer@example.test");

        await transformation.TransformAsync(principal);

        Assert.Contains(principal.Claims, claim =>
            claim.Type == AppClaimTypes.AppMemberId
            && claim.Value == "12"
            && claim.Issuer == AppClaimTypes.InternalIssuer);
    }

    [Fact]
    public async Task Unknown_user_receives_no_application_role_even_when_token_supplies_one()
    {
        var resolver = new Mock<IUidAppUserResolver>();
        resolver.Setup(value => value.ResolveAsync("uid", "person@example.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUserResolution(AppUserResolutionStatus.NotFound));
        var transformation = new FirebaseAppClaimsTransformation(
            resolver.Object,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var principal = CreatePrincipal(
            "person@example.test",
            new Claim(AppClaimTypes.AppRole, "Administrator"),
            new Claim(AppClaimTypes.IdentityResolutionAttempted, bool.TrueString));

        await transformation.TransformAsync(principal);

        Assert.DoesNotContain(principal.Claims, claim => claim.Type == AppClaimTypes.AppRole);
        Assert.False(principal.IsInRole("Administrator"));
    }

    [Fact]
    public async Task Firebase_onboarding_endpoint_strips_untrusted_claims_without_preliminary_sql_resolution()
    {
        var resolver = new Mock<IUidAppUserResolver>(MockBehavior.Strict);
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new AuthorizeAttribute(Policies.FirebaseOnboarding)),
            "Firebase onboarding"));
        var transformation = new FirebaseAppClaimsTransformation(
            resolver.Object,
            new HttpContextAccessor { HttpContext = context });
        var principal = CreatePrincipal(
            "person@example.test",
            new Claim(AppClaimTypes.AppRole, "Administrator"));

        await transformation.TransformAsync(principal);

        Assert.DoesNotContain(principal.Claims, claim => claim.Type == AppClaimTypes.AppRole);
        Assert.DoesNotContain(
            principal.Claims,
            claim => claim.Type == AppClaimTypes.IdentityResolutionAttempted);
        resolver.VerifyNoOtherCalls();
    }

    private static ClaimsPrincipal CreatePrincipal(string email, params Claim[] additionalClaims)
    {
        var claims = new List<Claim>
        {
            new(FirebaseClaimTypes.Subject, "uid"),
            new(FirebaseClaimTypes.Email, email),
            new(FirebaseClaimTypes.EmailVerified, bool.TrueString)
        };
        claims.AddRange(additionalClaims);
        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            "Bearer",
            FirebaseClaimTypes.Subject,
            AppClaimTypes.AppRole));
    }
}
