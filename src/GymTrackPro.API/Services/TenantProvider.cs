using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class TenantProvider : ITenantProvider
{
    private readonly TenantState _tenantState;

    public TenantProvider(TenantState tenantState)
    {
        _tenantState = tenantState;
    }

    public int? GetTenantId() => _tenantState.GymID;

    public bool IsPlatformAdmin() => _tenantState.IsPlatformAdmin;
}
