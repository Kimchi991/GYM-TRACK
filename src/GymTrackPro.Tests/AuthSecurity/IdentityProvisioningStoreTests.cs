using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class IdentityProvisioningStoreTests
{
    private static readonly IdentityOperationContext OperationContext = new("test-correlation", "127.0.0.1");

    [Fact]
    public async Task Member_redemption_is_atomic_and_same_uid_response_loss_replay_is_idempotent()
    {
        var now = new DateTime(2026, 7, 12, 1, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        SeedActorAndMember(context);
        var code = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(code, out var hash));
        context.AccountInvites.Add(CreateMemberInvite(hash, now.AddHours(-1), now.AddHours(1)));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var clock = new FixedClock(now);
        var store = new IdentityProvisioningStore(context, clock);
        var operationId = Guid.NewGuid();

        var first = await store.RedeemInviteAsync(
            "firebase-goer",
            "member@example.test",
            hash,
            operationId,
            OperationContext);
        var replay = await store.RedeemInviteAsync(
            "firebase-goer",
            "member@example.test",
            hash,
            operationId,
            OperationContext);

        Assert.Equal(first.UserId, replay.UserId);
        Assert.True(first.UserId > 0);
        Assert.Equal(UserRole.GymGoer, first.Role);
        Assert.Equal(10, first.MemberId);
        var user = await context.Users.SingleAsync(candidate => candidate.MemberID == 10);
        Assert.Equal("firebase-goer", user.FirebaseUid);
        Assert.Equal("member-10", user.Username);
        var invite = await context.AccountInvites.SingleAsync();
        Assert.Equal(operationId, invite.RedemptionOperationId);
        Assert.Equal("firebase-goer", invite.UsedByFirebaseUid);
        Assert.NotNull(invite.UsedAtUtc);
        Assert.True(invite.UsedAtUtc < invite.ExpiresAtUtc);
        Assert.Single(await context.Users.Where(candidate => candidate.MemberID == 10).ToListAsync());
    }

    [Fact]
    public async Task Used_invite_mismatches_return_the_same_generic_denial()
    {
        var now = new DateTime(2026, 7, 12, 1, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        SeedActorAndMember(context);
        var code = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(code, out var hash));
        context.AccountInvites.Add(CreateMemberInvite(hash, now.AddMinutes(-1), now.AddHours(1)));
        await context.SaveChangesAsync();
        var logger = new RecordingLogger<IdentityProvisioningStore>();
        var store = new IdentityProvisioningStore(context, new FixedClock(now), logger);
        var operationId = Guid.NewGuid();
        _ = await store.RedeemInviteAsync(
            "uid-one",
            "member@example.test",
            hash,
            operationId,
            OperationContext);

        var otherUid = await Assert.ThrowsAsync<AppAccessException>(() => store.RedeemInviteAsync(
            "uid-two",
            "member@example.test",
            hash,
            operationId,
            OperationContext));
        var otherOperation = await Assert.ThrowsAsync<AppAccessException>(() => store.RedeemInviteAsync(
            "uid-one",
            "member@example.test",
            hash,
            Guid.NewGuid(),
            OperationContext));
        Assert.True(InviteCodeCodec.TryHash(InviteCodeCodec.Generate(), out var unknownHash));
        var unavailable = await Assert.ThrowsAsync<AppAccessException>(() => store.RedeemInviteAsync(
            "uid-one",
            "member@example.test",
            unknownHash,
            Guid.NewGuid(),
            OperationContext));

        Assert.Equal(ErrorCodes.InviteInvalid, otherUid.ErrorCode);
        Assert.Equal(ErrorCodes.InviteInvalid, otherOperation.ErrorCode);
        Assert.Equal(otherUid.PublicMessage, otherOperation.PublicMessage);
        Assert.Equal(otherUid.StatusCode, unavailable.StatusCode);
        Assert.Equal(otherUid.ErrorCode, unavailable.ErrorCode);
        Assert.Equal(otherUid.PublicMessage, unavailable.PublicMessage);
        Assert.Equal(2, logger.Entries.Count(entry =>
            entry.Contains("INVITE_REPLAY_MISMATCH", StringComparison.Ordinal)));
        foreach (var entry in logger.Entries)
        {
            Assert.DoesNotContain(code, entry, StringComparison.Ordinal);
            Assert.DoesNotContain("uid-one", entry, StringComparison.Ordinal);
            Assert.DoesNotContain("uid-two", entry, StringComparison.Ordinal);
            Assert.DoesNotContain("member@example.test", entry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(OperationContext.IpAddress, entry, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToHexString(hash), entry, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Operation_reuse_with_different_invite_fingerprint_is_rejected()
    {
        var now = new DateTime(2026, 7, 12, 1, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        SeedActorAndMember(context);
        context.Members.Add(CreateMember(11, "second@example.test"));
        var firstCode = InviteCodeCodec.Generate();
        var secondCode = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(firstCode, out var firstHash));
        Assert.True(InviteCodeCodec.TryHash(secondCode, out var secondHash));
        var operationId = Guid.NewGuid();
        var firstInvite = CreateMemberInvite(firstHash, now.AddMinutes(-5), now.AddHours(1));
        firstInvite.UsedAtUtc = now.AddMinutes(-1);
        firstInvite.UsedByFirebaseUid = "uid-one";
        firstInvite.RedemptionOperationId = operationId;
        var secondInvite = CreateMemberInvite(secondHash, now.AddMinutes(-5), now.AddHours(1));
        secondInvite.TargetMemberID = 11;
        secondInvite.NormalizedEmail = "SECOND@EXAMPLE.TEST";
        context.AccountInvites.AddRange(firstInvite, secondInvite);
        context.Users.Add(CreateGoerUser(20, 10, "uid-one", "member@example.test"));
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(now));

        var exception = await Assert.ThrowsAsync<AppAccessException>(() => store.RedeemInviteAsync(
            "uid-two",
            "second@example.test",
            secondHash,
            operationId,
            OperationContext));

        Assert.Equal(ErrorCodes.ActivationOperationConflict, exception.ErrorCode);
    }

    [Fact]
    public async Task Exact_expiry_instant_is_end_exclusive_and_does_not_write()
    {
        var expiry = new DateTime(2026, 7, 12, 2, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        SeedActorAndMember(context);
        var code = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(code, out var hash));
        context.AccountInvites.Add(CreateMemberInvite(hash, expiry.AddHours(-1), expiry));
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(expiry));

        var exception = await Assert.ThrowsAsync<AppAccessException>(() => store.RedeemInviteAsync(
            "uid-expired",
            "member@example.test",
            hash,
            Guid.NewGuid(),
            OperationContext));

        Assert.Equal(ErrorCodes.InviteInvalid, exception.ErrorCode);
        Assert.DoesNotContain(context.Users, candidate => candidate.MemberID == 10);
        var invite = await context.AccountInvites.SingleAsync();
        Assert.Null(invite.UsedAtUtc);
        Assert.Null(invite.UsedByFirebaseUid);
        Assert.Null(invite.RedemptionOperationId);
    }

    [Fact]
    public void Unused_invite_with_all_null_redemption_metadata_has_valid_runtime_shape()
    {
        var now = new DateTime(2026, 7, 12, 1, 30, 0, DateTimeKind.Utc);
        var invite = CreateMemberInvite(
            Enumerable.Repeat((byte)1, InviteCodeCodec.HashBytes).ToArray(),
            now.AddHours(-1),
            now.AddHours(1));

        Assert.True(IdentityProvisioningStore.HasValidInviteShape(invite));
    }

    [Fact]
    public void Fully_populated_nonempty_redemption_metadata_has_valid_runtime_shape()
    {
        var now = new DateTime(2026, 7, 12, 1, 30, 0, DateTimeKind.Utc);
        var invite = CreateMemberInvite(
            Enumerable.Repeat((byte)1, InviteCodeCodec.HashBytes).ToArray(),
            now.AddHours(-1),
            now.AddHours(1));
        invite.UsedAtUtc = now.AddMinutes(-1);
        invite.UsedByFirebaseUid = "used-firebase-uid";
        invite.RedemptionOperationId = new Guid("00000000-0000-0000-0000-000000000001");

        Assert.True(IdentityProvisioningStore.HasValidInviteShape(invite));
    }

    [Fact]
    public void Fully_populated_Guid_Empty_redemption_metadata_is_rejected_by_runtime_shape()
    {
        var now = new DateTime(2026, 7, 12, 1, 30, 0, DateTimeKind.Utc);
        var invite = CreateMemberInvite(
            Enumerable.Repeat((byte)1, InviteCodeCodec.HashBytes).ToArray(),
            now.AddHours(-1),
            now.AddHours(1));
        invite.UsedAtUtc = now.AddMinutes(-1);
        invite.UsedByFirebaseUid = "used-firebase-uid";
        invite.RedemptionOperationId = Guid.Empty;

        Assert.False(IdentityProvisioningStore.HasValidInviteShape(invite));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task Partial_redemption_metadata_is_always_rejected_as_invalid_shape(
        bool hasUsedAt,
        bool hasUsedByUid,
        bool hasOperationId)
    {
        var now = new DateTime(2026, 7, 12, 1, 30, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        SeedActorAndMember(context);
        var code = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(code, out var hash));
        var invite = CreateMemberInvite(hash, now.AddHours(-1), now.AddHours(1));
        invite.UsedAtUtc = hasUsedAt ? now.AddMinutes(-1) : null;
        invite.UsedByFirebaseUid = hasUsedByUid ? "partial-used-uid" : null;
        invite.RedemptionOperationId = hasOperationId ? Guid.NewGuid() : null;
        Assert.False(IdentityProvisioningStore.HasValidInviteShape(invite));
        context.AccountInvites.Add(invite);
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(now));
        var attemptedOperationId = invite.RedemptionOperationId ?? Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            store.RedeemInviteAsync(
                "partial-used-uid",
                "member@example.test",
                hash,
                attemptedOperationId,
                OperationContext));

        Assert.Equal(ErrorCodes.InviteInvalid, exception.ErrorCode);
        Assert.DoesNotContain(context.Users, candidate => candidate.MemberID == 10);
    }

    [Fact]
    public async Task Replay_rejects_legacy_used_timestamp_equal_to_expiry()
    {
        var expiry = new DateTime(2026, 7, 12, 2, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        SeedActorAndMember(context);
        var code = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(code, out var hash));
        var operationId = Guid.NewGuid();
        var invite = CreateMemberInvite(hash, expiry.AddHours(-1), expiry);
        invite.UsedAtUtc = expiry;
        invite.UsedByFirebaseUid = "uid-goer";
        invite.RedemptionOperationId = operationId;
        context.AccountInvites.Add(invite);
        context.Users.Add(CreateGoerUser(20, 10, "uid-goer", "member@example.test"));
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(expiry.AddMinutes(1)));

        var exception = await Assert.ThrowsAsync<AppAccessException>(() => store.RedeemInviteAsync(
            "uid-goer",
            "member@example.test",
            hash,
            operationId,
            OperationContext));

        Assert.Equal(ErrorCodes.InviteInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Replacement_revokes_every_prior_unresolved_invite_including_expired_rows()
    {
        var now = new DateTime(2026, 7, 12, 3, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        SeedActorAndMember(context);
        context.AccountInvites.Add(CreateMemberInvite(
            Enumerable.Repeat((byte)1, 32).ToArray(),
            now.AddDays(-2),
            now.AddDays(-1)));
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(now));
        var replacementHash = Enumerable.Repeat((byte)2, 32).ToArray();

        var replacement = await store.CreateOrReplaceMemberInviteAsync(
            10,
            1,
            replacementHash,
            "Replacement",
            OperationContext);

        var rows = await context.AccountInvites.OrderBy(invite => invite.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(now, rows[0].RevokedAtUtc);
        Assert.Null(rows[1].RevokedAtUtc);
        Assert.Equal(replacement.AccountInviteID, rows[1].AccountInviteID);
        Assert.Equal(32, rows[1].TokenHash.Length);
    }

    [Fact]
    public async Task Sync_refreshes_only_email_metadata_for_existing_uid_and_preserves_authority_fields()
    {
        var now = new DateTime(2026, 7, 12, 4, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var user = CreateActor();
        user.Role = UserRole.Receptionist;
        user.LastLoginAt = now.AddDays(-1);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(now));

        var result = await store.SyncLinkedUserAsync(
            "owner-uid",
            "new@example.test",
            OperationContext);

        Assert.Equal(UserRole.Receptionist, result.Role);
        Assert.Null(result.MemberId);
        Assert.Equal("owner-uid", result.FirebaseUid);
        Assert.Equal("new@example.test", result.Email);
        Assert.Equal(now.AddDays(-1), result.LastLoginAt);
        Assert.Single(context.Users);
    }

    [Fact]
    public async Task Sync_unlinked_inactive_and_ambiguous_uid_states_fail_closed()
    {
        await using var unlinkedContext = CreateContext();
        var unlinkedStore = new IdentityProvisioningStore(
            unlinkedContext,
            new FixedClock(DateTime.UtcNow));
        var unlinked = await Assert.ThrowsAsync<AppAccessException>(() =>
            unlinkedStore.SyncLinkedUserAsync(
                "unknown-uid",
                "person@example.test",
                OperationContext));
        Assert.Equal(ErrorCodes.AccountPendingActivation, unlinked.ErrorCode);

        await using var inactiveContext = CreateContext();
        var inactive = CreateActor();
        inactive.IsActive = false;
        inactiveContext.Users.Add(inactive);
        await inactiveContext.SaveChangesAsync();
        var inactiveStore = new IdentityProvisioningStore(
            inactiveContext,
            new FixedClock(DateTime.UtcNow));
        var inactiveException = await Assert.ThrowsAsync<AppAccessException>(() =>
            inactiveStore.SyncLinkedUserAsync(
                "owner-uid",
                "owner@example.test",
                OperationContext));
        Assert.Equal("ACCESS_FORBIDDEN", inactiveException.ErrorCode);

        await using var ambiguousContext = CreateContext();
        var first = CreateActor();
        var second = CreateActor();
        second.UserID = 2;
        second.Username = "owner-two";
        second.Email = "owner-two@example.test";
        second.NormalizedEmail = "OWNER-TWO@EXAMPLE.TEST";
        ambiguousContext.Users.AddRange(first, second);
        await ambiguousContext.SaveChangesAsync();
        var ambiguousStore = new IdentityProvisioningStore(
            ambiguousContext,
            new FixedClock(DateTime.UtcNow));
        var ambiguousException = await Assert.ThrowsAsync<AppAccessException>(() =>
            ambiguousStore.SyncLinkedUserAsync(
                "owner-uid",
                "owner@example.test",
                OperationContext));
        Assert.Equal(ErrorCodes.IdentityConflict, ambiguousException.ErrorCode);
    }

    [Fact]
    public async Task Sync_email_refresh_conflict_preserves_uid_role_member_and_existing_email()
    {
        await using var context = CreateContext();
        var linked = CreateActor();
        linked.Role = UserRole.Receptionist;
        var owner = CreateActor();
        owner.UserID = 2;
        owner.FirebaseUid = "second-uid";
        owner.Username = "second-owner";
        owner.Email = "claimed@example.test";
        owner.NormalizedEmail = "CLAIMED@EXAMPLE.TEST";
        context.Users.AddRange(linked, owner);
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(DateTime.UtcNow));

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            store.SyncLinkedUserAsync(
                "owner-uid",
                "claimed@example.test",
                OperationContext));

        Assert.Equal(ErrorCodes.IdentityConflict, exception.ErrorCode);
        Assert.Equal("owner@example.test", linked.Email);
        Assert.Equal("owner-uid", linked.FirebaseUid);
        Assert.Equal(UserRole.Receptionist, linked.Role);
        Assert.Null(linked.MemberID);
    }

    [Fact]
    public async Task Staff_invite_preserves_preprovisioned_role_and_never_accepts_role_from_request()
    {
        var now = new DateTime(2026, 7, 12, 4, 30, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        context.Users.Add(CreateActor());
        context.Users.Add(new User
        {
            UserID = 2,
            Username = "frontdesk",
            Email = "staff@example.test",
            NormalizedEmail = "STAFF@EXAMPLE.TEST",
            FirstName = "Front",
            LastName = "Desk",
            Role = UserRole.Receptionist,
            IsActive = true,
            EmailVerified = false,
            CreatedAt = now.AddYears(-1),
            UpdatedAt = now.AddYears(-1)
        });
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(now));
        var code = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(code, out var hash));
        var invite = await store.CreateOrReplaceStaffInviteAsync(
            2,
            1,
            hash,
            "Staff mobile access",
            OperationContext);

        var result = await store.RedeemInviteAsync(
            "staff-firebase-uid",
            "staff@example.test",
            hash,
            Guid.NewGuid(),
            OperationContext);

        Assert.Equal(UserRole.Receptionist, invite.IntendedRole);
        Assert.Equal(UserRole.Receptionist, result.Role);
        Assert.Null(result.MemberId);
        var target = await context.Users.SingleAsync(user => user.UserID == 2);
        Assert.Equal("staff-firebase-uid", target.FirebaseUid);
        Assert.Equal(UserRole.Receptionist, target.Role);
    }

    [Fact]
    public async Task Revoke_winner_makes_redemption_invalid_without_partial_link()
    {
        var now = new DateTime(2026, 7, 12, 4, 45, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        SeedActorAndMember(context);
        var code = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(code, out var hash));
        context.AccountInvites.Add(CreateMemberInvite(hash, now.AddMinutes(-1), now.AddHours(1)));
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(now));

        await store.RevokeMemberInvitesAsync(10, 1, OperationContext);
        var exception = await Assert.ThrowsAsync<AppAccessException>(() => store.RedeemInviteAsync(
            "uid-loser",
            "member@example.test",
            hash,
            Guid.NewGuid(),
            OperationContext));

        Assert.Equal(ErrorCodes.InviteInvalid, exception.ErrorCode);
        Assert.DoesNotContain(context.Users, user => user.MemberID == 10);
        var invite = await context.AccountInvites.SingleAsync();
        Assert.NotNull(invite.RevokedAtUtc);
        Assert.Null(invite.UsedAtUtc);
    }

    [Fact]
    public async Task Ineligible_member_target_emits_only_controlled_conflict_category()
    {
        var now = new DateTime(2026, 7, 12, 4, 50, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        SeedActorAndMember(context);
        var code = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(code, out var hash));
        context.AccountInvites.Add(CreateMemberInvite(hash, now.AddMinutes(-1), now.AddHours(1)));
        await context.SaveChangesAsync();
        var member = await context.Members.SingleAsync(value => value.MemberID == 10);
        member.Status = "Inactive";
        await context.SaveChangesAsync();
        var logger = new RecordingLogger<IdentityProvisioningStore>();
        var store = new IdentityProvisioningStore(context, new FixedClock(now), logger);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() => store.RedeemInviteAsync(
            "member-target-uid",
            "member@example.test",
            hash,
            Guid.NewGuid(),
            OperationContext));

        Assert.Equal(ErrorCodes.InviteInvalid, exception.ErrorCode);
        var log = Assert.Single(logger.Entries, entry =>
            entry.Contains("MEMBER_TARGET_CONFLICT", StringComparison.Ordinal));
        Assert.DoesNotContain(code, log, StringComparison.Ordinal);
        Assert.DoesNotContain("member-target-uid", log, StringComparison.Ordinal);
        Assert.DoesNotContain("member@example.test", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OperationContext.IpAddress, log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2601, true)]
    [InlineData(2627, true)]
    [InlineData(1205, false)]
    [InlineData(4060, false)]
    public void Only_sql_unique_numbers_are_business_conflicts(int number, bool expected)
    {
        Assert.Equal(expected, IdentityDatabaseConflictClassifier.IsUniqueViolationNumber(number));
    }

    [Fact]
    public async Task Non_unique_provider_failure_propagates_instead_of_becoming_identity_conflict()
    {
        var now = new DateTime(2026, 7, 12, 5, 0, 0, DateTimeKind.Utc);
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ThrowingGymDbContext(options);
        SeedActorAndMember(context);
        await context.SaveChangesAsync();
        context.ThrowOnSave = true;
        var store = new IdentityProvisioningStore(context, new FixedClock(now));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.CreateOrReplaceMemberInviteAsync(
                10,
                1,
                Enumerable.Repeat((byte)3, 32).ToArray(),
                "Provider failure",
                OperationContext));

        Assert.IsType<TimeoutException>(exception.InnerException);
    }

    private static GymDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GymDbContext(options);
    }

    private static void SeedActorAndMember(GymDbContext context)
    {
        context.Users.Add(CreateActor());
        context.Members.Add(CreateMember(10, "member@example.test"));
    }

    private static User CreateActor() => new()
    {
        UserID = 1,
        FirebaseUid = "owner-uid",
        Username = "owner",
        Email = "owner@example.test",
        NormalizedEmail = "OWNER@EXAMPLE.TEST",
        FirstName = "Gym",
        LastName = "Owner",
        Role = UserRole.Administrator,
        IsActive = true,
        EmailVerified = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Member CreateMember(int memberId, string email) => new()
    {
        MemberID = memberId,
        FirstName = "Test",
        LastName = "Member",
        Gender = "Other",
        BirthDate = new DateTime(2000, 1, 1),
        PhoneNumber = $"0900{memberId:000000}",
        Email = email,
        EmergencyContact = "Test Contact",
        QRCode = $"member-{memberId}-qr",
        Status = "Active",
        IsDeleted = false
    };

    private static AccountInvite CreateMemberInvite(byte[] hash, DateTime created, DateTime expires) => new()
    {
        TargetMemberID = 10,
        TokenHash = hash,
        NormalizedEmail = "MEMBER@EXAMPLE.TEST",
        IntendedRole = UserRole.GymGoer,
        Purpose = "Mobile access",
        CreatedByUserID = 1,
        CreatedAtUtc = created,
        ExpiresAtUtc = expires
    };

    private static User CreateGoerUser(
        int userId,
        int memberId,
        string firebaseUid,
        string email) => new()
    {
        UserID = userId,
        FirebaseUid = firebaseUid,
        MemberID = memberId,
        Username = $"member-{memberId}",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FirstName = "Test",
        LastName = "Member",
        Role = UserRole.GymGoer,
        IsActive = true,
        EmailVerified = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class ThrowingGymDbContext : GymDbContext
    {
        public ThrowingGymDbContext(DbContextOptions<GymDbContext> options)
            : base(options)
        {
        }

        public bool ThrowOnSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new DbUpdateException(
                    "Simulated provider failure.",
                    new TimeoutException("Database unavailable."));
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception));
    }
}
