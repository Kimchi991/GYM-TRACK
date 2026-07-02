using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Events.Membership;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class SubscriptionExpirationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SubscriptionExpirationWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

    public SubscriptionExpirationWorker(IServiceProvider services, ILogger<SubscriptionExpirationWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Subscription Expiration Worker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Subscription Expiration Worker is executing expiration check checks...");

            try
            {
                using (var scope = _services.CreateScope())
                {
                    var settingsService = scope.ServiceProvider.GetRequiredService<ISystemSettingService>();
                    var eventPublisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
                    var context = scope.ServiceProvider.GetRequiredService<GymDbContext>();
                    var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

                    var reminderDays = await settingsService.GetValueIntAsync("ReminderDaysBeforeExpiration", 3);
                    var today = DateTime.UtcNow.Date;
                    var reminderTargetDate = today.AddDays(reminderDays);

                    // 1. Find active subscriptions that expire on reminderTargetDate to trigger reminders
                    var expiringSubs = await context.Subscriptions
                        .Include(s => s.Member)
                        .Include(s => s.Plan)
                        .Where(s => s.Status == "Active" && s.EndDate.Date == reminderTargetDate)
                        .ToListAsync(stoppingToken);

                    foreach (var sub in expiringSubs)
                    {
                        _logger.LogInformation("Subscription {SubId} is expiring in {Days} days on {EndDate}.", sub.SubscriptionID, reminderDays, sub.EndDate);
                        await eventPublisher.PublishAsync(new MembershipExpiringEvent
                        {
                            SubscriptionId = sub.SubscriptionID,
                            MemberId = sub.MemberID,
                            MemberEmail = sub.Member?.Email ?? string.Empty,
                            PlanName = sub.Plan?.PlanName ?? "Unknown Plan",
                            EndDate = sub.EndDate
                        });
                    }

                    // 2. Find active subscriptions that have passed their EndDate and mark them as Expired
                    var pastSubs = await context.Subscriptions
                        .Include(s => s.Member)
                        .Include(s => s.Plan)
                        .Where(s => s.Status == "Active" && s.EndDate.Date < today)
                        .ToListAsync(stoppingToken);

                    foreach (var sub in pastSubs)
                    {
                        _logger.LogInformation("Expiring subscription {SubId} because EndDate {EndDate} has passed today {Today}.", sub.SubscriptionID, sub.EndDate, today);
                        sub.Status = "Expired";
                        sub.LastModified = DateTime.UtcNow;

                        // Create Audit Log
                        await auditService.LogActivityAsync(
                            null, 
                            "Subscription Expired", 
                            $"Subscription ID: {sub.SubscriptionID} for member {sub.Member?.FirstName} {sub.Member?.LastName} (ID: {sub.MemberID}) automatically expired.", 
                            "System Background Service"
                        );
                    }

                    if (pastSubs.Any())
                    {
                        await context.SaveChangesAsync(stoppingToken);
                    }

                    _logger.LogInformation("Expiration checks successfully completed. Handled {ExpiringCount} expiring reminders and {ExpiredCount} expired subscriptions.", expiringSubs.Count, pastSubs.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while running subscription expiration check.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Subscription Expiration Worker is stopping.");
    }
}
