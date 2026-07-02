using GymTrackPro.Mobile.ViewModels;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.Views;

public partial class PlansPage : ContentPage
{
    private readonly PlansViewModel _viewModel;

    public PlansPage(PlansViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadPlansAsync();
    }
}
