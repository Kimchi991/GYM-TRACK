using System.Security.Claims;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class AppAuthorizationHandlerTests
{
    [Fact]
    public async Task Verified_unknown_firebase_user_can_onboard_but_cannot_access_business_policy()
    {
        var principal = CreatePrincipal(includeInternalClaims: false);
        var onboarding = new FirebaseOnboardingRequirement();
        var active = new ActiveAppUserRequirement();
        var context = new AuthorizationHandlerContext(new IAuthorizationRequirement[] { onboarding, active }, principal, null);

        await new AppAuthorizationHandler().HandleAsync(context);

        Assert.True(context.HasSucceededFor(onboarding));
        Assert.False(context.HasSucceededFor(active));
    }

    [Fact]
    public async Task Sql_derived_administrator_satisfies_active_backoffice_and_owner_policies()
    {
        var principal = CreatePrincipal(includeInternalClaims: true);
        var requirements = new IAuthorizationRequirement[]
        {
            new ActiveAppUserRequirement(),
            new BackOfficeRequirement(),
            new OwnerOnlyRequirement()
        };
        var context = new AuthorizationHandlerContext(requirements, principal, null);

        await new AppAuthorizationHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Token_forged_internal_claim_with_external_issuer_is_ignored()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(FirebaseClaimTypes.Subject, "uid"),
            new Claim(FirebaseClaimTypes.Email, "owner@example.test"),
            new Claim(FirebaseClaimTypes.EmailVerified, bool.TrueString),
            new Claim(AppClaimTypes.AppUserId, "1", ClaimValueTypes.String, "attacker"),
            new Claim(AppClaimTypes.AppRole, "Administrator", ClaimValueTypes.String, "attacker"),
            new Claim(AppClaimTypes.IdentityResolved, bool.TrueString, ClaimValueTypes.Boolean, "attacker")
        }, "Bearer", FirebaseClaimTypes.Subject, AppClaimTypes.AppRole);
        var requirement = new OwnerOnlyRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(identity), null);

        await new AppAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Unverified_email_satisfies_no_policy()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(FirebaseClaimTypes.Subject, "uid"),
            new Claim(FirebaseClaimTypes.Email, "owner@example.test"),
            new Claim(FirebaseClaimTypes.EmailVerified, bool.FalseString)
        }, "Bearer");
        var requirement = new FirebaseOnboardingRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(identity), null);

        await new AppAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Gym_goer_self_requires_internally_issued_member_id()
    {
        var principal = CreatePrincipal(includeInternalClaims: false);
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(AppClaimTypes.CreateInternal(AppClaimTypes.AppUserId, "9"));
        identity.AddClaim(AppClaimTypes.CreateInternal(AppClaimTypes.AppRole, "GymGoer"));
        identity.AddClaim(AppClaimTypes.CreateInternal(AppClaimTypes.IdentityResolved, bool.TrueString));
        var requirement = new GymGoerSelfRequirement();
        var activeRequirement = new ActiveAppUserRequirement();
        var context = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement, activeRequirement },
            principal,
            null);

        await new AppAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasSucceededFor(activeRequirement));
        identity.AddClaim(AppClaimTypes.CreateInternal(AppClaimTypes.AppMemberId, "15"));
        context = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement, activeRequirement },
            principal,
            null);
        await new AppAuthorizationHandler().HandleAsync(context);
        Assert.True(context.HasSucceeded);
    }

    private static ClaimsPrincipal CreatePrincipal(bool includeInternalClaims)
    {
        var claims = new List<Claim>
        {
            new(FirebaseClaimTypes.Subject, "uid"),
            new(FirebaseClaimTypes.Email, "owner@example.test"),
            new(FirebaseClaimTypes.EmailVerified, bool.TrueString)
        };
        if (includeInternalClaims)
        {
            claims.Add(AppClaimTypes.CreateInternal(AppClaimTypes.AppUserId, "1"));
            claims.Add(AppClaimTypes.CreateInternal(AppClaimTypes.AppRole, "Administrator"));
            claims.Add(AppClaimTypes.CreateInternal(AppClaimTypes.IdentityResolved, bool.TrueString));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            "Bearer",
            FirebaseClaimTypes.Subject,
            AppClaimTypes.AppRole));
    }
}

internal static class AuthorizationHandlerContextAssertions
{
    public static bool HasSucceededFor(
        this AuthorizationHandlerContext context,
        IAuthorizationRequirement requirement) => !context.PendingRequirements.Contains(requirement);
}
