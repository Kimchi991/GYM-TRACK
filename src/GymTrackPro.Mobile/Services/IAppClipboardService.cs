namespace GymTrackPro.Mobile.Services;

/// <summary>
/// Provides an explicit, testable boundary around the platform clipboard.
/// Invite codes are copied only after an owner requests it and are never stored
/// by the application.
/// </summary>
public interface IAppClipboardService
{
    Task SetTextAsync(string text);
}
