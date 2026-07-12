using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class GoerDashboardViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private readonly IFirebaseAuthService _authService;

    [ObservableProperty]
    public partial GoerDashboardDto DashboardData { get; set; } = new();

    public GoerDashboardViewModel(IApiService apiService, IFirebaseAuthService authService)
    {
        _apiService = apiService;
        _authService = authService;
        Title = "My Dashboard";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var response = await _apiService.GetGoerDashboardAsync();
            if (response.Success && response.Data != null)
            {
                DashboardData = response.Data;
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", response.Message ?? "Failed to load dashboard", "OK");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _authService.LogoutAsync();
        Application.Current.MainPage = new AppShell();
    }
}
