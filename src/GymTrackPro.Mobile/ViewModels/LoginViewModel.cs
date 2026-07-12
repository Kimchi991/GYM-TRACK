using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Helpers;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.Constants;

namespace GymTrackPro.Mobile.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private readonly IFirebaseAuthService _firebaseAuthService;
    private readonly IAppLogoutService _logoutService;
    private readonly Func<GoerAppShell> _goerShellFactory;
    private readonly Func<AppShell> _appShellFactory;
    private readonly IRootNavigationService _rootNavigationService;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPasswordHidden { get; set; } = true;

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordHidden = !IsPasswordHidden;
    }

    public LoginViewModel(
        IApiService apiService,
        IFirebaseAuthService firebaseAuthService,
        IAppLogoutService logoutService,
        Func<GoerAppShell> goerShellFactory,
        Func<AppShell> appShellFactory,
        IRootNavigationService rootNavigationService)
    {
        _apiService = apiService;
        _firebaseAuthService = firebaseAuthService;
        _logoutService = logoutService;
        _goerShellFactory = goerShellFactory;
        _appShellFactory = appShellFactory;
        _rootNavigationService = rootNavigationService
            ?? throw new ArgumentNullException(nameof(rootNavigationService));
        Title = "Login";
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Email and password are required.";
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ErrorMessage = "Please enter a valid email address.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            // Always clear any stale cached Firebase session before a new login.
            // FirebaseAuthentication.net persists the last session in SecureStorage;
            // if a previous account is still cached, SignInWithEmailAndPasswordAsync
            // throws "identity operation conflicts with existing account state".
            // If there is nothing to sign out, this is a safe no-op.
            try { await _logoutService.LogoutAsync(); } catch { /* no-op: clean slate is the goal */ }

            var firebaseToken = await _firebaseAuthService.LoginAsync(Email, Password);
            var result = await _apiService.SyncUserWithBackendAsync(firebaseToken);
            if (result.Success && result.Data is not null)
            {
                if (result.Data.Role == GymTrackPro.Shared.Enums.UserRole.GymGoer)
                {
                    if (!_rootNavigationService.TrySetRoot(_goerShellFactory()))
                    {
                        ErrorMessage = "Signed in, but the member dashboard could not be displayed.";
                    }
                }
                else
                {
                    if (!_rootNavigationService.TrySetRoot(_appShellFactory()))
                    {
                        ErrorMessage = "Signed in, but the staff dashboard could not be displayed.";
                    }
                }
            }
            else
            {
                ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? "The app could not verify this account."
                    : result.Message;
                if (IsPendingActivation(result.ErrorCode, result.Message))
                {
                    await Shell.Current.GoToAsync("register?mode=activate");
                }
                else
                {
                    await _logoutService.LogoutAsync();
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = FirebaseAuthErrorHandler.GetErrorMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsPendingActivation(string? errorCode, string? message) =>
        string.Equals(
            errorCode,
            ErrorCodes.AccountPendingActivation,
            StringComparison.Ordinal)
        || (!string.IsNullOrWhiteSpace(message)
            && message.Contains("pending activation", StringComparison.OrdinalIgnoreCase));


    [RelayCommand]
    private async Task GoToRegisterAsync()
    {
        await Shell.Current.GoToAsync("register");
    }

    [RelayCommand]
    private async Task GoToForgotPasswordAsync()
    {
        await Shell.Current.GoToAsync("forgotpassword");
    }
}
