using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class OvernightSessionCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<OvernightSessionCleanupWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public OvernightSessionCleanupWorker(
        IServiceProvider services,
        ILogger<OvernightSessionCleanupWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Overnight session cleanup worker is starting.");
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
                _logger.LogError(exception, "Overnight session cleanup processing failed.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Overnight session cleanup worker is stopping.");
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GymDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClockService>();
        var timezone = scope.ServiceProvider.GetRequiredService<ITimezoneService>();
        var nowUtc = clock.UtcNow;

        var today = await timezone.GetGymDateAsync(nowUtc, cancellationToken);
        var cutoffTime = nowUtc.AddHours(-12);

        // Find open attendance sessions that are over 12 hours old or from a previous gym date
        var abandonedSessions = await context.AttendanceLogs
            .Where(a => !a.IsVoided && a.CheckOutTime == null && (a.CheckInTime < cutoffTime || a.AttendanceDate < today))
            .ToListAsync(cancellationToken);

        if (abandonedSessions.Count > 0)
        {
            foreach (var session in abandonedSessions)
            {
                var autoCheckoutTime = session.CheckInTime.AddHours(2);
                if (autoCheckoutTime > nowUtc)
                {
                    autoCheckoutTime = nowUtc;
                }

                session.CheckOutTime = autoCheckoutTime;
                session.LastModified = nowUtc;

                context.AuditLogs.Add(new AuditLog
                {
                    Action = "OvernightAutoCheckout",
                    Details = $"Automatically checked out abandoned attendance session ID {session.AttendanceID} for Member ID {session.MemberID}.",
                    IPAddress = "System Background Worker",
                    Timestamp = nowUtc
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Overnight session cleanup worker auto-closed {Count} abandoned attendance sessions.", abandonedSessions.Count);
        }
    }
}
