using GymTrackPro.Mobile.ViewModels;

namespace GymTrackPro.Mobile.Views;

public partial class PaymentsPage : ContentPage
{
    public PaymentsPage(PaymentsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PaymentsViewModel vm)
        {
            await vm.LoadPaymentsAndDataAsync();
        }
    }
}
