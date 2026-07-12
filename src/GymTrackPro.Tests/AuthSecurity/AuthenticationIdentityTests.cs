using GymTrackPro.API.Authentication;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Moq;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class AuthenticationIdentityTests
{
    [Fact]
    public async Task Sync_returns_only_uid_linked_store_profile()
    {
        var store = new Mock<IIdentityProvisioningStore>(MockBehavior.Strict);
        store.Setup(value => value.SyncLinkedUserAsync(
                "uid-1",
                "owner@example.test",
                It.IsAny<IdentityOperationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity());
        var service = CreateService(store.Object);

        var response = await service.SyncUserAsync("uid-1", "owner@example.test");

        Assert.Equal(7, response.UserID);
        Assert.Equal(UserRole.Administrator, response.Role);
        store.VerifyAll();
    }

    [Fact]
    public async Task Sync_propagates_pending_activation_without_trying_an_invite_or_creation()
    {
        var store = new Mock<IIdentityProvisioningStore>(MockBehavior.Strict);
        store.Setup(value => value.SyncLinkedUserAsync(
                "uid-new",
                "person@example.test",
                It.IsAny<IdentityOperationContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AppAccessException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.AccountPendingActivation,
                "App access is pending activation."));
        var service = CreateService(store.Object);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            service.SyncUserAsync("uid-new", "person@example.test"));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
        Assert.Equal(ErrorCodes.AccountPendingActivation, exception.ErrorCode);
        store.VerifyAll();
    }

    [Fact]
    public async Task Create_invite_returns_code_once_and_passes_only_raw_hash_to_store()
    {
        byte[]? persistedHash = null;
        var store = new Mock<IIdentityProvisioningStore>(MockBehavior.Strict);
        store.Setup(value => value.CreateOrReplaceMemberInviteAsync(
                12,
                7,
                It.IsAny<byte[]>(),
                "Mobile access",
                It.IsAny<IdentityOperationContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<int, int, byte[], string, IdentityOperationContext, CancellationToken>(
                (_, _, hash, _, _, _) => persistedHash = hash.ToArray())
            .ReturnsAsync((int targetId, int actorId, byte[] hash, string purpose, IdentityOperationContext operation, CancellationToken cancellationToken) =>
                new AccountInvite
                {
                    TargetMemberID = 12,
                    TokenHash = hash,
                    NormalizedEmail = "MEMBER@EXAMPLE.TEST",
                    IntendedRole = UserRole.GymGoer,
                    Purpose = purpose,
                    CreatedByUserID = 7,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(72)
                });
        var service = CreateService(store.Object);

        var response = await service.CreateMemberInviteAsync(
            12,
            7,
            new CreateAppInviteDto { Purpose = "Mobile access" });

        Assert.True(InviteCodeCodec.IsValid(response.InviteCode));
        Assert.NotNull(persistedHash);
        Assert.Equal(InviteCodeCodec.HashBytes, persistedHash!.Length);
        Assert.True(InviteCodeCodec.TryHash(response.InviteCode, out var expectedHash));
        Assert.Equal(expectedHash, persistedHash);
        Assert.NotEqual(response.InviteCode, Convert.ToBase64String(persistedHash));
        store.VerifyAll();
    }

    [Fact]
    public async Task Invite_status_is_expired_at_exact_expiry_instant()
    {
        var now = new DateTime(2026, 7, 12, 6, 0, 0, DateTimeKind.Utc);
        var store = new Mock<IIdentityProvisioningStore>(MockBehavior.Strict);
        store.Setup(value => value.GetLatestMemberInviteAsync(12, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountInvite
            {
                TargetMemberID = 12,
                TokenHash = Enumerable.Repeat((byte)1, 32).ToArray(),
                NormalizedEmail = "MEMBER@EXAMPLE.TEST",
                IntendedRole = UserRole.GymGoer,
                Purpose = "Mobile access",
                CreatedByUserID = 7,
                CreatedAtUtc = now.AddHours(-1),
                ExpiresAtUtc = now
            });
        var service = new AuthenticationService(
            store.Object,
            new FixedClock(now),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var response = await service.GetMemberInviteStatusAsync(12);

        Assert.Equal("Expired", response.Status);
        store.VerifyAll();
    }

    [Theory]
    [InlineData(" Owner@Example.Test ", "OWNER@EXAMPLE.TEST")]
    [InlineData("member@example.test", "MEMBER@EXAMPLE.TEST")]
    public void Email_normalization_is_deterministic(string input, string expected)
    {
        Assert.True(EmailNormalization.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad email@example.test")]
    [InlineData("bad\u0001@example.test")]
    public void Invalid_email_claim_is_rejected(string input)
    {
        Assert.False(EmailNormalization.TryNormalize(input, out _));
    }

    private static AuthenticationService CreateService(IIdentityProvisioningStore store) => new(
        store,
        new FixedClock(DateTime.UtcNow),
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

    private static AppUserIdentity CreateIdentity() => new(
        7,
        "uid-1",
        null,
        "owner",
        "owner@example.test",
        "Gym",
        "Owner",
        UserRole.Administrator,
        true,
        true,
        DateTime.UtcNow.AddDays(-10),
        DateTime.UtcNow.AddDays(-1),
        null);
}

internal sealed class FixedClock : IClockService
{
    public FixedClock(DateTime utcNow)
    {
        UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    public DateTime UtcNow { get; set; }
    public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
}
