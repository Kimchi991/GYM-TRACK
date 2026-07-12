using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Helpers;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Mobile.ViewModels;

public partial class RegisterViewModel : BaseViewModel
{
    private readonly IFirebaseAuthService _firebaseAuthService;
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RepeatPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FirstName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InviteCode { get; set; } = string.Empty;

    public RegisterViewModel(IFirebaseAuthService firebaseAuthService, IApiService apiService)
    {
        _firebaseAuthService = firebaseAuthService;
        _apiService = apiService;
        Title = "Register";
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Email) ||
            string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(RepeatPassword) || 
            string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
        {
            ErrorMessage = "All fields are required.";
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

        if (Password.Length < 6)
        {
            ErrorMessage = "Password must be at least 6 characters long.";
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(Password, @"[A-Z]"))
        {
            ErrorMessage = "Password must contain at least one uppercase letter.";
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(Password, @"[a-z]"))
        {
            ErrorMessage = "Password must contain at least one lowercase letter.";
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(Password, @"[!@#$%^&*(),.?""':{}|<>]"))
        {
            ErrorMessage = "Password must contain at least one special character.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            await _firebaseAuthService.RegisterAsync(Email, Password);

            if (!string.IsNullOrWhiteSpace(InviteCode))
            {
                var firebaseToken = await _firebaseAuthService.LoginAsync(Email, Password);
                _apiService.SetAuthToken(firebaseToken);
                await _apiService.ActivateInviteAsync(new GymTrackPro.Shared.DTOs.ActivateInviteDto
                {
                    InviteCode = InviteCode,
                    OperationId = Guid.NewGuid()
                });
            }

            SuccessMessage = "Registration successful! Please verify your email before logging in.";
            Username = string.Empty;
            Email = string.Empty;
            Password = string.Empty;
            FirstName = string.Empty;
            LastName = string.Empty;
            InviteCode = string.Empty;
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
    private async Task GoToLoginAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
