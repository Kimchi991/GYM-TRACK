using SQLite;

namespace GymTrackPro.Mobile.Models;

[Table("LocalGoerDashboardCache")]
public sealed class LocalGoerDashboardCache
{
    [PrimaryKey]
    public string AccountUid { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTime CachedAtUtc { get; set; }
}
