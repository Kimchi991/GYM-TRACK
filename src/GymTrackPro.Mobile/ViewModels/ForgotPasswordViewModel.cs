using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Helpers;
using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile.ViewModels;

public partial class ForgotPasswordViewModel : BaseViewModel
{
    private readonly IFirebaseAuthService _firebaseAuthService;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    public ForgotPasswordViewModel(IFirebaseAuthService firebaseAuthService)
    {
        _firebaseAuthService = firebaseAuthService;
        Title = "Forgot Password";
    }

    [RelayCommand]
    private async Task ForgotPasswordAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Email is required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            await _firebaseAuthService.ResetPasswordAsync(Email);
            SuccessMessage = "If the email is registered, a reset token has been dispatched.";
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

    [RelayCommand]
    private async Task GoToResetPageAsync()
    {
        await Shell.Current.GoToAsync("resetpassword");
    }

    [RelayCommand]
    private async Task GoToLoginAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
