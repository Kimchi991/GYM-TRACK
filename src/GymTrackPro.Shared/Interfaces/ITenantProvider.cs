namespace GymTrackPro.Shared.Interfaces;

public interface ITenantProvider
{
    int? GetTenantId();
    bool IsPlatformAdmin();
}
