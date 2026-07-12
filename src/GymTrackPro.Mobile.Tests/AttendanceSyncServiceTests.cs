using System.Reflection;
using GymTrackPro.Mobile.Models;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using Microsoft.Extensions.Logging.Abstractions;
using SQLite;

namespace GymTrackPro.Mobile.Tests;

public sealed class AttendanceSyncServiceTests
{
    private const string AccountUid = "firebase-account-1";

    [Fact]
    public async Task Queue_serializes_identity_and_operation_then_deletes_only_after_success()
    {
        SQLitePCL.Batteries_V2.Init();
        var database = new SQLiteAsyncConnection(":memory:");
        var local = new FakeLocalDatabaseService(database);
        var network = new FakeNetworkService { IsConnected = false };
        var apiCalls = new List<Guid>();
        var service = CreateService(local, network, apiCalls, success: true);
        var operationId = Guid.NewGuid();

        await service.QueueAttendanceOperationAsync(
            AccountUid,
            AttendanceSyncAction.CheckIn,
            operationId);

        var queued = Assert.Single(await database.Table<SyncQueue>().ToListAsync());
        Assert.Equal(AccountUid, queued.AccountUid);
        Assert.Equal(operationId.ToString("D"), queued.RecordId);
        Assert.Contains(AccountUid, queued.SerializedData, StringComparison.Ordinal);
        Assert.Contains(operationId.ToString("D"), queued.SerializedData, StringComparison.OrdinalIgnoreCase);

        network.IsConnected = true;
        await service.SyncPendingOperationsAsync();

        Assert.Equal(new[] { operationId }, apiCalls);
        Assert.Empty(await database.Table<SyncQueue>().ToListAsync());
    }

    [Fact]
    public async Task Failed_operation_is_retained_and_foreign_account_is_never_replayed()
    {
        SQLitePCL.Batteries_V2.Init();
        var database = new SQLiteAsyncConnection(":memory:");
        var local = new FakeLocalDatabaseService(database);
        var network = new FakeNetworkService { IsConnected = false };
        var apiCalls = new List<Guid>();
        var service = CreateService(local, network, apiCalls, success: false);
        var ownOperation = Guid.NewGuid();
        await service.QueueAttendanceOperationAsync(
            AccountUid,
            AttendanceSyncAction.CheckOut,
            ownOperation);
        await database.InsertAsync(new SyncQueue
        {
            AccountUid = "different-account",
            TableName = "Attendance",
            RecordId = Guid.NewGuid().ToString("D"),
            Operation = AttendanceSyncAction.CheckIn.ToString(),
            SerializedData = "{}",
            CreatedAt = DateTime.UtcNow
        });

        network.IsConnected = true;
        await service.SyncPendingOperationsAsync();

        Assert.Equal(new[] { ownOperation }, apiCalls);
        var remaining = await database.Table<SyncQueue>().OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, item => item.AccountUid == "different-account");
    }

    private static SyncService CreateService(
        ILocalDatabaseService local,
        INetworkService network,
        List<Guid> apiCalls,
        bool success)
    {
        var api = CreateProxy<IApiService>((method, arguments) =>
        {
            if (method.Name is nameof(IApiService.GoerCheckInAsync) or nameof(IApiService.GoerCheckOutAsync))
            {
                apiCalls.Add((Guid)arguments![0]!);
                return Task.FromResult(new ApiResponse<AttendanceDto>
                {
                    Success = success,
                    Message = success ? "ok" : "retry"
                });
            }

            throw new NotSupportedException(method.Name);
        });
        var auth = CreateProxy<IFirebaseAuthService>((method, _) =>
            method.Name == nameof(IFirebaseAuthService.GetCurrentUid)
                ? AccountUid
                : throw new NotSupportedException(method.Name));
        return new SyncService(
            local,
            network,
            api,
            auth,
            NullLogger<SyncService>.Instance);
    }

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, MethodProxy>();
        ((MethodProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class MethodProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }

    private sealed class FakeNetworkService : INetworkService
    {
        public bool IsConnected { get; set; }
        public event EventHandler<bool>? ConnectivityChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeLocalDatabaseService(SQLiteAsyncConnection database)
        : ILocalDatabaseService
    {
        private bool _initialized;

        public SQLiteAsyncConnection GetConnection() => database;

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            await database.CreateTableAsync<SyncQueue>();
            _initialized = true;
        }

        public async Task<SQLiteAsyncConnection> GetInitializedConnectionAsync()
        {
            await InitializeAsync();
            return database;
        }

        public Task SaveGoerDashboardAsync(
            string accountUid,
            GoerDashboardDto dashboard,
            IReadOnlyCollection<AttendanceDto> recentAttendance) =>
            throw new NotSupportedException();

        public Task<GoerDashboardCacheSnapshot?> GetGoerDashboardAsync(string accountUid) =>
            throw new NotSupportedException();

        public Task ClearAccountAsync(string accountUid) => throw new NotSupportedException();
    }
}
