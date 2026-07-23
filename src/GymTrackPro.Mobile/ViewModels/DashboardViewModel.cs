using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Mobile.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private readonly IAppLogoutService _logoutService;
    private readonly Func<AppShell> _appShellFactory;
    private readonly IRootNavigationService _rootNavigationService;

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
    public partial int PendingApplicationsCount { get; set; }

    [ObservableProperty]
    public partial bool HasCheckedInMembers { get; set; }

    [ObservableProperty]
    public partial bool HasPlanDistribution { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public ObservableCollection<LiveOccupancyDto> CurrentlyCheckedIn { get; } = new();
    public ObservableCollection<PlanMembershipCountDto> MembershipPlanDistribution { get; } = new();

    [ObservableProperty]
    public partial bool CanManageStaff { get; set; }

    public DashboardViewModel(
        IApiService apiService,
        IAppLogoutService logoutService,
        Func<AppShell> appShellFactory,
        IRootNavigationService rootNavigationService)
    {
        _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
        _logoutService = logoutService ?? throw new ArgumentNullException(nameof(logoutService));
        _appShellFactory = appShellFactory ?? throw new ArgumentNullException(nameof(appShellFactory));
        _rootNavigationService = rootNavigationService
            ?? throw new ArgumentNullException(nameof(rootNavigationService));
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
            var identity = await _apiService.GetCurrentUserForStartupAsync();
            CanManageStaff = identity.Status == StartupIdentityLookupStatus.Success
                && identity.User?.Role == UserRole.Administrator;

            var result = await _apiService.GetDashboardMetricsAsync();
            if (result.Success && result.Data != null)
            {
                CheckedInCount = result.Data.MembersCheckedInCount;
                ActiveMembersCount = result.Data.ActiveMembershipsCount;
                RevenueToday = result.Data.RevenueToday;
                RevenueThisMonth = result.Data.RevenueThisMonth;
                ExpiringSoonCount = result.Data.ExpiringMembershipsCount;
                NewRegistrationsCount = result.Data.NewRegistrationsCount;
                PendingApplicationsCount = result.Data.PendingApplicationsCount;

                CurrentlyCheckedIn.Clear();
                if (result.Data.CurrentlyCheckedIn != null)
                {
                    foreach (var item in result.Data.CurrentlyCheckedIn)
                    {
                        CurrentlyCheckedIn.Add(item);
                    }
                }
                HasCheckedInMembers = CurrentlyCheckedIn.Count > 0;

                MembershipPlanDistribution.Clear();
                if (result.Data.MembershipPlanDistribution != null)
                {
                    foreach (var item in result.Data.MembershipPlanDistribution)
                    {
                        MembershipPlanDistribution.Add(item);
                    }
                }
                HasPlanDistribution = MembershipPlanDistribution.Count > 0;
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            CanManageStaff = false;
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

        var result = await _logoutService.LogoutAsync();
        if (result.AccountDataCleanerRegistered && !result.AccountDataCleared)
        {
            await Shell.Current.DisplayAlertAsync(
                "Signed Out",
                "You were signed out, but some offline data could not be cleared.",
                "OK");
        }

        if (!_rootNavigationService.TrySetRoot(_appShellFactory()))
        {
            ErrorMessage = "Signed out, but the login screen could not be displayed.";
        }
    }

    [RelayCommand]
    private async Task NavigateToAsync(string route)
    {
        await Shell.Current.GoToAsync($"///{route}");
    }

    [RelayCommand]
    private async Task NavigateToStaffProvisioningAsync()
    {
        if (!CanManageStaff)
        {
            return;
        }

        await Shell.Current.GoToAsync("staffprovisioning");
    }
}
