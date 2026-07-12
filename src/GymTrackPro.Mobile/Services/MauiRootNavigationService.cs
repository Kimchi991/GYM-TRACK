namespace GymTrackPro.Mobile.Services;

public sealed class MauiRootNavigationService : IRootNavigationService
{
    public bool TrySetRoot(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var application = Application.Current;
        if (application is null || application.Windows.Count == 0)
        {
            return false;
        }

        application.Windows[0].Page = page;
        return true;
    }

    public Page? GetRootPage()
    {
        var application = Application.Current;
        return application is not null && application.Windows.Count > 0
            ? application.Windows[0].Page
            : null;
    }
}
