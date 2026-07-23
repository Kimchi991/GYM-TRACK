using GymTrackPro.Mobile.ViewModels;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.Views;

public partial class TrainerDashboardPage : ContentPage
{
    private readonly TrainerDashboardViewModel _viewModel;

    public TrainerDashboardPage(TrainerDashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadClientsAsync();
    }
}
