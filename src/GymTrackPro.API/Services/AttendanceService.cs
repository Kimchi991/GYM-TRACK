using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Repositories;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.API.Services;

public class AttendanceService : IAttendanceService
{
    private const int MaximumHistoryDays = 366;
    private const int MaximumPageSize = 100;

    private readonly IAttendanceRepository _repository;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IClockService _clock;
    private readonly ITimezoneService _timezoneService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IProjectionVersionProvider _projectionVersionProvider;
    private readonly ILogger<AttendanceService> _logger;
    private readonly Data.GymDbContext _context;

    public AttendanceService(
        IAttendanceRepository repository,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        IClockService clock,
        ITimezoneService timezoneService,
        ICurrentUserContext currentUser,
        IProjectionVersionProvider projectionVersionProvider,
        ILogger<AttendanceService> logger,
        Data.GymDbContext context)
    {
        _repository = repository;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _clock = clock;
        _timezoneService = timezoneService;
        _currentUser = currentUser;
        _projectionVersionProvider = projectionVersionProvider;
        _logger = logger;
        _context = context;
    }

    public async Task<AttendanceDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        var attendance = await _repository.GetByIdAsync(
            id,
            cancellationToken: cancellationToken);
        return attendance is null ? null : MapToDto(attendance);
    }

    public Task<PagedResultDto<AttendanceDto>> GetMemberHistoryAsync(
        int memberId,
        DateOnly? fromGymDate,
        DateOnly? endExclusiveGymDate,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (memberId <= 0)
        {
            throw Validation(ErrorCodes.InvalidAttendanceRange, "A valid member is required.");
        }

        return GetHistoryCoreAsync(
            memberId,
            fromGymDate,
            endExclusiveGymDate,
            page,
            pageSize,
            cancellationToken);
    }

    public Task<IReadOnlyList<AttendanceDto>> GetLegacyMemberHistoryAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        if (memberId <= 0)
        {
            throw Validation(ErrorCodes.InvalidAttendanceRange, "A valid member is required.");
        }

        return _repository.ExecuteConsistentReadAsync(async transactionToken =>
        {
            var nowUtc = GetUtcNow();
            var timeZoneId = await _repository.GetTimezoneIdForAttendanceWriteAsync(transactionToken);
            var today = await _timezoneService.GetGymDateAsync(
                nowUtc,
                timeZoneId ?? string.Empty,
                transactionToken);
            var start = today.AddDays(-(MaximumHistoryDays - 1));
            var endExclusive = today.AddDays(1);
            var rows = new List<AttendanceDto>(MaximumHistoryDays);
            var page = 1;
            while (rows.Count < MaximumHistoryDays)
            {
                var result = await _repository.GetHistoryPageAsync(
                    memberId,
                    start,
                    endExclusive,
                    page,
                    MaximumPageSize,
                    transactionToken);
                rows.AddRange(result.Items.Select(MapToDto));
                if (rows.Count >= result.TotalCount || result.Items.Count == 0)
                {
                    break;
                }

                page++;
            }

            return (IReadOnlyList<AttendanceDto>)rows.Take(MaximumHistoryDays).ToList();
        }, cancellationToken);
    }

    public Task<AttendanceDto> CheckInAsync(
        string qrCode,
        CancellationToken cancellationToken = default)
    {
        return CheckInCoreAsync(
            new CheckInRequestDto { QrCode = qrCode, OperationId = Guid.NewGuid() },
            null,
            Attendance.LegacyStaffQrSource,
            AttendanceOperationType.StaffCheckIn,
            cancellationToken);
    }

    public async Task CheckOutAsync(
        int attendanceID,
        CancellationToken cancellationToken = default)
    {
        await CheckOutCoreAsync(
            attendanceID,
            null,
            new CheckOutRequestDto { OperationId = Guid.NewGuid() },
            AttendanceOperationType.StaffCheckOut,
            legacyAdapter: true,
            cancellationToken);
    }

    public Task<AttendanceDto> CheckInAsync(
        CheckInRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return CheckInCoreAsync(
            request,
            null,
            Attendance.StaffQrSource,
            AttendanceOperationType.StaffCheckIn,
            cancellationToken);
    }

    public Task<AttendanceDto> CheckInCurrentMemberAsync(
        AttendanceOperationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CheckInCoreAsync(
            new CheckInRequestDto
            {
                QrCode = "self",
                OperationId = request.OperationId
            },
            RequireMemberId(),
            Attendance.SelfCheckInSource,
            AttendanceOperationType.GymGoerCheckIn,
            cancellationToken);
    }

    public Task<AttendanceDto> CheckOutAsync(
        int attendanceID,
        CheckOutRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return CheckOutCoreAsync(
            attendanceID,
            null,
            request,
            AttendanceOperationType.StaffCheckOut,
            legacyAdapter: false,
            cancellationToken);
    }

    public Task<AttendanceDto> CheckOutCurrentMemberAsync(
        CheckOutRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return CheckOutCoreAsync(
            null,
            RequireMemberId(),
            request,
            AttendanceOperationType.GymGoerCheckOut,
            legacyAdapter: false,
            cancellationToken);
    }

    public async Task<AttendanceDto> CorrectCheckoutAsync(
        int attendanceID,
        CorrectCheckoutRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOperationId(request.OperationId);
        ValidateUtc(request.CorrectedCheckOutTimeUtc, nameof(request.CorrectedCheckOutTimeUtc));

        var actorUserId = RequireActorUserId();
        var reason = NormalizeReason(request.Reason);
        var nowUtc = GetUtcNow();
        var fingerprint = CreateFingerprint(
            AttendanceOperationType.CheckoutCorrection,
            $"attendance={attendanceID.ToString(CultureInfo.InvariantCulture)}",
            $"checkout={request.CorrectedCheckOutTimeUtc:O}",
            $"reasonHash={HashSensitiveInput(reason)}");
        var commitKey = new AttendanceOperationCommitKey(
            request.OperationId,
            actorUserId,
            AttendanceOperationType.CheckoutCorrection,
            fingerprint);

        try
        {
            return await _repository.ExecuteVerifiedMutationAsync(commitKey, async transactionToken =>
            {
                var existingOperation = await _repository.GetOperationAsync(
                    request.OperationId,
                    transactionToken);
                if (existingOperation is not null)
                {
                    return await ReplayAsync(
                        existingOperation,
                        AttendanceOperationType.CheckoutCorrection,
                        actorUserId,
                        fingerprint,
                        transactionToken);
                }

                var attendance = await _repository.GetByIdAsync(
                    attendanceID,
                    includeVoided: true,
                    asTracking: true,
                    transactionToken);
                if (attendance is null)
                {
                    throw NotFound(ErrorCodes.AttendanceNotFound, "Attendance was not found.");
                }

                if (attendance.IsVoided)
                {
                    throw Conflict(ErrorCodes.AttendanceAlreadyVoided, "Voided attendance cannot be corrected.");
                }

                if (request.CorrectedCheckOutTimeUtc <= attendance.CheckInTime
                    || request.CorrectedCheckOutTimeUtc > nowUtc)
                {
                    throw Conflict(ErrorCodes.InvalidCheckoutTime, "The corrected checkout time is invalid.");
                }

                var nextSession = await _repository.GetNextNonVoidedSessionAsync(
                    attendance.MemberID,
                    attendance.CheckInTime,
                    attendance.AttendanceID,
                    transactionToken);
                if (nextSession is not null
                    && request.CorrectedCheckOutTimeUtc > nextSession.CheckInTime)
                {
                    throw Conflict(ErrorCodes.AttendanceOverlap, "The corrected checkout overlaps another visit.");
                }

                var adjustment = new AttendanceAdjustment
                {
                    AttendanceID = attendance.AttendanceID,
                    Kind = AttendanceAdjustmentKind.CheckoutCorrection,
                    BeforeCheckOutTimeUtc = attendance.CheckOutTime,
                    AfterCheckOutTimeUtc = request.CorrectedCheckOutTimeUtc,
                    Reason = reason,
                    ActorUserID = actorUserId,
                    OperationID = request.OperationId,
                    CreatedAtUtc = nowUtc
                };

                attendance.CheckOutTime = request.CorrectedCheckOutTimeUtc;
                attendance.LastModified = nowUtc;

                _repository.AddAdjustment(adjustment);
                _repository.AddOperation(CreateCompletedOperation(
                    request.OperationId,
                    actorUserId,
                    AttendanceOperationType.CheckoutCorrection,
                    fingerprint,
                    attendance.AttendanceID,
                    StatusCodes.Status200OK,
                    "ATTENDANCE_CHECKOUT_CORRECTED",
                    nowUtc));

                await _repository.SaveChangesAsync(transactionToken);
                await WriteAuditAsync(
                    actorUserId,
                    "Attendance.CheckoutCorrected",
                    $"Attendance {attendance.AttendanceID} checkout corrected.");
                return MapToDto(attendance);
            }, cancellationToken);
        }
        catch (AppAccessException exception)
        {
            _repository.ClearTrackedChanges();
            var completedReplay = await RecordFailedOperationOrReplayAsync(
                request.OperationId,
                actorUserId,
                AttendanceOperationType.CheckoutCorrection,
                fingerprint,
                exception,
                nowUtc,
                "Attendance.CheckoutCorrectionRejected",
                cancellationToken);
            if (completedReplay is not null)
            {
                return completedReplay;
            }

            if (!IsTerminalOperationFailure(exception))
            {
                await TryWriteFailureAuditAsync(
                    actorUserId,
                    "Attendance.CheckoutCorrectionRejected",
                    exception.ErrorCode);
            }
            throw;
        }
        catch (AttendanceStoreConcurrencyException)
        {
            throw Conflict(
                ErrorCodes.AttendanceConcurrencyConflict,
                "Attendance changed before the correction was saved.");
        }
        catch (AttendanceStoreUniqueConstraintException)
        {
            return await ResolveOperationWriteConflictAsync(
                request.OperationId,
                AttendanceOperationType.CheckoutCorrection,
                actorUserId,
                fingerprint,
                cancellationToken);
        }
    }

    public async Task<AttendanceDto> VoidAsync(
        int attendanceID,
        VoidAttendanceRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOperationId(request.OperationId);

        var actorUserId = RequireActorUserId();
        var reason = NormalizeReason(request.Reason);
        var nowUtc = GetUtcNow();
        var fingerprint = CreateFingerprint(
            AttendanceOperationType.Void,
            $"attendance={attendanceID.ToString(CultureInfo.InvariantCulture)}",
            $"superseding={request.SupersedingAttendanceId?.ToString(CultureInfo.InvariantCulture) ?? "none"}",
            $"reasonHash={HashSensitiveInput(reason)}");
        var commitKey = new AttendanceOperationCommitKey(
            request.OperationId,
            actorUserId,
            AttendanceOperationType.Void,
            fingerprint);

        try
        {
            return await _repository.ExecuteVerifiedMutationAsync(commitKey, async transactionToken =>
            {
                var existingOperation = await _repository.GetOperationAsync(
                    request.OperationId,
                    transactionToken);
                if (existingOperation is not null)
                {
                    return await ReplayAsync(
                        existingOperation,
                        AttendanceOperationType.Void,
                        actorUserId,
                        fingerprint,
                        transactionToken);
                }

                var attendance = await _repository.GetByIdAsync(
                    attendanceID,
                    includeVoided: true,
                    asTracking: true,
                    transactionToken);
                if (attendance is null)
                {
                    throw NotFound(ErrorCodes.AttendanceNotFound, "Attendance was not found.");
                }

                if (attendance.IsVoided)
                {
                    throw Conflict(ErrorCodes.AttendanceAlreadyVoided, "Attendance is already voided.");
                }

                if (request.SupersedingAttendanceId == attendance.AttendanceID)
                {
                    throw Conflict(
                        ErrorCodes.InvalidSupersedingAttendance,
                        "The superseding attendance is invalid.");
                }

                Attendance? supersedingAttendance = null;
                if (request.SupersedingAttendanceId.HasValue)
                {
                    supersedingAttendance = await _repository.GetByIdAsync(
                        request.SupersedingAttendanceId.Value,
                        includeVoided: true,
                        cancellationToken: transactionToken);
                    if (supersedingAttendance is null
                        || supersedingAttendance.IsVoided
                        || supersedingAttendance.MemberID != attendance.MemberID
                        || supersedingAttendance.AttendanceDate != attendance.AttendanceDate)
                    {
                        throw Conflict(
                            ErrorCodes.InvalidSupersedingAttendance,
                            "The superseding attendance is invalid.");
                    }
                }

                var adjustment = new AttendanceAdjustment
                {
                    AttendanceID = attendance.AttendanceID,
                    Kind = supersedingAttendance is null
                        ? AttendanceAdjustmentKind.Void
                        : AttendanceAdjustmentKind.Supersede,
                    BeforeIsVoided = false,
                    AfterIsVoided = true,
                    BeforeSupersededByAttendanceID = attendance.SupersededByAttendanceID,
                    AfterSupersededByAttendanceID = supersedingAttendance?.AttendanceID,
                    Reason = reason,
                    ActorUserID = actorUserId,
                    OperationID = request.OperationId,
                    CreatedAtUtc = nowUtc
                };

                attendance.IsVoided = true;
                attendance.VoidActorUserID = actorUserId;
                attendance.VoidedAtUtc = nowUtc;
                attendance.VoidReason = reason;
                attendance.SupersededByAttendanceID = supersedingAttendance?.AttendanceID;
                attendance.LastModified = nowUtc;

                _repository.AddAdjustment(adjustment);
                _repository.AddOperation(CreateCompletedOperation(
                    request.OperationId,
                    actorUserId,
                    AttendanceOperationType.Void,
                    fingerprint,
                    attendance.AttendanceID,
                    StatusCodes.Status200OK,
                    "ATTENDANCE_VOIDED",
                    nowUtc));

                await _repository.SaveChangesAsync(transactionToken);
                await WriteAuditAsync(
                    actorUserId,
                    "Attendance.Voided",
                    $"Attendance {attendance.AttendanceID} voided.");
                return MapToDto(attendance);
            }, cancellationToken);
        }
        catch (AppAccessException exception)
        {
            _repository.ClearTrackedChanges();
            var completedReplay = await RecordFailedOperationOrReplayAsync(
                request.OperationId,
                actorUserId,
                AttendanceOperationType.Void,
                fingerprint,
                exception,
                nowUtc,
                "Attendance.VoidRejected",
                cancellationToken);
            if (completedReplay is not null)
            {
                return completedReplay;
            }

            if (!IsTerminalOperationFailure(exception))
            {
                await TryWriteFailureAuditAsync(
                    actorUserId,
                    "Attendance.VoidRejected",
                    exception.ErrorCode);
            }
            throw;
        }
        catch (AttendanceStoreConcurrencyException)
        {
            throw Conflict(
                ErrorCodes.AttendanceConcurrencyConflict,
                "Attendance changed before it was voided.");
        }
        catch (AttendanceStoreUniqueConstraintException)
        {
            return await ResolveOperationWriteConflictAsync(
                request.OperationId,
                AttendanceOperationType.Void,
                actorUserId,
                fingerprint,
                cancellationToken);
        }
    }

    public async Task<AttendanceDto?> GetCurrentOpenSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var memberId = RequireMemberId();
        await EnsureMemberAvailableAsync(memberId, cancellationToken);
        var attendance = await _repository.GetOpenSessionAsync(
            memberId,
            cancellationToken: cancellationToken);
        return attendance is null ? null : MapToDto(attendance);
    }

    public Task<AttendanceHistoryPageDto> GetAttendanceHistoryAsync(
        DateOnly? fromGymDate,
        DateOnly? endExclusiveGymDate,
        int page = 1,
        int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        ValidateHistoryPage(page, pageSize);
        var memberId = RequireMemberId();
        return _repository.ExecuteConsistentReadAsync(async transactionToken =>
        {
            await EnsureMemberAvailableAsync(memberId, transactionToken);
            var nowUtc = GetUtcNow();
            var timeZoneId = await _repository.GetTimezoneIdForAttendanceWriteAsync(transactionToken);
            var today = await _timezoneService.GetGymDateAsync(
                nowUtc,
                timeZoneId ?? string.Empty,
                transactionToken);
            var (start, endExclusive) = ResolveHistoryRange(
                today,
                fromGymDate,
                endExclusiveGymDate);
            var result = await _repository.GetHistoryPageAsync(
                memberId,
                start,
                endExclusive,
                page,
                pageSize,
                transactionToken);
            var items = result.Items.Select(MapToDto).ToList();
            var mutationVersion = await _projectionVersionProvider
                .GetMutationVersionForMemberAsync(memberId, transactionToken);
            var timeZone = await _timezoneService.GetGymTimeZoneAsync(
                timeZoneId ?? string.Empty,
                transactionToken);
            var totalPages = CalculateTotalPages(result.TotalCount, pageSize);

            return new AttendanceHistoryPageDto
            {
                Metadata = CreateHistoryMetadata(
                    timeZone,
                    today,
                    nowUtc,
                    mutationVersion,
                    start,
                    endExclusive,
                    page,
                    pageSize,
                    result.TotalCount,
                    items),
                Items = items,
                TotalCount = result.TotalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                FromGymDate = start,
                EndExclusiveGymDate = endExclusive
            };
        }, cancellationToken);
    }

    public async Task<EmergencyEvacuationManifestDto> GetEmergencyEvacuationManifestAsync(
        CancellationToken cancellationToken = default)
    {
        var nowUtc = GetUtcNow();
        var openSessions = await _context.AttendanceLogs
            .Include(a => a.Member)
            .Where(a => !a.IsVoided && a.CheckOutTime == null)
            .OrderBy(a => a.CheckInTime)
            .ToListAsync(cancellationToken);

        var occupants = openSessions.Select(a => new EmergencyManifestItemDto
        {
            AttendanceID = a.AttendanceID,
            MemberID = a.MemberID,
            MemberName = a.Member != null ? $"{a.Member.FirstName} {a.Member.LastName}" : "Walk-In Guest",
            ContactNumber = a.Member?.PhoneNumber ?? "N/A",
            EmergencyContactName = a.Member?.EmergencyContact ?? "N/A",
            EmergencyContactPhone = "N/A",
            CheckInTime = a.CheckInTime,
            Source = a.Source ?? string.Empty
        }).ToList();

        var actorUserId = RequireActorUserId();
        await WriteAuditAsync(
            actorUserId,
            "EmergencyEvacuationManifestExported",
            $"Exported emergency evacuation roster containing {occupants.Count} on-site occupants.");

        return new EmergencyEvacuationManifestDto
        {
            ExportedAtUtc = nowUtc,
            TotalCheckedInOccupants = occupants.Count,
            Occupants = occupants
        };
    }

    private async Task<AttendanceDto> CheckInCoreAsync(
        CheckInRequestDto request,
        int? scopedMemberId,
        string source,
        AttendanceOperationType operationType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOperationId(request.OperationId);
        var normalizedQrCode = request.QrCode == null ? string.Empty : NormalizeQrCode(request.QrCode);
        var actorUserId = RequireActorUserId();
        var nowUtc = GetUtcNow();

        var targetDescriptor = scopedMemberId.HasValue ? "self=current" : $"qrHash={HashSensitiveInput(normalizedQrCode)}";
        var fingerprint = CreateFingerprint(
            operationType,
            $"source={source}",
            targetDescriptor);

        var persistOperation = source != Attendance.LegacyStaffQrSource;
        var commitKey = new AttendanceOperationCommitKey(
            request.OperationId,
            actorUserId,
            operationType,
            fingerprint);

        int? resolvedMemberId = null;
        DateOnly? gymDate = null;
        try
        {
            return await ExecuteAttendanceMutationAsync(
                persistOperation,
                commitKey,
                async transactionToken =>
            {
                if (persistOperation)
                {
                    var existingOperation = await _repository.GetOperationAsync(
                        request.OperationId,
                        transactionToken);
                    if (existingOperation is not null)
                    {
                        return await ReplayAsync(
                            existingOperation,
                            operationType,
                            actorUserId,
                            fingerprint,
                            transactionToken);
                    }
                }

                var authoritativeTimeZoneId = await _repository.GetTimezoneIdForAttendanceWriteAsync(
                    transactionToken);
                gymDate = await _timezoneService.GetGymDateAsync(
                    nowUtc,
                    authoritativeTimeZoneId ?? string.Empty,
                    transactionToken);

                Member? member = null;
                if (scopedMemberId.HasValue)
                {
                    await EnsureMemberAvailableAsync(scopedMemberId.Value, transactionToken);
                    member = new Member { MemberID = scopedMemberId.Value }; // Temporary proxy, we only need the ID
                }
                else
                {
                    member = await _repository.GetActiveMemberByQrCodeAsync(
                        normalizedQrCode,
                        transactionToken);
                    if (member is null)
                    {
                        throw NotFound(
                            ErrorCodes.InvalidCheckInCode,
                            "The check-in code is invalid.");
                    }
                }

                resolvedMemberId = member.MemberID;
                var membership = await _repository.GetMembershipSnapshotAsync(
                    member.MemberID,
                    gymDate.Value,
                    transactionToken);
                var membershipState = membership.State;
                if (membershipState == AttendanceMembershipState.Paused)
                {
                    throw Conflict(ErrorCodes.MembershipPaused, "The membership is paused.");
                }

                if (membershipState != AttendanceMembershipState.Active)
                {
                    throw Conflict(ErrorCodes.MembershipInactive, "The membership is inactive.");
                }

                if (await _repository.GetOpenSessionAsync(
                        member.MemberID,
                        cancellationToken: transactionToken) is not null)
                {
                    throw Conflict(ErrorCodes.ActiveSessionExists, "An open attendance session already exists.");
                }

                if (await _repository.HasVisitOnDateAsync(
                    member.MemberID,
                    gymDate.Value,
                    transactionToken))
                {
                    throw Conflict(ErrorCodes.DailyVisitLimit, "The daily visit limit has been reached.");
                }

                var attendance = new Attendance
                {
                    MemberID = member.MemberID,
                    AttendanceDate = gymDate.Value,
                    CheckInTime = nowUtc,
                    Source = source,
                    ActorUserID = actorUserId,
                    IsVoided = false,
                    LastModified = nowUtc
                };

                _repository.AddAttendance(attendance);
                await _repository.SaveChangesAsync(transactionToken);

                if (persistOperation)
                {
                    _repository.AddOperation(CreateCompletedOperation(
                        request.OperationId,
                        actorUserId,
                        operationType,
                        fingerprint,
                        attendance.AttendanceID,
                        StatusCodes.Status201Created,
                        "ATTENDANCE_CHECKED_IN",
                        nowUtc));
                    await _repository.SaveChangesAsync(transactionToken);
                }
                await WriteAuditAsync(
                    actorUserId,
                    source == Attendance.LegacyStaffQrSource
                        ? "Attendance.LegacyCheckInUsed"
                        : "Attendance.CheckedIn",
                    $"Attendance {attendance.AttendanceID} created.");

                attendance.Member = member;
                return MapToDto(attendance);
            }, cancellationToken);
        }
        catch (AppAccessException exception)
        {
            _repository.ClearTrackedChanges();
            var completedReplay = persistOperation
                ? await RecordFailedOperationOrReplayAsync(
                    request.OperationId,
                    actorUserId,
                    operationType,
                    fingerprint,
                    exception,
                    nowUtc,
                    "Attendance.CheckInRejected",
                    cancellationToken)
                : null;
            if (completedReplay is not null)
            {
                return completedReplay;
            }

            if (!persistOperation || !IsTerminalOperationFailure(exception))
            {
                await TryWriteFailureAuditAsync(
                    actorUserId,
                    "Attendance.CheckInRejected",
                    exception.ErrorCode);
            }
            throw;
        }
        catch (AttendanceStoreUniqueConstraintException)
        {
            _repository.ClearTrackedChanges();
            if (persistOperation)
            {
                var existingOperation = await _repository.GetOperationAsync(
                    request.OperationId,
                    cancellationToken);
                if (existingOperation is not null)
                {
                    return await ReplayAsync(
                        existingOperation,
                        operationType,
                        actorUserId,
                        fingerprint,
                        cancellationToken);
                }
            }

            AppAccessException terminalFailure;
            if (resolvedMemberId.HasValue && gymDate.HasValue)
            {
                if (await _repository.GetOpenSessionAsync(
                    resolvedMemberId.Value,
                    cancellationToken: cancellationToken) is not null)
                {
                    terminalFailure = Conflict(
                        ErrorCodes.ActiveSessionExists,
                        "An open attendance session already exists.");
                }
                else if (await _repository.HasVisitOnDateAsync(
                    resolvedMemberId.Value,
                    gymDate.Value,
                    cancellationToken))
                {
                    terminalFailure = Conflict(
                        ErrorCodes.DailyVisitLimit,
                        "The daily visit limit has been reached.");
                }
                else
                {
                    terminalFailure = Conflict(
                        ErrorCodes.AttendanceConflict,
                        "The attendance operation conflicts with another request.");
                }
            }
            else
            {
                terminalFailure = Conflict(
                    ErrorCodes.AttendanceConflict,
                    "The attendance operation conflicts with another request.");
            }

            var completedReplay = persistOperation
                ? await RecordFailedOperationOrReplayAsync(
                    request.OperationId,
                    actorUserId,
                    operationType,
                    fingerprint,
                    terminalFailure,
                    nowUtc,
                    "Attendance.CheckInRejected",
                    cancellationToken)
                : null;
            if (completedReplay is not null)
            {
                return completedReplay;
            }

            if (!persistOperation)
            {
                await TryWriteFailureAuditAsync(
                    actorUserId,
                    "Attendance.CheckInRejected",
                    terminalFailure.ErrorCode);
            }
            throw terminalFailure;
        }
    }

    private async Task<AttendanceDto> CheckOutCoreAsync(
        int? attendanceId,
        int? scopedMemberId,
        CheckOutRequestDto request,
        AttendanceOperationType operationType,
        bool legacyAdapter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOperationId(request.OperationId);
        var actorUserId = RequireActorUserId();
        var nowUtc = GetUtcNow();
        var targetDescriptor = attendanceId.HasValue
            ? $"attendance={attendanceId.Value.ToString(CultureInfo.InvariantCulture)}"
            : "self=current";
        var fingerprint = CreateFingerprint(
            operationType,
            targetDescriptor,
            legacyAdapter ? "contract=legacy" : "contract=v1");
        var commitKey = new AttendanceOperationCommitKey(
            request.OperationId,
            actorUserId,
            operationType,
            fingerprint);

        try
        {
            return await ExecuteAttendanceMutationAsync(
                !legacyAdapter,
                commitKey,
                async transactionToken =>
            {
                if (scopedMemberId.HasValue)
                {
                    await EnsureMemberAvailableAsync(scopedMemberId.Value, transactionToken);
                }

                if (!legacyAdapter)
                {
                    var existingOperation = await _repository.GetOperationAsync(
                        request.OperationId,
                        transactionToken);
                    if (existingOperation is not null)
                    {
                        return await ReplayAsync(
                            existingOperation,
                            operationType,
                            actorUserId,
                            fingerprint,
                            transactionToken,
                            scopedMemberId);
                    }
                }

                var attendance = attendanceId.HasValue
                    ? await _repository.GetByIdAsync(
                        attendanceId.Value,
                        asTracking: true,
                        cancellationToken: transactionToken)
                    : await _repository.GetOpenSessionAsync(
                        scopedMemberId!.Value,
                        asTracking: true,
                        cancellationToken: transactionToken);

                if (attendance is null)
                {
                    throw NotFound(
                        attendanceId.HasValue
                            ? ErrorCodes.AttendanceNotFound
                            : ErrorCodes.NoActiveSession,
                        attendanceId.HasValue
                            ? "Attendance was not found."
                            : "No open attendance session was found.");
                }

                if (scopedMemberId.HasValue && attendance.MemberID != scopedMemberId.Value)
                {
                    throw NotFound(ErrorCodes.NoActiveSession, "No open attendance session was found.");
                }

                if (attendance.CheckOutTime.HasValue)
                {
                    throw Conflict(ErrorCodes.AlreadyCheckedOut, "Attendance is already checked out.");
                }

                if (nowUtc <= attendance.CheckInTime)
                {
                    throw Conflict(ErrorCodes.InvalidCheckoutTime, "Checkout must be after check-in.");
                }

                attendance.CheckOutTime = nowUtc;
                attendance.LastModified = nowUtc;
                if (!legacyAdapter)
                {
                    _repository.AddOperation(CreateCompletedOperation(
                        request.OperationId,
                        actorUserId,
                        operationType,
                        fingerprint,
                        attendance.AttendanceID,
                        StatusCodes.Status200OK,
                        "ATTENDANCE_CHECKED_OUT",
                        nowUtc));
                }

                await _repository.SaveChangesAsync(transactionToken);
                await WriteAuditAsync(
                    actorUserId,
                    legacyAdapter
                        ? "Attendance.LegacyCheckOutUsed"
                        : "Attendance.CheckedOut",
                    $"Attendance {attendance.AttendanceID} checked out.");
                return MapToDto(attendance);
            }, cancellationToken);
        }
        catch (AppAccessException exception)
        {
            _repository.ClearTrackedChanges();
            var completedReplay = !legacyAdapter
                ? await RecordFailedOperationOrReplayAsync(
                    request.OperationId,
                    actorUserId,
                    operationType,
                    fingerprint,
                    exception,
                    nowUtc,
                    "Attendance.CheckOutRejected",
                    cancellationToken,
                    scopedMemberId)
                : null;
            if (completedReplay is not null)
            {
                return completedReplay;
            }

            if (legacyAdapter || !IsTerminalOperationFailure(exception))
            {
                await TryWriteFailureAuditAsync(
                    actorUserId,
                    "Attendance.CheckOutRejected",
                    exception.ErrorCode);
            }
            throw;
        }
        catch (AttendanceStoreConcurrencyException)
        {
            throw Conflict(
                ErrorCodes.AttendanceConcurrencyConflict,
                "Attendance changed before checkout was saved.");
        }
        catch (AttendanceStoreUniqueConstraintException)
        {
            return await ResolveOperationWriteConflictAsync(
                request.OperationId,
                operationType,
                actorUserId,
                fingerprint,
                cancellationToken,
                scopedMemberId);
        }
    }

    private async Task<PagedResultDto<AttendanceDto>> GetHistoryCoreAsync(
        int memberId,
        DateOnly? fromGymDate,
        DateOnly? endExclusiveGymDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidateHistoryPage(page, pageSize);

        var today = await _timezoneService.GetGymDateAsync(GetUtcNow(), cancellationToken);
        var (start, endExclusive) = ResolveHistoryRange(
            today,
            fromGymDate,
            endExclusiveGymDate);

        var result = await _repository.GetHistoryPageAsync(
            memberId,
            start,
            endExclusive,
            page,
            pageSize,
            cancellationToken);

        return new PagedResultDto<AttendanceDto>
        {
            Items = result.Items.Select(MapToDto).ToList(),
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = CalculateTotalPages(result.TotalCount, pageSize)
        };
    }

    private async Task<AttendanceDto> ResolveOperationWriteConflictAsync(
        Guid operationId,
        AttendanceOperationType operationType,
        int actorUserId,
        byte[] fingerprint,
        CancellationToken cancellationToken,
        int? requiredMemberId = null)
    {
        _repository.ClearTrackedChanges();
        var operation = await _repository.GetOperationAsync(operationId, cancellationToken);
        if (operation is not null)
        {
            return await ReplayAsync(
                operation,
                operationType,
                actorUserId,
                fingerprint,
                cancellationToken,
                requiredMemberId);
        }

        throw Conflict(
            ErrorCodes.AttendanceConcurrencyConflict,
            "The attendance operation conflicts with another request.");
    }

    private async Task<AttendanceDto> ReplayAsync(
        AttendanceOperation operation,
        AttendanceOperationType expectedType,
        int actorUserId,
        byte[] fingerprint,
        CancellationToken cancellationToken,
        int? requiredMemberId = null)
    {
        if (operation.ActorUserID != actorUserId
            || operation.OperationType != expectedType)
        {
            throw Conflict(
                ErrorCodes.OperationIdReused,
                "The operation ID was already used for a different request.");
        }

        if (operation.RequestFingerprint.Length != fingerprint.Length
            || !CryptographicOperations.FixedTimeEquals(operation.RequestFingerprint, fingerprint))
        {
            throw Conflict(
                ErrorCodes.OperationIdReused,
                "The operation ID was already used for a different request.");
        }

        if (operation.State == AttendanceOperationState.Failed)
        {
            throw new AppAccessException(
                operation.OriginalHttpStatus,
                operation.OriginalResultCode,
                "The original attendance operation was rejected.");
        }

        if (operation.State != AttendanceOperationState.Completed
            || !operation.TargetAttendanceID.HasValue)
        {
            throw Conflict(
                ErrorCodes.AttendanceConflict,
                "The original attendance operation is not replayable.");
        }

        // Replay never executes business logic again. The immutable ledger fixes the
        // target and original outcome; the response uses the current authorized row.
        var attendance = await _repository.GetByIdAsync(
            operation.TargetAttendanceID.Value,
            includeVoided: true,
            cancellationToken: cancellationToken);
        if (attendance is null)
        {
            throw Conflict(
                ErrorCodes.AttendanceConflict,
                "The original attendance result is unavailable.");
        }

        if (requiredMemberId.HasValue && attendance.MemberID != requiredMemberId.Value)
        {
            throw NotFound(ErrorCodes.NoActiveSession, "No open attendance session was found.");
        }

        return MapToDto(attendance);
    }

    private async Task<AttendanceDto?> RecordFailedOperationOrReplayAsync(
        Guid operationId,
        int actorUserId,
        AttendanceOperationType operationType,
        byte[] fingerprint,
        AppAccessException failure,
        DateTime nowUtc,
        string failureAuditAction,
        CancellationToken cancellationToken,
        int? requiredMemberId = null)
    {
        if (!IsTerminalOperationFailure(failure))
        {
            return null;
        }

        var commitKey = new AttendanceOperationCommitKey(
            operationId,
            actorUserId,
            operationType,
            fingerprint);

        try
        {
            return await _repository.ExecuteVerifiedMutationAsync(commitKey, async transactionToken =>
            {
                var existingOperation = await _repository.GetOperationAsync(
                    operationId,
                    transactionToken);
                if (existingOperation is not null)
                {
                    return await ReplayAsync(
                        existingOperation,
                        operationType,
                        actorUserId,
                        fingerprint,
                        transactionToken,
                        requiredMemberId);
                }

                _repository.AddOperation(CreateFailedOperation(
                    operationId,
                    actorUserId,
                    operationType,
                    fingerprint,
                    failure.StatusCode,
                    failure.ErrorCode,
                    nowUtc));
                _repository.AddAudit(CreateFailureAudit(
                    actorUserId,
                    failureAuditAction,
                    failure.ErrorCode,
                    nowUtc));
                await _repository.SaveChangesAsync(transactionToken);
                return null;
            }, cancellationToken);
        }
        catch (AttendanceStoreUniqueConstraintException)
        {
            return await ResolveOperationWriteConflictAsync(
                operationId,
                operationType,
                actorUserId,
                fingerprint,
                cancellationToken,
                requiredMemberId);
        }
    }

    private Task<TResult> ExecuteAttendanceMutationAsync<TResult>(
        bool requireCommitVerification,
        AttendanceOperationCommitKey commitKey,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        return requireCommitVerification
            ? _repository.ExecuteVerifiedMutationAsync(commitKey, action, cancellationToken)
            : _repository.ExecuteSerializableAsync(action, cancellationToken);
    }

    private static bool IsTerminalOperationFailure(AppAccessException failure)
    {
        return failure.StatusCode >= StatusCodes.Status400BadRequest
            && failure.StatusCode < StatusCodes.Status500InternalServerError
            && failure.StatusCode != StatusCodes.Status408RequestTimeout
            && failure.StatusCode != StatusCodes.Status429TooManyRequests;
    }

    private static AttendanceOperation CreateCompletedOperation(
        Guid operationId,
        int actorUserId,
        AttendanceOperationType operationType,
        byte[] fingerprint,
        int attendanceId,
        int httpStatus,
        string resultCode,
        DateTime nowUtc)
    {
        return new AttendanceOperation
        {
            OperationID = operationId,
            ActorUserID = actorUserId,
            OperationType = operationType,
            RequestFingerprint = fingerprint,
            TargetAttendanceID = attendanceId,
            OriginalHttpStatus = httpStatus,
            OriginalResultCode = resultCode,
            State = AttendanceOperationState.Completed,
            CreatedAtUtc = nowUtc,
            CompletedAtUtc = nowUtc
        };
    }

    private static AttendanceOperation CreateFailedOperation(
        Guid operationId,
        int actorUserId,
        AttendanceOperationType operationType,
        byte[] fingerprint,
        int httpStatus,
        string resultCode,
        DateTime nowUtc)
    {
        return new AttendanceOperation
        {
            OperationID = operationId,
            ActorUserID = actorUserId,
            OperationType = operationType,
            RequestFingerprint = fingerprint,
            OriginalHttpStatus = httpStatus,
            OriginalResultCode = resultCode,
            State = AttendanceOperationState.Failed,
            CreatedAtUtc = nowUtc,
            CompletedAtUtc = nowUtc
        };
    }

    private int RequireActorUserId()
    {
        return _currentUser.UserId is > 0
            ? _currentUser.UserId.Value
            : throw Forbidden();
    }

    private int RequireMemberId()
    {
        return _currentUser.MemberId is > 0
            ? _currentUser.MemberId.Value
            : throw Forbidden();
    }

    private async Task EnsureMemberAvailableAsync(
        int memberId,
        CancellationToken cancellationToken)
    {
        if (!await _repository.IsMemberAvailableAsync(memberId, cancellationToken))
        {
            throw NotFound(ErrorCodes.MemberInactive, "The linked member is unavailable.");
        }
    }

    private DateTime GetUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The application clock must return UTC values.");
        }

        return now;
    }

    private static void ValidateHistoryPage(int page, int pageSize)
    {
        if (page < 1
            || pageSize < 1
            || pageSize > MaximumPageSize
            || page - 1 > int.MaxValue / pageSize)
        {
            throw Validation(ErrorCodes.InvalidAttendanceRange, "The attendance page is invalid.");
        }
    }

    private static (DateOnly Start, DateOnly EndExclusive) ResolveHistoryRange(
        DateOnly today,
        DateOnly? fromGymDate,
        DateOnly? endExclusiveGymDate)
    {
        var start = fromGymDate ?? today.AddDays(-29);
        var endExclusive = endExclusiveGymDate ?? today.AddDays(1);
        var rangeDays = endExclusive.DayNumber - start.DayNumber;
        if (rangeDays <= 0 || rangeDays > MaximumHistoryDays)
        {
            throw Validation(
                ErrorCodes.InvalidAttendanceRange,
                "The attendance date range is invalid.");
        }

        return (start, endExclusive);
    }

    private static int CalculateTotalPages(int totalCount, int pageSize)
    {
        return totalCount / pageSize + (totalCount % pageSize == 0 ? 0 : 1);
    }

    private static ProjectionMetadataDto CreateHistoryMetadata(
        TimeZoneInfo timeZone,
        DateOnly effectiveGymDate,
        DateTime generatedAtUtc,
        long mutationVersion,
        DateOnly start,
        DateOnly endExclusive,
        int page,
        int pageSize,
        int totalCount,
        IReadOnlyList<AttendanceDto> items)
    {
        var canonical = new StringBuilder("schema=attendance-history-v1\n")
            .Append("timezone=").Append(timeZone.Id).Append('\n')
            .Append("effectiveGymDate=").Append(effectiveGymDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n')
            .Append("from=").Append(start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n')
            .Append("to=").Append(endExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n')
            .Append("page=").Append(page.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("pageSize=").Append(pageSize.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("totalCount=").Append(totalCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var item in items)
        {
            canonical
                .Append("row=").Append(item.AttendanceID.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(item.MemberID.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(item.AttendanceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|')
                .Append(item.CheckInTime.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(item.CheckOutTime?.ToString("O", CultureInfo.InvariantCulture) ?? "null").Append('|')
                .Append(item.Source.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(item.Source).Append('|')
                .Append(item.LastModified.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        }

        return new ProjectionMetadataDto
        {
            SchemaVersion = "attendance-history-v1",
            DataVersion = ProjectionVersionComposer.Compose(mutationVersion, effectiveGymDate),
            ContentETag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))),
            Timezone = timeZone.Id,
            GeneratedAtUtc = generatedAtUtc,
            CacheFreshUntilUtc = generatedAtUtc.AddMinutes(15)
        };
    }

    private static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw Validation(ErrorCodes.InvalidCheckoutTime, "A UTC timestamp is required.", parameterName);
        }
    }

    private static void ValidateOperationId(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw Validation(ErrorCodes.InvalidOperationId, "A non-empty operation ID is required.");
        }
    }

    private static string NormalizeQrCode(string? qrCode)
    {
        var normalized = qrCode?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 100)
        {
            throw Validation(ErrorCodes.InvalidCheckInCode, "The check-in code is invalid.");
        }

        return normalized;
    }

    private static string NormalizeReason(string? reason)
    {
        var normalized = reason?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 255)
        {
            throw Validation(
                ErrorCodes.InvalidAttendanceReason,
                "A reason between 1 and 255 characters is required.");
        }

        return normalized;
    }

    private static byte[] CreateFingerprint(
        AttendanceOperationType operationType,
        params string[] components)
    {
        var canonical = new StringBuilder("attendance-operation-v1\n")
            .Append("type=")
            .Append(((int)operationType).ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (var component in components)
        {
            canonical.Append(component).Append('\n');
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    private static string HashSensitiveInput(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private AuditLog CreateFailureAudit(
        int actorUserId,
        string action,
        string errorCode,
        DateTime timestampUtc)
    {
        var correlationId = _httpContextAccessor.HttpContext?.TraceIdentifier ?? "Unavailable";
        correlationId = correlationId.Replace('\r', '_').Replace('\n', '_');
        if (correlationId.Length > 128)
        {
            correlationId = correlationId[..128];
        }

        return new AuditLog
        {
            UserID = actorUserId,
            Action = action,
            Details = $"Attendance request rejected: {errorCode}. CorrelationId: {correlationId}.",
            IPAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
                ?? "Unknown",
            Timestamp = timestampUtc
        };
    }

    private Task WriteAuditAsync(int actorUserId, string action, string details)
    {
        var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
            ?? "Unknown";
        var correlationId = _httpContextAccessor.HttpContext?.TraceIdentifier ?? "Unavailable";
        correlationId = correlationId
            .Replace('\r', '_')
            .Replace('\n', '_');
        if (correlationId.Length > 128)
        {
            correlationId = correlationId[..128];
        }

        return _auditService.LogActivityAsync(
            actorUserId,
            action,
            $"{details} CorrelationId: {correlationId}.",
            ipAddress);
    }

    private async Task TryWriteFailureAuditAsync(
        int actorUserId,
        string action,
        string errorCode)
    {
        try
        {
            await WriteAuditAsync(actorUserId, action, $"Attendance request rejected: {errorCode}.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Attendance failure-audit persistence failed. ActorUserId: {ActorUserId}; Category: {Category}; CorrelationId: {CorrelationId}",
                actorUserId,
                action,
                _httpContextAccessor.HttpContext?.TraceIdentifier ?? "Unavailable");
        }
    }

    private static AttendanceDto MapToDto(Attendance attendance)
    {
        return new AttendanceDto
        {
            AttendanceID = attendance.AttendanceID,
            MemberID = attendance.MemberID,
            MemberName = attendance.Member is null
                ? string.Empty
                : $"{attendance.Member.FirstName} {attendance.Member.LastName}",
            AttendanceDate = attendance.AttendanceDate,
            CheckInTime = NormalizePersistedUtc(attendance.CheckInTime),
            CheckOutTime = attendance.CheckOutTime.HasValue
                ? NormalizePersistedUtc(attendance.CheckOutTime.Value)
                : null,
            Source = attendance.Source ?? string.Empty,
            IsVoided = attendance.IsVoided,
            SupersededByAttendanceID = attendance.SupersededByAttendanceID,
            LastModified = NormalizePersistedUtc(attendance.LastModified)
        };
    }

    private static DateTime NormalizePersistedUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => throw new InvalidOperationException("A persisted UTC timestamp had an invalid DateTime kind.")
        };
    }

    private static AppAccessException Validation(
        string errorCode,
        string message,
        string? parameterName = null)
    {
        _ = parameterName;
        return new AppAccessException(StatusCodes.Status400BadRequest, errorCode, message);
    }

    private static AppAccessException NotFound(string errorCode, string message)
    {
        return new AppAccessException(StatusCodes.Status404NotFound, errorCode, message);
    }

    private static AppAccessException Conflict(string errorCode, string message)
    {
        return new AppAccessException(StatusCodes.Status409Conflict, errorCode, message);
    }

    private static AppAccessException Forbidden()
    {
        return new AppAccessException(
            StatusCodes.Status403Forbidden,
            ErrorCodes.AccessForbidden,
            "Access is forbidden.");
    }
}
