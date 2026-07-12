using GymTrackPro.Mobile.ViewModels;
using GymTrackPro.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymTrackPro.Mobile.Views;

public partial class GoerDashboardPage : ContentPage
{
    private GoerDashboardViewModel _viewModel;

    public GoerDashboardPage(GoerDashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.LoadDataCommand.CanExecute(null))
        {
            _viewModel.LoadDataCommand.Execute(null);
        }
    }
}
