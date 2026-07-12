using System.Reflection;
using GymTrackPro.API.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class SensitiveLoggingRegressionTests
{
    [Fact]
    public void Qr_lookup_uses_body_contract_not_route_or_query_value()
    {
        var action = typeof(MembersController).GetMethod(nameof(MembersController.GetByQRCode));
        Assert.NotNull(action);
        var route = action!.GetCustomAttributes<HttpPostAttribute>(inherit: true).Single();
        Assert.Equal("qr/lookup", route.Template);
        Assert.DoesNotContain("{", route.Template, StringComparison.Ordinal);
        var parameter = action.GetParameters().Single();
        Assert.Equal("QrCodeLookupRequestDto", parameter.ParameterType.Name);
        Assert.NotNull(parameter.GetCustomAttribute<FromBodyAttribute>());
    }

    [Fact]
    public void Legacy_reset_and_member_audit_sources_do_not_log_tokens_or_qr_values()
    {
        var root = TestWorkspace.FindRoot();
        var notificationHandler = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GymTrackPro.API",
            "Services",
            "NotificationHandler.cs"));
        var memberService = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GymTrackPro.API",
            "Services",
            "MemberService.cs"));
        var memberRepository = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GymTrackPro.API",
            "Repositories",
            "MemberRepository.cs"));

        Assert.DoesNotContain("{ResetToken}", notificationHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("@event.ResetToken", notificationHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("QR: {member.QRCode}", memberService, StringComparison.Ordinal);
        Assert.DoesNotContain("m.QRCode.Contains(search)", memberRepository, StringComparison.Ordinal);
    }

    [Fact]
    public void Owned_business_paths_do_not_parse_firebase_subject_or_fallback_actor_to_zero()
    {
        var root = TestWorkspace.FindRoot();
        var relativePaths = new[]
        {
            "src/GymTrackPro.API/Services/MemberService.cs",
            "src/GymTrackPro.API/Services/MembershipPlanService.cs",
            "src/GymTrackPro.API/Services/PaymentService.cs",
            "src/GymTrackPro.API/Services/SubscriptionService.cs",
            "src/GymTrackPro.API/Services/SystemSettingService.cs",
            "src/GymTrackPro.API/Controllers/MembersController.cs",
            "src/GymTrackPro.API/Controllers/UsersController.cs"
        };

        foreach (var relativePath in relativePaths)
        {
            var source = File.ReadAllText(Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.DoesNotContain("ClaimTypes.NameIdentifier", source, StringComparison.Ordinal);
            Assert.DoesNotContain("UserId ?? 0", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FirebaseClaimTypes.Subject)?.Value", source, StringComparison.Ordinal);
        }
    }

}
