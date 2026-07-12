using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private readonly IAppLogoutService _logoutService;
    private readonly Func<AppShell> _appShellFactory;
    private readonly IRootNavigationService _rootNavigationService;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    public ObservableCollection<SystemSettingDto> Settings { get; } = new();

    public SettingsViewModel(
        IApiService apiService,
        IAppLogoutService logoutService,
        Func<AppShell> appShellFactory,
        IRootNavigationService rootNavigationService)
    {
        _apiService = apiService;
        _logoutService = logoutService;
        _appShellFactory = appShellFactory ?? throw new ArgumentNullException(nameof(appShellFactory));
        _rootNavigationService = rootNavigationService
            ?? throw new ArgumentNullException(nameof(rootNavigationService));
        Title = "Settings";
    }

    [RelayCommand]
    public async Task LoadSettingsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var result = await _apiService.GetSettingsAsync();
            if (result.Success && result.Data != null)
            {
                Settings.Clear();
                foreach (var setting in result.Data)
                {
                    Settings.Add(setting);
                }
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading settings: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingAsync(SystemSettingDto setting)
    {
        if (setting == null) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var result = await _apiService.UpdateSettingAsync(setting.SettingKey, setting.SettingValue);
            if (result.Success)
            {
                SuccessMessage = $"Setting '{setting.SettingKey}' updated successfully.";
                await LoadSettingsAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to update setting: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        bool confirm = await Shell.Current.DisplayAlertAsync("Logout", "Are you sure you want to log out?", "Yes", "No");
        if (confirm)
        {
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
    }
}
