using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace GymTrackPro.Mobile.Tests;

public sealed class MobileCompositionSourceContractTests
{
    [Fact]
    public void Mobile_source_uses_window_root_navigation_and_async_dialogs()
    {
        var mobileRoot = GetMobileRoot();
        var sourceFiles = Directory.GetFiles(
            mobileRoot,
            "*.cs",
            SearchOption.AllDirectories)
            .Where(sourceFile => !IsGeneratedOutput(sourceFile, mobileRoot));

        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain("MainPage", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DisplayAlert(", source, StringComparison.Ordinal);
        }

        var navigator = File.ReadAllText(Path.Combine(
            mobileRoot,
            "Services",
            "MauiRootNavigationService.cs"));
        Assert.Contains("application.Windows[0].Page", navigator, StringComparison.Ordinal);
        Assert.Contains("application.Windows.Count == 0", navigator, StringComparison.Ordinal);

        var dialogs = File.ReadAllText(Path.Combine(
            mobileRoot,
            "Services",
            "MauiAppDialogService.cs"));
        Assert.Contains("DisplayAlertAsync", dialogs, StringComparison.Ordinal);
    }

    [Fact]
    public void Goer_shell_composes_all_pages_through_dependency_injection()
    {
        var mobileRoot = GetMobileRoot();
        var registrations = File.ReadAllText(Path.Combine(mobileRoot, "MauiProgram.cs"));
        var shellCodeBehind = File.ReadAllText(Path.Combine(mobileRoot, "GoerAppShell.xaml.cs"));
        var digitalCardPage = File.ReadAllText(Path.Combine(
            mobileRoot,
            "Views",
            "GoerDigitalCardPage.xaml.cs"));
        var progressPage = File.ReadAllText(Path.Combine(
            mobileRoot,
            "Views",
            "GoerProgressPage.xaml.cs"));

        Assert.Contains("AddTransient<GoerProgressViewModel>()", registrations, StringComparison.Ordinal);
        Assert.Contains("AddTransient<GoerProgressPage>()", registrations, StringComparison.Ordinal);
        Assert.Contains("AddTransient<GoerDigitalCardPage>()", registrations, StringComparison.Ordinal);
        Assert.Contains("ProgressContent.Content = progressPage", shellCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DigitalCardContent.Content = digitalCardPage", shellCodeBehind, StringComparison.Ordinal);
        Assert.Contains("GoerDigitalCardPage(GoerDigitalCardViewModel viewModel)", digitalCardPage, StringComparison.Ordinal);
        Assert.Contains("GoerProgressPage(GoerProgressViewModel viewModel)", progressPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<", digitalCardPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<", progressPage, StringComparison.Ordinal);
    }

    [Fact]
    public void Terminal_firebase_refresh_failure_uses_uid_scoped_session_invalidator()
    {
        var mobileRoot = GetMobileRoot();
        var service = File.ReadAllText(Path.Combine(
            mobileRoot,
            "Services",
            "FirebaseAuthService.cs"));

        var terminalCatch = service.IndexOf(
            "catch (FirebaseAuthException exception) when (IsTerminalSessionFailure(exception))",
            StringComparison.Ordinal);
        var finallyBlock = service.IndexOf("finally", terminalCatch, StringComparison.Ordinal);
        var terminalHandling = service[terminalCatch..finallyBlock];

        Assert.Contains("var firebaseUid = user.Uid", terminalHandling, StringComparison.Ordinal);
        Assert.Contains("_sessionInvalidator", terminalHandling, StringComparison.Ordinal);
        Assert.Contains("CancellationToken.None", terminalHandling, StringComparison.Ordinal);
        Assert.Contains("_client.SignOut()", terminalHandling, StringComparison.Ordinal);
        Assert.DoesNotContain(".SignOutAsync(", terminalHandling, StringComparison.Ordinal);
    }

    [Fact]
    public void Android_firebase_auth_is_rest_only_and_ignores_native_google_services_file()
    {
        var mobileRoot = GetMobileRoot();
        var project = XDocument.Load(Path.Combine(mobileRoot, "GymTrackPro.Mobile.csproj"));
        var elementValues = project.Descendants()
            .ToLookup(element => element.Name.LocalName, element => element.Value.Trim());

        Assert.Contains("com.companyname.gymtrackpro.mobile", elementValues["ApplicationId"]);
        Assert.Empty(elementValues["GoogleServicesJson"]);

        var googleServicesRemoval = project.Descendants()
            .Single(element =>
                element.Name.LocalName == "None" &&
                string.Equals(
                    (string?)element.Attribute("Remove"),
                    "google-services.json",
                    StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(googleServicesRemoval);

        var packageNames = project.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(package => package is not null)
            .ToArray();
        Assert.Contains("FirebaseAuthentication.net", packageNames);
        Assert.DoesNotContain(packageNames, package =>
            package!.Contains("Firebase.Messaging", StringComparison.OrdinalIgnoreCase) ||
            package.Contains("GooglePlayServices", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("GoerDashboardPage.xaml", 9)]
    [InlineData("GoerProgressPage.xaml", 3)]
    [InlineData("GoerDigitalCardPage.xaml", 1)]
    public void Goer_pages_use_borders_instead_of_frames(
        string fileName,
        int expectedBorderCount)
    {
        var mobileRoot = GetMobileRoot();
        var document = XDocument.Load(Path.Combine(mobileRoot, "Views", fileName));
        var elementNames = document.Descendants().Select(element => element.Name.LocalName).ToArray();

        Assert.DoesNotContain("Frame", elementNames);
        Assert.Equal(expectedBorderCount, elementNames.Count(name => name == "Border"));
    }

    [Fact]
    public void Persisted_session_routing_is_guarded_and_can_use_uid_scoped_goer_cache()
    {
        var mobileRoot = GetMobileRoot();
        var shell = File.ReadAllText(Path.Combine(mobileRoot, "AppShell.xaml.cs"));

        Assert.Contains("_isRoutingPersistedSession", shell, StringComparison.Ordinal);
        Assert.Contains("try", shell, StringComparison.Ordinal);
        Assert.Contains("catch (Exception)", shell, StringComparison.Ordinal);
        Assert.Contains("finally", shell, StringComparison.Ordinal);
        Assert.Contains("_isRoutingPersistedSession = true", shell, StringComparison.Ordinal);
        Assert.Contains("_isRoutingPersistedSession = false", shell, StringComparison.Ordinal);
        Assert.Contains("GetGoerDashboardAsync(firebaseUid)", shell, StringComparison.Ordinal);
        Assert.Contains("TryRouteToCachedGoerShellAsync", shell, StringComparison.Ordinal);
        Assert.Contains(
            "identity.Status == StartupIdentityLookupStatus.Unavailable",
            shell,
            StringComparison.Ordinal);
        Assert.Contains("InvalidateRejectedSessionAsync", shell, StringComparison.Ordinal);
        Assert.Contains("_logoutService.LogoutAsync", shell, StringComparison.Ordinal);

        var unavailableBranch = shell.IndexOf(
            "identity.Status == StartupIdentityLookupStatus.Unavailable",
            StringComparison.Ordinal);
        var invalidation = shell.IndexOf(
            "await InvalidateRejectedSessionAsync()",
            unavailableBranch,
            StringComparison.Ordinal);
        var branchSource = shell[unavailableBranch..invalidation];
        Assert.Contains("TryRouteToCachedGoerShellAsync", branchSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Owner_staff_provisioning_page_is_di_composed_and_has_no_role_or_password_input()
    {
        var mobileRoot = GetMobileRoot();
        var registrations = File.ReadAllText(Path.Combine(mobileRoot, "MauiProgram.cs"));
        var shell = File.ReadAllText(Path.Combine(mobileRoot, "AppShell.xaml.cs"));
        var dashboard = XDocument.Load(Path.Combine(mobileRoot, "Views", "DashboardPage.xaml"));
        var page = XDocument.Load(Path.Combine(mobileRoot, "Views", "StaffProvisioningPage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(
            mobileRoot,
            "Views",
            "StaffProvisioningPage.xaml.cs"));

        Assert.Contains("AddTransient<StaffProvisioningViewModel>()", registrations, StringComparison.Ordinal);
        Assert.Contains("AddTransient<StaffProvisioningPage>()", registrations, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IAppClipboardService, MauiAppClipboardService>()", registrations, StringComparison.Ordinal);
        Assert.Contains("Routing.RegisterRoute(\"staffprovisioning\"", shell, StringComparison.Ordinal);
        var staffButton = dashboard.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && string.Equals(
                    (string?)element.Attribute("Command"),
                    "{Binding NavigateToStaffProvisioningCommand}",
                    StringComparison.Ordinal));
        Assert.Equal("{Binding CanManageStaff}", (string?)staffButton.Attribute("IsVisible"));
        Assert.Equal("Add Receptionist", staffButton.Attributes().Single(attribute =>
            attribute.Name.LocalName == "SemanticProperties.Description").Value);
        Assert.False(string.IsNullOrWhiteSpace(staffButton.Attributes().Single(attribute =>
            attribute.Name.LocalName == "SemanticProperties.Hint").Value));
        Assert.Contains("StaffProvisioningPage(StaffProvisioningViewModel viewModel)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("viewModel.Deactivate()", codeBehind, StringComparison.Ordinal);

        var dashboardViewModel = File.ReadAllText(Path.Combine(
            mobileRoot,
            "ViewModels",
            "DashboardViewModel.cs"));
        var navigationStart = dashboardViewModel.IndexOf(
            "NavigateToStaffProvisioningAsync",
            StringComparison.Ordinal);
        var navigation = dashboardViewModel[navigationStart..];
        Assert.Contains("if (!CanManageStaff)", navigation, StringComparison.Ordinal);

        var entries = page.Descendants()
            .Where(element => element.Name.LocalName == "Entry")
            .Select(element => (string?)element.Attribute("Text"))
            .ToArray();
        Assert.Contains("{Binding FirstName}", entries);
        Assert.Contains("{Binding LastName}", entries);
        Assert.Contains("{Binding Email}", entries);
        Assert.Contains("{Binding Purpose}", entries);
        Assert.DoesNotContain(entries, binding =>
            binding?.Contains("Role", StringComparison.OrdinalIgnoreCase) == true
            || binding?.Contains("Password", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Goer_profile_picture_is_loaded_as_authenticated_bytes_with_default_fallback()
    {
        var mobileRoot = GetMobileRoot();
        var viewModel = File.ReadAllText(Path.Combine(
            mobileRoot,
            "ViewModels",
            "GoerDashboardViewModel.cs"));
        var page = File.ReadAllText(Path.Combine(
            mobileRoot,
            "Views",
            "GoerDashboardPage.xaml"));

        Assert.Contains("GetCurrentProfilePictureForRefreshAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("ImageSource.FromStream", viewModel, StringComparison.Ordinal);
        Assert.Contains("ProfilePictureSource = null", viewModel, StringComparison.Ordinal);
        Assert.Contains("Source=\"{Binding ProfilePictureSource}\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("me/profile-picture", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Goer_operational_refresh_fails_closed_before_sync_or_cache_fallback()
    {
        var mobileRoot = GetMobileRoot();
        var viewModel = File.ReadAllText(Path.Combine(
            mobileRoot,
            "ViewModels",
            "GoerDashboardViewModel.cs"));

        var refreshStart = viewModel.IndexOf(
            "private async Task RefreshCoreAsync()",
            StringComparison.Ordinal);
        var profileStart = viewModel.IndexOf(
            "private async Task<bool> LoadProfilePictureAsync()",
            refreshStart,
            StringComparison.Ordinal);
        var refresh = viewModel[refreshStart..profileStart];

        var authorizationLookup = refresh.IndexOf(
            "GetGoerDashboardForRefreshAsync",
            StringComparison.Ordinal);
        var sync = refresh.IndexOf("SyncPendingOperationsAsync", StringComparison.Ordinal);
        var refreshedDashboard = refresh.IndexOf(
            "GetGoerDashboardForRefreshAsync",
            authorizationLookup + 1,
            StringComparison.Ordinal);
        var refreshedCurrent = refresh.IndexOf(
            "GetGoerCurrentAttendanceForRefreshAsync",
            StringComparison.Ordinal);
        var refreshedHistory = refresh.IndexOf(
            "GetGoerAttendanceHistoryForRefreshAsync",
            StringComparison.Ordinal);
        Assert.True(
            authorizationLookup >= 0
            && sync > authorizationLookup
            && refreshedDashboard > sync
            && refreshedCurrent > sync
            && refreshedHistory > sync);
        Assert.Contains("HandleOperationalFailureAsync", refresh, StringComparison.Ordinal);
        Assert.Contains("GetGoerCurrentAttendanceForRefreshAsync", refresh, StringComparison.Ordinal);
        Assert.Contains("GetGoerAttendanceHistoryForRefreshAsync", refresh, StringComparison.Ordinal);

        Assert.Contains("OperationalResourceStatus.Unavailable", viewModel, StringComparison.Ordinal);
        Assert.Contains("InvalidateRejectedSessionAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsSessionRejected", viewModel, StringComparison.Ordinal);

        var profile = viewModel[profileStart..];
        Assert.Contains("GetCurrentProfilePictureForRefreshAsync", profile, StringComparison.Ordinal);
        Assert.Contains("OperationalResourceStatus.Rejected", profile, StringComparison.Ordinal);
        Assert.Contains("await InvalidateRejectedSessionAsync()", profile, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "OperationalResourceStatus.InvalidResponse\n            or OperationalResourceStatus.Rejected",
            profile,
            StringComparison.Ordinal);
    }

    private static string GetMobileRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "..",
            "GymTrackPro.Mobile"));

    private static bool IsGeneratedOutput(string sourceFile, string mobileRoot)
    {
        var relativePath = Path.GetRelativePath(mobileRoot, sourceFile);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "artifacts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, ".artifacts", StringComparison.OrdinalIgnoreCase));
    }
}
