using System.Threading.Tasks;

namespace GymTrackPro.Shared.Interfaces;

public interface IFirebaseNotificationService
{
    Task SendPushNotificationAsync(string deviceToken, string title, string body);
}
