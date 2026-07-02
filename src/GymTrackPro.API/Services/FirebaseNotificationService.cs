using System;
using System.Threading.Tasks;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class FirebaseNotificationService : IFirebaseNotificationService
{
    public Task SendPushNotificationAsync(string deviceToken, string title, string body)
    {
        // Mock: Integrate Firebase Admin SDK for FCM push delivery (Phase 10)
        // Ensure credentials are loaded securely from configurations.
        return Task.CompletedTask;
    }
}
