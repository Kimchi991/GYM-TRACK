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

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private string successMessage = string.Empty;

    public ObservableCollection<SystemSettingDto> Settings { get; } = new();

    public SettingsViewModel(IApiService apiService)
    {
        _apiService = apiService;
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
}
