using System.Threading.Tasks;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Interfaces;

public interface INotificationService
{
    Task SendPushNotificationAsync(string recipientToken, string title, string body);
    Task SendNotificationAsync(int memberId, string title, string body, NotificationChannel channel);
}
