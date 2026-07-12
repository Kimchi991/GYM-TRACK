using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class OwnerBootstrapServiceTests
{
    private static readonly IdentityOperationContext OperationContext = new("bootstrap-test", "127.0.0.1");

    [Fact]
    public async Task Dry_run_validates_target_but_writes_nothing()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateOwner());
        await context.SaveChangesAsync();
        var service = CreateService(context, enabled: true);

        var result = await service.ExecuteAsync(
            new OwnerBootstrapRequest(
                1,
                "firebase-owner",
                "OWNER@EXAMPLE.TEST",
                DryRun: true,
                Confirm: false),
            OperationContext);

        Assert.True(result.WouldBind);
        Assert.False(result.Applied);
        Assert.Null((await context.Users.SingleAsync()).FirebaseUid);
        Assert.Empty(context.AuditLogs);
    }

    [Fact]
    public async Task Confirmed_execution_binds_exact_active_administrator_once_and_audits()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateOwner());
        await context.SaveChangesAsync();
        var service = CreateService(context, enabled: true);

        var result = await service.ExecuteAsync(
            new OwnerBootstrapRequest(
                1,
                "firebase-owner",
                "OWNER@EXAMPLE.TEST",
                DryRun: false,
                Confirm: true),
            OperationContext);

        Assert.True(result.Applied);
        var owner = await context.Users.SingleAsync();
        Assert.Equal("firebase-owner", owner.FirebaseUid);
        Assert.Equal(UserRole.Administrator, owner.Role);
        Assert.True(owner.EmailVerified);
        var audit = await context.AuditLogs.SingleAsync();
        Assert.DoesNotContain("firebase-owner", audit.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("owner@example.test", audit.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Commit_acknowledgement_loss_retry_accepts_only_the_exact_same_operation_binding()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateOwner());
        await context.SaveChangesAsync();
        var service = CreateService(context, enabled: true);
        var request = new OwnerBootstrapRequest(
            1,
            "firebase-owner",
            "OWNER@EXAMPLE.TEST",
            DryRun: false,
            Confirm: true);

        var first = await service.ExecuteAsync(request, OperationContext);
        context.ChangeTracker.Clear();

        // Re-entering with the same operation marker models execution-strategy replay
        // after the database committed but the commit acknowledgement was lost.
        var replay = await service.ExecuteAsync(request, OperationContext);

        Assert.True(first.Applied);
        Assert.True(replay.Applied);
        Assert.Single(await context.AuditLogs.AsNoTracking().ToListAsync());

        var separateInvocation = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.ExecuteAsync(
                request,
                new IdentityOperationContext("different-bootstrap-run", "127.0.0.1")));
        Assert.Equal(ErrorCodes.IdentityConflict, separateInvocation.ErrorCode);
    }

    [Fact]
    public async Task Commit_replay_never_accepts_a_second_bound_owner()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateOwner());
        await context.SaveChangesAsync();
        var service = CreateService(context, enabled: true);
        var request = new OwnerBootstrapRequest(
            1,
            "firebase-owner",
            "OWNER@EXAMPLE.TEST",
            DryRun: false,
            Confirm: true);
        await service.ExecuteAsync(request, OperationContext);

        context.ChangeTracker.Clear();
        var secondOwner = CreateOwner();
        secondOwner.UserID = 2;
        secondOwner.Username = "second-owner";
        secondOwner.Email = "second@example.test";
        secondOwner.NormalizedEmail = "SECOND@EXAMPLE.TEST";
        secondOwner.FirebaseUid = "firebase-second-owner";
        secondOwner.EmailVerified = true;
        context.Users.Add(secondOwner);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.ExecuteAsync(request, OperationContext));

        Assert.Equal(ErrorCodes.IdentityConflict, exception.ErrorCode);
        Assert.Single(await context.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(false, "Production")]
    [InlineData(true, "Development")]
    public async Task Disabled_or_wrong_environment_fails_closed(bool enabled, string allowedEnvironment)
    {
        await using var context = CreateContext();
        context.Users.Add(CreateOwner());
        await context.SaveChangesAsync();
        var service = CreateService(context, enabled, allowedEnvironment);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() => service.ExecuteAsync(
            new OwnerBootstrapRequest(
                1,
                "firebase-owner",
                "OWNER@EXAMPLE.TEST",
                DryRun: true,
                Confirm: false),
            OperationContext));

        Assert.Equal(403, exception.StatusCode);
        Assert.Null((await context.Users.SingleAsync()).FirebaseUid);
    }

    [Fact]
    public async Task Existing_bound_owner_or_uid_conflict_is_rejected()
    {
        await using var context = CreateContext();
        var target = CreateOwner();
        var existing = CreateOwner();
        existing.UserID = 2;
        existing.Username = "existing-owner";
        existing.Email = "existing@example.test";
        existing.NormalizedEmail = "EXISTING@EXAMPLE.TEST";
        existing.FirebaseUid = "existing-owner-uid";
        context.Users.AddRange(target, existing);
        await context.SaveChangesAsync();
        var service = CreateService(context, enabled: true);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() => service.ExecuteAsync(
            new OwnerBootstrapRequest(
                1,
                "firebase-owner",
                "OWNER@EXAMPLE.TEST",
                DryRun: false,
                Confirm: true),
            OperationContext));

        Assert.Equal(ErrorCodes.IdentityConflict, exception.ErrorCode);
        Assert.Null(target.FirebaseUid);
    }

    private static GymDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GymDbContext(options);
    }

    private static OwnerBootstrapService CreateService(
        GymDbContext context,
        bool enabled,
        string allowedEnvironment = "Production") => new(
            context,
            new FixedClock(new DateTime(2026, 7, 12, 1, 0, 0, DateTimeKind.Utc)),
            Options.Create(new OwnerBootstrapOptions
            {
                Enabled = enabled,
                AllowedEnvironment = allowedEnvironment
            }),
            new TestHostEnvironment { EnvironmentName = "Production" });

    private static User CreateOwner() => new()
    {
        UserID = 1,
        Username = "owner",
        Email = "owner@example.test",
        NormalizedEmail = null,
        FirstName = "Gym",
        LastName = "Owner",
        Role = UserRole.Administrator,
        IsActive = true,
        EmailVerified = false,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "GymTrackPro.Bootstrap.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
