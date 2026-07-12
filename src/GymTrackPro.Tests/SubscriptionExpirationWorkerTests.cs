using GymTrackPro.API.Data;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Events.Membership;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace GymTrackPro.Tests;

public class SubscriptionExpirationWorkerTests
{
    [Fact]
    public async Task Worker_expires_by_gym_date_and_reminds_only_effective_current_coverage()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var nowUtc = new DateTime(2026, 7, 12, 4, 0, 0, DateTimeKind.Utc);
        var today = new DateOnly(2026, 7, 12);
        await using (var seed = new GymDbContext(options))
        {
            seed.Members.Add(Member(1));
            seed.MembershipPlans.Add(new MembershipPlan
            {
                PlanID = 1,
                PlanName = "Standard",
                DurationDays = 30,
                Status = GymMembershipPolicy.PlanActive
            });
            seed.Subscriptions.AddRange(
                Subscription(1, today.AddDays(-30), today.AddDays(-1), GymMembershipPolicy.Active),
                Subscription(2, today.AddDays(-5), today.AddDays(3), GymMembershipPolicy.Active));
            await seed.SaveChangesAsync();
        }

        var settings = new Mock<ISystemSettingService>();
        settings.Setup(item => item.GetValueIntAsync("ReminderDaysBeforeExpiration", 3))
            .ReturnsAsync(3);
        var clock = new Mock<IClockService>();
        clock.SetupGet(item => item.UtcNow).Returns(nowUtc);
        var timezone = new Mock<ITimezoneService>();
        timezone.Setup(item => item.GetGymDateAsync(
                nowUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(today);
        var publisher = new Mock<IDomainEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<MembershipExpiringEvent>()))
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddScoped(_ => new GymDbContext(options));
        services.AddScoped(_ => settings.Object);
        services.AddScoped(_ => clock.Object);
        services.AddScoped(_ => timezone.Object);
        services.AddScoped(_ => publisher.Object);
        await using var provider = services.BuildServiceProvider();
        var worker = new SubscriptionExpirationWorker(
            provider,
            Mock.Of<ILogger<SubscriptionExpirationWorker>>());

        await worker.RunOnceAsync();

        await using var verification = new GymDbContext(options);
        Assert.Equal(
            GymMembershipPolicy.Expired,
            (await verification.Subscriptions.SingleAsync(item => item.SubscriptionID == 1)).Status);
        Assert.Equal(
            GymMembershipPolicy.Active,
            (await verification.Subscriptions.SingleAsync(item => item.SubscriptionID == 2)).Status);
        Assert.Single(await verification.AuditLogs.ToListAsync());
        publisher.Verify(item => item.PublishAsync(
            It.Is<MembershipExpiringEvent>(@event => @event.SubscriptionId == 2)), Times.Once);
    }

    private static Member Member(int id)
    {
        return new Member
        {
            MemberID = id,
            FirstName = "Worker",
            LastName = "Member",
            Email = "worker@example.com",
            QRCode = "worker-member",
            Status = GymMembershipPolicy.MemberActive
        };
    }

    private static Subscription Subscription(
        int id,
        DateOnly start,
        DateOnly end,
        string status)
    {
        return new Subscription
        {
            SubscriptionID = id,
            MemberID = 1,
            PlanID = 1,
            StartDate = GymMembershipPolicy.ToStorageDate(start),
            EndDate = GymMembershipPolicy.ToStorageDate(end),
            Status = status,
            LastModified = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }
}
