using GymTrackPro.Mobile.ViewModels;

namespace GymTrackPro.Mobile.Views;

public partial class MembersPage : ContentPage
{
    public MembersPage(MembersViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MembersViewModel vm)
        {
            await vm.LoadMembersAsync();
        }
    }
}
