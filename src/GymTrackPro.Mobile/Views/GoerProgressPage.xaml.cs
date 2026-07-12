using GymTrackPro.Mobile.ViewModels;
using GymTrackPro.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymTrackPro.Mobile.Views;

public partial class GoerProgressPage : ContentPage
{
    private GoerProgressViewModel _viewModel;

    public GoerProgressPage()
    {
        InitializeComponent();
        
        var apiService = Handler?.MauiContext?.Services.GetService<IApiService>() 
            ?? Application.Current?.MainPage?.Handler?.MauiContext?.Services.GetService<IApiService>();
            
        _viewModel = new GoerProgressViewModel(apiService);
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
