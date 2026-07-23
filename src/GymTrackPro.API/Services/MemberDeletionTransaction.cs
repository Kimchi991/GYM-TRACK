using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.API.Services;

public interface IMemberDeletionTransaction
{
    Task<bool> SoftDeleteAndRevokeAsync(
        int memberId,
        int actorUserId,
        string ipAddress,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Commits member soft deletion, linked gym-goer revocation, and its audit as one
/// serializable operation. Back-office identities are never deactivated here.
/// </summary>
public class MemberDeletionTransaction : IMemberDeletionTransaction
{
    private readonly GymDbContext _dbContext;
    private readonly IClockService _clock;

    public MemberDeletionTransaction(GymDbContext dbContext, IClockService clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public Task<bool> SoftDeleteAndRevokeAsync(
        int memberId,
        int actorUserId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (memberId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memberId));
        }

        if (actorUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actorUserId));
        }

        var operationId = Guid.NewGuid();
        return GymMembershipTransaction.ExecuteVerifiedAsync(
            _dbContext,
            operationId,
            async (id, transactionToken) =>
            {
                // Canonical member-deletion lock order is invite range -> member ->
                // linked user range. Invite issuance uses the same target ordering.
                var outstandingInvites = await LockOutstandingMemberInvitesAsync(
                    memberId,
                    transactionToken);
                var member = await LockMemberAsync(memberId, transactionToken);
                if (member is null || member.IsDeleted)
                {
                    return false;
                }

                var linkedUsers = await LockLinkedUsersAsync(memberId, transactionToken);
                var now = _clock.UtcNow;
                if (now.Kind != DateTimeKind.Utc)
                {
                    throw new InvalidOperationException(
                        "Member deletion requires an unambiguous UTC clock value.");
                }

                member.IsDeleted = true;
                member.LastModified = now;

                foreach (var invite in outstandingInvites)
                {
                    invite.RevokedAtUtc = now;
                }

                foreach (var user in linkedUsers.Where(user => user.Role == UserRole.GymGoer))
                {
                    user.IsActive = false;
                    user.UpdatedAt = now;
                }

                var trainerAssignments = await _dbContext.TrainerClients
                    .Where(tc => tc.MemberID == memberId && tc.IsActive)
                    .ToListAsync(transactionToken);
                foreach (var tc in trainerAssignments)
                {
                    tc.IsActive = false;
                }

                _dbContext.AuditLogs.Add(new AuditLog
                {
                    UserID = actorUserId,
                    Action = "Member Deleted",
                    Details = AuditDetails(memberId, id),
                    Timestamp = now,
                    IPAddress = SafeIpAddress(ipAddress)
                });
                return true;
            },
            async (id, verificationToken) =>
            {
                var details = AuditDetails(memberId, id);
                return await _dbContext.Members
                    .AsNoTracking()
                    .Where(member => member.MemberID == memberId && member.IsDeleted)
                    .AnyAsync(verificationToken)
                    && await _dbContext.AuditLogs
                        .AsNoTracking()
                        .AnyAsync(
                            audit => audit.Action == "Member Deleted"
                                && audit.Details == details,
                            verificationToken)
                    && !await _dbContext.Users
                        .AsNoTracking()
                        .AnyAsync(
                            user => user.MemberID == memberId
                                && user.Role == UserRole.GymGoer
                                && user.IsActive,
                            verificationToken)
                    && !await _dbContext.AccountInvites
                        .AsNoTracking()
                        .AnyAsync(
                            invite => invite.TargetMemberID == memberId
                                && invite.UsedAtUtc == null
                                && invite.RevokedAtUtc == null,
                            verificationToken);
            },
            cancellationToken);
    }

    protected virtual Task<List<AccountInvite>> LockOutstandingMemberInvitesAsync(
        int memberId,
        CancellationToken cancellationToken)
    {
        if (IsSqlServer)
        {
            return _dbContext.AccountInvites
                .FromSqlInterpolated(
                    $"SELECT * FROM [AccountInvites] WITH (UPDLOCK, HOLDLOCK) WHERE [TargetMemberID] = {memberId} AND [UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL")
                .ToListAsync(cancellationToken);
        }

        return _dbContext.AccountInvites
            .Where(invite => invite.TargetMemberID == memberId
                && invite.UsedAtUtc == null
                && invite.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
    }

    protected virtual Task<Member?> LockMemberAsync(
        int memberId,
        CancellationToken cancellationToken) =>
        GymMembershipTransaction.LockMemberAsync(
            _dbContext,
            memberId,
            cancellationToken);

    protected virtual Task<List<User>> LockLinkedUsersAsync(
        int memberId,
        CancellationToken cancellationToken)
    {
        if (IsSqlServer)
        {
            return _dbContext.Users
                .FromSqlInterpolated(
                    $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [MemberID] = {memberId}")
                .ToListAsync(cancellationToken);
        }

        return _dbContext.Users
            .Where(user => user.MemberID == memberId)
            .ToListAsync(cancellationToken);
    }

    private bool IsSqlServer => string.Equals(
        _dbContext.Database.ProviderName,
        "Microsoft.EntityFrameworkCore.SqlServer",
        StringComparison.Ordinal);

    private static string AuditDetails(int memberId, Guid operationId) =>
        $"Soft-deleted member record ID {memberId}. OperationId:{operationId:D}.";

    private static string SafeIpAddress(string value) =>
        !string.IsNullOrWhiteSpace(value)
            && value.Length <= 50
            && !value.Any(char.IsControl)
                ? value
                : "Unknown";
}
