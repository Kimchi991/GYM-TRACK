using GymTrackPro.Mobile.ViewModels;

namespace GymTrackPro.Mobile.Views;

public partial class AttendancePage : ContentPage
{
    public AttendancePage(AttendanceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is AttendanceViewModel vm)
        {
            await vm.LoadTodayCheckInsAsync();
        }
    }
}
