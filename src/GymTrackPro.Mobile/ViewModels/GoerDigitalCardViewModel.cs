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
    private readonly IAppDialogService _dialogService;

    [ObservableProperty]
    public partial GoerDigitalCardDto CardData { get; set; } = new();

    public GoerDigitalCardViewModel(IApiService apiService, IAppDialogService dialogService)
    {
        _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
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
                await _dialogService.ShowAlertAsync(
                    "Error",
                    response.Message ?? "Failed to load card",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
