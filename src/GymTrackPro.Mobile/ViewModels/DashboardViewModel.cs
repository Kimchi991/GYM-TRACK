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

    [ObservableProperty]
    private int checkedInCount;

    [ObservableProperty]
    private int activeMembersCount;

    [ObservableProperty]
    private decimal revenueToday;

    [ObservableProperty]
    private decimal revenueThisMonth;

    [ObservableProperty]
    private int expiringSoonCount;

    [ObservableProperty]
    private int newRegistrationsCount;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    public DashboardViewModel(IApiService apiService)
    {
        _apiService = apiService;
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
    private async Task NavigateToAsync(string route)
    {
        await Shell.Current.GoToAsync($"///{route}");
    }
}
