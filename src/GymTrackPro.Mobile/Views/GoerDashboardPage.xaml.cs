using GymTrackPro.Mobile.ViewModels;
using GymTrackPro.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymTrackPro.Mobile.Views;

public partial class GoerDashboardPage : ContentPage
{
    private GoerDashboardViewModel _viewModel;

    public GoerDashboardPage()
    {
        InitializeComponent();
        
        var apiService = Handler?.MauiContext?.Services.GetService<IApiService>() 
            ?? Application.Current?.MainPage?.Handler?.MauiContext?.Services.GetService<IApiService>();
        var authService = Handler?.MauiContext?.Services.GetService<IFirebaseAuthService>()
            ?? Application.Current?.MainPage?.Handler?.MauiContext?.Services.GetService<IFirebaseAuthService>();
            
        _viewModel = new GoerDashboardViewModel(apiService, authService);
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
