using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class NotificationService : INotificationService
{
    private readonly IFirebaseNotificationService _firebaseProvider;
    private readonly IEmailService _emailService;
    private readonly GymDbContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IFirebaseNotificationService firebaseProvider,
        IEmailService emailService,
        GymDbContext context,
        ILogger<NotificationService> logger)
    {
        _firebaseProvider = firebaseProvider;
        _emailService = emailService;
        _context = context;
        _logger = logger;
    }

    public async Task SendPushNotificationAsync(string recipientToken, string title, string body)
    {
        await _firebaseProvider.SendPushNotificationAsync(recipientToken, title, body);
    }

    public async Task SendNotificationAsync(int memberId, string title, string body, NotificationChannel channel)
    {
        _logger.LogInformation("Routing notification to Member ID {MemberID} via {Channel}. Title: {Title}", memberId, channel, title);

        var member = await _context.Members.FindAsync(memberId);
        if (member == null)
        {
            _logger.LogWarning("Member with ID {MemberID} not found. Cannot route notification.", memberId);
            return;
        }

        switch (channel)
        {
            case NotificationChannel.Email:
                if (!string.IsNullOrEmpty(member.Email))
                {
                    await _emailService.SendEmailAsync(member.Email, title, body);
                }
                else
                {
                    _logger.LogWarning("Member {MemberID} has no registered email. Email notification skipped.", memberId);
                }
                break;

            case NotificationChannel.SMS:
                // Mock SMS Provider integration
                _logger.LogInformation("[SMS Mock] Sending to {Phone}: {Body}", member.PhoneNumber, body);
                break;

            case NotificationChannel.Push:
                // Mock Push notification provider logic
                var dummyToken = $"token-member-{member.MemberID}";
                await SendPushNotificationAsync(dummyToken, title, body);
                break;

            case NotificationChannel.InApp:
                var notification = new Notification
                {
                    MemberID = memberId,
                    Title = title,
                    Message = body,
                    Status = NotificationStatus.Unread,
                    ScheduledTime = DateTime.UtcNow,
                    SentTime = DateTime.UtcNow
                };
                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
        }
    }
}
