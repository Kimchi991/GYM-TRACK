using GymTrackPro.API.Data;
using GymTrackPro.API.Repositories;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.Tests;

public class AttendanceRepositoryTests
{
    [Fact]
    public async Task History_page_applies_range_order_skip_and_take_in_provider_query()
    {
        await using var context = CreateContext();
        var start = Utc(2026, 1, 1);
        for (var index = 0; index < 250; index++)
        {
            context.AttendanceLogs.Add(new Attendance
            {
                MemberID = 1,
                AttendanceDate = DateOnly.FromDateTime(start.AddDays(index)),
                CheckInTime = start.AddDays(index),
                Source = Attendance.StaffQrSource,
                ActorUserID = 1,
                LastModified = start.AddDays(index)
            });
        }

        await context.SaveChangesAsync();
        var repository = new AttendanceRepository(context);

        var result = await repository.GetHistoryPageAsync(
            1,
            DateOnly.FromDateTime(start),
            DateOnly.FromDateTime(start.AddDays(250)),
            page: 4,
            pageSize: 25);

        Assert.Equal(250, result.TotalCount);
        Assert.Equal(25, result.Items.Count);
        Assert.Equal(start.AddDays(174), result.Items[0].CheckInTime);
        Assert.Equal(start.AddDays(150), result.Items[^1].CheckInTime);
    }

    [Fact]
    public async Task History_page_uses_attendance_id_descending_to_break_tied_checkin_times()
    {
        await using var context = CreateContext();
        var checkIn = Utc(2026, 7, 12);
        for (var attendanceId = 1; attendanceId <= 4; attendanceId++)
        {
            context.AttendanceLogs.Add(new Attendance
            {
                AttendanceID = attendanceId,
                MemberID = 1,
                AttendanceDate = new DateOnly(2026, 7, 12),
                CheckInTime = checkIn,
                Source = Attendance.HistoricalImportSource,
                LastModified = checkIn
            });
        }

        await context.SaveChangesAsync();
        var repository = new AttendanceRepository(context);

        var firstPage = await repository.GetHistoryPageAsync(
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 7, 13),
            page: 1,
            pageSize: 2);
        var secondPage = await repository.GetHistoryPageAsync(
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 7, 13),
            page: 2,
            pageSize: 2);

        Assert.Equal(new[] { 4, 3 }, firstPage.Items.Select(item => item.AttendanceID));
        Assert.Equal(new[] { 2, 1 }, secondPage.Items.Select(item => item.AttendanceID));
    }

    [Fact]
    public async Task Member_qr_lookup_hides_deleted_and_inactive_members()
    {
        await using var context = CreateContext();
        context.Members.AddRange(
            Member(1, "active", "Active"),
            Member(2, "deleted", "Active", isDeleted: true),
            Member(3, "inactive", "Inactive"));
        await context.SaveChangesAsync();
        var repository = new AttendanceRepository(context);

        Assert.NotNull(await repository.GetActiveMemberByQrCodeAsync("active"));
        Assert.Null(await repository.GetActiveMemberByQrCodeAsync("deleted"));
        Assert.Null(await repository.GetActiveMemberByQrCodeAsync("inactive"));
    }

    [Fact]
    public async Task Membership_boundaries_distinguish_active_future_expired_and_paused()
    {
        await using var context = CreateContext();
        var date = new DateOnly(2026, 7, 12);
        context.Subscriptions.AddRange(
            Subscription(1, 1, date.AddDays(-1), date, "Active"),
            Subscription(2, 2, date.AddDays(1), date.AddDays(30), "Active"),
            Subscription(3, 3, date.AddDays(-30), date.AddDays(-1), "Active"),
            Subscription(4, 4, date.AddDays(-1), date.AddDays(30), "Paused"),
            Subscription(5, 5, date.AddDays(-1), date.AddDays(30), "Active"),
            Subscription(6, 6, date.AddDays(-1), date.AddDays(60), "Paused"),
            Subscription(7, 6, date.AddDays(-1), date.AddDays(10), "Active"),
            Subscription(8, 7, date.AddDays(-1), date.AddDays(30), "Active"));
        context.MembershipPauses.AddRange(
            new MembershipPause
            {
                SubscriptionID = 5,
                PauseStartDate = Utc(2026, 7, 10),
                PauseEndDate = Utc(2026, 7, 11),
                Reason = "Closed pause"
            },
            new MembershipPause
            {
                SubscriptionID = 8,
                PauseStartDate = Utc(2026, 7, 12),
                Reason = "Open pause"
            });
        await context.SaveChangesAsync();
        var repository = new AttendanceRepository(context);

        Assert.Equal(AttendanceMembershipState.Active, await repository.GetMembershipStateAsync(1, date));
        Assert.Equal(AttendanceMembershipState.Inactive, await repository.GetMembershipStateAsync(2, date));
        Assert.Equal(AttendanceMembershipState.Inactive, await repository.GetMembershipStateAsync(3, date));
        Assert.Equal(AttendanceMembershipState.Paused, await repository.GetMembershipStateAsync(4, date));
        Assert.Equal(AttendanceMembershipState.Active, await repository.GetMembershipStateAsync(5, date));
        var anomaly = await repository.GetMembershipSnapshotAsync(6, date);
        Assert.Equal(AttendanceMembershipState.Active, anomaly.State);
        Assert.Equal(7, anomaly.SubscriptionId);
        Assert.Equal(date.AddDays(10).ToDateTime(TimeOnly.MinValue), anomaly.ExpiryDate);
        Assert.Equal(AttendanceMembershipState.Paused, await repository.GetMembershipStateAsync(7, date));
    }

    [Fact]
    public async Task Terminal_operation_verification_requires_exact_immutable_commit_key()
    {
        await using var context = CreateContext();
        var fingerprint = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var operationId = Guid.NewGuid();
        var key = new AttendanceOperationCommitKey(
            operationId,
            5,
            AttendanceOperationType.StaffCheckIn,
            fingerprint);
        fingerprint[0] = 255;
        context.Set<AttendanceOperation>().Add(new AttendanceOperation
        {
            OperationID = operationId,
            ActorUserID = 5,
            OperationType = AttendanceOperationType.StaffCheckIn,
            RequestFingerprint = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(),
            TargetAttendanceID = 10,
            OriginalHttpStatus = 201,
            OriginalResultCode = "ATTENDANCE_CHECKED_IN",
            State = AttendanceOperationState.Completed,
            CreatedAtUtc = Utc(2026, 7, 12),
            CompletedAtUtc = Utc(2026, 7, 12)
        });
        await context.SaveChangesAsync();
        var repository = new AttendanceRepository(context);

        Assert.True(await repository.VerifyTerminalOperationAsync(key));
        Assert.False(await repository.VerifyTerminalOperationAsync(
            new AttendanceOperationCommitKey(
                operationId,
                6,
                AttendanceOperationType.StaffCheckIn,
                key.RequestFingerprint.Span)));
    }

    [Fact]
    public void Structured_mutation_source_uses_execute_in_transaction_positive_verification()
    {
        var root = FindWorkspaceRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GymTrackPro.API",
            "Repositories",
            "AttendanceRepository.cs"));
        var start = source.IndexOf(
            "public async Task<TResult> ExecuteVerifiedMutationAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "public async Task<bool> VerifyTerminalOperationAsync",
            start,
            StringComparison.Ordinal);
        var method = source[start..end];

        Assert.Contains("ExecuteInTransactionAsync", method, StringComparison.Ordinal);
        Assert.Contains("VerifyTerminalOperationAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RollbackAsync", method, StringComparison.Ordinal);
        Assert.NotNull(typeof(AttendanceRepository).GetMethod(
            nameof(AttendanceRepository.VerifyTerminalOperationAsync)));
    }

    [Fact]
    public async Task Provider_neutral_transaction_executes_once_but_does_not_prove_sql_locking()
    {
        await using var context = CreateContext();
        var repository = new AttendanceRepository(context);
        var invocationCount = 0;

        var result = await repository.ExecuteSerializableAsync(_ =>
        {
            invocationCount++;
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.Equal(1, invocationCount);
        Assert.False(context.Database.IsRelational());
    }

    private static GymDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new GymDbContext(options);
    }

    private static Member Member(int id, string qr, string status, bool isDeleted = false)
    {
        return new Member
        {
            MemberID = id,
            QRCode = qr,
            Status = status,
            IsDeleted = isDeleted
        };
    }

    private static Subscription Subscription(
        int id,
        int memberId,
        DateOnly start,
        DateOnly end,
        string status)
    {
        return new Subscription
        {
            SubscriptionID = id,
            MemberID = memberId,
            PlanID = 1,
            StartDate = start.ToDateTime(TimeOnly.MinValue),
            EndDate = end.ToDateTime(TimeOnly.MinValue),
            Status = status
        };
    }

    private static DateTime Utc(int year, int month, int day)
    {
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static string FindWorkspaceRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var candidate = new DirectoryInfo(
            Path.GetDirectoryName(sourceFilePath)
                ?? throw new DirectoryNotFoundException("Test source directory is unavailable."));
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "src", "GymTrackPro.slnx"))
                && Directory.Exists(Path.Combine(candidate.FullName, "src", "GymTrackPro.API")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new DirectoryNotFoundException("Workspace root could not be located.");
    }
}
