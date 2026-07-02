using System.Threading.Tasks;

namespace GymTrackPro.Mobile.Services;

public interface ISyncService
{
    Task QueueSyncOperationAsync(string tableName, string recordId, string operation, string serializedData);
    Task SyncPendingOperationsAsync();
}
