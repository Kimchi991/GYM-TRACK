using System.Reflection;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class OwnedEndpointAuthorizationTests
{
    public static IEnumerable<object[]> FrozenPolicyCases()
    {
        yield return Case<AuthController>(nameof(AuthController.SyncUser), Policies.FirebaseOnboarding);
        yield return Case<AuthController>(nameof(AuthController.ActivateApp), Policies.FirebaseOnboarding);
        yield return Case<DashboardController>(nameof(DashboardController.GetMetrics), Policies.BackOffice);
        yield return Case<MembersController>(nameof(MembersController.GetAll), Policies.BackOffice);
        yield return Case<MembersController>(nameof(MembersController.GetById), Policies.BackOffice);
        yield return Case<MembersController>(nameof(MembersController.GetByQRCode), Policies.BackOffice);
        yield return Case<MembersController>(nameof(MembersController.Create), Policies.BackOffice);
        yield return Case<MembersController>(nameof(MembersController.Update), Policies.BackOffice);
        yield return Case<MembersController>(nameof(MembersController.Search), Policies.BackOffice);
        yield return Case<MembersController>(nameof(MembersController.Delete), Policies.OwnerOnly);
        yield return Case<MembersController>(nameof(MembersController.CreateMemberInvite), Policies.BackOffice);
        yield return Case<MembersController>(nameof(MembersController.GetMemberInviteStatus), Policies.BackOffice);
        yield return Case<MembersController>(nameof(MembersController.RevokeMemberInvite), Policies.BackOffice);
        yield return Case<PlansController>(nameof(PlansController.GetAll), Policies.BackOffice);
        yield return Case<PlansController>(nameof(PlansController.GetById), Policies.BackOffice);
        yield return Case<PlansController>(nameof(PlansController.Create), Policies.OwnerOnly);
        yield return Case<PlansController>(nameof(PlansController.Update), Policies.OwnerOnly);
        yield return Case<PlansController>(nameof(PlansController.Delete), Policies.OwnerOnly);
        yield return Case<SubscriptionsController>(nameof(SubscriptionsController.GetById), Policies.BackOffice);
        yield return Case<SubscriptionsController>(nameof(SubscriptionsController.GetByMemberId), Policies.BackOffice);
        yield return Case<SubscriptionsController>(nameof(SubscriptionsController.Subscribe), Policies.BackOffice);
        yield return Case<SubscriptionsController>(nameof(SubscriptionsController.Pause), Policies.BackOffice);
        yield return Case<SubscriptionsController>(nameof(SubscriptionsController.Resume), Policies.BackOffice);
        yield return Case<SubscriptionsController>(nameof(SubscriptionsController.Renew), Policies.BackOffice);
        yield return Case<PaymentsController>(nameof(PaymentsController.GetById), Policies.BackOffice);
        yield return Case<PaymentsController>(nameof(PaymentsController.GetByMemberId), Policies.BackOffice);
        yield return Case<PaymentsController>(nameof(PaymentsController.Create), Policies.BackOffice);
        yield return Case<PaymentsController>(nameof(PaymentsController.Search), Policies.BackOffice);
        yield return Case<PaymentsController>(nameof(PaymentsController.Refund), Policies.OwnerOnly);
        yield return Case<SettingsController>(nameof(SettingsController.GetAllSettings), Policies.BackOffice);
        yield return Case<SettingsController>(nameof(SettingsController.UpdateSetting), Policies.OwnerOnly);
        yield return Case<NotificationsController>(nameof(NotificationsController.GetNotifications), Policies.BackOffice);
        yield return Case<NotificationsController>(nameof(NotificationsController.MarkAsRead), Policies.BackOffice);
        yield return Case<UsersController>(nameof(UsersController.CreateUserInvite), Policies.OwnerOnly);
        yield return Case<UsersController>(nameof(UsersController.GetUserInviteStatus), Policies.OwnerOnly);
        yield return Case<UsersController>(nameof(UsersController.RevokeUserInvite), Policies.OwnerOnly);
        yield return Case<MeController>(nameof(MeController.GetCurrentProfile), Policies.ActiveAppUser);
    }

    [Theory]
    [MemberData(nameof(FrozenPolicyCases))]
    public void Owned_action_has_required_named_policy(
        Type controllerType,
        string actionName,
        string requiredPolicy)
    {
        var action = controllerType.GetMethod(actionName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(action);
        var policies = GetEffectiveAuthorizeAttributes(controllerType, action!)
            .Select(attribute => attribute.Policy)
            .ToArray();

        Assert.Contains(requiredPolicy, policies);
        Assert.DoesNotContain(GetEffectiveAuthorizeAttributes(controllerType, action!), attribute =>
            !string.IsNullOrWhiteSpace(attribute.Roles));
    }

    [Fact]
    public void Repository_business_actions_have_explicit_named_policies_without_role_attributes()
    {
        var controllerAssembly = typeof(AuthController).Assembly;
        var violations = new List<string>();
        var controllers = controllerAssembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .Where(type => !type.Name.Contains("Attendance", StringComparison.Ordinal)
                && !type.Name.Contains("Reports", StringComparison.Ordinal));

        foreach (var controller in controllers)
        {
            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(IsHttpAction))
            {
                if (action.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null)
                {
                    continue;
                }

                var attributes = GetEffectiveAuthorizeAttributes(controller, action).ToArray();
                if (attributes.Length == 0)
                {
                    violations.Add($"{controller.Name}.{action.Name}: missing explicit authorization metadata");
                    continue;
                }

                if (attributes.Any(attribute => string.IsNullOrWhiteSpace(attribute.Policy)))
                {
                    violations.Add($"{controller.Name}.{action.Name}: bare Authorize metadata");
                }

                if (attributes.Any(attribute => !string.IsNullOrWhiteSpace(attribute.Roles)))
                {
                    violations.Add($"{controller.Name}.{action.Name}: token role-based metadata");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static object[] Case<TController>(string actionName, string policy) =>
        new object[] { typeof(TController), actionName, policy };

    private static IEnumerable<AuthorizeAttribute> GetEffectiveAuthorizeAttributes(
        Type controllerType,
        MethodInfo action) =>
        controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true));

    private static bool IsHttpAction(MethodInfo method) => method
        .GetCustomAttributes(inherit: true)
        .Any(attribute => attribute.GetType().Name.StartsWith("Http", StringComparison.Ordinal)
            && attribute.GetType().Name.EndsWith("Attribute", StringComparison.Ordinal));
}
