using GymTrackPro.API.Data;
using GymTrackPro.API.Services;
using GymTrackPro.API.Repositories;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.Tests;

public sealed class MemberAccessRevocationTests
{
    private static readonly DateTime Now =
        new(2026, 7, 12, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Soft_delete_deactivates_linked_goer_and_audits_in_one_save()
    {
        await using var context = CreateContext();
        context.Members.Add(CreateMember(10));
        context.Users.AddRange(
            CreateUser(1, UserRole.Administrator, null, true),
            CreateUser(2, UserRole.GymGoer, 10, true));
        context.AccountInvites.AddRange(
            CreateOutstandingInvite(10, 1, Now.AddHours(1)),
            CreateOutstandingInvite(10, 2, Now.AddHours(-1)));
        await context.SaveChangesAsync();
        var transaction = new MemberDeletionTransaction(context, new FixedClock(Now));

        var deleted = await transaction.SoftDeleteAndRevokeAsync(10, 1, "127.0.0.1");

        Assert.True(deleted);
        Assert.True((await context.Members.AsNoTracking().SingleAsync()).IsDeleted);
        var goer = await context.Users.AsNoTracking().SingleAsync(user => user.UserID == 2);
        Assert.False(goer.IsActive);
        Assert.Equal(Now, goer.UpdatedAt);
        var invites = await context.AccountInvites.AsNoTracking().ToListAsync();
        Assert.Equal(2, invites.Count);
        Assert.All(invites, invite => Assert.Equal(Now, invite.RevokedAtUtc));
        var audit = await context.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal("Member Deleted", audit.Action);
        Assert.Contains("OperationId:", audit.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Member 10", audit.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_repository_delete_path_cannot_bypass_atomic_revocation()
    {
        await using var context = CreateContext();
        var repository = new MemberRepository(context);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            repository.DeleteAsync(CreateMember(99)));
    }

    [Fact]
    public async Task Canonical_member_creation_atomically_initializes_projection_version()
    {
        await using var context = CreateContext();
        var repository = new MemberRepository(context);
        var member = CreateMember(0);

        await repository.AddAsync(member);

        Assert.True(member.MemberID > 0);
        var version = await context.MemberProjectionVersions
            .AsNoTracking()
            .SingleAsync(item => item.MemberID == member.MemberID);
        Assert.Equal(0, version.Version);
    }

    [Fact]
    public async Task Failed_member_creation_persists_neither_member_nor_projection_version()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ThrowingDbContext(options) { ThrowOnSave = true };
        var repository = new MemberRepository(context);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.AddAsync(CreateMember(0)));

        context.ChangeTracker.Clear();
        Assert.Empty(context.Members);
        Assert.Empty(context.MemberProjectionVersions);
    }

    [Fact]
    public async Task Corrupt_backoffice_member_link_is_not_deactivated_by_goer_revocation()
    {
        await using var context = CreateContext();
        context.Members.Add(CreateMember(11));
        context.Users.AddRange(
            CreateUser(1, UserRole.Administrator, null, true),
            CreateUser(3, UserRole.Receptionist, 11, true));
        await context.SaveChangesAsync();
        var transaction = new MemberDeletionTransaction(context, new FixedClock(Now));

        Assert.True(await transaction.SoftDeleteAndRevokeAsync(11, 1, "127.0.0.1"));

        Assert.True((await context.Users.AsNoTracking()
            .SingleAsync(user => user.UserID == 3)).IsActive);
    }

    [Fact]
    public async Task Audit_ip_controls_are_sanitized()
    {
        await using var context = CreateContext();
        context.Members.Add(CreateMember(13));
        context.Users.Add(CreateUser(1, UserRole.Administrator, null, true));
        await context.SaveChangesAsync();

        var transaction = new MemberDeletionTransaction(context, new FixedClock(Now));
        Assert.True(await transaction.SoftDeleteAndRevokeAsync(13, 1, "127.0.0.1\r\nInjected"));

        Assert.Equal("Unknown", (await context.AuditLogs.AsNoTracking().SingleAsync()).IPAddress);
    }

    [Fact]
    public async Task Soft_delete_uses_invite_then_member_then_user_lock_order()
    {
        await using var context = CreateContext();
        context.Members.Add(CreateMember(15));
        context.Users.AddRange(
            CreateUser(1, UserRole.Administrator, null, true),
            CreateUser(6, UserRole.GymGoer, 15, true));
        context.AccountInvites.Add(CreateOutstandingInvite(15, 3, Now.AddHours(1)));
        await context.SaveChangesAsync();
        var transaction = new RecordingMemberDeletionTransaction(
            context,
            new FixedClock(Now));

        Assert.True(await transaction.SoftDeleteAndRevokeAsync(15, 1, "127.0.0.1"));

        Assert.Equal(new[] { "Invites", "Member", "Users" }, transaction.LockOrder);
    }

    [Fact]
    public async Task Non_utc_clock_fails_without_persisting_mutation()
    {
        await using var context = CreateContext();
        context.Members.Add(CreateMember(14));
        context.Users.AddRange(
            CreateUser(1, UserRole.Administrator, null, true),
            CreateUser(5, UserRole.GymGoer, 14, true));
        await context.SaveChangesAsync();
        var transaction = new MemberDeletionTransaction(
            context,
            new FixedClock(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transaction.SoftDeleteAndRevokeAsync(14, 1, "127.0.0.1"));

        Assert.False((await context.Members.AsNoTracking()
            .SingleAsync(member => member.MemberID == 14)).IsDeleted);
        Assert.True((await context.Users.AsNoTracking()
            .SingleAsync(user => user.UserID == 5)).IsActive);
        Assert.Empty(context.AuditLogs);
    }

    [Fact]
    public async Task Failed_save_persists_neither_delete_revocation_nor_audit()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        await using (var seed = new GymDbContext(options))
        {
            seed.Members.Add(CreateMember(12));
            seed.Users.AddRange(
                CreateUser(1, UserRole.Administrator, null, true),
                CreateUser(4, UserRole.GymGoer, 12, true));
            seed.AccountInvites.Add(CreateOutstandingInvite(12, 4, Now.AddHours(1)));
            await seed.SaveChangesAsync();
        }

        await using (var failing = new ThrowingDbContext(options))
        {
            failing.ThrowOnSave = true;
            var transaction = new MemberDeletionTransaction(failing, new FixedClock(Now));
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                transaction.SoftDeleteAndRevokeAsync(12, 1, "127.0.0.1"));
        }

        await using var verify = new GymDbContext(options);
        Assert.False((await verify.Members.AsNoTracking().SingleAsync()).IsDeleted);
        Assert.True((await verify.Users.AsNoTracking()
            .SingleAsync(user => user.UserID == 4)).IsActive);
        Assert.Null((await verify.AccountInvites.AsNoTracking().SingleAsync()).RevokedAtUtc);
        Assert.Empty(verify.AuditLogs);
    }

    private static GymDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GymDbContext(options);
    }

    private static Member CreateMember(int id) => new()
    {
        MemberID = id,
        FirstName = "Member",
        LastName = id.ToString(),
        Gender = "Other",
        BirthDate = new DateTime(1990, 1, 1),
        PhoneNumber = $"555{id:D7}",
        EmergencyContact = "Contact",
        QRCode = $"QR-{id}",
        Status = "Active",
        DateRegistered = Now,
        LastModified = Now
    };

    private static User CreateUser(
        int id,
        UserRole role,
        int? memberId,
        bool active) => new()
    {
        UserID = id,
        FirebaseUid = $"uid-{id}",
        MemberID = memberId,
        Username = $"user-{id}",
        Email = $"user-{id}@example.test",
        NormalizedEmail = $"USER-{id}@EXAMPLE.TEST",
        FirstName = "Test",
        LastName = "User",
        Role = role,
        IsActive = active,
        EmailVerified = true,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static AccountInvite CreateOutstandingInvite(
        int memberId,
        byte discriminator,
        DateTime expiresAtUtc) => new()
    {
        TargetMemberID = memberId,
        TokenHash = Enumerable.Repeat(discriminator, 32).ToArray(),
        NormalizedEmail = $"MEMBER-{memberId}@EXAMPLE.TEST",
        IntendedRole = UserRole.GymGoer,
        Purpose = "Mobile access",
        CreatedByUserID = 1,
        CreatedAtUtc = Now.AddHours(-2),
        ExpiresAtUtc = expiresAtUtc
    };

    private sealed class FixedClock : IClockService
    {
        public FixedClock(DateTime now)
        {
            UtcNow = now;
        }

        public DateTime UtcNow { get; }
    }

    private sealed class RecordingMemberDeletionTransaction : MemberDeletionTransaction
    {
        public RecordingMemberDeletionTransaction(
            GymDbContext context,
            IClockService clock)
            : base(context, clock)
        {
        }

        public List<string> LockOrder { get; } = new();

        protected override async Task<List<AccountInvite>> LockOutstandingMemberInvitesAsync(
            int memberId,
            CancellationToken cancellationToken)
        {
            LockOrder.Add("Invites");
            return await base.LockOutstandingMemberInvitesAsync(memberId, cancellationToken);
        }

        protected override async Task<Member?> LockMemberAsync(
            int memberId,
            CancellationToken cancellationToken)
        {
            LockOrder.Add("Member");
            return await base.LockMemberAsync(memberId, cancellationToken);
        }

        protected override async Task<List<User>> LockLinkedUsersAsync(
            int memberId,
            CancellationToken cancellationToken)
        {
            LockOrder.Add("Users");
            return await base.LockLinkedUsersAsync(memberId, cancellationToken);
        }
    }

    private sealed class ThrowingDbContext : GymDbContext
    {
        public ThrowingDbContext(DbContextOptions<GymDbContext> options)
            : base(options)
        {
        }

        public bool ThrowOnSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new DbUpdateException(
                    "Simulated atomic deletion failure.",
                    new TimeoutException("Database unavailable."));
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
