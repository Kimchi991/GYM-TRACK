using System.Threading.Tasks;
using GymTrackPro.Shared.Events.Members;
using GymTrackPro.Shared.Events.Payments;
using GymTrackPro.Shared.Events.Membership;
using GymTrackPro.Shared.Events.Authentication;
using GymTrackPro.Shared.Events.Attendance;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymTrackPro.API.Services;

public class NotificationHandler :
    IDomainEventHandler<MemberRegisteredEvent>,
    IDomainEventHandler<PaymentReceivedEvent>,
    IDomainEventHandler<RefundProcessedEvent>,
    IDomainEventHandler<MembershipPausedEvent>,
    IDomainEventHandler<MembershipResumedEvent>,
    IDomainEventHandler<PasswordResetRequestedEvent>,
    IDomainEventHandler<MembershipExpiringEvent>,
    IDomainEventHandler<CheckInFailedEvent>
{
    private readonly INotificationQueue _queue;
    private readonly ILogger<NotificationHandler> _logger;

    public NotificationHandler(INotificationQueue queue, ILogger<NotificationHandler> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public async Task HandleAsync(MemberRegisteredEvent @event)
    {
        _logger.LogInformation("Handling MemberRegisteredEvent for Member ID {MemberId}", @event.MemberId);
        var title = "Welcome to GymTrackPro!";
        var body = $"Hi {@event.FirstName}, welcome to our gym family! Your personal check-in QR Code is {@event.QRCode}.";
        
        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.Email);
        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.InApp);
        await Task.CompletedTask;
    }

    public async Task HandleAsync(PaymentReceivedEvent @event)
    {
        _logger.LogInformation("Handling PaymentReceivedEvent for Member ID {MemberId}", @event.MemberId);
        var title = "Payment Successful";
        var body = $"Thank you for your payment of {@event.Amount:C}. Your receipt number is {@event.ReceiptNumber}.";

        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.Email);
        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.InApp);
        await Task.CompletedTask;
    }

    public async Task HandleAsync(RefundProcessedEvent @event)
    {
        _logger.LogInformation("Handling RefundProcessedEvent for Member ID {MemberId}", @event.MemberId);
        var title = "Refund Processed";
        var body = $"A refund of {@event.Amount:C} has been successfully processed for receipt {@event.ReceiptNumber}.";

        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.Email);
        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.InApp);
        await Task.CompletedTask;
    }

    public async Task HandleAsync(MembershipPausedEvent @event)
    {
        _logger.LogInformation("Handling MembershipPausedEvent for Member ID {MemberId}", @event.MemberId);
        var title = "Membership Paused";
        var body = $"Your subscription for {@event.PlanName} has been successfully paused.";

        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.Email);
        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.InApp);
        await Task.CompletedTask;
    }

    public async Task HandleAsync(MembershipResumedEvent @event)
    {
        _logger.LogInformation("Handling MembershipResumedEvent for Member ID {MemberId}", @event.MemberId);
        var title = "Membership Resumed";
        var body = $"Your subscription for {@event.PlanName} has been successfully resumed. Welcome back!";

        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.Email);
        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.InApp);
        await Task.CompletedTask;
    }

    public async Task HandleAsync(PasswordResetRequestedEvent @event)
    {
        _logger.LogInformation("Handling PasswordResetRequestedEvent for User ID {UserId}", @event.UserId);
        _logger.LogInformation("Sending password reset instructions to {Email}. Token: {ResetToken}", @event.Email, @event.ResetToken);
        await Task.CompletedTask;
    }

    public async Task HandleAsync(MembershipExpiringEvent @event)
    {
        _logger.LogInformation("Handling MembershipExpiringEvent for Member ID {MemberId}", @event.MemberId);
        var title = "Membership Expiring Soon";
        var body = $"Your subscription for {@event.PlanName} is expiring on {@event.EndDate:d}. Please renew soon to avoid service interruption.";

        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.Email);
        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.InApp);
        await Task.CompletedTask;
    }

    public async Task HandleAsync(CheckInFailedEvent @event)
    {
        _logger.LogInformation("Handling CheckInFailedEvent for Member ID {MemberId}", @event.MemberId);
        var title = "Failed Check-In Attempt";
        var body = $"A check-in attempt failed on your account. Reason: {@event.Reason}.";

        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.Push);
        _queue.QueueNotification(@event.MemberId, title, body, NotificationChannel.InApp);
        await Task.CompletedTask;
    }
}
