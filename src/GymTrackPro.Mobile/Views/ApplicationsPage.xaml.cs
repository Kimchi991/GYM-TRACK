using GymTrackPro.Mobile.ViewModels;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.Views;

public partial class ApplicationsPage : ContentPage
{
    private readonly ApplicationsViewModel _viewModel;

    public ApplicationsPage(ApplicationsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadApplicationsAsync();
    }
}
