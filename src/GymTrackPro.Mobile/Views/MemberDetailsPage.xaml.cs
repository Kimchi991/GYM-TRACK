using GymTrackPro.Mobile.ViewModels;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.Views;

public partial class MemberDetailsPage : ContentPage
{
    private readonly MemberDetailsViewModel _viewModel;

    public MemberDetailsPage(MemberDetailsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadMemberDetailsDataAsync();
    }
}
