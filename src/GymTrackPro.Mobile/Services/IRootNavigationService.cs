namespace GymTrackPro.Mobile.Services;

public interface IRootNavigationService
{
    bool TrySetRoot(Page page);

    Page? GetRootPage();
}
