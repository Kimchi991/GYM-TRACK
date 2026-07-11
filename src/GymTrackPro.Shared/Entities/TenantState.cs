namespace GymTrackPro.Shared.Entities;

public class TenantState
{
    public int? GymID { get; set; }
    public string? UserRole { get; set; }
    public bool IsPlatformAdmin => UserRole == "PlatformAdmin";
}
