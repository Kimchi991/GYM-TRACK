using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using GymTrackPro.API.Data;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Repositories;

public sealed class AttendanceStoreUniqueConstraintException : Exception
{
    public AttendanceStoreUniqueConstraintException(Exception innerException)
        : base("An attendance uniqueness constraint was violated.", innerException)
    {
    }
}

public sealed class AttendanceStoreConcurrencyException : Exception
{
    public AttendanceStoreConcurrencyException(Exception innerException)
        : base("An attendance concurrency conflict occurred.", innerException)
    {
    }
}

public class AttendanceRepository : IAttendanceRepository
{
    private readonly GymDbContext _context;
    private readonly DbSet<Attendance> _attendance;

    public AttendanceRepository(GymDbContext context)
    {
        _context = context;
        _attendance = context.Set<Attendance>();
    }

    public async Task<TResult> ExecuteSerializableAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_context.Database.IsRelational())
        {
            return await action(cancellationToken);
        }

        var executionStrategy = _context.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            // Execution-strategy retries reuse the scoped context. Clear any entities
            // left tracked by a rolled-back prior attempt before rebuilding the unit of work.
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var result = await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // Preserve the original read/legacy-operation exception.
                }

                _context.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public async Task<TResult> ExecuteVerifiedMutationAsync<TResult>(
        AttendanceOperationCommitKey commitKey,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitKey);
        ArgumentNullException.ThrowIfNull(action);

        if (!_context.Database.IsRelational())
        {
            _context.ChangeTracker.Clear();
            return await action(cancellationToken);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteInTransactionAsync(
                commitKey,
                async (_, transactionToken) =>
                {
                    _context.ChangeTracker.Clear();
                    return await action(transactionToken);
                },
                async (key, verificationToken) =>
                {
                    _context.ChangeTracker.Clear();
                    return await VerifyTerminalOperationAsync(key, verificationToken);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }
        catch
        {
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> VerifyTerminalOperationAsync(
        AttendanceOperationCommitKey commitKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitKey);
        var operation = await _context.Set<AttendanceOperation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OperationID == commitKey.OperationId,
                cancellationToken);
        if (operation is null
            || operation.ActorUserID != commitKey.ActorUserId
            || operation.OperationType != commitKey.OperationType
            || operation.State is not (AttendanceOperationState.Completed or AttendanceOperationState.Failed))
        {
            return false;
        }

        var expectedFingerprint = commitKey.RequestFingerprint.Span;
        return operation.RequestFingerprint.Length == expectedFingerprint.Length
            && CryptographicOperations.FixedTimeEquals(
                operation.RequestFingerprint,
                expectedFingerprint);
    }

    public Task<TResult> ExecuteConsistentReadAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        // A projection spans several bounded queries. Serializable keeps those reads
        // on one database snapshot even where SQL Server SNAPSHOT is not enabled.
        return ExecuteSerializableAsync(action, cancellationToken);
    }

    public async Task<string?> GetTimezoneIdForAttendanceWriteAsync(
        CancellationToken cancellationToken = default)
    {
        IQueryable<SystemSetting> query;
        if (_context.Database.IsRelational()
            && string.Equals(
                _context.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.SqlServer",
                StringComparison.Ordinal))
        {
            query = _context.SystemSettings.FromSqlRaw(
                "SELECT TOP(1) * FROM [SystemSettings] WITH (HOLDLOCK) WHERE [SettingKey] = N'Timezone'");
        }
        else
        {
            // Provider-neutral tests exercise ordering only. They do not prove SQL locks.
            query = _context.SystemSettings;
        }

        return await query
            .Where(setting => setting.SettingKey == TimezoneService.TimezoneSettingKey)
            .Select(setting => setting.SettingValue)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Attendance?> GetByIdAsync(
        int attendanceId,
        bool includeVoided = false,
        bool asTracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Attendance> query = _attendance.Include(a => a.Member);
        if (!asTracking)
        {
            query = query.AsNoTracking();
        }

        if (!includeVoided)
        {
            query = query.Where(a => !a.IsVoided);
        }

        return await query.SingleOrDefaultAsync(
            a => a.AttendanceID == attendanceId,
            cancellationToken);
    }

    public async Task<Attendance?> GetOpenSessionAsync(
        int memberId,
        bool asTracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Attendance> query = _attendance.Include(a => a.Member);
        if (!asTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
            a => a.MemberID == memberId
                && !a.IsVoided
                && a.CheckOutTime == null,
            cancellationToken);
    }

    public Task<bool> HasVisitOnDateAsync(
        int memberId,
        DateOnly gymDate,
        CancellationToken cancellationToken = default)
    {
        return _attendance
            .AsNoTracking()
            .AnyAsync(
                a => a.MemberID == memberId
                    && !a.IsVoided
                    && a.AttendanceDate == gymDate,
                cancellationToken);
    }

    public Task<Attendance?> GetNextNonVoidedSessionAsync(
        int memberId,
        DateTime afterCheckInUtc,
        int excludingAttendanceId,
        CancellationToken cancellationToken = default)
    {
        return _attendance
            .AsNoTracking()
            .Where(a => a.MemberID == memberId
                && !a.IsVoided
                && a.AttendanceID != excludingAttendanceId
                && a.CheckInTime > afterCheckInUtc)
            .OrderBy(a => a.CheckInTime)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Member?> GetActiveMemberByQrCodeAsync(
        string normalizedQrCode,
        CancellationToken cancellationToken = default)
    {
        return _context.Members
            .AsNoTracking()
            .SingleOrDefaultAsync(
                member => !member.IsDeleted
                    && member.Status == "Active"
                    && member.QRCode == normalizedQrCode,
                cancellationToken);
    }

    public Task<bool> IsMemberAvailableAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        return _context.Members
            .AsNoTracking()
            .AnyAsync(
                member => member.MemberID == memberId && !member.IsDeleted,
                cancellationToken);
    }

    public async Task<AttendanceMembershipState> GetMembershipStateAsync(
        int memberId,
        DateOnly gymDate,
        CancellationToken cancellationToken = default)
    {
        return (await GetMembershipSnapshotAsync(memberId, gymDate, cancellationToken)).State;
    }

    public async Task<AttendanceMembershipSnapshot> GetMembershipSnapshotAsync(
        int memberId,
        DateOnly gymDate,
        CancellationToken cancellationToken = default)
    {
        var dayStart = GymMembershipPolicy.ToStorageDate(gymDate);
        var dayEndExclusive = GymMembershipPolicy.ToStorageDate(gymDate.AddDays(1));
        var rows = await _context.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.MemberID == memberId
                && subscription.StartDate < dayEndExclusive
                && subscription.EndDate >= dayStart
                && (subscription.Status == GymMembershipPolicy.Active
                    || subscription.Status == GymMembershipPolicy.Paused))
            .Select(subscription => new
            {
                Subscription = subscription,
                HasOpenPause = _context.MembershipPauses.Any(pause =>
                    pause.SubscriptionID == subscription.SubscriptionID
                    && pause.PauseEndDate == null)
            })
            .ToListAsync(cancellationToken);
        var selection = GymMembershipPolicy.SelectCurrentCoverage(
            rows.Select(row => new MembershipCoverageCandidate(
                row.Subscription,
                row.HasOpenPause)),
            gymDate);
        return new AttendanceMembershipSnapshot(
            selection.State,
            selection.ExpiryDate,
            selection.PlanId,
            selection.Subscription?.SubscriptionID);
    }

    public Task<DateTime?> GetMembershipExpiryAsync(
        int memberId,
        DateOnly gymDate,
        CancellationToken cancellationToken = default)
    {
        return GetMembershipExpiryCoreAsync(memberId, gymDate, cancellationToken);
    }

    private async Task<DateTime?> GetMembershipExpiryCoreAsync(
        int memberId,
        DateOnly gymDate,
        CancellationToken cancellationToken)
    {
        return (await GetMembershipSnapshotAsync(memberId, gymDate, cancellationToken)).ExpiryDate;
    }

    public Task<AttendanceOperation?> GetOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        return _context.Set<AttendanceOperation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.OperationID == operationId, cancellationToken);
    }

    public async Task<AttendancePage> GetHistoryPageAsync(
        int memberId,
        DateOnly fromGymDate,
        DateOnly endExclusiveGymDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _attendance
            .AsNoTracking()
            .Where(a => a.MemberID == memberId
                && !a.IsVoided
                && a.AttendanceDate >= fromGymDate
                && a.AttendanceDate < endExclusiveGymDate);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.CheckInTime)
            .ThenByDescending(a => a.AttendanceID)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AttendancePage(items, totalCount);
    }

    public async Task<IReadOnlyList<DateOnly>> GetDistinctVisitDatesAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        return await _attendance
            .AsNoTracking()
            .Where(a => a.MemberID == memberId && !a.IsVoided)
            .Select(a => a.AttendanceDate)
            .Distinct()
            .OrderBy(date => date)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Attendance>> GetCompletedSessionsOverlappingAsync(
        int memberId,
        DateTime startUtc,
        DateTime endExclusiveUtc,
        CancellationToken cancellationToken = default)
    {
        return await _attendance
            .AsNoTracking()
            .Where(a => a.MemberID == memberId
                && !a.IsVoided
                && a.CheckOutTime != null
                && a.CheckInTime < endExclusiveUtc
                && a.CheckOutTime > startUtc)
            .OrderBy(a => a.CheckInTime)
            .ToListAsync(cancellationToken);
    }

    public void AddAttendance(Attendance attendance)
    {
        _attendance.Add(attendance);
    }

    public void AddOperation(AttendanceOperation operation)
    {
        _context.Set<AttendanceOperation>().Add(operation);
    }

    public void AddAdjustment(AttendanceAdjustment adjustment)
    {
        _context.Set<AttendanceAdjustment>().Add(adjustment);
    }

    public void AddAudit(AuditLog auditLog)
    {
        _context.AuditLogs.Add(auditLog);
    }

    public void ClearTrackedChanges()
    {
        _context.ChangeTracker.Clear();
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new AttendanceStoreConcurrencyException(exception);
        }
        catch (DbUpdateException exception) when (IsSqlUniqueViolation(exception))
        {
            throw new AttendanceStoreUniqueConstraintException(exception);
        }
    }

    private static bool IsSqlUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException
                && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return true;
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        return false;
    }
}
