using System.ComponentModel.DataAnnotations;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Controllers;
using GymTrackPro.API.Data;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class StaffInviteProvisioningTests
{
    private static readonly DateTime Now = new(2026, 7, 12, 8, 0, 0, DateTimeKind.Utc);
    private static readonly IdentityOperationContext OperationContext = new("staff-test", "127.0.0.1");

    [Fact]
    public async Task Owner_provisions_receptionist_and_invite_atomically_with_fixed_server_role()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateOwner());
        await context.SaveChangesAsync();
        var code = InviteCodeCodec.Generate();
        Assert.True(InviteCodeCodec.TryHash(code, out var hash));
        var store = new IdentityProvisioningStore(context, new FixedClock(Now));

        var result = await store.CreateStaffWithInviteAsync(
            1,
            "  Ada  ",
            " Lovelace ",
            " Ada.Staff@Example.Test ",
            hash,
            " Front desk access ",
            OperationContext);

        Assert.True(result.User.UserId > 0);
        Assert.Equal(UserRole.Receptionist, result.User.Role);
        Assert.Equal("Ada", result.User.FirstName);
        Assert.Equal("Lovelace", result.User.LastName);
        Assert.Equal("Ada.Staff@Example.Test", result.User.Email);
        Assert.StartsWith("staff-", result.User.Username, StringComparison.Ordinal);
        Assert.Equal(46, result.User.Username.Length);
        Assert.Null(result.User.MemberId);
        Assert.Empty(result.User.FirebaseUid);
        Assert.True(result.User.IsActive);
        Assert.False(result.User.EmailVerified);

        var user = await context.Users.SingleAsync(candidate => candidate.UserID == result.User.UserId);
        Assert.Equal("ADA.STAFF@EXAMPLE.TEST", user.NormalizedEmail);
        Assert.Null(user.PasswordHash);
        Assert.Equal(UserRole.Receptionist, user.Role);
        var invite = await context.AccountInvites.SingleAsync();
        Assert.Equal(user.UserID, invite.TargetUserID);
        Assert.Equal(UserRole.Receptionist, invite.IntendedRole);
        Assert.Equal("Front desk access", invite.Purpose);
        Assert.Equal(Now.AddHours(72), invite.ExpiresAtUtc);
        Assert.Equal(hash, invite.TokenHash);
        Assert.Single(await context.AuditLogs.Where(item => item.Action == "StaffProfileAndInviteCreated").ToListAsync());
    }

    [Fact]
    public async Task Duplicate_email_is_case_insensitive_and_leaves_no_partial_staff_or_invite()
    {
        await using var context = CreateContext();
        context.Users.AddRange(CreateOwner(), new User
        {
            UserID = 2,
            Username = "existing",
            Email = "staff@example.test",
            NormalizedEmail = "STAFF@EXAMPLE.TEST",
            FirstName = "Existing",
            LastName = "Staff",
            Role = UserRole.Receptionist,
            IsActive = true,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(Now));

        var exception = await Assert.ThrowsAsync<AppAccessException>(() => store.CreateStaffWithInviteAsync(
            1,
            "New",
            "Staff",
            "  STAFF@EXAMPLE.TEST  ",
            ValidHash(),
            "Front desk",
            OperationContext));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal(ErrorCodes.IdentityConflict, exception.ErrorCode);
        Assert.Equal(2, await context.Users.CountAsync());
        Assert.Empty(await context.AccountInvites.ToListAsync());
    }

    [Fact]
    public async Task Receptionist_actor_cannot_provision_another_staff_account()
    {
        await using var context = CreateContext();
        var actor = CreateOwner();
        actor.Role = UserRole.Receptionist;
        context.Users.Add(actor);
        await context.SaveChangesAsync();
        var store = new IdentityProvisioningStore(context, new FixedClock(Now));

        var exception = await Assert.ThrowsAsync<AppAccessException>(() => store.CreateStaffWithInviteAsync(
            1, "New", "Staff", "new@example.test", ValidHash(), "Front desk", OperationContext));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
        Assert.Equal(ErrorCodes.AccessForbidden, exception.ErrorCode);
        Assert.Single(await context.Users.ToListAsync());
        Assert.Empty(await context.AccountInvites.ToListAsync());
    }

    [Fact]
    public async Task Save_failure_rolls_back_staff_invite_and_audit_unit()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FailingSaveGymDbContext(options);
        context.Users.Add(CreateOwner());
        await context.SaveChangesAsync();
        context.FailSaves = true;
        var store = new IdentityProvisioningStore(context, new FixedClock(Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => store.CreateStaffWithInviteAsync(
            1, "New", "Staff", "new@example.test", ValidHash(), "Front desk", OperationContext));

        context.FailSaves = false;
        Assert.Single(await context.Users.AsNoTracking().ToListAsync());
        Assert.Empty(await context.AccountInvites.AsNoTracking().ToListAsync());
        Assert.Empty(await context.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Controller_returns_created_wrapper_and_delegates_actor_identity()
    {
        var dto = ValidDto();
        var expected = new StaffInviteProvisioningResponseDto
        {
            User = new UserResponseDto { UserID = 9, Role = UserRole.Receptionist },
            Invite = new AppInviteCodeResponseDto { InviteCode = InviteCodeCodec.Generate() }
        };
        var service = new Mock<IAuthenticationService>(MockBehavior.Strict);
        service.Setup(item => item.CreateStaffWithInviteAsync(1, dto)).ReturnsAsync(expected);
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.SetupGet(item => item.UserId).Returns(1);
        var controller = new UsersController(service.Object, currentUser.Object);

        var action = await controller.CreateStaff(dto);

        var created = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var response = Assert.IsType<ApiResponse<StaffInviteProvisioningResponseDto>>(created.Value);
        Assert.True(response.Success);
        Assert.Same(expected, response.Data);
        service.VerifyAll();
    }

    [Fact]
    public async Task Service_returns_plaintext_code_once_and_marks_unlinked_staff_pending_activation()
    {
        byte[]? capturedHash = null;
        var store = new Mock<IIdentityProvisioningStore>(MockBehavior.Strict);
        store.Setup(item => item.CreateStaffWithInviteAsync(
                1,
                "Ada",
                "Lovelace",
                "ada@example.test",
                It.IsAny<byte[]>(),
                "Front desk",
                It.IsAny<IdentityOperationContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<int, string, string, string, byte[], string, IdentityOperationContext, CancellationToken>(
                (_, _, _, _, hash, _, _, _) => capturedHash = hash.ToArray())
            .ReturnsAsync((int _, string _, string _, string _, byte[] hash, string purpose,
                    IdentityOperationContext _, CancellationToken _) =>
                new StaffInviteProvisioningResult(
                    new AppUserIdentity(
                        9, string.Empty, null, "staff-test", "ada@example.test", "Ada", "Lovelace",
                        UserRole.Receptionist, true, false, Now, Now, null),
                    new AccountInvite
                    {
                        TargetUserID = 9,
                        TokenHash = hash,
                        NormalizedEmail = "ADA@EXAMPLE.TEST",
                        IntendedRole = UserRole.Receptionist,
                        Purpose = purpose,
                        CreatedByUserID = 1,
                        CreatedAtUtc = Now,
                        ExpiresAtUtc = Now.AddHours(72)
                    }));
        var service = new AuthenticationService(
            store.Object,
            new FixedClock(Now),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var response = await service.CreateStaffWithInviteAsync(1, ValidDto());

        Assert.True(InviteCodeCodec.IsValid(response.Invite.InviteCode));
        Assert.NotNull(capturedHash);
        Assert.True(InviteCodeCodec.TryHash(response.Invite.InviteCode, out var expectedHash));
        Assert.Equal(expectedHash, capturedHash);
        Assert.Equal(ErrorCodes.AccountPendingActivation, response.User.OnboardingState);
        Assert.Empty(response.User.Capabilities);
        Assert.Equal(UserRole.Receptionist, response.User.Role);
        store.VerifyAll();
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Request_model_rejects_invalid_input(CreateStaffInviteDto dto)
    {
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.NotEmpty(results);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return new object[] { CreateDto(firstName: "") };
        yield return new object[] { CreateDto(lastName: new string('x', 101)) };
        yield return new object[] { CreateDto(email: "not-an-email") };
        yield return new object[] { CreateDto(purpose: "") };
    }

    private static CreateStaffInviteDto ValidDto() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@example.test",
        Purpose = "Front desk"
    };

    private static CreateStaffInviteDto CreateDto(
        string firstName = "Ada",
        string lastName = "Lovelace",
        string email = "ada@example.test",
        string purpose = "Front desk") => new()
    {
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        Purpose = purpose
    };

    private static byte[] ValidHash() => Enumerable.Repeat((byte)7, InviteCodeCodec.HashBytes).ToArray();

    private static GymDbContext CreateContext() => new(
        new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static User CreateOwner() => new()
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
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private sealed class FailingSaveGymDbContext : GymDbContext
    {
        public FailingSaveGymDbContext(DbContextOptions<GymDbContext> options) : base(options) { }

        public bool FailSaves { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailSaves)
            {
                throw new DbUpdateException("Simulated save failure.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
