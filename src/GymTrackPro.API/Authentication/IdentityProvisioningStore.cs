using System.Data;
using System.Security.Cryptography;
using System.Text;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymTrackPro.API.Authentication;

public sealed record IdentityOperationContext(string CorrelationId, string IpAddress);

public sealed record StaffInviteProvisioningResult(AppUserIdentity User, AccountInvite Invite);

public interface IIdentityProvisioningStore
{
    Task<AppUserIdentity> SyncLinkedUserAsync(
        string firebaseUid,
        string verifiedEmail,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default);

    Task<AppUserIdentity> GetCurrentUserAsync(
        int userId,
        string firebaseUid,
        CancellationToken cancellationToken = default);

    Task<AccountInvite> CreateOrReplaceMemberInviteAsync(
        int memberId,
        int actorUserId,
        byte[] tokenHash,
        string purpose,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default);

    Task<AccountInvite> CreateOrReplaceStaffInviteAsync(
        int userId,
        int actorUserId,
        byte[] tokenHash,
        string purpose,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default);

    Task<StaffInviteProvisioningResult> CreateStaffWithInviteAsync(
        int actorUserId,
        string firstName,
        string lastName,
        string email,
        byte[] tokenHash,
        string purpose,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default);

    Task<AccountInvite?> GetLatestMemberInviteAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    Task<AccountInvite?> GetLatestStaffInviteAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task RevokeMemberInvitesAsync(
        int memberId,
        int actorUserId,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default);

    Task RevokeStaffInvitesAsync(
        int userId,
        int actorUserId,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default);

    Task<AppUserIdentity> RedeemInviteAsync(
        string firebaseUid,
        string verifiedEmail,
        byte[] tokenHash,
        Guid operationId,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityProvisioningStore : IIdentityProvisioningStore
{
    private const string ActiveMemberStatus = "Active";
    private readonly GymDbContext _dbContext;
    private readonly IClockService _clock;
    private readonly ILogger<IdentityProvisioningStore> _logger;

    public IdentityProvisioningStore(
        GymDbContext dbContext,
        IClockService clock,
        ILogger<IdentityProvisioningStore>? logger = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger ?? NullLogger<IdentityProvisioningStore>.Instance;
    }

    public async Task<AppUserIdentity> SyncLinkedUserAsync(
        string firebaseUid,
        string verifiedEmail,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default)
    {
        RequireFirebaseIdentity(firebaseUid, verifiedEmail, out var canonicalEmail, out var normalizedEmail);

        return await ExecuteSerializableAsync(async ct =>
        {
            var candidates = await GetUsersByUidForUpdateAsync(firebaseUid, ct);
            var user = RequireSingleLinkedUser(candidates, firebaseUid);
            EnsureActiveAndConsistent(user);

            if (!string.Equals(user.Email, canonicalEmail, StringComparison.Ordinal)
                || !string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
            {
                await EnsureEmailAvailableForUpdateAsync(normalizedEmail, user.UserID, ct);
                user.Email = canonicalEmail;
                user.NormalizedEmail = normalizedEmail;
                user.UpdatedAt = _clock.UtcNow;
                AddAudit(
                    user.UserID,
                    "FirebaseEmailRefreshed",
                    $"Bound user email metadata refreshed; CorrelationId={SafeCorrelation(operationContext.CorrelationId)}.",
                    operationContext.IpAddress);
            }

            return ToIdentity(user);
        }, cancellationToken);
    }

    public async Task<AppUserIdentity> GetCurrentUserAsync(
        int userId,
        string firebaseUid,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || !FirebaseIdentityValidation.TryValidateUid(firebaseUid))
        {
            throw AccessForbidden();
        }

        var candidates = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.UserID == userId && user.FirebaseUid == firebaseUid)
            .Take(3)
            .ToListAsync(cancellationToken);
        var exact = candidates
            .Where(user => string.Equals(user.FirebaseUid, firebaseUid, StringComparison.Ordinal))
            .Take(2)
            .ToList();
        var user = RequireSingleLinkedUser(exact, firebaseUid);
        EnsureActiveAndConsistent(user);
        return ToIdentity(user);
    }

    public Task<AccountInvite> CreateOrReplaceMemberInviteAsync(
        int memberId,
        int actorUserId,
        byte[] tokenHash,
        string purpose,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default)
    {
        ValidateIssueInput(memberId, actorUserId, tokenHash, ref purpose);

        return ExecuteMappedIdentityMutationAsync(() =>
            ExecuteWithConcurrencyRetryAsync(async ct =>
            await ExecuteSerializableAsync(async innerCt =>
            {
                // Invite range first keeps lock ordering aligned with redemption.
                var priorInvites = await GetOpenMemberInvitesForUpdateAsync(memberId, innerCt);
                var member = await GetMemberForUpdateAsync(memberId, innerCt)
                    ?? throw ResourceNotFound();
                await RequireActorAsync(actorUserId, ownerOnly: false, innerCt);

                if (member.IsDeleted
                    || !string.Equals(member.Status, ActiveMemberStatus, StringComparison.OrdinalIgnoreCase)
                    || !EmailNormalization.TryCanonicalize(
                        member.Email,
                        out _,
                        out var normalizedEmail))
                {
                    LogSecurityWarning(
                        "MEMBER_INVITE_TARGET_CONFLICT",
                        operationContext.CorrelationId);
                    throw IdentityConflict();
                }

                if (await HasMemberUserForUpdateAsync(memberId, innerCt)
                    || await HasUsernameForUpdateAsync($"member-{memberId}", innerCt))
                {
                    LogSecurityWarning(
                        "MEMBER_INVITE_EXISTING_LINK_CONFLICT",
                        operationContext.CorrelationId);
                    throw IdentityConflict();
                }

                await EnsureEmailAvailableForUpdateAsync(normalizedEmail, allowedUserId: null, innerCt);
                var now = _clock.UtcNow;
                foreach (var invite in priorInvites)
                {
                    invite.RevokedAtUtc = now;
                }

                var created = new AccountInvite
                {
                    TargetMemberID = memberId,
                    TokenHash = tokenHash.ToArray(),
                    NormalizedEmail = normalizedEmail,
                    IntendedRole = UserRole.GymGoer,
                    Purpose = purpose,
                    CreatedByUserID = actorUserId,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddHours(72)
                };
                _dbContext.AccountInvites.Add(created);
                AddAudit(
                    actorUserId,
                    "MemberAppInviteCreated",
                    $"Member app invite replaced for MemberID={memberId}; CorrelationId={SafeCorrelation(operationContext.CorrelationId)}.",
                    operationContext.IpAddress);
                return created;
            }, ct), cancellationToken));
    }

    public Task<AccountInvite> CreateOrReplaceStaffInviteAsync(
        int userId,
        int actorUserId,
        byte[] tokenHash,
        string purpose,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default)
    {
        ValidateIssueInput(userId, actorUserId, tokenHash, ref purpose);

        return ExecuteMappedIdentityMutationAsync(() =>
            ExecuteWithConcurrencyRetryAsync(async ct =>
            await ExecuteSerializableAsync(async innerCt =>
            {
                var priorInvites = await GetOpenStaffInvitesForUpdateAsync(userId, innerCt);
                var target = await GetUserForUpdateAsync(userId, innerCt)
                    ?? throw ResourceNotFound();
                await RequireActorAsync(actorUserId, ownerOnly: true, innerCt);

                if (!target.IsActive
                    || target.Role is not UserRole.Administrator and not UserRole.Receptionist
                    || target.MemberID.HasValue
                    || !string.IsNullOrEmpty(target.FirebaseUid)
                    || !EmailNormalization.TryCanonicalize(
                        target.Email,
                        out _,
                        out var normalizedEmail))
                {
                    throw IdentityConflict();
                }

                await EnsureEmailAvailableForUpdateAsync(normalizedEmail, target.UserID, innerCt);
                target.NormalizedEmail = normalizedEmail;
                var now = _clock.UtcNow;
                target.UpdatedAt = now;
                foreach (var invite in priorInvites)
                {
                    invite.RevokedAtUtc = now;
                }

                var created = new AccountInvite
                {
                    TargetUserID = target.UserID,
                    TokenHash = tokenHash.ToArray(),
                    NormalizedEmail = normalizedEmail,
                    IntendedRole = target.Role,
                    Purpose = purpose,
                    CreatedByUserID = actorUserId,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddHours(72)
                };
                _dbContext.AccountInvites.Add(created);
                AddAudit(
                    actorUserId,
                    "StaffAppInviteCreated",
                    $"Staff app invite replaced for UserID={userId}; CorrelationId={SafeCorrelation(operationContext.CorrelationId)}.",
                    operationContext.IpAddress);
                return created;
            }, ct), cancellationToken));
    }

    public async Task<StaffInviteProvisioningResult> CreateStaffWithInviteAsync(
        int actorUserId,
        string firstName,
        string lastName,
        string email,
        byte[] tokenHash,
        string purpose,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId <= 0
            || tokenHash is null
            || tokenHash.Length != InviteCodeCodec.HashBytes
            || !EmailNormalization.TryCanonicalize(email, out var canonicalEmail, out var normalizedEmail))
        {
            throw ValidationFailed();
        }

        firstName = NormalizeStaffName(firstName);
        lastName = NormalizeStaffName(lastName);
        purpose = NormalizePurpose(purpose);
        var username = CreateStaffUsername(normalizedEmail);

        var persisted = await ExecuteMappedIdentityMutationAsync(() =>
            ExecuteWithConcurrencyRetryAsync(async ct =>
            await ExecuteSerializableAsync(async innerCt =>
            {
                await RequireActorAsync(actorUserId, ownerOnly: true, innerCt);
                await EnsureEmailAvailableForUpdateAsync(normalizedEmail, allowedUserId: null, innerCt);
                if (await HasUsernameForUpdateAsync(username, innerCt))
                {
                    throw IdentityConflict();
                }

                var now = _clock.UtcNow;
                var user = new User
                {
                    FirebaseUid = null,
                    MemberID = null,
                    Username = username,
                    Email = canonicalEmail,
                    NormalizedEmail = normalizedEmail,
                    PasswordHash = null,
                    FirstName = firstName,
                    LastName = lastName,
                    Role = UserRole.Receptionist,
                    IsActive = true,
                    EmailVerified = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _dbContext.Users.Add(user);

                var invite = new AccountInvite
                {
                    TargetUser = user,
                    TokenHash = tokenHash.ToArray(),
                    NormalizedEmail = normalizedEmail,
                    IntendedRole = UserRole.Receptionist,
                    Purpose = purpose,
                    CreatedByUserID = actorUserId,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddHours(72)
                };
                _dbContext.AccountInvites.Add(invite);
                AddAudit(
                    actorUserId,
                    "StaffProfileAndInviteCreated",
                    $"Receptionist profile and app invite created; CorrelationId={SafeCorrelation(operationContext.CorrelationId)}.",
                    operationContext.IpAddress);

                return (User: user, Invite: invite);
            }, ct), cancellationToken));

        // Identity keys and relationship foreign keys are materialized by SaveChanges,
        // which completes inside the transaction before this response is built.
        return new StaffInviteProvisioningResult(ToIdentity(persisted.User), persisted.Invite);
    }

    public Task<AccountInvite?> GetLatestMemberInviteAsync(
        int memberId,
        CancellationToken cancellationToken = default) =>
        _dbContext.AccountInvites
            .AsNoTracking()
            .Where(invite => invite.TargetMemberID == memberId)
            .OrderByDescending(invite => invite.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<AccountInvite?> GetLatestStaffInviteAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        _dbContext.AccountInvites
            .AsNoTracking()
            .Where(invite => invite.TargetUserID == userId)
            .OrderByDescending(invite => invite.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task RevokeMemberInvitesAsync(
        int memberId,
        int actorUserId,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default) =>
        ExecuteMappedIdentityMutationAsync(() =>
            ExecuteWithConcurrencyRetryAsync(async ct =>
            {
                await ExecuteSerializableAsync(async innerCt =>
                {
                    var invites = await GetOpenMemberInvitesForUpdateAsync(memberId, innerCt);
                    _ = await GetMemberForUpdateAsync(memberId, innerCt) ?? throw ResourceNotFound();
                    await RequireActorAsync(actorUserId, ownerOnly: false, innerCt);
                    if (invites.Count > 0)
                    {
                        var now = _clock.UtcNow;
                        foreach (var invite in invites)
                        {
                            invite.RevokedAtUtc = now;
                        }

                        AddAudit(
                            actorUserId,
                            "MemberAppInviteRevoked",
                            $"Unused member app invites revoked for MemberID={memberId}; CorrelationId={SafeCorrelation(operationContext.CorrelationId)}.",
                            operationContext.IpAddress);
                    }

                    return true;
                }, ct);
                return true;
            }, cancellationToken));

    public Task RevokeStaffInvitesAsync(
        int userId,
        int actorUserId,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default) =>
        ExecuteMappedIdentityMutationAsync(() =>
            ExecuteWithConcurrencyRetryAsync(async ct =>
            {
                await ExecuteSerializableAsync(async innerCt =>
                {
                    var invites = await GetOpenStaffInvitesForUpdateAsync(userId, innerCt);
                    _ = await GetUserForUpdateAsync(userId, innerCt) ?? throw ResourceNotFound();
                    await RequireActorAsync(actorUserId, ownerOnly: true, innerCt);
                    if (invites.Count > 0)
                    {
                        var now = _clock.UtcNow;
                        foreach (var invite in invites)
                        {
                            invite.RevokedAtUtc = now;
                        }

                        AddAudit(
                            actorUserId,
                            "StaffAppInviteRevoked",
                            $"Unused staff app invites revoked for UserID={userId}; CorrelationId={SafeCorrelation(operationContext.CorrelationId)}.",
                            operationContext.IpAddress);
                    }

                    return true;
                }, ct);
                return true;
            }, cancellationToken));

    public async Task<AppUserIdentity> RedeemInviteAsync(
        string firebaseUid,
        string verifiedEmail,
        byte[] tokenHash,
        Guid operationId,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default)
    {
        RequireFirebaseIdentity(firebaseUid, verifiedEmail, out var canonicalEmail, out var normalizedEmail);
        if (operationId == Guid.Empty || tokenHash.Length != InviteCodeCodec.HashBytes)
        {
            LogSecurityWarning("ACTIVATION_INPUT_INVALID", operationContext.CorrelationId);
            throw InviteInvalid();
        }

        try
        {
            var redemption = await ExecuteWithConcurrencyRetryAsync(async ct =>
                await ExecuteSerializableAsync(async innerCt =>
                {
                    // Lock the operation-key range before the invite so operation reuse has one winner.
                    var operationInvite = await GetInviteByOperationForUpdateAsync(operationId, innerCt);
                    if (operationInvite is not null
                        && !CryptographicOperations.FixedTimeEquals(operationInvite.TokenHash, tokenHash))
                    {
                        LogSecurityWarning(
                            "ACTIVATION_OPERATION_CONFLICT",
                            operationContext.CorrelationId);
                        throw ActivationOperationConflict();
                    }

                    var invite = operationInvite
                        ?? await GetInviteByHashForUpdateAsync(tokenHash, innerCt);
                    if (invite is null)
                    {
                        LogSecurityWarning("INVITE_NOT_AVAILABLE", operationContext.CorrelationId);
                        throw InviteInvalid();
                    }

                    if (invite.RevokedAtUtc.HasValue)
                    {
                        LogSecurityWarning("INVITE_REVOKED", operationContext.CorrelationId);
                        throw InviteInvalid();
                    }

                    if (!HasValidInviteShape(invite))
                    {
                        LogSecurityWarning("INVITE_INVALID_SHAPE", operationContext.CorrelationId);
                        throw InviteInvalid();
                    }

                    if (invite.UsedAtUtc.HasValue)
                    {
                        var replayUser = await ResolveReplayUserAsync(
                            invite,
                            firebaseUid,
                            tokenHash,
                            operationId,
                            operationContext.CorrelationId,
                            innerCt);
                        return (User: replayUser, IsReplay: true);
                    }

                    var now = _clock.UtcNow;
                    // Expiry is end-exclusive: the code is no longer redeemable at the
                    // exact ExpiresAtUtc instant.
                    if (now >= invite.ExpiresAtUtc)
                    {
                        LogSecurityWarning("INVITE_EXPIRED", operationContext.CorrelationId);
                        throw InviteInvalid();
                    }

                    if (!string.Equals(invite.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
                    {
                        LogSecurityWarning("INVITE_EMAIL_MISMATCH", operationContext.CorrelationId);
                        throw InviteInvalid();
                    }

                    var uidOwners = await GetUsersByUidForUpdateAsync(firebaseUid, innerCt);
                    if (uidOwners.Count != 0)
                    {
                        LogSecurityWarning("FIREBASE_UID_ALREADY_BOUND", operationContext.CorrelationId);
                        throw IdentityConflict();
                    }

                    User activatedUser;
                    if (invite.TargetMemberID.HasValue && !invite.TargetUserID.HasValue)
                    {
                        activatedUser = await ActivateMemberInviteAsync(
                            invite,
                            canonicalEmail,
                            normalizedEmail,
                            now,
                            operationContext.CorrelationId,
                            innerCt);
                    }
                    else if (invite.TargetUserID.HasValue && !invite.TargetMemberID.HasValue)
                    {
                        activatedUser = await ActivateStaffInviteAsync(
                            invite,
                            firebaseUid,
                            canonicalEmail,
                            normalizedEmail,
                            now,
                            operationContext.CorrelationId,
                            innerCt);
                    }
                    else
                    {
                        throw InviteInvalid();
                    }

                    if (!string.IsNullOrEmpty(activatedUser.FirebaseUid))
                    {
                        if (!string.Equals(activatedUser.FirebaseUid, firebaseUid, StringComparison.Ordinal))
                        {
                            LogSecurityWarning(
                                "ACTIVATED_USER_UID_CONFLICT",
                                operationContext.CorrelationId);
                            throw IdentityConflict();
                        }
                    }
                    else
                    {
                        activatedUser.FirebaseUid = firebaseUid;
                    }

                    invite.UsedAtUtc = now;
                    invite.UsedByFirebaseUid = firebaseUid;
                    invite.RedemptionOperationId = operationId;

                    AddAudit(
                        activatedUser,
                        "AppInviteRedeemed",
                        $"App identity activated; CorrelationId={SafeCorrelation(operationContext.CorrelationId)}.",
                        operationContext.IpAddress);
                    return (User: activatedUser, IsReplay: false);
                }, ct), cancellationToken);
            LogSecurityInformation(
                redemption.IsReplay ? "INVITE_REPLAY_SUCCEEDED" : "INVITE_REDEEMED",
                operationContext.CorrelationId);
            return ToIdentity(redemption.User);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogSecurityWarning("IDENTITY_CONCURRENCY_CONFLICT", operationContext.CorrelationId);
            throw IdentityConflict();
        }
        catch (DbUpdateException exception)
            when (IdentityDatabaseConflictClassifier.IsUniqueViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            var operationInvite = await _dbContext.AccountInvites
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    invite => invite.RedemptionOperationId == operationId,
                    cancellationToken);
            if (operationInvite is not null
                && !CryptographicOperations.FixedTimeEquals(operationInvite.TokenHash, tokenHash))
            {
                LogSecurityWarning(
                    "ACTIVATION_OPERATION_CONFLICT",
                    operationContext.CorrelationId);
                throw ActivationOperationConflict();
            }

            LogSecurityWarning("IDENTITY_UNIQUE_CONFLICT", operationContext.CorrelationId);
            throw IdentityConflict();
        }
    }

    private async Task<User> ActivateMemberInviteAsync(
        AccountInvite invite,
        string canonicalEmail,
        string normalizedEmail,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (invite.IntendedRole != UserRole.GymGoer)
        {
            LogSecurityWarning("MEMBER_TARGET_CONFLICT", correlationId);
            throw InviteInvalid();
        }

        var memberId = invite.TargetMemberID!.Value;
        var member = await GetMemberForUpdateAsync(memberId, cancellationToken);
        if (member is null
            || member.IsDeleted
            || !string.Equals(member.Status, ActiveMemberStatus, StringComparison.OrdinalIgnoreCase)
            || !EmailNormalization.TryNormalize(member.Email, out var memberEmail)
            || !string.Equals(memberEmail, invite.NormalizedEmail, StringComparison.Ordinal)
            || await HasMemberUserForUpdateAsync(memberId, cancellationToken)
            || await HasUsernameForUpdateAsync($"member-{memberId}", cancellationToken))
        {
            LogSecurityWarning("MEMBER_TARGET_CONFLICT", correlationId);
            throw InviteInvalid();
        }

        await EnsureEmailAvailableForUpdateAsync(normalizedEmail, allowedUserId: null, cancellationToken);
        var user = new User
        {
            FirebaseUid = null,
            MemberID = memberId,
            Username = $"member-{memberId}",
            Email = canonicalEmail,
            NormalizedEmail = normalizedEmail,
            PasswordHash = null,
            FirstName = member.FirstName,
            LastName = member.LastName,
            Role = UserRole.GymGoer,
            IsActive = true,
            EmailVerified = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Users.Add(user);
        return user;
    }

    internal static bool HasValidInviteShape(AccountInvite invite)
    {
        var hasNoUsedMetadata = !invite.UsedAtUtc.HasValue
            && invite.UsedByFirebaseUid is null
            && !invite.RedemptionOperationId.HasValue;
        var hasCompleteUsedMetadata = invite.UsedAtUtc.HasValue
            && FirebaseIdentityValidation.TryValidateUid(invite.UsedByFirebaseUid)
            && invite.RedemptionOperationId.HasValue
            && invite.RedemptionOperationId.Value != Guid.Empty;
        var hasExactlyOneTarget = invite.TargetMemberID.HasValue
            != invite.TargetUserID.HasValue;
        var hasValidTargetRole = invite.TargetMemberID.HasValue
            ? invite.IntendedRole == UserRole.GymGoer
            : invite.IntendedRole is UserRole.Administrator or UserRole.Receptionist;
        var usedTimestampIsValid = !invite.UsedAtUtc.HasValue
            || (invite.UsedAtUtc >= invite.CreatedAtUtc
                && invite.UsedAtUtc < invite.ExpiresAtUtc);
        var revokedTimestampIsValid = !invite.RevokedAtUtc.HasValue
            || invite.RevokedAtUtc >= invite.CreatedAtUtc;
        return invite.TokenHash is { Length: InviteCodeCodec.HashBytes }
            && invite.CreatedAtUtc < invite.ExpiresAtUtc
            && (hasNoUsedMetadata || hasCompleteUsedMetadata)
            && !(invite.UsedAtUtc.HasValue && invite.RevokedAtUtc.HasValue)
            && hasExactlyOneTarget
            && hasValidTargetRole
            && usedTimestampIsValid
            && revokedTimestampIsValid;
    }

    private async Task<User> ActivateStaffInviteAsync(
        AccountInvite invite,
        string firebaseUid,
        string canonicalEmail,
        string normalizedEmail,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var user = await GetUserForUpdateAsync(invite.TargetUserID!.Value, cancellationToken);
        if (user is null
            || !user.IsActive
            || user.Role is not UserRole.Administrator and not UserRole.Receptionist
            || user.MemberID.HasValue
            || user.Role != invite.IntendedRole
            || !string.IsNullOrEmpty(user.FirebaseUid)
            || !EmailNormalization.TryNormalize(user.Email, out var targetEmail)
            || !string.Equals(targetEmail, invite.NormalizedEmail, StringComparison.Ordinal))
        {
            LogSecurityWarning("STAFF_TARGET_CONFLICT", correlationId);
            throw InviteInvalid();
        }

        await EnsureEmailAvailableForUpdateAsync(normalizedEmail, user.UserID, cancellationToken);
        user.FirebaseUid = firebaseUid;
        user.Email = canonicalEmail;
        user.NormalizedEmail = normalizedEmail;
        user.EmailVerified = true;
        user.UpdatedAt = now;
        return user;
    }

    private async Task<User> ResolveReplayUserAsync(
        AccountInvite invite,
        string firebaseUid,
        byte[] tokenHash,
        Guid operationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (invite.RedemptionOperationId != operationId
            || !string.Equals(invite.UsedByFirebaseUid, firebaseUid, StringComparison.Ordinal)
            || invite.UsedAtUtc >= invite.ExpiresAtUtc
            || !CryptographicOperations.FixedTimeEquals(invite.TokenHash, tokenHash))
        {
            // A caller must not be able to distinguish a used-code owner, operation, or
            // fingerprint mismatch from another unavailable invite.
            LogSecurityWarning("INVITE_REPLAY_MISMATCH", correlationId);
            throw InviteInvalid();
        }

        var candidates = await GetUsersByUidForUpdateAsync(firebaseUid, cancellationToken);
        User user;
        try
        {
            user = RequireSingleLinkedUser(candidates, firebaseUid);
        }
        catch (AppAccessException)
        {
            LogSecurityWarning("INVITE_REPLAY_UID_CONFLICT", correlationId);
            throw;
        }

        if ((invite.TargetMemberID.HasValue && user.MemberID != invite.TargetMemberID)
            || (invite.TargetMemberID.HasValue && invite.IntendedRole != UserRole.GymGoer)
            || (invite.TargetUserID.HasValue && user.UserID != invite.TargetUserID)
            || (invite.TargetUserID.HasValue && user.Role != invite.IntendedRole))
        {
            LogSecurityWarning("INVITE_REPLAY_TARGET_CONFLICT", correlationId);
            throw IdentityConflict();
        }

        try
        {
            EnsureActiveAndConsistent(user);
        }
        catch (AppAccessException)
        {
            LogSecurityWarning("INVITE_REPLAY_USER_STATE_CONFLICT", correlationId);
            throw;
        }

        return user;
    }

    private async Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = _dbContext.Database.IsRelational()
                    ? await _dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken)
                    : null;
                try
                {
                    var result = await operation(cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return result;
                }
                catch
                {
                    if (transaction is not null)
                    {
                        try
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                        }
                        catch
                        {
                            // Preserve the original operation/commit failure for retry or 500 mapping.
                        }
                    }

                    // ExecutionStrategy may retry the delegate. Clear failed tracked state so
                    // a retry rebuilds the unit of work instead of duplicating staged entities.
                    _dbContext.ChangeTracker.Clear();
                    throw;
                }
            });
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static async Task<T> ExecuteWithConcurrencyRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt == 0)
            {
                // Re-run once so the losing request observes and maps the committed winner.
            }
        }
    }

    private static async Task<T> ExecuteMappedIdentityMutationAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw IdentityConflict();
        }
        catch (DbUpdateException exception)
            when (IdentityDatabaseConflictClassifier.IsUniqueViolation(exception))
        {
            throw IdentityConflict();
        }
    }

    private async Task<List<User>> GetUsersByUidForUpdateAsync(
        string firebaseUid,
        CancellationToken cancellationToken)
    {
        var query = IsSqlServer
            ? _dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [FirebaseUid] = {firebaseUid}")
            : _dbContext.Users.Where(user => user.FirebaseUid == firebaseUid);
        var coarse = await query.Take(3).ToListAsync(cancellationToken);
        return coarse;
    }

    private Task<User?> GetUserForUpdateAsync(int userId, CancellationToken cancellationToken) =>
        (IsSqlServer
            ? _dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [UserID] = {userId}")
            : _dbContext.Users.Where(user => user.UserID == userId))
        .SingleOrDefaultAsync(cancellationToken);

    private Task<Member?> GetMemberForUpdateAsync(int memberId, CancellationToken cancellationToken) =>
        (IsSqlServer
            ? _dbContext.Members.FromSqlInterpolated(
                $"SELECT * FROM [Members] WITH (UPDLOCK, HOLDLOCK) WHERE [MemberID] = {memberId}")
            : _dbContext.Members.Where(member => member.MemberID == memberId))
        .SingleOrDefaultAsync(cancellationToken);

    private Task<AccountInvite?> GetInviteByOperationForUpdateAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        (IsSqlServer
            ? _dbContext.AccountInvites.FromSqlInterpolated(
                $"SELECT * FROM [AccountInvites] WITH (UPDLOCK, HOLDLOCK) WHERE [RedemptionOperationId] = {operationId}")
            : _dbContext.AccountInvites.Where(invite => invite.RedemptionOperationId == operationId))
        .SingleOrDefaultAsync(cancellationToken);

    private Task<AccountInvite?> GetInviteByHashForUpdateAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken) =>
        (IsSqlServer
            ? _dbContext.AccountInvites.FromSqlInterpolated(
                $"SELECT * FROM [AccountInvites] WITH (UPDLOCK, HOLDLOCK) WHERE [TokenHash] = {tokenHash}")
            : _dbContext.AccountInvites.Where(invite => invite.TokenHash.SequenceEqual(tokenHash)))
        .SingleOrDefaultAsync(cancellationToken);

    private Task<List<AccountInvite>> GetOpenMemberInvitesForUpdateAsync(
        int memberId,
        CancellationToken cancellationToken) =>
        (IsSqlServer
            ? _dbContext.AccountInvites.FromSqlInterpolated(
                $"SELECT * FROM [AccountInvites] WITH (UPDLOCK, HOLDLOCK) WHERE [TargetMemberID] = {memberId} AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL")
            : _dbContext.AccountInvites.Where(invite =>
                invite.TargetMemberID == memberId
                && invite.UsedAtUtc == null
                && invite.RevokedAtUtc == null))
        .ToListAsync(cancellationToken);

    private Task<List<AccountInvite>> GetOpenStaffInvitesForUpdateAsync(
        int userId,
        CancellationToken cancellationToken) =>
        (IsSqlServer
            ? _dbContext.AccountInvites.FromSqlInterpolated(
                $"SELECT * FROM [AccountInvites] WITH (UPDLOCK, HOLDLOCK) WHERE [TargetUserID] = {userId} AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL")
            : _dbContext.AccountInvites.Where(invite =>
                invite.TargetUserID == userId
                && invite.UsedAtUtc == null
                && invite.RevokedAtUtc == null))
        .ToListAsync(cancellationToken);

    private Task<bool> HasMemberUserForUpdateAsync(int memberId, CancellationToken cancellationToken) =>
        (IsSqlServer
            ? _dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [MemberID] = {memberId}")
            : _dbContext.Users.Where(user => user.MemberID == memberId))
        .AnyAsync(cancellationToken);

    private Task<bool> HasUsernameForUpdateAsync(string username, CancellationToken cancellationToken) =>
        (IsSqlServer
            ? _dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [Username] = {username}")
            : _dbContext.Users.Where(user => user.Username == username))
        .AnyAsync(cancellationToken);

    private async Task EnsureEmailAvailableForUpdateAsync(
        string normalizedEmail,
        int? allowedUserId,
        CancellationToken cancellationToken)
    {
        var candidates = await (IsSqlServer
            ? _dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [NormalizedEmail] = {normalizedEmail}")
            : _dbContext.Users.Where(user => user.NormalizedEmail == normalizedEmail))
            .ToListAsync(cancellationToken);

        if (candidates.Any(user =>
                user.UserID != allowedUserId
                && (string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal)
                    || (EmailNormalization.TryNormalize(user.Email, out var candidateEmail)
                        && string.Equals(candidateEmail, normalizedEmail, StringComparison.Ordinal)))))
        {
            throw IdentityConflict();
        }

        // During the staged nullable schema, any other unnormalized row makes exact
        // uniqueness impossible to prove. Fail closed until the reviewed backfill/final
        // binary unique index is applied.
        var hasUnnormalizedOther = await (IsSqlServer
            ? _dbContext.Users.FromSqlRaw(
                "SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [NormalizedEmail] IS NULL")
            : _dbContext.Users.Where(user => user.NormalizedEmail == null))
            .AnyAsync(user => !allowedUserId.HasValue || user.UserID != allowedUserId.Value, cancellationToken);
        if (hasUnnormalizedOther)
        {
            throw IdentityConflict();
        }
    }

    private async Task RequireActorAsync(
        int actorUserId,
        bool ownerOnly,
        CancellationToken cancellationToken)
    {
        var actor = await GetUserForUpdateAsync(actorUserId, cancellationToken);
        if (actor is null
            || !actor.IsActive
            || string.IsNullOrEmpty(actor.FirebaseUid)
            || (ownerOnly && actor.Role != UserRole.Administrator)
            || (!ownerOnly
                && actor.Role is not UserRole.Administrator and not UserRole.Receptionist))
        {
            throw AccessForbidden();
        }
    }

    private void AddAudit(int userId, string action, string details, string ipAddress) =>
        _dbContext.AuditLogs.Add(new AuditLog
        {
            UserID = userId,
            Action = action,
            Details = details,
            IPAddress = SafeIpAddress(ipAddress),
            Timestamp = _clock.UtcNow
        });

    private void AddAudit(User user, string action, string details, string ipAddress) =>
        _dbContext.AuditLogs.Add(new AuditLog
        {
            User = user,
            Action = action,
            Details = details,
            IPAddress = SafeIpAddress(ipAddress),
            Timestamp = _clock.UtcNow
        });

    private static User RequireSingleLinkedUser(
        IReadOnlyCollection<User> candidates,
        string firebaseUid)
    {
        var exactCandidates = candidates
            .Where(user => string.Equals(user.FirebaseUid, firebaseUid, StringComparison.Ordinal))
            .Take(2)
            .ToList();
        if (exactCandidates.Count == 0)
        {
            throw AccountPendingActivation();
        }

        if (exactCandidates.Count != 1)
        {
            throw IdentityConflict();
        }

        return exactCandidates[0];
    }

    private static void EnsureActiveAndConsistent(User user)
    {
        if (!user.IsActive)
        {
            throw AccessForbidden();
        }

        if (!Enum.IsDefined(user.Role)
            || (user.Role == UserRole.GymGoer && !user.MemberID.HasValue)
            || (user.Role is UserRole.Administrator or UserRole.Receptionist && user.MemberID.HasValue))
        {
            throw IdentityConflict();
        }
    }

    private static AppUserIdentity ToIdentity(User user) => new(
        user.UserID,
        user.FirebaseUid ?? string.Empty,
        user.MemberID,
        user.Username,
        user.Email,
        user.FirstName,
        user.LastName,
        user.Role,
        user.IsActive,
        user.EmailVerified,
        user.CreatedAt,
        user.UpdatedAt,
        user.LastLoginAt);

    private static void RequireFirebaseIdentity(
        string firebaseUid,
        string verifiedEmail,
        out string canonicalEmail,
        out string normalizedEmail)
    {
        if (!FirebaseIdentityValidation.TryValidateUid(firebaseUid)
            || !EmailNormalization.TryCanonicalize(
                verifiedEmail,
                out canonicalEmail,
                out normalizedEmail))
        {
            throw AccessForbidden();
        }
    }

    private static void ValidateIssueInput(
        int targetId,
        int actorUserId,
        byte[] tokenHash,
        ref string purpose)
    {
        if (targetId <= 0
            || actorUserId <= 0
            || tokenHash.Length != InviteCodeCodec.HashBytes
            || string.IsNullOrWhiteSpace(purpose))
        {
            throw ValidationFailed();
        }

        purpose = purpose.Trim().Normalize(System.Text.NormalizationForm.FormKC);
        if (purpose.Length is 0 or > 100 || purpose.Any(char.IsControl))
        {
            throw ValidationFailed();
        }
    }

    private static string NormalizeStaffName(string? value)
    {
        var normalized = value?.Trim().Normalize(NormalizationForm.FormKC) ?? string.Empty;
        if (normalized.Length is 0 or > 100 || normalized.Any(char.IsControl))
        {
            throw ValidationFailed();
        }

        return normalized;
    }

    private static string NormalizePurpose(string? value)
    {
        var normalized = value?.Trim().Normalize(NormalizationForm.FormKC) ?? string.Empty;
        if (normalized.Length is 0 or > 100 || normalized.Any(char.IsControl))
        {
            throw ValidationFailed();
        }

        return normalized;
    }

    private static string CreateStaffUsername(string normalizedEmail)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return $"staff-{Convert.ToHexString(digest.AsSpan(0, 20)).ToLowerInvariant()}";
    }

    private bool IsSqlServer => string.Equals(
        _dbContext.Database.ProviderName,
        "Microsoft.EntityFrameworkCore.SqlServer",
        StringComparison.Ordinal);

    private static string SafeCorrelation(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 100
        && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            ? value
            : "unavailable";

    private static string SafeIpAddress(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 50 ? value : "Unknown";

    private void LogSecurityWarning(string category, string correlationId) =>
        _logger.LogWarning(
            "Identity security event. Category: {Category}; CorrelationId: {CorrelationId}",
            category,
            SafeCorrelation(correlationId));

    private void LogSecurityInformation(string category, string correlationId) =>
        _logger.LogInformation(
            "Identity security event. Category: {Category}; CorrelationId: {CorrelationId}",
            category,
            SafeCorrelation(correlationId));

    private static AppAccessException ValidationFailed() => new(
        StatusCodes.Status400BadRequest,
        "VALIDATION_ERROR",
        "The request is invalid.");

    private static AppAccessException InviteInvalid() => new(
        StatusCodes.Status400BadRequest,
        ErrorCodes.InviteInvalid,
        "The activation request is invalid or no longer available.");

    private static AppAccessException AccountPendingActivation() => new(
        StatusCodes.Status403Forbidden,
        ErrorCodes.AccountPendingActivation,
        "App access is pending activation.");

    private static AppAccessException IdentityConflict() => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.IdentityConflict,
        "The identity operation conflicts with existing account state.");

    private static AppAccessException ActivationOperationConflict() => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.ActivationOperationConflict,
        "The activation operation conflicts with an earlier request.");

    private static AppAccessException AccessForbidden() => new(
        StatusCodes.Status403Forbidden,
        "ACCESS_FORBIDDEN",
        "Access is forbidden.");

    private static AppAccessException ResourceNotFound() => new(
        StatusCodes.Status404NotFound,
        "RESOURCE_NOT_FOUND",
        "The requested resource was not found.");
}
