using GymTrackPro.Mobile.ViewModels;

namespace GymTrackPro.Mobile.Views;

public partial class StaffProvisioningPage : ContentPage
{
    public StaffProvisioningPage(StaffProvisioningViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is StaffProvisioningViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    protected override void OnDisappearing()
    {
        if (BindingContext is StaffProvisioningViewModel viewModel)
        {
            viewModel.Deactivate();
        }

        base.OnDisappearing();
    }
}
