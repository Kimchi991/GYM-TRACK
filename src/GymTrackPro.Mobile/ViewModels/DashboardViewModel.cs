using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private readonly IFirebaseAuthService _authService;

    [ObservableProperty]
    public partial int CheckedInCount { get; set; }

    [ObservableProperty]
    public partial int ActiveMembersCount { get; set; }

    [ObservableProperty]
    public partial decimal RevenueToday { get; set; }

    [ObservableProperty]
    public partial decimal RevenueThisMonth { get; set; }

    [ObservableProperty]
    public partial int ExpiringSoonCount { get; set; }

    [ObservableProperty]
    public partial int NewRegistrationsCount { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public DashboardViewModel(IApiService apiService, IFirebaseAuthService authService)
    {
        _apiService = apiService;
        _authService = authService;
        Title = "Dashboard";
    }

    [RelayCommand]
    public async Task LoadDashboardDataAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _apiService.GetDashboardMetricsAsync();
            if (result.Success && result.Data != null)
            {
                CheckedInCount = result.Data.MembersCheckedInCount;
                ActiveMembersCount = result.Data.ActiveMembershipsCount;
                RevenueToday = result.Data.RevenueToday;
                RevenueThisMonth = result.Data.RevenueThisMonth;
                ExpiringSoonCount = result.Data.ExpiringMembershipsCount;
                NewRegistrationsCount = result.Data.NewRegistrationsCount;
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading dashboard: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        bool confirmed = await Shell.Current.DisplayAlertAsync(
            "Sign Out",
            "Are you sure you want to sign out?",
            "Sign Out",
            "Cancel");

        if (!confirmed) return;

        await _authService.LogoutAsync();
        await Shell.Current.GoToAsync("///login");
    }

    [RelayCommand]
    private async Task NavigateToAsync(string route)
    {
        await Shell.Current.GoToAsync($"///{route}");
    }
}
