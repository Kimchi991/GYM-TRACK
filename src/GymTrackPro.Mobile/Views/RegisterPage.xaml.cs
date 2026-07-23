using GymTrackPro.Mobile.ViewModels;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.Views;

public partial class RegisterPage : ContentPage
{
    private readonly RegisterViewModel _viewModel;

    public RegisterPage(RegisterViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadPlansAsync();
    }
}
