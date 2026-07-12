namespace GymTrackPro.Mobile.Services;

public interface IAppDialogService
{
    Task ShowAlertAsync(string title, string message, string cancel);

    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string accept,
        string cancel);
}
