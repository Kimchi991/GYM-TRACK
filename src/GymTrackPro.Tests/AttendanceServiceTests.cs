using GymTrackPro.API.Authentication;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Moq;
using Microsoft.Extensions.Logging;

namespace GymTrackPro.Tests;

public class AttendanceServiceTests
{
    [Theory]
    [InlineData("checkout")]
    [InlineData("correction")]
    [InlineData("void")]
    public async Task Check_in_replay_returns_same_target_current_representation_without_mutating_again(
        string laterMutation)
    {
        var harness = new Harness();
        var request = new CheckInRequestDto
        {
            OperationId = Guid.NewGuid(),
            QrCode = "member-qr"
        };

        var original = await harness.Service.CheckInAsync(request);
        var row = harness.AttendanceById[original.AttendanceID];
        if (laterMutation == "checkout")
        {
            row.CheckOutTime = harness.Now.AddHours(1);
        }
        else if (laterMutation == "correction")
        {
            row.CheckOutTime = harness.Now.AddHours(2);
            row.LastModified = harness.Now.AddHours(2);
        }
        else
        {
            row.IsVoided = true;
            row.VoidReason = "Owner correction";
        }

        var replay = await harness.Service.CheckInAsync(request);

        Assert.Equal(original.AttendanceID, replay.AttendanceID);
        Assert.Equal(row.CheckOutTime, replay.CheckOutTime);
        Assert.Equal(row.IsVoided, replay.IsVoided);
        Assert.Single(harness.AttendanceById);
        Assert.Single(harness.Operations);
        Assert.Equal(StatusCodes.Status201Created, harness.Operations[request.OperationId].OriginalHttpStatus);
        Assert.Equal("ATTENDANCE_CHECKED_IN", harness.Operations[request.OperationId].OriginalResultCode);
        harness.Repository.Verify(repository => repository.AddAttendance(It.IsAny<Attendance>()), Times.Once);
    }

    [Fact]
    public async Task Same_operation_with_different_fingerprint_is_conflict()
    {
        var harness = new Harness();
        var operationId = Guid.NewGuid();
        await harness.Service.CheckInAsync(new CheckInRequestDto
        {
            OperationId = operationId,
            QrCode = "member-qr"
        });

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CheckInAsync(new CheckInRequestDto
            {
                OperationId = operationId,
                QrCode = "different-qr"
            }));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal(ErrorCodes.OperationIdReused, exception.ErrorCode);
    }

    [Fact]
    public async Task Same_operation_with_different_actor_is_conflict()
    {
        var harness = new Harness();
        var request = new CheckInRequestDto
        {
            OperationId = Guid.NewGuid(),
            QrCode = "member-qr"
        };
        await harness.Service.CheckInAsync(request);
        harness.ActorUserId = 99;

        var exception = await Assert.ThrowsAsync<AppAccessException>(
            () => harness.Service.CheckInAsync(request));

        Assert.Equal(ErrorCodes.OperationIdReused, exception.ErrorCode);
    }

    [Fact]
    public async Task Thirty_two_provider_neutral_retries_mutate_once_under_store_gate()
    {
        var harness = new Harness();
        var request = new CheckInRequestDto
        {
            OperationId = Guid.NewGuid(),
            QrCode = "member-qr"
        };

        var results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => harness.Service.CheckInAsync(request)));

        Assert.All(results, result => Assert.Equal(results[0].AttendanceID, result.AttendanceID));
        harness.Repository.Verify(repository => repository.AddAttendance(It.IsAny<Attendance>()), Times.Once);
        Assert.Single(harness.Operations);
    }

    [Fact]
    public async Task Timezone_update_committed_first_is_used_for_authoritative_visit_date()
    {
        var harness = new Harness();
        Assert.True(await harness.TryUpdateTimezoneAsync("UTC"));

        var result = await harness.Service.CheckInAsync(new CheckInRequestDto
        {
            OperationId = Guid.NewGuid(),
            QrCode = "member-qr"
        });

        Assert.Equal(harness.AlternateGymDate, result.AttendanceDate);
    }

    [Fact]
    public async Task Checkin_lock_acquired_first_blocks_later_timezone_mutation()
    {
        var harness = new Harness
        {
            TimezoneReadEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
            ContinueTimezoneRead = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var checkInTask = harness.Service.CheckInAsync(new CheckInRequestDto
        {
            OperationId = Guid.NewGuid(),
            QrCode = "member-qr"
        });
        await harness.TimezoneReadEntered.Task;

        var updateTask = harness.TryUpdateTimezoneAsync("UTC");
        Assert.False(updateTask.IsCompleted);
        harness.ContinueTimezoneRead.SetResult();
        await checkInTask;

        Assert.False(await updateTask);
        Assert.Equal("Asia/Manila", harness.TimeZoneId);
    }

    [Fact]
    public async Task Empty_operation_id_is_rejected_before_any_write()
    {
        var harness = new Harness();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CheckInAsync(new CheckInRequestDto
            {
                OperationId = Guid.Empty,
                QrCode = "member-qr"
            }));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(ErrorCodes.InvalidOperationId, exception.ErrorCode);
        harness.Repository.Verify(repository => repository.AddAttendance(It.IsAny<Attendance>()), Times.Never);
    }

    [Fact]
    public async Task Self_checkin_uses_authenticated_member_without_resolving_a_qr_code()
    {
        var harness = new Harness();
        var request = new AttendanceOperationRequestDto { OperationId = Guid.NewGuid() };

        var result = await harness.Service.CheckInCurrentMemberAsync(request);

        Assert.Equal(harness.MemberId, result.MemberID);
        Assert.Equal(Attendance.SelfCheckInSource, result.Source);
        Assert.Equal(
            AttendanceOperationType.GymGoerCheckIn,
            harness.Operations[request.OperationId].OperationType);
        harness.Repository.Verify(repository => repository.GetActiveMemberByQrCodeAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Self_checkin_rejects_an_empty_operation_id_before_writing()
    {
        var harness = new Harness();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CheckInCurrentMemberAsync(new AttendanceOperationRequestDto()));

        Assert.Equal(ErrorCodes.InvalidOperationId, exception.ErrorCode);
        harness.Repository.Verify(repository => repository.AddAttendance(It.IsAny<Attendance>()), Times.Never);
    }

    [Fact]
    public async Task Legacy_checkin_emits_non_sensitive_usage_telemetry()
    {
        var harness = new Harness();

        await harness.Service.CheckInAsync("member-qr");

        harness.Audit.Verify(audit => audit.LogActivityAsync(
            harness.ActorUserId,
            "Attendance.LegacyCheckInUsed",
            It.Is<string>(details => !details.Contains("member-qr", StringComparison.Ordinal)),
            It.IsAny<string>()));
        Assert.Empty(harness.Operations);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task History_rejects_invalid_paging_without_querying_rows(int page, int pageSize)
    {
        var harness = new Harness();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.GetAttendanceHistoryAsync(null, null, page, pageSize));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(ErrorCodes.InvalidAttendanceRange, exception.ErrorCode);
        harness.Repository.Verify(repository => repository.GetHistoryPageAsync(
            It.IsAny<int>(),
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Paused_membership_is_rejected_and_failure_audit_contains_no_qr()
    {
        var harness = new Harness();
        harness.Repository
            .Setup(repository => repository.GetMembershipSnapshotAsync(
                10,
                harness.GymDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceMembershipSnapshot(
                AttendanceMembershipState.Paused,
                harness.GymDate.AddDays(5).ToDateTime(TimeOnly.MinValue),
                1,
                1));

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CheckInAsync(new CheckInRequestDto
            {
                OperationId = Guid.NewGuid(),
                QrCode = "member-qr"
        }));

        Assert.Equal(ErrorCodes.MembershipPaused, exception.ErrorCode);
        var audit = Assert.Single(harness.AtomicAudits);
        Assert.Equal(harness.ActorUserId, audit.UserID);
        Assert.Equal("Attendance.CheckInRejected", audit.Action);
        Assert.Contains(ErrorCodes.MembershipPaused, audit.Details);
        Assert.DoesNotContain("member-qr", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failure_audit_outage_is_logged_without_masking_business_error()
    {
        var harness = new Harness();
        harness.Timezone
            .Setup(service => service.GetGymDateAsync(
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AppAccessException(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.GymTimezoneInvalid,
                "The gym timezone configuration is unavailable."));
        harness.Audit
            .Setup(audit => audit.LogActivityAsync(
                It.IsAny<int?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Audit store unavailable."));

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CheckInAsync(new CheckInRequestDto
            {
                OperationId = Guid.NewGuid(),
                QrCode = "member-qr"
            }));

        Assert.Equal(ErrorCodes.GymTimezoneInvalid, exception.ErrorCode);
        Assert.Contains(
            harness.Logger.Entries,
            entry => entry.Contains("ActorUserId: 5", StringComparison.Ordinal)
                && entry.Contains("Attendance.CheckInRejected", StringComparison.Ordinal)
                && !entry.Contains("member-qr", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Checkout_rejects_timestamp_not_after_checkin()
    {
        var harness = new Harness();
        var attendance = harness.AddExistingAttendance(checkIn: harness.Now);
        harness.Repository
            .Setup(repository => repository.GetByIdAsync(
                attendance.AttendanceID,
                false,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CheckOutAsync(attendance.AttendanceID, new CheckOutRequestDto
            {
                OperationId = Guid.NewGuid()
            }));

        Assert.Equal(ErrorCodes.InvalidCheckoutTime, exception.ErrorCode);
    }

    [Fact]
    public async Task Self_checkout_replay_hides_target_after_member_link_changes()
    {
        var harness = new Harness();
        var attendance = harness.AddExistingAttendance(checkIn: harness.Now.AddHours(-1));
        harness.Repository
            .Setup(repository => repository.GetOpenSessionAsync(
                harness.MemberId,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);
        var request = new CheckOutRequestDto
        {
            OperationId = Guid.NewGuid()
        };
        await harness.Service.CheckOutCurrentMemberAsync(request);
        harness.MemberId = 11;

        var exception = await Assert.ThrowsAsync<AppAccessException>(
            () => harness.Service.CheckOutCurrentMemberAsync(request));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal(ErrorCodes.NoActiveSession, exception.ErrorCode);
    }

    [Fact]
    public async Task Wrong_self_replay_fingerprint_is_conflict_without_target_lookup()
    {
        var harness = new Harness();
        var attendance = harness.AddExistingAttendance(checkIn: harness.Now.AddHours(-1));
        harness.Repository
            .Setup(repository => repository.GetOpenSessionAsync(
                harness.MemberId,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);
        var operationId = Guid.NewGuid();
        await harness.Service.CheckOutCurrentMemberAsync(new CheckOutRequestDto
        {
            OperationId = operationId
        });
        harness.Operations[operationId].RequestFingerprint = new byte[32];
        harness.MemberId = 11;

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CheckOutCurrentMemberAsync(new CheckOutRequestDto
            {
                OperationId = operationId
            }));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal(ErrorCodes.OperationIdReused, exception.ErrorCode);
        harness.Repository.Verify(repository => repository.GetByIdAsync(
            attendance.AttendanceID,
            true,
            false,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Correction_rejects_future_overlap_and_blank_reason()
    {
        var harness = new Harness();
        var attendance = harness.AddExistingAttendance(checkIn: harness.Now.AddHours(-3));
        var next = harness.AddExistingAttendance(
            attendanceId: 2,
            checkIn: harness.Now.AddHours(-1));
        harness.SetupTrackedAttendance(attendance);
        harness.Repository
            .Setup(repository => repository.GetNextNonVoidedSessionAsync(
                attendance.MemberID,
                attendance.CheckInTime,
                attendance.AttendanceID,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(next);

        var future = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CorrectCheckoutAsync(attendance.AttendanceID, new CorrectCheckoutRequestDto
            {
                OperationId = Guid.NewGuid(),
                CorrectedCheckOutTimeUtc = harness.Now.AddMinutes(1),
                Reason = "Owner correction"
            }));
        var overlap = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CorrectCheckoutAsync(attendance.AttendanceID, new CorrectCheckoutRequestDto
            {
                OperationId = Guid.NewGuid(),
                CorrectedCheckOutTimeUtc = next.CheckInTime.AddSeconds(1),
                Reason = "Owner correction"
            }));
        var blank = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.CorrectCheckoutAsync(attendance.AttendanceID, new CorrectCheckoutRequestDto
            {
                OperationId = Guid.NewGuid(),
                CorrectedCheckOutTimeUtc = next.CheckInTime,
                Reason = "  "
            }));

        Assert.Equal(ErrorCodes.InvalidCheckoutTime, future.ErrorCode);
        Assert.Equal(ErrorCodes.AttendanceOverlap, overlap.ErrorCode);
        Assert.Equal(ErrorCodes.InvalidAttendanceReason, blank.ErrorCode);
    }

    [Fact]
    public async Task Multiple_corrections_append_multiple_typed_adjustments()
    {
        var harness = new Harness();
        var attendance = harness.AddExistingAttendance(checkIn: harness.Now.AddHours(-4));
        harness.SetupTrackedAttendance(attendance);

        await harness.Service.CorrectCheckoutAsync(attendance.AttendanceID, new CorrectCheckoutRequestDto
        {
            OperationId = Guid.NewGuid(),
            CorrectedCheckOutTimeUtc = harness.Now.AddHours(-2),
            Reason = "First correction"
        });
        await harness.Service.CorrectCheckoutAsync(attendance.AttendanceID, new CorrectCheckoutRequestDto
        {
            OperationId = Guid.NewGuid(),
            CorrectedCheckOutTimeUtc = harness.Now.AddHours(-1),
            Reason = "Second correction"
        });

        Assert.Equal(2, harness.Adjustments.Count);
        Assert.All(
            harness.Adjustments,
            adjustment => Assert.Equal(AttendanceAdjustmentKind.CheckoutCorrection, adjustment.Kind));
        Assert.Equal(
            harness.Now.AddHours(-2),
            harness.Adjustments[1].BeforeCheckOutTimeUtc);
    }

    [Fact]
    public async Task Superseding_attendance_must_match_member_and_authoritative_visit_date()
    {
        var harness = new Harness();
        var target = harness.AddExistingAttendance(checkIn: harness.Now.AddHours(-3));
        var unrelatedDate = harness.AddExistingAttendance(
            attendanceId: 2,
            checkIn: harness.Now.AddDays(1),
            attendanceDate: harness.GymDate.AddDays(1));
        harness.SetupTrackedAttendance(target);
        harness.SetupAttendanceLookup(unrelatedDate);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            harness.Service.VoidAsync(target.AttendanceID, new VoidAttendanceRequestDto
            {
                OperationId = Guid.NewGuid(),
                Reason = "Duplicate imported row",
                SupersedingAttendanceId = unrelatedDate.AttendanceID
            }));

        Assert.Equal(ErrorCodes.InvalidSupersedingAttendance, exception.ErrorCode);
        Assert.False(target.IsVoided);
        Assert.Empty(harness.Adjustments);
    }

    [Fact]
    public async Task Failed_self_checkout_replays_original_terminal_result_after_state_changes()
    {
        var harness = new Harness();
        var request = new CheckOutRequestDto { OperationId = Guid.NewGuid() };

        var first = await Assert.ThrowsAsync<AppAccessException>(
            () => harness.Service.CheckOutCurrentMemberAsync(request));
        var laterSession = harness.AddExistingAttendance(checkIn: harness.Now.AddHours(-1));
        harness.Repository.Setup(repository => repository.GetOpenSessionAsync(
                harness.MemberId,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(laterSession);

        var replay = await Assert.ThrowsAsync<AppAccessException>(
            () => harness.Service.CheckOutCurrentMemberAsync(request));

        Assert.Equal(ErrorCodes.NoActiveSession, first.ErrorCode);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(first.ErrorCode, replay.ErrorCode);
        Assert.Equal(AttendanceOperationState.Failed, harness.Operations[request.OperationId].State);
        Assert.Null(laterSession.CheckOutTime);
    }

    [Fact]
    public async Task Transient_timezone_failure_is_not_ledgered_and_same_operation_can_recover()
    {
        var harness = new Harness();
        var calls = 0;
        harness.Timezone.Setup(service => service.GetGymDateAsync(
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((DateTime _, string _, CancellationToken _) =>
                Interlocked.Increment(ref calls) == 1
                    ? Task.FromException<DateOnly>(new AppAccessException(
                        StatusCodes.Status503ServiceUnavailable,
                        ErrorCodes.GymTimezoneInvalid,
                        "The gym timezone configuration is unavailable."))
                    : Task.FromResult(harness.GymDate));
        var request = new CheckInRequestDto
        {
            OperationId = Guid.NewGuid(),
            QrCode = "member-qr"
        };

        var first = await Assert.ThrowsAsync<AppAccessException>(
            () => harness.Service.CheckInAsync(request));
        var recovered = await harness.Service.CheckInAsync(request);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, first.StatusCode);
        Assert.Equal(1, recovered.AttendanceID);
        Assert.Equal(AttendanceOperationState.Completed, harness.Operations[request.OperationId].State);
    }

    [Fact]
    public async Task Non_utc_internal_clock_is_server_invariant_failure_not_client_validation()
    {
        var harness = new Harness
        {
            Now = DateTime.SpecifyKind(new DateTime(2026, 7, 12, 4, 0, 0), DateTimeKind.Unspecified)
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.CheckInAsync(new CheckInRequestDto
            {
                OperationId = Guid.NewGuid(),
                QrCode = "member-qr"
            }));

        Assert.Contains("clock", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Operations);
    }

    [Fact]
    public async Task Two_distinct_self_checkout_operations_produce_one_success_and_one_terminal_conflict()
    {
        var harness = new Harness();
        var attendance = harness.AddExistingAttendance(checkIn: harness.Now.AddHours(-1));
        harness.Repository.Setup(repository => repository.GetOpenSessionAsync(
                harness.MemberId,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);
        var requests = new[]
        {
            new CheckOutRequestDto { OperationId = Guid.NewGuid() },
            new CheckOutRequestDto { OperationId = Guid.NewGuid() }
        };

        var outcomes = await Task.WhenAll(requests.Select(async request =>
        {
            try
            {
                return (object)await harness.Service.CheckOutCurrentMemberAsync(request);
            }
            catch (AppAccessException exception)
            {
                return exception;
            }
        }));

        Assert.Single(outcomes.OfType<AttendanceDto>());
        var conflict = Assert.Single(outcomes.OfType<AppAccessException>());
        Assert.Equal(ErrorCodes.AlreadyCheckedOut, conflict.ErrorCode);
        Assert.Single(
            harness.Operations.Values,
            operation => operation.State == AttendanceOperationState.Completed);
        Assert.Single(
            harness.Operations.Values,
            operation => operation.State == AttendanceOperationState.Failed);
    }

    [Fact]
    public async Task Self_history_returns_consistent_metadata_total_pages_and_utc_wire_values()
    {
        var harness = new Harness();
        var row = harness.AddExistingAttendance(checkIn: harness.Now.AddHours(-1));
        row.CheckInTime = DateTime.SpecifyKind(row.CheckInTime, DateTimeKind.Unspecified);
        row.LastModified = DateTime.SpecifyKind(row.LastModified, DateTimeKind.Unspecified);
        harness.Repository.Setup(repository => repository.GetHistoryPageAsync(
                harness.MemberId,
                harness.GymDate.AddDays(-1),
                harness.GymDate.AddDays(1),
                2,
                30,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePage(new[] { row }, 31));

        var result = await harness.Service.GetAttendanceHistoryAsync(
            harness.GymDate.AddDays(-1),
            harness.GymDate.AddDays(1),
            page: 2,
            pageSize: 30);

        Assert.Equal(2, result.TotalPages);
        Assert.Equal(ProjectionVersionComposer.Compose(7, harness.GymDate), result.Metadata.DataVersion);
        Assert.Equal("attendance-history-v1", result.Metadata.SchemaVersion);
        Assert.NotEmpty(result.Metadata.ContentETag);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(result.Items).CheckInTime.Kind);
    }

    [Fact]
    public async Task Deleted_member_link_is_hidden_from_self_attendance_reads()
    {
        var harness = new Harness();
        harness.Repository.Setup(repository => repository.IsMemberAvailableAsync(
                harness.MemberId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<AppAccessException>(
            () => harness.Service.GetCurrentOpenSessionAsync());

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal(ErrorCodes.MemberInactive, exception.ErrorCode);
    }

    private sealed class Harness
    {
        private readonly SemaphoreSlim _transactionGate = new(1, 1);

        public Harness()
        {
            Repository = new Mock<IAttendanceRepository>();
            Audit = new Mock<IAuditService>();
            Clock = new Mock<IClockService>();
            Timezone = new Mock<ITimezoneService>();
            CurrentUser = new Mock<ICurrentUserContext>();
            ProjectionVersion = new Mock<IProjectionVersionProvider>();
            HttpContext = new Mock<IHttpContextAccessor>();
            Logger = new RecordingLogger<AttendanceService>();

            Clock.SetupGet(clock => clock.UtcNow).Returns(() => Now);
            Timezone.Setup(service => service.GetGymDateAsync(
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => GymDate);
            Repository.Setup(repository => repository.GetTimezoneIdForAttendanceWriteAsync(
                    It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    TimezoneReadEntered?.TrySetResult();
                    if (ContinueTimezoneRead is not null)
                    {
                        await ContinueTimezoneRead.Task.WaitAsync(cancellationToken);
                    }
                    return TimeZoneId;
                });
            Timezone.Setup(service => service.GetGymDateAsync(
                    It.IsAny<DateTime>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateTime _, string timeZoneId, CancellationToken _) =>
                    timeZoneId == "UTC" ? AlternateGymDate : GymDate);
            Timezone.Setup(service => service.GetGymTimeZoneAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
            CurrentUser.SetupGet(context => context.UserId).Returns(() => ActorUserId);
            CurrentUser.SetupGet(context => context.MemberId).Returns(() => MemberId);
            ProjectionVersion.Setup(provider => provider.GetMutationVersionForMemberAsync(
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(7L);
            Repository.Setup(repository => repository.ExecuteSerializableAsync(
                    It.IsAny<Func<CancellationToken, Task<AttendanceDto>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (
                    Func<CancellationToken, Task<AttendanceDto>> action,
                    CancellationToken cancellationToken) =>
                {
                    await _transactionGate.WaitAsync(cancellationToken);
                    try
                    {
                        return await action(cancellationToken);
                    }
                    finally
                    {
                        _transactionGate.Release();
                    }
                });
            Repository.Setup(repository => repository.ExecuteVerifiedMutationAsync(
                    It.IsAny<AttendanceOperationCommitKey>(),
                    It.IsAny<Func<CancellationToken, Task<AttendanceDto>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (
                    AttendanceOperationCommitKey _,
                    Func<CancellationToken, Task<AttendanceDto>> action,
                    CancellationToken cancellationToken) =>
                {
                    await _transactionGate.WaitAsync(cancellationToken);
                    try
                    {
                        return await action(cancellationToken);
                    }
                    finally
                    {
                        _transactionGate.Release();
                    }
                });
            Repository.Setup(repository => repository.ExecuteConsistentReadAsync(
                    It.IsAny<Func<CancellationToken, Task<AttendanceHistoryPageDto>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    Func<CancellationToken, Task<AttendanceHistoryPageDto>> action,
                    CancellationToken cancellationToken) => action(cancellationToken));
            Repository.Setup(repository => repository.ExecuteConsistentReadAsync(
                    It.IsAny<Func<CancellationToken, Task<IReadOnlyList<AttendanceDto>>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    Func<CancellationToken, Task<IReadOnlyList<AttendanceDto>>> action,
                    CancellationToken cancellationToken) => action(cancellationToken));
            Repository.Setup(repository => repository.GetOperationAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid operationId, CancellationToken _) =>
                    Operations.GetValueOrDefault(operationId));
            Repository.Setup(repository => repository.GetActiveMemberByQrCodeAsync(
                    "member-qr",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Member
                {
                    MemberID = 10,
                    FirstName = "Test",
                    LastName = "Member",
                    QRCode = "member-qr",
                    Status = "Active"
                });
            Repository.Setup(repository => repository.IsMemberAvailableAsync(
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            Repository.Setup(repository => repository.GetMembershipSnapshotAsync(
                    10,
                    It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AttendanceMembershipSnapshot(
                    AttendanceMembershipState.Active,
                    GymDate.AddDays(30).ToDateTime(TimeOnly.MinValue),
                    1,
                    1));
            Repository.Setup(repository => repository.GetOpenSessionAsync(
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Attendance?)null);
            Repository.Setup(repository => repository.HasVisitOnDateAsync(
                    It.IsAny<int>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Repository.Setup(repository => repository.GetNextNonVoidedSessionAsync(
                    It.IsAny<int>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Attendance?)null);
            Repository.Setup(repository => repository.AddAttendance(It.IsAny<Attendance>()))
                .Callback((Attendance attendance) =>
                {
                    attendance.AttendanceID = AttendanceById.Count + 1;
                    attendance.RowVersion = RowVersion();
                    AttendanceById[attendance.AttendanceID] = attendance;
                });
            Repository.Setup(repository => repository.AddOperation(It.IsAny<AttendanceOperation>()))
                .Callback((AttendanceOperation operation) => Operations[operation.OperationID] = operation);
            Repository.Setup(repository => repository.AddAdjustment(It.IsAny<AttendanceAdjustment>()))
                .Callback((AttendanceAdjustment adjustment) => Adjustments.Add(adjustment));
            Repository.Setup(repository => repository.AddAudit(It.IsAny<AuditLog>()))
                .Callback((AuditLog audit) => AtomicAudits.Add(audit));
            Repository.Setup(repository => repository.GetByIdAsync(
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, bool _, bool _, CancellationToken _) =>
                    AttendanceById.GetValueOrDefault(id));
            Repository.Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            Service = new AttendanceService(
                Repository.Object,
                Audit.Object,
                HttpContext.Object,
                Clock.Object,
                Timezone.Object,
                CurrentUser.Object,
                ProjectionVersion.Object,
                Logger);
        }

        public Mock<IAttendanceRepository> Repository { get; }
        public Mock<IAuditService> Audit { get; }
        public Mock<IClockService> Clock { get; }
        public Mock<ITimezoneService> Timezone { get; }
        public Mock<ICurrentUserContext> CurrentUser { get; }
        public Mock<IProjectionVersionProvider> ProjectionVersion { get; }
        public Mock<IHttpContextAccessor> HttpContext { get; }
        public RecordingLogger<AttendanceService> Logger { get; }
        public AttendanceService Service { get; }
        public Dictionary<int, Attendance> AttendanceById { get; } = new();
        public Dictionary<Guid, AttendanceOperation> Operations { get; } = new();
        public List<AttendanceAdjustment> Adjustments { get; } = new();
        public List<AuditLog> AtomicAudits { get; } = new();
        public DateTime Now { get; set; } = new(2026, 7, 12, 4, 0, 0, DateTimeKind.Utc);
        public DateOnly GymDate { get; } = new(2026, 7, 12);
        public DateOnly AlternateGymDate { get; } = new(2026, 7, 11);
        public string TimeZoneId { get; set; } = "Asia/Manila";
        public TaskCompletionSource? TimezoneReadEntered { get; set; }
        public TaskCompletionSource? ContinueTimezoneRead { get; set; }
        public int ActorUserId { get; set; } = 5;
        public int MemberId { get; set; } = 10;

        public async Task<bool> TryUpdateTimezoneAsync(string timeZoneId)
        {
            await _transactionGate.WaitAsync();
            try
            {
                if (AttendanceById.Count != 0)
                {
                    return false;
                }

                TimeZoneId = timeZoneId;
                return true;
            }
            finally
            {
                _transactionGate.Release();
            }
        }

        public Attendance AddExistingAttendance(
            int attendanceId = 1,
            DateTime? checkIn = null,
            DateOnly? attendanceDate = null)
        {
            var attendance = new Attendance
            {
                AttendanceID = attendanceId,
                MemberID = MemberId,
                AttendanceDate = attendanceDate ?? GymDate,
                CheckInTime = checkIn ?? Now.AddHours(-1),
                Source = Attendance.StaffQrSource,
                ActorUserID = ActorUserId,
                RowVersion = RowVersion(),
                LastModified = checkIn ?? Now.AddHours(-1)
            };
            AttendanceById[attendanceId] = attendance;
            return attendance;
        }

        public void SetupTrackedAttendance(Attendance attendance)
        {
            Repository.Setup(repository => repository.GetByIdAsync(
                    attendance.AttendanceID,
                    true,
                    true,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(attendance);
        }

        public void SetupAttendanceLookup(Attendance attendance)
        {
            Repository.Setup(repository => repository.GetByIdAsync(
                    attendance.AttendanceID,
                    true,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(attendance);
        }

        private static byte[] RowVersion()
        {
            return new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
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
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
        }
    }
}
