using GymTrackPro.Mobile.Models;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymTrackPro.Mobile.Tests;

public sealed class LocalDatabaseServiceTests
{
    [Fact]
    public async Task Initialization_adds_account_scope_to_a_legacy_queue_and_is_idempotent()
    {
        SQLitePCL.Batteries_V2.Init();
        var path = Path.Combine(Path.GetTempPath(), $"gymtrackpro-{Guid.NewGuid():N}.db3");
        LocalDatabaseService? service = null;
        try
        {
            service = new LocalDatabaseService(
                NullLogger<LocalDatabaseService>.Instance,
                path);
            var database = service.GetConnection();
            await database.ExecuteAsync(
                "CREATE TABLE SyncQueue (Id INTEGER PRIMARY KEY AUTOINCREMENT, TableName TEXT NOT NULL, RecordId TEXT NOT NULL, Operation TEXT NOT NULL, SerializedData TEXT NOT NULL, CreatedAt TEXT NOT NULL);");

            await service.InitializeAsync();
            await service.InitializeAsync();

            var columns = await database.GetTableInfoAsync("SyncQueue");
            Assert.Contains(columns, column => column.Name == "AccountUid");
            Assert.Equal(2, await database.ExecuteScalarAsync<int>("PRAGMA user_version;"));
        }
        finally
        {
            if (service is not null)
            {
                await service.GetConnection().CloseAsync();
            }
            DeleteSqliteFiles(path);
        }
    }

    [Fact]
    public async Task Dashboard_cache_and_queue_cleanup_are_isolated_by_firebase_uid()
    {
        SQLitePCL.Batteries_V2.Init();
        var path = Path.Combine(Path.GetTempPath(), $"gymtrackpro-{Guid.NewGuid():N}.db3");
        LocalDatabaseService? service = null;
        try
        {
            service = new LocalDatabaseService(
                NullLogger<LocalDatabaseService>.Instance,
                path);
            await service.InitializeAsync();
            var dashboard = new GoerDashboardDto { MembershipStatus = "Active", VisitCount = 4 };
            await service.SaveGoerDashboardAsync(
                "uid-one",
                dashboard,
                new[] { new AttendanceDto { AttendanceID = 3 } });
            await service.SaveGoerDashboardAsync(
                "uid-two",
                new GoerDashboardDto { MembershipStatus = "Paused" },
                Array.Empty<AttendanceDto>());
            var database = service.GetConnection();
            await database.InsertAsync(new SyncQueue
            {
                AccountUid = "uid-one",
                TableName = "Attendance",
                RecordId = Guid.NewGuid().ToString("D"),
                Operation = "CheckIn",
                SerializedData = "{}",
                CreatedAt = DateTime.UtcNow
            });
            await database.InsertAsync(new SyncQueue
            {
                AccountUid = "uid-two",
                TableName = "Attendance",
                RecordId = Guid.NewGuid().ToString("D"),
                Operation = "CheckIn",
                SerializedData = "{}",
                CreatedAt = DateTime.UtcNow
            });

            var cached = await service.GetGoerDashboardAsync("uid-one");
            await service.ClearAccountAsync("uid-one");

            Assert.Equal("Active", cached!.Dashboard.MembershipStatus);
            Assert.Single(cached.RecentAttendance);
            Assert.Null(await service.GetGoerDashboardAsync("uid-one"));
            Assert.NotNull(await service.GetGoerDashboardAsync("uid-two"));
            Assert.DoesNotContain(
                await database.Table<SyncQueue>().ToListAsync(),
                item => item.AccountUid == "uid-one");
            Assert.Contains(
                await database.Table<SyncQueue>().ToListAsync(),
                item => item.AccountUid == "uid-two");
        }
        finally
        {
            if (service is not null)
            {
                await service.GetConnection().CloseAsync();
            }
            DeleteSqliteFiles(path);
        }
    }

    private static void DeleteSqliteFiles(string path)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var candidate = path + suffix;
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
