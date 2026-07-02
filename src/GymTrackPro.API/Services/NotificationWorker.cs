using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class NotificationWorker : BackgroundService
{
    private readonly INotificationQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationWorker> _logger;

    public NotificationWorker(
        INotificationQueue queue,
        IServiceProvider serviceProvider,
        ILogger<NotificationWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationWorker background service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (memberId, title, message, channel) = await _queue.DequeueAsync(stoppingToken);
                
                _logger.LogInformation("Processing queued notification for Member ID {MemberID} via {Channel}.", memberId, channel);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                    await notificationService.SendNotificationAsync(memberId, title, message, channel);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown, ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing queued background notification.");
            }
        }

        _logger.LogInformation("NotificationWorker background service is stopping.");
    }
}
