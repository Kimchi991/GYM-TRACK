using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Interfaces;

public sealed record AttendancePage(IReadOnlyList<Attendance> Items, int TotalCount);
public sealed record AttendanceMembershipSnapshot(
    AttendanceMembershipState State,
    DateTime? ExpiryDate,
    int? PlanId,
    int? SubscriptionId);

public sealed class AttendanceOperationCommitKey
{
    private readonly byte[] _requestFingerprint;

    public AttendanceOperationCommitKey(
        Guid operationId,
        int actorUserId,
        AttendanceOperationType operationType,
        ReadOnlySpan<byte> requestFingerprint)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("The operation ID must not be empty.", nameof(operationId));
        }

        if (actorUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actorUserId));
        }

        if (requestFingerprint.Length != 32)
        {
            throw new ArgumentException("The request fingerprint must contain 32 bytes.", nameof(requestFingerprint));
        }

        OperationId = operationId;
        ActorUserId = actorUserId;
        OperationType = operationType;
        _requestFingerprint = requestFingerprint.ToArray();
    }

    public Guid OperationId { get; }
    public int ActorUserId { get; }
    public AttendanceOperationType OperationType { get; }
    public ReadOnlyMemory<byte> RequestFingerprint => _requestFingerprint;
}

public interface IAttendanceRepository
{
    Task<TResult> ExecuteSerializableAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteVerifiedMutationAsync<TResult>(
        AttendanceOperationCommitKey commitKey,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyTerminalOperationAsync(
        AttendanceOperationCommitKey commitKey,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteConsistentReadAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);

    Task<string?> GetTimezoneIdForAttendanceWriteAsync(
        CancellationToken cancellationToken = default);

    Task<Attendance?> GetByIdAsync(
        int attendanceId,
        bool includeVoided = false,
        bool asTracking = false,
        CancellationToken cancellationToken = default);

    Task<Attendance?> GetOpenSessionAsync(
        int memberId,
        bool asTracking = false,
        CancellationToken cancellationToken = default);

    Task<bool> HasVisitOnDateAsync(
        int memberId,
        DateOnly gymDate,
        CancellationToken cancellationToken = default);

    Task<Attendance?> GetNextNonVoidedSessionAsync(
        int memberId,
        DateTime afterCheckInUtc,
        int excludingAttendanceId,
        CancellationToken cancellationToken = default);

    Task<Member?> GetActiveMemberByQrCodeAsync(
        string normalizedQrCode,
        CancellationToken cancellationToken = default);

    Task<bool> IsMemberAvailableAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    Task<AttendanceMembershipState> GetMembershipStateAsync(
        int memberId,
        DateOnly gymDate,
        CancellationToken cancellationToken = default);

    Task<AttendanceMembershipSnapshot> GetMembershipSnapshotAsync(
        int memberId,
        DateOnly gymDate,
        CancellationToken cancellationToken = default);

    Task<DateTime?> GetMembershipExpiryAsync(
        int memberId,
        DateOnly gymDate,
        CancellationToken cancellationToken = default);

    Task<AttendanceOperation?> GetOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<AttendancePage> GetHistoryPageAsync(
        int memberId,
        DateOnly fromGymDate,
        DateOnly endExclusiveGymDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DateOnly>> GetDistinctVisitDatesAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Attendance>> GetCompletedSessionsOverlappingAsync(
        int memberId,
        DateTime startUtc,
        DateTime endExclusiveUtc,
        CancellationToken cancellationToken = default);

    Task<int> GetActiveOccupancyCountAsync(CancellationToken cancellationToken = default);

    void AddAttendance(Attendance attendance);
    void AddOperation(AttendanceOperation operation);
    void AddAdjustment(AttendanceAdjustment adjustment);
    void AddAudit(AuditLog auditLog);
    void ClearTrackedChanges();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
