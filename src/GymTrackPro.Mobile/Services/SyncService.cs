using System;
using System.Threading.Tasks;
using GymTrackPro.Mobile.Models;

namespace GymTrackPro.Mobile.Services;

public class SyncService : ISyncService
{
    private readonly ILocalDatabaseService _databaseService;
    private readonly INetworkService _networkService;

    public SyncService(ILocalDatabaseService databaseService, INetworkService networkService)
    {
        _databaseService = databaseService;
        _networkService = networkService;

        // Automatically trigger sync when connectivity is restored
        _networkService.ConnectivityChanged += async (sender, isConnected) =>
        {
            if (isConnected)
            {
                await SyncPendingOperationsAsync();
            }
        };
    }

    public async Task QueueSyncOperationAsync(string tableName, string recordId, string operation, string serializedData)
    {
        var db = _databaseService.GetConnection();
        var syncItem = new SyncQueue
        {
            TableName = tableName,
            RecordId = recordId,
            Operation = operation,
            SerializedData = serializedData,
            CreatedAt = DateTime.UtcNow
        };
        await db.InsertAsync(syncItem);

        if (_networkService.IsConnected)
        {
            await SyncPendingOperationsAsync();
        }
    }

    public async Task SyncPendingOperationsAsync()
    {
        if (!_networkService.IsConnected) return;

        var db = _databaseService.GetConnection();
        var pendingItems = await db.Table<SyncQueue>().OrderBy(x => x.CreatedAt).ToListAsync();

        if (pendingItems.Count == 0) return;

        foreach (var item in pendingItems)
        {
            try
            {
                // TODO: Perform Web API call to synchronization endpoints (Phase 5)
                
                // Remove from queue upon success
                await db.DeleteAsync(item);
            }
            catch (Exception)
            {
                // Stop processing to maintain strict sequential execution of operations
                break;
            }
        }
    }
}
