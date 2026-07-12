using System.Runtime.CompilerServices;

namespace GymTrackPro.Mobile.Tests;

public sealed class RegistrationRecoveryContractTests
{
    [Fact]
    public void Service_exposes_typed_partial_success_without_signing_out_session()
    {
        var mobileRoot = GetMobileRoot();
        var service = File.ReadAllText(Path.Combine(
            mobileRoot,
            "Services",
            "FirebaseAuthService.cs"));
        var contract = File.ReadAllText(Path.Combine(
            mobileRoot,
            "Services",
            "IFirebaseAuthService.cs"));

        Assert.Contains(
            "FirebaseRegistrationVerificationPendingException",
            service,
            StringComparison.Ordinal);
        Assert.Contains(
            "FirebaseRegistrationVerificationPendingException",
            contract,
            StringComparison.Ordinal);
        var registerStart = service.IndexOf(
            "public async Task<string> RegisterAsync",
            StringComparison.Ordinal);
        var resetStart = service.IndexOf(
            "public Task ResetPasswordAsync",
            registerStart,
            StringComparison.Ordinal);
        var registerMethod = service[registerStart..resetStart];
        Assert.DoesNotContain("SignOut", registerMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void View_model_has_recovery_resend_cooldown_and_verifies_before_activation()
    {
        var mobileRoot = GetMobileRoot();
        var viewModel = File.ReadAllText(Path.GetFullPath(Path.Combine(
            mobileRoot,
            "ViewModels",
            "RegisterViewModel.cs")));

        Assert.Contains(
            "catch (FirebaseRegistrationVerificationPendingException)",
            viewModel,
            StringComparison.Ordinal);
        Assert.Contains("ResendCooldown", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsResendingVerification", viewModel, StringComparison.Ordinal);
        Assert.Contains("SendEmailVerificationAsync", viewModel, StringComparison.Ordinal);

        var verify = viewModel.IndexOf("IsEmailVerifiedAsync", StringComparison.Ordinal);
        var activate = viewModel.IndexOf("ActivateInviteAsync", StringComparison.Ordinal);
        Assert.True(verify >= 0 && activate > verify);
    }

    private static string GetMobileRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "..",
            "GymTrackPro.Mobile"));
}
