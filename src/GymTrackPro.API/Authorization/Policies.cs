using GymTrackPro.API.Authentication;
using GymTrackPro.Shared.Enums;
using Microsoft.AspNetCore.Authorization;

namespace GymTrackPro.API.Authorization;

public static class Policies
{
    public const string ActiveAppUser = "ActiveAppUser";
    public const string BackOffice = "BackOffice";
    public const string OwnerOnly = "OwnerOnly";
    public const string GymGoerSelf = "GymGoerSelf";
    public const string FirebaseOnboarding = "FirebaseOnboarding";
}

public sealed class ActiveAppUserRequirement : IAuthorizationRequirement;
public sealed class BackOfficeRequirement : IAuthorizationRequirement;
public sealed class OwnerOnlyRequirement : IAuthorizationRequirement;
public sealed class GymGoerSelfRequirement : IAuthorizationRequirement;
public sealed class FirebaseOnboardingRequirement : IAuthorizationRequirement;

public sealed class AppAuthorizationHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var hasVerifiedFirebaseIdentity = FirebaseClaimTypes.TryGetVerifiedIdentity(
            context.User,
            out _,
            out _);
        var hasResolvedAppIdentity = HasInternalClaim(
            context,
            AppClaimTypes.IdentityResolved,
            bool.TrueString);
        var role = FindInternalClaim(context, AppClaimTypes.AppRole)?.Value;
        var hasAppUserId = int.TryParse(
            FindInternalClaim(context, AppClaimTypes.AppUserId)?.Value,
            out var appUserId)
            && appUserId > 0;
        var hasMemberId = int.TryParse(
            FindInternalClaim(context, AppClaimTypes.AppMemberId)?.Value,
            out var memberId)
            && memberId > 0;
        var hasConsistentRoleLink = role switch
        {
            nameof(UserRole.Administrator) or nameof(UserRole.Receptionist) => !hasMemberId,
            nameof(UserRole.GymGoer) => hasMemberId,
            _ => false
        };

        foreach (var requirement in context.PendingRequirements.ToArray())
        {
            switch (requirement)
            {
                case FirebaseOnboardingRequirement when hasVerifiedFirebaseIdentity:
                    context.Succeed(requirement);
                    break;

                case ActiveAppUserRequirement
                    when hasVerifiedFirebaseIdentity
                        && hasResolvedAppIdentity
                        && hasAppUserId
                        && hasConsistentRoleLink:
                    context.Succeed(requirement);
                    break;

                case BackOfficeRequirement
                    when hasVerifiedFirebaseIdentity
                        && hasResolvedAppIdentity
                        && hasAppUserId
                        && !hasMemberId
                        && role is nameof(UserRole.Administrator) or nameof(UserRole.Receptionist):
                    context.Succeed(requirement);
                    break;

                case OwnerOnlyRequirement
                    when hasVerifiedFirebaseIdentity
                        && hasResolvedAppIdentity
                        && hasAppUserId
                        && !hasMemberId
                        && role == nameof(UserRole.Administrator):
                    context.Succeed(requirement);
                    break;

                case GymGoerSelfRequirement
                    when hasVerifiedFirebaseIdentity
                        && hasResolvedAppIdentity
                        && hasAppUserId
                        && hasMemberId
                        && role == nameof(UserRole.GymGoer):
                    context.Succeed(requirement);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private static System.Security.Claims.Claim? FindInternalClaim(
        AuthorizationHandlerContext context,
        string type) => context.User.Claims.FirstOrDefault(claim =>
            claim.Type == type
            && claim.Issuer == AppClaimTypes.InternalIssuer
            && claim.OriginalIssuer == AppClaimTypes.InternalIssuer);

    private static bool HasInternalClaim(
        AuthorizationHandlerContext context,
        string type,
        string value) => context.User.Claims.Any(claim =>
            claim.Type == type
            && claim.Value == value
            && claim.Issuer == AppClaimTypes.InternalIssuer
            && claim.OriginalIssuer == AppClaimTypes.InternalIssuer);
}
