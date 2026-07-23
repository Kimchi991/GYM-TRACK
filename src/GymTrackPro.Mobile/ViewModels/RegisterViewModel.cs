using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Helpers;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;
using Firebase.Auth;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.ViewModels;

[QueryProperty(nameof(Mode), "mode")]
public partial class RegisterViewModel : BaseViewModel
{
    private readonly IFirebaseAuthService _firebaseAuthService;
    private readonly IApiService _apiService;
    private readonly Func<GoerAppShell> _goerShellFactory;
    private readonly Func<AppShell> _appShellFactory;
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

    // --- Application Fields ---
    [ObservableProperty]
    public partial string FullName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ContactNumber { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EmergencyContact { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOneDayPass { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMembershipSelected))]
    public partial MembershipPlanResponseDto? SelectedPlan { get; set; }

    public bool IsMembershipSelected => !IsOneDayPass;

    [ObservableProperty]
    public partial PaymentMethod SelectedPaymentMethod { get; set; }

    [ObservableProperty]
    public partial string PaymentReferenceNumber { get; set; } = string.Empty;

    public ObservableCollection<MembershipPlanResponseDto> MembershipPlans { get; } = new();
    
    public ObservableCollection<PaymentMethod> PaymentMethods { get; } = new(Enum.GetValues(typeof(PaymentMethod)).Cast<PaymentMethod>());
    // -------------------------

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
        Func<AppShell> appShellFactory,
        IRootNavigationService rootNavigationService)
    {
        _firebaseAuthService = firebaseAuthService;
        _apiService = apiService;
        _goerShellFactory = goerShellFactory;
        _appShellFactory = appShellFactory;
        _rootNavigationService = rootNavigationService
            ?? throw new ArgumentNullException(nameof(rootNavigationService));
        Title = "Create Account";

        IsOneDayPass = true; // Default to walk-in pass
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

    partial void OnIsOneDayPassChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMembershipSelected));
        if (value)
        {
            SelectedPlan = null;
        }
    }

    [RelayCommand]
    public async Task LoadPlansAsync()
    {
        if (MembershipPlans.Any()) return; // already loaded

        try
        {
            var response = await _apiService.GetPlansAsync();
            if (response.Success && response.Data != null)
            {
                MembershipPlans.Clear();
                foreach (var plan in response.Data.Where(p => p.Status == "Active"))
                {
                    MembershipPlans.Add(plan);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load plans: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(RepeatPassword))
        {
            ErrorMessage = "Email and both password fields are required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(FullName) || string.IsNullOrWhiteSpace(ContactNumber))
        {
            ErrorMessage = "Full Name and Contact Number are required for the application.";
            return;
        }

        if (!IsOneDayPass && SelectedPlan == null)
        {
            ErrorMessage = "Please select a Membership Plan.";
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ErrorMessage = "Please enter a valid email address.";
            return;
        }

        if (Password != RepeatPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        var submitDto = new SubmitApplicationDto
        {
            FullName = FullName,
            ContactNumber = ContactNumber,
            EmailAddress = Email.Trim(),
            EmergencyContact = EmergencyContact,
            IsOneDayPass = IsOneDayPass,
            SelectedPlanID = SelectedPlan?.PlanID,
            PaymentMethod = SelectedPaymentMethod,
            PaymentReferenceNumber = PaymentReferenceNumber
        };

        try
        {
            await _firebaseAuthService.RegisterAsync(Email.Trim(), Password);
            
            // Firebase succeeded, now submit application
            await SubmitApplicationInternalAsync(submitDto);
            
            TransitionToAwaitingVerification();
        }
        catch (FirebaseRegistrationVerificationPendingException)
        {
            // Registration succeeded but email failed to send. Submit application anyway.
            await SubmitApplicationInternalAsync(submitDto);
            
            TransitionToAwaitingVerification();
            if (await _firebaseAuthService.HasSessionAsync())
            {
                ErrorMessage = "Your account was created, but the verification email could not be sent. Tap 'Resend Verification Email' to continue.";
            }
        }
        catch (FirebaseAuthException exception) when (exception.Reason == AuthErrorReason.EmailExists)
        {
            // Fallback: The user already exists in Firebase. Let's just submit the application for them.
            try
            {
                await SubmitApplicationInternalAsync(submitDto);
                Password = string.Empty;
                RepeatPassword = string.Empty;
                ErrorMessage = "Firebase account already exists. We have submitted your application. Use 'Sign In' below, complete email verification and invite activation.";
            }
            catch (Exception appEx)
            {
                ErrorMessage = $"Account exists, but failed to submit application: {appEx.Message}";
            }
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

    private async Task SubmitApplicationInternalAsync(SubmitApplicationDto dto)
    {
        var result = await _apiService.SubmitApplicationAsync(dto);
        if (!result.Success)
        {
            throw new Exception(result.Message ?? "Failed to submit application to GymTrackPro.");
        }
    }

    private void TransitionToAwaitingVerification()
    {
        Password = string.Empty;
        RepeatPassword = string.Empty;
        IsAwaitingVerification = true;
        SuccessMessage = "Account created & Application submitted! Verify your email, wait for receptionist approval, then return here to activate with your Invite Code.";
    }

    [RelayCommand]
    private async Task ResendVerificationAsync()
    {
        if (IsBusy || IsResendingVerification) return;

        var now = DateTimeOffset.UtcNow;
        if (now < _resendAvailableAtUtc)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((_resendAvailableAtUtc - now).TotalSeconds));
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
                ErrorMessage = "Sign in to your Firebase account first, then return here to resend verification.";
                return;
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _resendAvailableAtUtc = DateTimeOffset.UtcNow.Add(ResendCooldown);
            await _firebaseAuthService.SendEmailVerificationAsync(timeoutCts.Token);
            SuccessMessage = "Verification email requested. Check your inbox and spam folder before trying again.";
        }
        catch (Exception)
        {
            ErrorMessage = "The verification email could not be sent right now. Wait briefly, then try again or sign in again.";
        }
        finally
        {
            IsResendingVerification = false;
        }
    }

    [RelayCommand]
    private async Task VerifyAndActivateAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(InviteCode))
        {
            ErrorMessage = "An invite code is required to activate app access.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

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
                ErrorMessage = string.IsNullOrWhiteSpace(response.Message) ? "The invite could not be activated." : response.Message;
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

        if (!_rootNavigationService.TrySetRoot(_appShellFactory()))
        {
            ErrorMessage = "Account activated, but the staff dashboard could not be displayed.";
        }
    }
}
