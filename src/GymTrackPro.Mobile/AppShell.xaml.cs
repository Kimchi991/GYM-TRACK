using GymTrackPro.Mobile.Views;
using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile;

public partial class AppShell : Shell
{
    private readonly Func<GoerAppShell>? _goerShellFactory;
    private readonly GymTrackPro.Mobile.Services.IFirebaseAuthService? _authService;
    private readonly GymTrackPro.Mobile.Services.IApiService? _apiService;
    private readonly IRootNavigationService? _rootNavigationService;
    private readonly ILocalDatabaseService? _localDatabaseService;
    private readonly IAppLogoutService? _logoutService;
    private bool _isRoutingPersistedSession;

    public AppShell()
    {
        InitializeShell();
    }

    public AppShell(
        Func<GoerAppShell> goerShellFactory,
        GymTrackPro.Mobile.Services.IFirebaseAuthService authService,
        GymTrackPro.Mobile.Services.IApiService apiService,
        IRootNavigationService rootNavigationService,
        ILocalDatabaseService localDatabaseService,
        IAppLogoutService logoutService)
    {
        _goerShellFactory = goerShellFactory
            ?? throw new ArgumentNullException(nameof(goerShellFactory));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
        _rootNavigationService = rootNavigationService
            ?? throw new ArgumentNullException(nameof(rootNavigationService));
        _localDatabaseService = localDatabaseService
            ?? throw new ArgumentNullException(nameof(localDatabaseService));
        _logoutService = logoutService ?? throw new ArgumentNullException(nameof(logoutService));
        InitializeShell();
    }

    private void InitializeShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("forgotpassword", typeof(ForgotPasswordPage));
        Routing.RegisterRoute("resetpassword", typeof(ResetPasswordPage));
        Routing.RegisterRoute("memberdetails", typeof(MemberDetailsPage));
        Routing.RegisterRoute("staffprovisioning", typeof(StaffProvisioningPage));
        Routing.RegisterRoute("plans", typeof(PlansPage));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_isRoutingPersistedSession
            || _authService is null
            || _apiService is null)
        {
            return;
        }

        _isRoutingPersistedSession = true;
        try
        {
            if (!await _authService.HasValidSessionAsync())
            {
                return;
            }

            var identity = await _apiService.GetCurrentUserForStartupAsync();
            if (identity.Status == StartupIdentityLookupStatus.Success
                && identity.User is not null)
            {
                if (identity.User.Role == GymTrackPro.Shared.Enums.UserRole.GymGoer
                    && _goerShellFactory is not null
                    && _rootNavigationService is not null)
                {
                    _rootNavigationService.TrySetRoot(_goerShellFactory());
                }
                else
                {
                    await Shell.Current.GoToAsync("///dashboard");
                }
                return;
            }

            if (identity.Status == StartupIdentityLookupStatus.Unavailable)
            {
                await TryRouteToCachedGoerShellAsync();
                return;
            }

            await InvalidateRejectedSessionAsync();
        }
        catch (Exception)
        {
            // Unexpected failures cannot prove that the SQL application identity is
            // active. Fail closed and leave the existing login shell visible.
            await InvalidateRejectedSessionAsync();
        }
        finally
        {
            _isRoutingPersistedSession = false;
        }
    }

    private async Task InvalidateRejectedSessionAsync()
    {
        if (_logoutService is null)
        {
            return;
        }

        try
        {
            await _logoutService.LogoutAsync(CancellationToken.None);
        }
        catch
        {
            // The login shell remains visible even if low-level session removal fails.
        }
    }

    private async Task<bool> TryRouteToCachedGoerShellAsync()
    {
        if (_authService is null
            || _localDatabaseService is null
            || _goerShellFactory is null
            || _rootNavigationService is null)
        {
            return false;
        }

        var firebaseUid = _authService.GetCurrentUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
        {
            return false;
        }

        try
        {
            await _localDatabaseService.InitializeAsync();
            var cachedDashboard = await _localDatabaseService
                .GetGoerDashboardAsync(firebaseUid);
            return cachedDashboard is not null
                && _rootNavigationService.TrySetRoot(_goerShellFactory());
        }
        catch
        {
            return false;
        }
    }
}
