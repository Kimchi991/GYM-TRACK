using System.Threading.Tasks;

namespace GymTrackPro.Mobile.Services;

public interface ISyncService
{
    Task QueueAttendanceOperationAsync(
        string accountUid,
        AttendanceSyncAction action,
        Guid operationId);
    Task SyncPendingOperationsAsync();
}

public enum AttendanceSyncAction
{
    CheckIn = 0,
    CheckOut = 1
}
