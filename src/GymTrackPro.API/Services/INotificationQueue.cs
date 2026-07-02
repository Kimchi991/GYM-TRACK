using System.Threading;
using System.Threading.Tasks;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.API.Services;

public interface INotificationQueue
{
    void QueueNotification(int memberId, string title, string message, NotificationChannel channel);
    Task<(int MemberId, string Title, string Message, NotificationChannel Channel)> DequeueAsync(CancellationToken cancellationToken);
}
