using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class ResetPasswordViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Token { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    public ResetPasswordViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Reset Password";
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(NewPassword))
        {
            ErrorMessage = "All fields are required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var resetDto = new ResetPasswordDto
            {
                Email = Email,
                Token = Token,
                NewPassword = NewPassword
            };

            var result = await _apiService.ResetPasswordAsync(resetDto);
            if (result.Success)
            {
                SuccessMessage = "Password reset successfully! You can now log in.";
                Email = string.Empty;
                Token = string.Empty;
                NewPassword = string.Empty;
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Reset failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoToLoginAsync()
    {
        await Shell.Current.GoToAsync("///login");
    }
}
