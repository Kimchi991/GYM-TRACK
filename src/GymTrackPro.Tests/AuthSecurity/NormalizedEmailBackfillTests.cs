using System.Globalization;
using GymTrackPro.API.Authentication;
using GymTrackPro.NormalizedEmailBackfill;
using Microsoft.Data.SqlClient;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class NormalizedEmailBackfillTests
{
    private const string ValidConnection =
        "Server=backfill-private-source;Database=backfill-private-catalog;" +
        "User Id=backfill-user;Password=backfill-password;" +
        "Encrypt=True;TrustServerCertificate=False";
    private static readonly string ValidFingerprint = new('A', 64);

    [Fact]
    public void Parser_defaults_to_read_only_dry_run_and_requires_fingerprint_for_confirmation()
    {
        Assert.True(NormalizedEmailBackfillCommandParser.TryParse(
            Array.Empty<string>(),
            EnvironmentWithConnection,
            out var dryRun));
        Assert.Equal(NormalizedEmailBackfillMode.DryRun, dryRun!.Mode);
        Assert.Null(dryRun.ExpectedFingerprint);
        Assert.Equal(
            ApplicationIntent.ReadOnly,
            new SqlConnectionStringBuilder(dryRun.ConnectionString).ApplicationIntent);

        Assert.True(NormalizedEmailBackfillCommandParser.TryParse(
            new[] { "--confirm", ValidFingerprint.ToLowerInvariant(), "--batch-size", "17" },
            EnvironmentWithConnection,
            out var confirmed));
        Assert.Equal(NormalizedEmailBackfillMode.Confirm, confirmed!.Mode);
        Assert.Equal(ValidFingerprint, confirmed.ExpectedFingerprint);
        Assert.Equal(17, confirmed.BatchSize);
        Assert.Equal(
            ApplicationIntent.ReadWrite,
            new SqlConnectionStringBuilder(confirmed.ConnectionString).ApplicationIntent);
    }

    [Theory]
    [InlineData("--connection-string")]
    [InlineData("--confirm")]
    [InlineData("--confirm malformed")]
    [InlineData("--batch-size 0")]
    [InlineData("--batch-size 501")]
    public void Parser_rejects_connection_switch_missing_or_malformed_fingerprint_and_unbounded_batches(
        string commandLine)
    {
        Assert.False(NormalizedEmailBackfillCommandParser.TryParse(
            commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            EnvironmentWithConnection,
            out _));
    }

    [Fact]
    public void Parser_rejects_duplicate_confirmation_gate()
    {
        Assert.False(NormalizedEmailBackfillCommandParser.TryParse(
            new[] { "--confirm", ValidFingerprint, "--confirm", ValidFingerprint },
            EnvironmentWithConnection,
            out _));
    }

    [Theory]
    [InlineData("Server=db;Database=Gym;Encrypt=True")]
    [InlineData("Server=db;Database=Gym;TrustServerCertificate=False")]
    public void Parser_rejects_connection_strings_that_rely_on_transport_defaults(
        string connectionString)
    {
        Assert.False(NormalizedEmailBackfillCommandParser.TryParse(
            Array.Empty<string>(),
            key => key == NormalizedEmailBackfillCommand.ConnectionStringEnvironmentVariable
                ? connectionString
                : null,
            out var command));
        Assert.Null(command);
    }

    [Fact]
    public void Parser_fails_closed_when_environment_provider_throws()
    {
        Assert.False(NormalizedEmailBackfillCommandParser.TryParse(
            Array.Empty<string>(),
            _ => throw new InvalidOperationException("private-provider-value"),
            out var command));
        Assert.Null(command);
        Assert.DoesNotContain(
            "private-provider-value",
            NormalizedEmailBackfillCommandOutput.Rejected,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyzer_uses_FormKC_invariant_uppercase_and_culture_stable_ordered_fingerprint()
    {
        var turkish = await AnalyzeUnderCultureAsync("tr-TR", batchSize: 1);
        var english = await AnalyzeUnderCultureAsync("en-US", batchSize: 2);

        Assert.Equal(2, turkish.ScannedCount);
        Assert.Equal(1, turkish.CollisionGroupCount);
        Assert.Equal(2, turkish.CollisionRowCount);
        Assert.Equal(english.SnapshotFingerprint, turkish.SnapshotFingerprint);
        Assert.True(NormalizedEmailBackfillFingerprint.TryNormalize(
            turkish.SnapshotFingerprint,
            out _));
        Assert.True(EmailNormalization.TryCanonicalize(
            "\uFF49@example.test",
            out _,
            out var normalized));
        Assert.Equal("I@EXAMPLE.TEST", normalized);
    }

    [Fact]
    public async Task Dry_run_emits_candidate_fingerprint_and_never_calls_mutating_store_path()
    {
        var store = new InMemoryBackfillStore(
            Row(1, "one@example.test"),
            Row(2, "two@example.test"));

        var result = await new NormalizedEmailBackfillService(store).ExecuteAsync(
            NormalizedEmailBackfillMode.DryRun,
            batchSize: 1);
        var output = NormalizedEmailBackfillCommandOutput.Format(result);

        Assert.Equal(0, store.ApplyCallCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(2, result.InitialAnalysis.PendingCount);
        Assert.Contains(
            $"FINGERPRINT={result.InitialAnalysis.SnapshotFingerprint}",
            output,
            StringComparison.Ordinal);
        Assert.Null(store.GetNormalizedEmail(1));
        Assert.Null(store.GetNormalizedEmail(2));
    }

    [Fact]
    public async Task Invalid_source_and_existing_mismatch_block_matching_confirmation_before_any_write()
    {
        var store = new InMemoryBackfillStore(
            Row(1, "contains whitespace@example.test"),
            Row(2, "valid@example.test", "WRONG@EXAMPLE.TEST"));
        var service = new NormalizedEmailBackfillService(store);
        var dryRun = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.DryRun,
            batchSize: 2);

        var result = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.Confirm,
            batchSize: 2,
            dryRun.InitialAnalysis.SnapshotFingerprint);

        Assert.True(result.HasBlockingFindings);
        Assert.Equal(1, result.InitialAnalysis.InvalidSourceCount);
        Assert.Equal(1, result.InitialAnalysis.ExistingMismatchCount);
        Assert.Equal(0, store.ApplyCallCount);
        Assert.Equal(0, result.UpdatedCount);
    }

    [Fact]
    public async Task Changed_snapshot_rejects_stale_fingerprint_without_writes()
    {
        var store = new InMemoryBackfillStore(Row(1, "before@example.test"));
        var service = new NormalizedEmailBackfillService(store);
        var dryRun = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.DryRun,
            batchSize: 1);
        store.SetEmail(1, "after@example.test");

        var result = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.Confirm,
            batchSize: 1,
            dryRun.InitialAnalysis.SnapshotFingerprint);

        Assert.True(result.SnapshotMismatchDetected);
        Assert.True(result.HasBlockingFindings);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(0, store.ApplyCallCount);
        Assert.Null(store.GetNormalizedEmail(1));
    }

    [Fact]
    public async Task Service_rejects_missing_or_malformed_confirm_fingerprint_before_store_access()
    {
        var store = new InMemoryBackfillStore(Row(1, "one@example.test"));
        var service = new NormalizedEmailBackfillService(store);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(
            NormalizedEmailBackfillMode.Confirm,
            batchSize: 1,
            expectedFingerprint: null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(
            NormalizedEmailBackfillMode.Confirm,
            batchSize: 1,
            expectedFingerprint: "malformed"));

        Assert.Equal(0, store.ReadCallCount);
        Assert.Equal(0, store.ApplyCallCount);
    }

    [Fact]
    public async Task Confirmed_batches_require_new_snapshot_after_partial_progress_and_are_idempotent()
    {
        var store = new InMemoryBackfillStore(
            Row(1, "one@example.test"),
            Row(2, "two@example.test"),
            Row(3, "three@example.test"),
            Row(4, "four@example.test"))
        {
            ConflictOnApplyCall = 2
        };
        var service = new NormalizedEmailBackfillService(store);
        var initialDryRun = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.DryRun,
            batchSize: 2);

        var interrupted = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.Confirm,
            batchSize: 2,
            initialDryRun.InitialAnalysis.SnapshotFingerprint);

        Assert.True(interrupted.HasBlockingFindings);
        Assert.True(interrupted.ConcurrentConflictDetected);
        Assert.Equal(2, interrupted.UpdatedCount);
        Assert.Equal("ONE@EXAMPLE.TEST", store.GetNormalizedEmail(1));
        Assert.Null(store.GetNormalizedEmail(3));

        var callsBeforeStaleAttempt = store.ApplyCallCount;
        var staleAttempt = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.Confirm,
            batchSize: 2,
            initialDryRun.InitialAnalysis.SnapshotFingerprint);
        Assert.True(staleAttempt.SnapshotMismatchDetected);
        Assert.Equal(callsBeforeStaleAttempt, store.ApplyCallCount);

        store.ConflictOnApplyCall = null;
        var resumedDryRun = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.DryRun,
            batchSize: 2);
        var resumed = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.Confirm,
            batchSize: 2,
            resumedDryRun.InitialAnalysis.SnapshotFingerprint);
        var idempotentDryRun = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.DryRun,
            batchSize: 2);
        var idempotentRerun = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.Confirm,
            batchSize: 2,
            idempotentDryRun.InitialAnalysis.SnapshotFingerprint);

        Assert.True(resumed.Completed);
        Assert.Equal(2, resumed.UpdatedCount);
        Assert.Equal(0, resumed.FinalAnalysis!.PendingCount);
        Assert.True(idempotentRerun.Completed);
        Assert.Equal(0, idempotentRerun.UpdatedCount);
        Assert.All(store.ObservedBatchSizes, size => Assert.InRange(size, 1, 2));
    }

    [Fact]
    public void Output_and_command_string_are_count_only_and_redacted()
    {
        Assert.True(NormalizedEmailBackfillCommandParser.TryParse(
            Array.Empty<string>(),
            EnvironmentWithConnection,
            out var command));
        var analysis = new NormalizedEmailBackfillAnalysis(
            1,
            1,
            0,
            0,
            0,
            0,
            0,
            ValidFingerprint);
        var output = NormalizedEmailBackfillCommandOutput.Format(
            new NormalizedEmailBackfillResult(
                NormalizedEmailBackfillMode.DryRun,
                analysis,
                FinalAnalysis: null,
                UpdatedCount: 0,
                SnapshotMismatchDetected: false,
                ConcurrentConflictDetected: false));

        foreach (var sensitive in new[]
                 {
                     "backfill-private-source",
                     "backfill-private-catalog",
                     "backfill-password",
                     "person@example.test"
                 })
        {
            Assert.DoesNotContain(sensitive, output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sensitive, command!.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sensitive, NormalizedEmailBackfillCommandOutput.Rejected, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sensitive, NormalizedEmailBackfillCommandOutput.Failed, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Sql_store_source_preserves_bounded_serializable_snapshot_and_retry_contract()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspace.FindRoot(),
            "tools",
            "GymTrackPro.NormalizedEmailBackfill",
            "SqlServerNormalizedEmailBackfillStore.cs"));

        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("CreateExecutionStrategy", source, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Clear", source, StringComparison.Ordinal);
        Assert.Contains("PreStateFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("PostStateFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("EmailNormalization.TryCanonicalize", source, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", source, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<NormalizedEmailBackfillAnalysis> AnalyzeUnderCultureAsync(
        string cultureName,
        int batchSize)
    {
        var priorCulture = CultureInfo.CurrentCulture;
        var priorUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var store = new InMemoryBackfillStore(
                Row(1, "\uFF49@example.test"),
                Row(2, "I@EXAMPLE.TEST"));
            return await new NormalizedEmailBackfillAnalyzer(store)
                .AnalyzeAsync(batchSize);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    private static string? EnvironmentWithConnection(string key) =>
        key == NormalizedEmailBackfillCommand.ConnectionStringEnvironmentVariable
            ? ValidConnection
            : null;

    private static NormalizedEmailBackfillRow Row(
        int id,
        string email,
        string? normalizedEmail = null) =>
        new(id, email, normalizedEmail);

    private sealed class InMemoryBackfillStore : INormalizedEmailBackfillStore
    {
        private readonly List<MutableRow> _rows;

        public InMemoryBackfillStore(params NormalizedEmailBackfillRow[] rows)
        {
            _rows = rows
                .Select(row => new MutableRow(row.UserId, row.Email, row.NormalizedEmail))
                .OrderBy(row => row.UserId)
                .ToList();
        }

        public int ReadCallCount { get; private set; }
        public int ApplyCallCount { get; private set; }
        public int? ConflictOnApplyCall { get; set; }
        public List<int> ObservedBatchSizes { get; } = new();

        public Task<IReadOnlyList<NormalizedEmailBackfillRow>> ReadBatchAsync(
            int afterUserId,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            ReadCallCount++;
            IReadOnlyList<NormalizedEmailBackfillRow> rows = _rows
                .Where(row => row.UserId > afterUserId)
                .Take(batchSize)
                .Select(row => new NormalizedEmailBackfillRow(
                    row.UserId,
                    row.Email,
                    row.NormalizedEmail))
                .ToArray();
            return Task.FromResult(rows);
        }

        public Task<NormalizedEmailBackfillBatchResult> ApplyNextBatchAsync(
            int afterUserId,
            int batchSize,
            IReadOnlyList<NormalizedEmailBackfillExpectedRow> expectedRows,
            CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            ObservedBatchSizes.Add(batchSize);
            var rows = _rows
                .Where(row => row.UserId > afterUserId)
                .Take(batchSize)
                .ToArray();
            if (ConflictOnApplyCall == ApplyCallCount
                || rows.Length != expectedRows.Count
                || rows.Where((row, index) => row.UserId != expectedRows[index].UserId).Any())
            {
                return Task.FromResult(Conflict(afterUserId, rows.Length));
            }

            var candidates = new List<(MutableRow Row, string NormalizedEmail)>();
            foreach (var row in rows)
            {
                if (!EmailNormalization.TryCanonicalize(
                        row.Email,
                        out _,
                        out var normalizedEmail)
                    || (row.NormalizedEmail is not null
                        && !string.Equals(
                            row.NormalizedEmail,
                            normalizedEmail,
                            StringComparison.Ordinal)))
                {
                    return Task.FromResult(Conflict(afterUserId, rows.Length));
                }

                candidates.Add((row, normalizedEmail));
            }

            var updated = 0;
            foreach (var target in candidates.Where(target => target.Row.NormalizedEmail is null))
            {
                target.Row.NormalizedEmail = target.NormalizedEmail;
                updated++;
            }

            return Task.FromResult(new NormalizedEmailBackfillBatchResult(
                expectedRows.Count == 0 ? afterUserId : expectedRows[^1].UserId,
                rows.Length,
                updated,
                IsComplete: expectedRows.Count < batchSize,
                ConcurrentConflictDetected: false));
        }

        public string? GetNormalizedEmail(int userId) =>
            _rows.Single(row => row.UserId == userId).NormalizedEmail;

        public void SetEmail(int userId, string email) =>
            _rows.Single(row => row.UserId == userId).Email = email;

        private static NormalizedEmailBackfillBatchResult Conflict(
            int cursor,
            int scanned) =>
            new(
                cursor,
                scanned,
                UpdatedCount: 0,
                IsComplete: false,
                ConcurrentConflictDetected: true);

        private sealed class MutableRow
        {
            public MutableRow(int userId, string? email, string? normalizedEmail)
            {
                UserId = userId;
                Email = email;
                NormalizedEmail = normalizedEmail;
            }

            public int UserId { get; }
            public string? Email { get; set; }
            public string? NormalizedEmail { get; set; }
        }
    }
}
