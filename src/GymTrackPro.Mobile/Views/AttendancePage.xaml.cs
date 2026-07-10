using GymTrackPro.Mobile.ViewModels;
using System.Linq;

namespace GymTrackPro.Mobile.Views;

public partial class AttendancePage : ContentPage
{
    public AttendancePage(AttendanceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        
        barcodeReader.Options = new ZXing.Net.Maui.BarcodeReaderOptions
        {
            Formats = ZXing.Net.Maui.BarcodeFormats.OneDimensional | ZXing.Net.Maui.BarcodeFormats.TwoDimensional,
            AutoRotate = true,
            Multiple = false
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Camera>();
        }

        if (status == PermissionStatus.Granted)
        {
            barcodeReader.IsDetecting = true;
        }

        if (BindingContext is AttendanceViewModel vm)
        {
            await vm.LoadTodayCheckInsAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        barcodeReader.IsDetecting = false;
    }

    private void CameraBarcodeReaderView_BarcodesDetected(object? sender, ZXing.Net.Maui.BarcodeDetectionEventArgs e)
    {
        var result = e.Results?.FirstOrDefault();
        if (result != null)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (BindingContext is AttendanceViewModel vm && !vm.IsBusy)
                {
                    vm.QrCodeInput = result.Value;
                    await vm.CheckInAsync();
                }
            });
        }
    }
}
