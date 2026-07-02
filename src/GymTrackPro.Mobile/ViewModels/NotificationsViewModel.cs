using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Mobile.ViewModels;

public partial class NotificationsViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int UnreadCount { get; set; }

    public ObservableCollection<Notification> Notifications { get; } = new();

    public NotificationsViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Notifications";
    }

    [RelayCommand]
    public async Task LoadNotificationsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _apiService.GetNotificationsAsync();
            if (result.Success && result.Data != null)
            {
                Notifications.Clear();
                foreach (var note in result.Data)
                {
                    Notifications.Add(note);
                }
                UnreadCount = Notifications.Count(n => n.Status == NotificationStatus.Unread);
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading notifications: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task MarkAsReadAsync(Notification notification)
    {
        if (notification == null || notification.Status == NotificationStatus.Read) return;

        try
        {
            var result = await _apiService.MarkNotificationAsReadAsync(notification.NotificationID);
            if (result.Success)
            {
                notification.Status = NotificationStatus.Read;
                // Force UI notification update
                var index = Notifications.IndexOf(notification);
                if (index >= 0)
                {
                    Notifications[index] = notification;
                }
                UnreadCount = Notifications.Count(n => n.Status == NotificationStatus.Unread);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to mark notification read: {ex.Message}";
        }
    }
}
