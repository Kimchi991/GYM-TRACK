using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;
using SQLite;

namespace GymTrackPro.Mobile.Services;

public interface ILocalDatabaseService
{
    SQLiteAsyncConnection GetConnection();
    Task InitializeAsync();
    Task<SQLiteAsyncConnection> GetInitializedConnectionAsync();
    Task SaveGoerDashboardAsync(
        string accountUid,
        GoerDashboardDto dashboard,
        IReadOnlyCollection<AttendanceDto> recentAttendance);
    Task<GoerDashboardCacheSnapshot?> GetGoerDashboardAsync(string accountUid);
    Task ClearAccountAsync(string accountUid);
}

public sealed record GoerDashboardCacheSnapshot(
    GoerDashboardDto Dashboard,
    IReadOnlyList<AttendanceDto> RecentAttendance,
    DateTime CachedAtUtc);
