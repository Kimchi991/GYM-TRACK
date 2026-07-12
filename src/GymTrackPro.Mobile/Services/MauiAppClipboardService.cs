namespace GymTrackPro.Mobile.Services;

public sealed class MauiAppClipboardService : IAppClipboardService
{
    public Task SetTextAsync(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Clipboard.Default.SetTextAsync(text);
    }
}
