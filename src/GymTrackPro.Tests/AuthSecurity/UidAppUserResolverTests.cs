using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class UidAppUserResolverTests
{
    [Fact]
    public async Task Verified_email_match_without_uid_link_does_not_resolve()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateUser(1, null, "owner@example.test", UserRole.Administrator, true));
        await context.SaveChangesAsync();
        var resolver = CreateResolver(context);

        var result = await resolver.ResolveAsync("uid-owner", "owner@example.test");

        Assert.Equal(AppUserResolutionStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Exact_uid_link_resolves_full_active_sql_identity_without_tracking_mutation()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateUser(7, "Uid-Exact", "owner@example.test", UserRole.Administrator, true));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var resolver = CreateResolver(context);

        var result = await resolver.ResolveAsync("Uid-Exact", "new-address@example.test");

        Assert.Equal(AppUserResolutionStatus.Resolved, result.Status);
        Assert.Equal(7, result.User!.UserId);
        Assert.Equal("Uid-Exact", result.User.FirebaseUid);
        Assert.DoesNotContain(context.ChangeTracker.Entries(), entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public async Task Inactive_uid_link_is_denied()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateUser(2, "uid-staff", "staff@example.test", UserRole.Receptionist, false));
        await context.SaveChangesAsync();

        var result = await CreateResolver(context)
            .ResolveAsync("uid-staff", "staff@example.test");

        Assert.Equal(AppUserResolutionStatus.Inactive, result.Status);
    }

    [Fact]
    public async Task Gym_goer_without_member_link_is_denied_and_never_advertises_self_capability()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateUser(3, "uid-goer", "goer@example.test", UserRole.GymGoer, true));
        await context.SaveChangesAsync();

        var result = await CreateResolver(context)
            .ResolveAsync("uid-goer", "goer@example.test");

        Assert.Equal(AppUserResolutionStatus.InvalidLink, result.Status);
        var malformed = new AppUserIdentity(
            3,
            "uid-goer",
            null,
            "member-3",
            "goer@example.test",
            "Test",
            "Goer",
            UserRole.GymGoer,
            true,
            true,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null).ToResponse();
        Assert.Equal("ACCOUNT_PENDING_ACTIVATION", malformed.OnboardingState);
        Assert.Empty(malformed.Capabilities);
    }

    [Fact]
    public async Task Linked_gym_goer_maps_internal_member_identity()
    {
        await using var context = CreateContext();
        context.Members.Add(CreateMember(44));
        var user = CreateUser(4, "uid-goer", "goer@example.test", UserRole.GymGoer, true);
        user.MemberID = 44;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var result = await CreateResolver(context)
            .ResolveAsync("uid-goer", "goer@example.test");

        Assert.Equal(AppUserResolutionStatus.Resolved, result.Status);
        Assert.Equal(44, result.User!.MemberId);
        Assert.Contains("GymGoerSelf", result.User.ToResponse().Capabilities);
    }

    [Fact]
    public async Task Gym_goer_link_to_missing_member_fails_closed()
    {
        await using var context = CreateContext();
        var user = CreateUser(5, "uid-missing", "missing@example.test", UserRole.GymGoer, true);
        user.MemberID = 404;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var result = await CreateResolver(context)
            .ResolveAsync("uid-missing", "missing@example.test");

        Assert.Equal(AppUserResolutionStatus.InvalidLink, result.Status);
    }

    [Fact]
    public async Task Active_user_linked_to_soft_deleted_member_is_denied()
    {
        await using var context = CreateContext();
        var deletedMember = CreateMember(45);
        deletedMember.IsDeleted = true;
        context.Members.Add(deletedMember);
        var user = CreateUser(6, "uid-deleted", "deleted@example.test", UserRole.GymGoer, true);
        user.MemberID = 45;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var result = await CreateResolver(context)
            .ResolveAsync("uid-deleted", "deleted@example.test");

        Assert.Equal(AppUserResolutionStatus.Inactive, result.Status);
    }

    [Fact]
    public async Task Inactive_member_status_alone_does_not_revoke_identity()
    {
        await using var context = CreateContext();
        var member = CreateMember(46);
        member.Status = "Inactive";
        context.Members.Add(member);
        var user = CreateUser(7, "uid-status", "status@example.test", UserRole.GymGoer, true);
        user.MemberID = 46;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var result = await CreateResolver(context)
            .ResolveAsync("uid-status", "status@example.test");

        Assert.Equal(AppUserResolutionStatus.Resolved, result.Status);
    }

    private static GymDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GymDbContext(options);
    }

    private static UidAppUserResolver CreateResolver(GymDbContext context) => new(
        context,
        NullLogger<UidAppUserResolver>.Instance);

    private static User CreateUser(
        int id,
        string? firebaseUid,
        string email,
        UserRole role,
        bool active) => new()
    {
        UserID = id,
        FirebaseUid = firebaseUid,
        Username = $"user-{id}",
        Email = email,
        NormalizedEmail = email.Trim().ToUpperInvariant(),
        FirstName = "Test",
        LastName = "User",
        Role = role,
        IsActive = active,
        EmailVerified = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Member CreateMember(int id) => new()
    {
        MemberID = id,
        FirstName = "Test",
        LastName = "Member",
        Gender = "Other",
        BirthDate = new DateTime(1990, 1, 1),
        PhoneNumber = $"555{id:D7}",
        EmergencyContact = "Contact",
        QRCode = $"QR-{id}",
        Status = "Active",
        DateRegistered = DateTime.UtcNow,
        LastModified = DateTime.UtcNow
    };
}
