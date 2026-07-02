using CommunityToolkit.Mvvm.ComponentModel;

namespace GymTrackPro.Mobile.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }
}
