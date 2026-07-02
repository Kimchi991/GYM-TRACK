using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.API.Services;

public class MemoryNotificationQueue : INotificationQueue
{
    private readonly ConcurrentQueue<(int MemberId, string Title, string Message, NotificationChannel Channel)> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);

    public void QueueNotification(int memberId, string title, string message, NotificationChannel channel)
    {
        _queue.Enqueue((memberId, title, message, channel));
        _signal.Release();
    }

    public async Task<(int MemberId, string Title, string Message, NotificationChannel Channel)> DequeueAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken);
        _queue.TryDequeue(out var workItem);
        return workItem;
    }
}
