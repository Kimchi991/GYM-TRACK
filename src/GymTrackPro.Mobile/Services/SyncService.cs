using System;
using System.Threading.Tasks;
using System.Text.Json;
using GymTrackPro.Mobile.Models;
using Microsoft.Extensions.Logging;

namespace GymTrackPro.Mobile.Services;

public class SyncService : ISyncService
{
    private readonly ILocalDatabaseService _databaseService;
    private readonly INetworkService _networkService;
    private readonly IApiService _apiService;
    private readonly IFirebaseAuthService _authService;
    private readonly ILogger<SyncService> _logger;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public SyncService(
        ILocalDatabaseService databaseService,
        INetworkService networkService,
        IApiService apiService,
        IFirebaseAuthService authService,
        ILogger<SyncService> logger)
    {
        _databaseService = databaseService;
        _networkService = networkService;
        _apiService = apiService;
        _authService = authService;
        _logger = logger;

        // Automatically trigger sync when connectivity is restored
        _networkService.ConnectivityChanged += (_, isConnected) =>
        {
            if (isConnected)
            {
                _ = SyncAfterConnectivityChangeAsync();
            }
        };
    }

    public async Task QueueAttendanceOperationAsync(
        string accountUid,
        AttendanceSyncAction action,
        Guid operationId)
    {
        ValidateOperation(accountUid, action, operationId);
        var currentUid = _authService.GetCurrentUid();
        if (!string.Equals(currentUid, accountUid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The attendance operation does not belong to the active account.");
        }

        var db = await _databaseService.GetInitializedConnectionAsync().ConfigureAwait(false);
        var payload = new AttendanceSyncPayload(accountUid, action, operationId);
        var syncItem = new SyncQueue
        {
            AccountUid = accountUid,
            TableName = "Attendance",
            RecordId = operationId.ToString("D"),
            Operation = action.ToString(),
            SerializedData = JsonSerializer.Serialize(payload, PayloadJsonOptions),
            CreatedAt = DateTime.UtcNow
        };
        await db.InsertAsync(syncItem).ConfigureAwait(false);

        if (_networkService.IsConnected)
        {
            await SyncPendingOperationsAsync().ConfigureAwait(false);
        }
    }

    public async Task SyncPendingOperationsAsync()
    {
        if (!_networkService.IsConnected) return;

        var currentUid = _authService.GetCurrentUid();
        if (string.IsNullOrWhiteSpace(currentUid))
        {
            return;
        }

        await _syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var db = await _databaseService.GetInitializedConnectionAsync().ConfigureAwait(false);
            var pendingItems = await db.Table<SyncQueue>()
                .Where(item => item.AccountUid == currentUid)
                .OrderBy(item => item.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var item in pendingItems)
            {
                if (!TryReadPayload(item, currentUid, out var payload))
                {
                    _logger.LogWarning(
                        "Attendance sync stopped because queue item {QueueItemId} is invalid.",
                        item.Id);
                    break;
                }

                try
                {
                    var response = payload.Action switch
                    {
                        AttendanceSyncAction.CheckIn =>
                            await _apiService.GoerCheckInAsync(payload.OperationId).ConfigureAwait(false),
                        AttendanceSyncAction.CheckOut =>
                            await _apiService.GoerCheckOutAsync(payload.OperationId).ConfigureAwait(false),
                        _ => null
                    };

                    if (response is null || !response.Success)
                    {
                        break;
                    }

                    await db.DeleteAsync(item).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Attendance sync retained queue item {QueueItemId} for a later retry.",
                        item.Id);
                    break;
                }
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task SyncAfterConnectivityChangeAsync()
    {
        try
        {
            await SyncPendingOperationsAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Attendance sync will retry after the next connectivity change.");
        }
    }

    private static bool TryReadPayload(
        SyncQueue item,
        string currentUid,
        out AttendanceSyncPayload payload)
    {
        payload = default!;
        if (!string.Equals(item.TableName, "Attendance", StringComparison.Ordinal)
            || !string.Equals(item.AccountUid, currentUid, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<AttendanceSyncPayload>(
                item.SerializedData,
                PayloadJsonOptions)!;
            return payload is not null
                && string.Equals(payload.AccountUid, currentUid, StringComparison.Ordinal)
                && string.Equals(item.RecordId, payload.OperationId.ToString("D"), StringComparison.Ordinal)
                && string.Equals(item.Operation, payload.Action.ToString(), StringComparison.Ordinal)
                && payload.OperationId != Guid.Empty
                && Enum.IsDefined(payload.Action);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ValidateOperation(
        string accountUid,
        AttendanceSyncAction action,
        Guid operationId)
    {
        if (string.IsNullOrWhiteSpace(accountUid)
            || accountUid.Length > 128
            || operationId == Guid.Empty
            || !Enum.IsDefined(action))
        {
            throw new ArgumentException("The attendance sync operation is invalid.");
        }
    }

    private sealed record AttendanceSyncPayload(
        string AccountUid,
        AttendanceSyncAction Action,
        Guid OperationId);
}
