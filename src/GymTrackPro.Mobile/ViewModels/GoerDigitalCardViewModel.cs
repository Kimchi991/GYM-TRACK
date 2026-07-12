using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class GoerDigitalCardViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial GoerDigitalCardDto CardData { get; set; } = new();

    public GoerDigitalCardViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Digital Card";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var response = await _apiService.GetGoerDigitalCardAsync();
            if (response.Success && response.Data != null)
            {
                CardData = response.Data;
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", response.Message ?? "Failed to load card", "OK");
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
