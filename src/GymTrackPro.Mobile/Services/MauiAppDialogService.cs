namespace GymTrackPro.Mobile.Services;

public sealed class MauiAppDialogService : IAppDialogService
{
    private readonly IRootNavigationService _rootNavigationService;

    public MauiAppDialogService(IRootNavigationService rootNavigationService)
    {
        _rootNavigationService = rootNavigationService
            ?? throw new ArgumentNullException(nameof(rootNavigationService));
    }

    public Task ShowAlertAsync(string title, string message, string cancel) =>
        RequireRootPage().DisplayAlertAsync(title, message, cancel);

    public Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string accept,
        string cancel) =>
        RequireRootPage().DisplayAlertAsync(title, message, accept, cancel);

    private Page RequireRootPage() =>
        _rootNavigationService.GetRootPage()
        ?? throw new InvalidOperationException("No active application window is available for dialogs.");
}
