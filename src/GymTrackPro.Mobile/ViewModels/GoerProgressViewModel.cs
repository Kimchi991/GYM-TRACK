using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class GoerProgressViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial GoerProgressDto ProgressData { get; set; } = new();

    public GoerProgressViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "My Progress";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var currentMonth = DateTime.Now.ToString("yyyy-MM");
            var response = await _apiService.GetGoerProgressAsync(currentMonth);
            if (response.Success && response.Data != null)
            {
                ProgressData = response.Data;
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", response.Message ?? "Failed to load progress", "OK");
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
}
