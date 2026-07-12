using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Events.Membership;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.API.Services;

public class SubscriptionExpirationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SubscriptionExpirationWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

    public SubscriptionExpirationWorker(
        IServiceProvider services,
        ILogger<SubscriptionExpirationWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Subscription expiration worker is starting.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Subscription expiration processing failed.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Subscription expiration worker is stopping.");
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISystemSettingService>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
        var context = scope.ServiceProvider.GetRequiredService<GymDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClockService>();
        var timezone = scope.ServiceProvider.GetRequiredService<ITimezoneService>();
        var nowUtc = clock.UtcNow;
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The application clock must return UTC values.");
        }

        var today = await timezone.GetGymDateAsync(nowUtc, cancellationToken);
        var reminderDays = await settings.GetValueIntAsync(
            "ReminderDaysBeforeExpiration",
            3);
        if (reminderDays is < 1 or > 366)
        {
            throw new InvalidOperationException("ReminderDaysBeforeExpiration is outside 1..366.");
        }

        var todayStorage = GymMembershipPolicy.ToStorageDate(today);
        var tomorrowStorage = GymMembershipPolicy.ToStorageDate(today.AddDays(1));
        var currentRows = await context.Subscriptions
            .AsNoTracking()
            .Include(subscription => subscription.Member)
            .Include(subscription => subscription.Plan)
            .Where(subscription => subscription.Status == GymMembershipPolicy.Active
                && subscription.StartDate < tomorrowStorage
                && subscription.EndDate >= todayStorage
                && subscription.Member != null
                && !subscription.Member.IsDeleted
                && subscription.Member.Status == GymMembershipPolicy.MemberActive)
            .Select(subscription => new
            {
                Subscription = subscription,
                HasOpenPause = context.MembershipPauses.Any(pause =>
                    pause.SubscriptionID == subscription.SubscriptionID
                    && pause.PauseEndDate == null)
            })
            .ToListAsync(cancellationToken);
        var selectedCurrent = currentRows
            .GroupBy(row => row.Subscription.MemberID)
            .Select(group => GymMembershipPolicy.SelectCurrentCoverage(
                group.Select(row => new MembershipCoverageCandidate(
                    row.Subscription,
                    row.HasOpenPause)),
                today))
            .Where(selection => selection.State == AttendanceMembershipState.Active
                && selection.Subscription is not null)
            .Select(selection => selection.Subscription!)
            .ToList();

        var expiredIds = await context.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.Status == GymMembershipPolicy.Active
                && subscription.EndDate < todayStorage)
            .Select(subscription => subscription.SubscriptionID)
            .ToListAsync(cancellationToken);
        if (expiredIds.Count > 0)
        {
            var key = new ExpirationBatchKey(expiredIds.OrderBy(id => id).ToArray(), nowUtc);
            await GymMembershipTransaction.ExecuteVerifiedAsync(
                context,
                key,
                async (operationKey, transactionToken) =>
                {
                    var subscriptions = await context.Subscriptions
                        .Where(subscription => operationKey.SubscriptionIds.Contains(
                            subscription.SubscriptionID))
                        .ToListAsync(transactionToken);
                    foreach (var subscription in subscriptions)
                    {
                        if (!string.Equals(
                                subscription.Status,
                                GymMembershipPolicy.Active,
                                StringComparison.Ordinal)
                            || GymMembershipPolicy.ToCalendarDate(subscription.EndDate) >= today)
                        {
                            continue;
                        }

                        subscription.Status = GymMembershipPolicy.Expired;
                        subscription.LastModified = operationKey.TimestampUtc;
                        context.AuditLogs.Add(new AuditLog
                        {
                            Action = "Subscription Expired",
                            Details = $"Subscription ID {subscription.SubscriptionID} automatically expired.",
                            IPAddress = "System Background Service",
                            Timestamp = operationKey.TimestampUtc
                        });
                    }

                    return true;
                },
                async (operationKey, verificationToken) =>
                    await context.Subscriptions.AsNoTracking().CountAsync(
                        subscription => operationKey.SubscriptionIds.Contains(subscription.SubscriptionID)
                            && subscription.Status == GymMembershipPolicy.Expired
                            && subscription.LastModified == operationKey.TimestampUtc,
                        verificationToken) == operationKey.SubscriptionIds.Length,
                cancellationToken);
        }

        var reminderDate = today.AddDays(reminderDays);
        var expiring = selectedCurrent
            .Where(subscription => GymMembershipPolicy.ToCalendarDate(subscription.EndDate) == reminderDate)
            .ToList();
        foreach (var subscription in expiring)
        {
            await publisher.PublishAsync(new MembershipExpiringEvent
            {
                SubscriptionId = subscription.SubscriptionID,
                MemberId = subscription.MemberID,
                MemberEmail = subscription.Member?.Email ?? string.Empty,
                PlanName = subscription.Plan?.PlanName ?? "Unknown Plan",
                EndDate = GymMembershipPolicy.NormalizeCalendarDate(subscription.EndDate)
            });
        }

        _logger.LogInformation(
            "Expiration processing completed. Reminders: {ReminderCount}; expired: {ExpiredCount}.",
            expiring.Count,
            expiredIds.Count);
    }

    private sealed record ExpirationBatchKey(
        int[] SubscriptionIds,
        DateTime TimestampUtc);
}
