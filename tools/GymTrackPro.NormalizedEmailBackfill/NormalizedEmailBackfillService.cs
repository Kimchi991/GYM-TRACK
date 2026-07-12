using GymTrackPro.API.Authentication;

namespace GymTrackPro.NormalizedEmailBackfill;

public sealed record NormalizedEmailBackfillRow(
    int UserId,
    string? Email,
    string? NormalizedEmail);

public sealed record NormalizedEmailBackfillExpectedRow(
    int UserId,
    string PreStateFingerprint,
    string PostStateFingerprint);

public sealed record NormalizedEmailBackfillAnalysis(
    long ScannedCount,
    long PendingCount,
    long AlreadyCanonicalCount,
    long InvalidSourceCount,
    long ExistingMismatchCount,
    long CollisionGroupCount,
    long CollisionRowCount,
    string SnapshotFingerprint)
{
    public bool HasBlockingFindings =>
        InvalidSourceCount > 0
        || ExistingMismatchCount > 0
        || CollisionGroupCount > 0;
}

public sealed record NormalizedEmailBackfillBatchResult(
    int NextCursor,
    int ScannedCount,
    int UpdatedCount,
    bool IsComplete,
    bool ConcurrentConflictDetected);

public sealed record NormalizedEmailBackfillResult(
    NormalizedEmailBackfillMode Mode,
    NormalizedEmailBackfillAnalysis InitialAnalysis,
    NormalizedEmailBackfillAnalysis? FinalAnalysis,
    long UpdatedCount,
    bool SnapshotMismatchDetected,
    bool ConcurrentConflictDetected)
{
    private NormalizedEmailBackfillAnalysis EffectiveAnalysis =>
        FinalAnalysis ?? InitialAnalysis;

    public bool HasBlockingFindings =>
        EffectiveAnalysis.HasBlockingFindings
        || SnapshotMismatchDetected
        || ConcurrentConflictDetected
        || (Mode == NormalizedEmailBackfillMode.Confirm
            && FinalAnalysis is not null
            && FinalAnalysis.PendingCount > 0);

    public bool Completed =>
        Mode == NormalizedEmailBackfillMode.Confirm
        && FinalAnalysis is not null
        && !HasBlockingFindings
        && FinalAnalysis.PendingCount == 0;
}

public interface INormalizedEmailBackfillStore
{
    Task<IReadOnlyList<NormalizedEmailBackfillRow>> ReadBatchAsync(
        int afterUserId,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<NormalizedEmailBackfillBatchResult> ApplyNextBatchAsync(
        int afterUserId,
        int batchSize,
        IReadOnlyList<NormalizedEmailBackfillExpectedRow> expectedRows,
        CancellationToken cancellationToken = default);
}

internal sealed record NormalizedEmailBackfillSnapshot(
    NormalizedEmailBackfillAnalysis Analysis,
    IReadOnlyList<NormalizedEmailBackfillExpectedRow> ExpectedRows,
    string ExpectedFinalFingerprint);

public sealed class NormalizedEmailBackfillAnalyzer
{
    private readonly INormalizedEmailBackfillStore _store;

    public NormalizedEmailBackfillAnalyzer(INormalizedEmailBackfillStore store)
    {
        _store = store;
    }

    public async Task<NormalizedEmailBackfillAnalysis> AnalyzeAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        (await CaptureSnapshotAsync(batchSize, cancellationToken)).Analysis;

    internal async Task<NormalizedEmailBackfillSnapshot> CaptureSnapshotAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        RequireBatchSize(batchSize);
        long scanned = 0;
        long pending = 0;
        long canonical = 0;
        long invalid = 0;
        long mismatch = 0;
        var derivedCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var expectedRows = new List<NormalizedEmailBackfillExpectedRow>();
        var cursor = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await _store.ReadBatchAsync(cursor, batchSize, cancellationToken);
            ValidateReadBatch(rows, cursor, batchSize);
            if (rows.Count == 0)
            {
                break;
            }

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                var isValid = EmailNormalization.TryCanonicalize(
                    row.Email,
                    out _,
                    out var expectedNormalizedEmail);
                var derivedNormalizedEmail = isValid
                    ? expectedNormalizedEmail
                    : null;
                var postNormalizedEmail = isValid && row.NormalizedEmail is null
                    ? expectedNormalizedEmail
                    : row.NormalizedEmail;
                expectedRows.Add(new NormalizedEmailBackfillExpectedRow(
                    row.UserId,
                    NormalizedEmailBackfillFingerprint.ComputeRowState(
                        row.UserId,
                        row.Email,
                        row.NormalizedEmail,
                        derivedNormalizedEmail),
                    NormalizedEmailBackfillFingerprint.ComputeRowState(
                        row.UserId,
                        row.Email,
                        postNormalizedEmail,
                        derivedNormalizedEmail)));

                if (!isValid)
                {
                    invalid++;
                    continue;
                }

                derivedCounts[expectedNormalizedEmail] =
                    derivedCounts.GetValueOrDefault(expectedNormalizedEmail) + 1;
                if (row.NormalizedEmail is null)
                {
                    pending++;
                }
                else if (string.Equals(
                             row.NormalizedEmail,
                             expectedNormalizedEmail,
                             StringComparison.Ordinal))
                {
                    canonical++;
                }
                else
                {
                    mismatch++;
                }
            }

            cursor = rows[^1].UserId;
            if (rows.Count < batchSize)
            {
                break;
            }
        }

        var collisions = derivedCounts.Values.Where(count => count > 1).ToArray();
        var snapshotFingerprint = NormalizedEmailBackfillFingerprint.ComputeSnapshot(
            expectedRows,
            usePostState: false);
        var analysis = new NormalizedEmailBackfillAnalysis(
            scanned,
            pending,
            canonical,
            invalid,
            mismatch,
            collisions.LongLength,
            collisions.Sum(),
            snapshotFingerprint);
        return new NormalizedEmailBackfillSnapshot(
            analysis,
            expectedRows,
            NormalizedEmailBackfillFingerprint.ComputeSnapshot(
                expectedRows,
                usePostState: true));
    }

    internal static void RequireBatchSize(int batchSize)
    {
        if (batchSize is < 1 or > NormalizedEmailBackfillCommand.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
    }

    private static void ValidateReadBatch(
        IReadOnlyList<NormalizedEmailBackfillRow> rows,
        int cursor,
        int batchSize)
    {
        if (rows.Count > batchSize)
        {
            throw new InvalidOperationException("Backfill store exceeded the bounded batch size.");
        }

        foreach (var row in rows)
        {
            if (row.UserId <= cursor)
            {
                throw new InvalidOperationException("Backfill store returned a non-progressing keyset batch.");
            }

            cursor = row.UserId;
        }
    }
}

public sealed class NormalizedEmailBackfillService
{
    private readonly INormalizedEmailBackfillStore _store;
    private readonly NormalizedEmailBackfillAnalyzer _analyzer;

    public NormalizedEmailBackfillService(INormalizedEmailBackfillStore store)
    {
        _store = store;
        _analyzer = new NormalizedEmailBackfillAnalyzer(store);
    }

    public async Task<NormalizedEmailBackfillResult> ExecuteAsync(
        NormalizedEmailBackfillMode mode,
        int batchSize,
        string? expectedFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        NormalizedEmailBackfillAnalyzer.RequireBatchSize(batchSize);
        if (mode is not (NormalizedEmailBackfillMode.DryRun
            or NormalizedEmailBackfillMode.Confirm)
            || (mode == NormalizedEmailBackfillMode.DryRun
                && expectedFingerprint is not null)
            || (mode == NormalizedEmailBackfillMode.Confirm
                && !NormalizedEmailBackfillFingerprint.TryNormalize(
                    expectedFingerprint,
                    out _)))
        {
            throw new ArgumentException("Invalid normalized-email backfill execution gate.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await _analyzer.CaptureSnapshotAsync(batchSize, cancellationToken);
        if (mode == NormalizedEmailBackfillMode.DryRun)
        {
            return Result(
                mode,
                snapshot.Analysis,
                final: null,
                updated: 0,
                snapshotMismatch: false,
                concurrentConflict: false);
        }

        NormalizedEmailBackfillFingerprint.TryNormalize(
            expectedFingerprint,
            out var normalizedExpectedFingerprint);
        var snapshotMismatch = !string.Equals(
            snapshot.Analysis.SnapshotFingerprint,
            normalizedExpectedFingerprint,
            StringComparison.Ordinal);
        if (snapshotMismatch || snapshot.Analysis.HasBlockingFindings)
        {
            return Result(
                mode,
                snapshot.Analysis,
                final: null,
                updated: 0,
                snapshotMismatch,
                concurrentConflict: false);
        }

        long updated = 0;
        var cursor = 0;
        var offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedBatch = snapshot.ExpectedRows
                .Skip(offset)
                .Take(batchSize)
                .ToArray();
            var batch = await _store.ApplyNextBatchAsync(
                cursor,
                batchSize,
                expectedBatch,
                cancellationToken);
            ValidateApplyResult(batch, cursor, batchSize, expectedBatch);
            updated += batch.UpdatedCount;

            if (batch.ConcurrentConflictDetected)
            {
                return Result(
                    mode,
                    snapshot.Analysis,
                    final: null,
                    updated,
                    snapshotMismatch: false,
                    concurrentConflict: true);
            }

            offset += expectedBatch.Length;
            if (batch.IsComplete)
            {
                break;
            }

            cursor = expectedBatch[^1].UserId;
        }

        var finalSnapshot = await _analyzer.CaptureSnapshotAsync(
            batchSize,
            cancellationToken);
        var finalStateChanged = !string.Equals(
            finalSnapshot.Analysis.SnapshotFingerprint,
            snapshot.ExpectedFinalFingerprint,
            StringComparison.Ordinal);
        return Result(
            mode,
            snapshot.Analysis,
            finalSnapshot.Analysis,
            updated,
            snapshotMismatch: false,
            concurrentConflict: finalStateChanged);
    }

    private static NormalizedEmailBackfillResult Result(
        NormalizedEmailBackfillMode mode,
        NormalizedEmailBackfillAnalysis initial,
        NormalizedEmailBackfillAnalysis? final,
        long updated,
        bool snapshotMismatch,
        bool concurrentConflict) =>
        new(
            mode,
            initial,
            final,
            updated,
            snapshotMismatch,
            concurrentConflict);

    private static void ValidateApplyResult(
        NormalizedEmailBackfillBatchResult result,
        int cursor,
        int batchSize,
        IReadOnlyList<NormalizedEmailBackfillExpectedRow> expectedRows)
    {
        var expectedComplete = expectedRows.Count < batchSize;
        var expectedCursor = expectedRows.Count == 0
            ? cursor
            : expectedRows[^1].UserId;
        if (result.ScannedCount is < 0
            || result.ScannedCount > batchSize
            || result.UpdatedCount is < 0
            || result.UpdatedCount > result.ScannedCount
            || (!result.ConcurrentConflictDetected
                && (result.ScannedCount != expectedRows.Count
                    || result.NextCursor != expectedCursor
                    || result.IsComplete != expectedComplete)))
        {
            throw new InvalidOperationException("Backfill store returned an invalid batch result.");
        }
    }
}
