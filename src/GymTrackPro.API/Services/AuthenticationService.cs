using GymTrackPro.API.Authentication;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IIdentityProvisioningStore _identityStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IClockService _clock;

    public AuthenticationService(
        IIdentityProvisioningStore identityStore,
        IClockService clock,
        IHttpContextAccessor httpContextAccessor)
    {
        _identityStore = identityStore;
        _clock = clock;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<UserResponseDto> SyncUserAsync(string firebaseUid, string email)
    {
        var user = await _identityStore.SyncLinkedUserAsync(
            firebaseUid,
            email,
            GetOperationContext(),
            RequestAborted);
        return user.ToResponse();
    }

    public async Task<UserResponseDto> GetCurrentUserAsync(int userId, string firebaseUid)
    {
        var user = await _identityStore.GetCurrentUserAsync(
            userId,
            firebaseUid,
            RequestAborted);
        return user.ToResponse();
    }

    public async Task<UserResponseDto> ActivateAppAsync(
        string firebaseUid,
        string email,
        ActivateInviteDto request)
    {
        if (request is null
            || request.OperationId == Guid.Empty
            || !InviteCodeCodec.TryHash(request.InviteCode, out var tokenHash))
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                Shared.Constants.ErrorCodes.InviteInvalid,
                "The activation request is invalid or no longer available.");
        }

        var user = await _identityStore.RedeemInviteAsync(
            firebaseUid,
            email,
            tokenHash,
            request.OperationId,
            GetOperationContext(),
            RequestAborted);
        return user.ToResponse();
    }

    public async Task<AppInviteCodeResponseDto> CreateMemberInviteAsync(
        int memberId,
        int creatorUserId,
        CreateAppInviteDto dto)
    {
        var inviteCode = InviteCodeCodec.Generate();
        _ = InviteCodeCodec.TryHash(inviteCode, out var tokenHash);
        var invite = await _identityStore.CreateOrReplaceMemberInviteAsync(
            memberId,
            creatorUserId,
            tokenHash,
            dto?.Purpose ?? string.Empty,
            GetOperationContext(),
            RequestAborted);
        return new AppInviteCodeResponseDto
        {
            InviteCode = inviteCode,
            Details = MapInvite(invite)
        };
    }

    public async Task<AppInviteResponseDto> GetMemberInviteStatusAsync(int memberId)
    {
        var invite = await _identityStore.GetLatestMemberInviteAsync(memberId, RequestAborted);
        return invite is null
            ? new AppInviteResponseDto { Status = "NotFound" }
            : MapInvite(invite);
    }

    public Task RevokeMemberInviteAsync(int memberId, int actorUserId) =>
        _identityStore.RevokeMemberInvitesAsync(
            memberId,
            actorUserId,
            GetOperationContext(),
            RequestAborted);

    public async Task<AppInviteCodeResponseDto> CreateUserInviteAsync(
        int userId,
        int creatorUserId,
        CreateAppInviteDto dto)
    {
        var inviteCode = InviteCodeCodec.Generate();
        _ = InviteCodeCodec.TryHash(inviteCode, out var tokenHash);
        var invite = await _identityStore.CreateOrReplaceStaffInviteAsync(
            userId,
            creatorUserId,
            tokenHash,
            dto?.Purpose ?? string.Empty,
            GetOperationContext(),
            RequestAborted);
        return new AppInviteCodeResponseDto
        {
            InviteCode = inviteCode,
            Details = MapInvite(invite)
        };
    }

    public async Task<AppInviteResponseDto> GetUserInviteStatusAsync(int userId)
    {
        var invite = await _identityStore.GetLatestStaffInviteAsync(userId, RequestAborted);
        return invite is null
            ? new AppInviteResponseDto { Status = "NotFound" }
            : MapInvite(invite);
    }

    public Task RevokeUserInviteAsync(int userId, int actorUserId) =>
        _identityStore.RevokeStaffInvitesAsync(
            userId,
            actorUserId,
            GetOperationContext(),
            RequestAborted);

    private AppInviteResponseDto MapInvite(AccountInvite invite)
    {
        var status = "Unused";
        if (invite.UsedAtUtc.HasValue)
        {
            status = "Used";
        }
        else if (invite.RevokedAtUtc.HasValue)
        {
            status = "Revoked";
        }
        else if (_clock.UtcNow >= invite.ExpiresAtUtc)
        {
            status = "Expired";
        }

        return new AppInviteResponseDto
        {
            TargetMemberID = invite.TargetMemberID,
            TargetUserID = invite.TargetUserID,
            IntendedRole = invite.IntendedRole,
            Purpose = invite.Purpose,
            ExpiresAtUtc = invite.ExpiresAtUtc,
            Status = status,
            UsedAtUtc = invite.UsedAtUtc,
            RevokedAtUtc = invite.RevokedAtUtc,
            CreatedAtUtc = invite.CreatedAtUtc
        };
    }

    private IdentityOperationContext GetOperationContext()
    {
        var context = _httpContextAccessor.HttpContext;
        return new IdentityOperationContext(
            context?.TraceIdentifier ?? "unavailable",
            context?.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
    }

    private CancellationToken RequestAborted =>
        _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
