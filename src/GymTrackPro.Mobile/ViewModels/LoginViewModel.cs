using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Helpers;
using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private readonly IFirebaseAuthService _firebaseAuthService;

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

    public LoginViewModel(IApiService apiService, IFirebaseAuthService firebaseAuthService)
    {
        _apiService = apiService;
        _firebaseAuthService = firebaseAuthService;
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

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var firebaseToken = await _firebaseAuthService.LoginAsync(Email, Password);
            var result = await _apiService.SyncUserWithBackendAsync(firebaseToken);
            if (result.Success)
            {
                await Shell.Current.GoToAsync("///dashboard");
            }
            else
            {
                ErrorMessage = result.Message;
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
