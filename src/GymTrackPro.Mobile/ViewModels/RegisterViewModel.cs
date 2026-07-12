using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Helpers;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;
using Firebase.Auth;

namespace GymTrackPro.Mobile.ViewModels;

[QueryProperty(nameof(Mode), "mode")]
public partial class RegisterViewModel : BaseViewModel
{
    private readonly IFirebaseAuthService _firebaseAuthService;
    private readonly IApiService _apiService;
    private readonly Func<GoerAppShell> _goerShellFactory;
    private readonly IRootNavigationService _rootNavigationService;
    private Guid _activationOperationId = Guid.NewGuid();
    private DateTimeOffset _resendAvailableAtUtc = DateTimeOffset.MinValue;
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(30);

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RepeatPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InviteCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAwaitingVerification { get; set; }

    [ObservableProperty]
    public partial bool IsActivationOnly { get; set; }

    [ObservableProperty]
    public partial bool IsResendingVerification { get; set; }

    public string Mode
    {
        set => IsActivationOnly = string.Equals(
            value,
            "activate",
            StringComparison.OrdinalIgnoreCase);
    }

    public bool ShowRegistration => !IsActivationOnly && !IsAwaitingVerification;
    public bool ShowActivation => IsActivationOnly || IsAwaitingVerification;

    public RegisterViewModel(
        IFirebaseAuthService firebaseAuthService,
        IApiService apiService,
        Func<GoerAppShell> goerShellFactory,
        IRootNavigationService rootNavigationService)
    {
        _firebaseAuthService = firebaseAuthService;
        _apiService = apiService;
        _goerShellFactory = goerShellFactory;
        _rootNavigationService = rootNavigationService
            ?? throw new ArgumentNullException(nameof(rootNavigationService));
        Title = "Create Account";
    }

    partial void OnInviteCodeChanged(string value) =>
        _activationOperationId = Guid.NewGuid();

    partial void OnIsAwaitingVerificationChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRegistration));
        OnPropertyChanged(nameof(ShowActivation));
    }

    partial void OnIsActivationOnlyChanged(bool value)
    {
        Title = value ? "Activate Account" : "Create Account";
        OnPropertyChanged(nameof(ShowRegistration));
        OnPropertyChanged(nameof(ShowActivation));
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Email)
            || string.IsNullOrWhiteSpace(Password)
            || string.IsNullOrWhiteSpace(RepeatPassword))
        {
            ErrorMessage = "Email and both password fields are required.";
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                Email,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ErrorMessage = "Please enter a valid email address.";
            return;
        }

        if (Password != RepeatPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }

        if (Password.Length < 6
            || !System.Text.RegularExpressions.Regex.IsMatch(Password, @"[A-Z]")
            || !System.Text.RegularExpressions.Regex.IsMatch(Password, @"[a-z]")
            || !System.Text.RegularExpressions.Regex.IsMatch(
                Password,
                @"[!@#$%^&*(),.?""':{}|<>]"))
        {
            ErrorMessage = "Use at least 6 characters with uppercase, lowercase, and a special character.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            await _firebaseAuthService.RegisterAsync(Email.Trim(), Password);
            Password = string.Empty;
            RepeatPassword = string.Empty;
            IsAwaitingVerification = true;
            SuccessMessage =
                "Firebase account created. Verify the email, then return here and tap 'I've Verified - Activate'.";
        }
        catch (FirebaseRegistrationVerificationPendingException)
        {
            Password = string.Empty;
            RepeatPassword = string.Empty;
            IsAwaitingVerification = true;
            if (await _firebaseAuthService.HasSessionAsync())
            {
                ErrorMessage =
                    "Your account was created, but the verification email could not be sent. Tap 'Resend Verification Email' to continue.";
            }
            else
            {
                ErrorMessage =
                    "Your account may already be created. Sign in again, then return to activation and resend verification.";
            }
        }
        catch (FirebaseAuthException exception) when (exception.Reason == AuthErrorReason.EmailExists)
        {
            Password = string.Empty;
            RepeatPassword = string.Empty;
            ErrorMessage =
                "This Firebase account already exists. Use 'Sign In' below, then complete email verification and invite activation.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FirebaseAuthErrorHandler.GetErrorMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResendVerificationAsync()
    {
        if (IsBusy || IsResendingVerification)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _resendAvailableAtUtc)
        {
            var seconds = Math.Max(
                1,
                (int)Math.Ceiling((_resendAvailableAtUtc - now).TotalSeconds));
            ErrorMessage = $"Please wait {seconds} seconds before requesting another verification email.";
            return;
        }

        IsResendingVerification = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;
        try
        {
            if (!await _firebaseAuthService.HasSessionAsync())
            {
                ErrorMessage =
                    "Sign in to your Firebase account first, then return here to resend verification.";
                return;
            }

            // Set the cooldown before the provider call so repeated failures cannot be
            // used to hammer the email endpoint with rapid taps.
            _resendAvailableAtUtc = DateTimeOffset.UtcNow.Add(ResendCooldown);
            await _firebaseAuthService.SendEmailVerificationAsync();
            SuccessMessage =
                "Verification email requested. Check your inbox and spam folder before trying again.";
        }
        catch (Exception)
        {
            ErrorMessage =
                "The verification email could not be sent right now. Wait briefly, then try again or sign in again.";
        }
        finally
        {
            IsResendingVerification = false;
        }
    }

    [RelayCommand]
    private async Task VerifyAndActivateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(InviteCode))
        {
            ErrorMessage = "An invite code is required to activate app access.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        // Prevent infinite loading — timeout after 15 seconds if Firebase token
        // refresh or the API call hangs (network issues, dead semaphore, etc.).
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            if (!await _firebaseAuthService.HasSessionAsync(timeoutCts.Token))
            {
                ErrorMessage = "Sign in first, then return to activate your invite.";
                return;
            }

            if (!await _firebaseAuthService.IsEmailVerifiedAsync(timeoutCts.Token))
            {
                ErrorMessage = "The email is not verified yet. Open the Firebase email link, then try again.";
                return;
            }

            var response = await _apiService.ActivateInviteAsync(new ActivateInviteDto
            {
                InviteCode = InviteCode.Trim(),
                OperationId = _activationOperationId
            }, timeoutCts.Token);

            if (!response.Success || response.Data is null)
            {
                ErrorMessage = string.IsNullOrWhiteSpace(response.Message)
                    ? "The invite could not be activated."
                    : response.Message;
                return;
            }

            SuccessMessage = "Account activated successfully.";
            await RouteAuthenticatedUserAsync(response.Data.Role);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "The activation request timed out. Check your internet connection and try again.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FirebaseAuthErrorHandler.GetErrorMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GoToLoginAsync() => Shell.Current.GoToAsync("..");

    private async Task RouteAuthenticatedUserAsync(UserRole role)
    {
        if (role == UserRole.GymGoer)
        {
            if (!_rootNavigationService.TrySetRoot(_goerShellFactory()))
            {
                ErrorMessage = "Account activated, but the member dashboard could not be displayed.";
            }
            return;
        }

        await Shell.Current.GoToAsync("///dashboard");
    }
}
