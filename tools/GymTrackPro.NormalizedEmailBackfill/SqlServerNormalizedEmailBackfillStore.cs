using System.Data;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.API.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.NormalizedEmailBackfill;

public sealed class SqlServerNormalizedEmailBackfillStore : INormalizedEmailBackfillStore
{
    private readonly GymDbContext _dbContext;

    public SqlServerNormalizedEmailBackfillStore(GymDbContext dbContext)
    {
        _dbContext = dbContext;
        if (!string.Equals(
                dbContext.Database.ProviderName,
                SqlServerConnectionPolicy.ProviderInvariantName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Normalized-email backfill requires SQL Server.");
        }
    }

    public async Task<IReadOnlyList<NormalizedEmailBackfillRow>> ReadBatchAsync(
        int afterUserId,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        NormalizedEmailBackfillAnalyzer.RequireBatchSize(batchSize);
        return await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.UserID > afterUserId)
            .OrderBy(user => user.UserID)
            .Select(user => new NormalizedEmailBackfillRow(
                user.UserID,
                user.Email,
                user.NormalizedEmail))
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<NormalizedEmailBackfillBatchResult> ApplyNextBatchAsync(
        int afterUserId,
        int batchSize,
        IReadOnlyList<NormalizedEmailBackfillExpectedRow> expectedRows,
        CancellationToken cancellationToken = default)
    {
        NormalizedEmailBackfillAnalyzer.RequireBatchSize(batchSize);
        ValidateExpectedRows(afterUserId, batchSize, expectedRows);
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async () => await ApplyNextBatchOnceAsync(
                afterUserId,
                batchSize,
                expectedRows,
                cancellationToken));
    }

    private async Task<NormalizedEmailBackfillBatchResult> ApplyNextBatchOnceAsync(
        int afterUserId,
        int batchSize,
        IReadOnlyList<NormalizedEmailBackfillExpectedRow> expectedRows,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var users = await _dbContext.Users
                .FromSqlInterpolated($"SELECT TOP ({batchSize}) * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [UserID] > {afterUserId} ORDER BY [UserID]")
                .AsTracking()
                .ToListAsync(cancellationToken);

            if (users.Count != expectedRows.Count
                || users.Where((user, index) =>
                        user.UserID != expectedRows[index].UserId)
                    .Any())
            {
                await RollBackWithoutDisclosureAsync(transaction, cancellationToken);
                return new NormalizedEmailBackfillBatchResult(
                    afterUserId,
                    users.Count,
                    UpdatedCount: 0,
                    IsComplete: false,
                    ConcurrentConflictDetected: true);
            }

            var expectedInBatch = new HashSet<string>(StringComparer.Ordinal);
            var updated = 0;
            for (var index = 0; index < users.Count; index++)
            {
                var user = users[index];
                var expectedRow = expectedRows[index];
                if (!EmailNormalization.TryCanonicalize(
                        user.Email,
                        out _,
                        out var expectedNormalizedEmail)
                    || !expectedInBatch.Add(expectedNormalizedEmail))
                {
                    await RollBackWithoutDisclosureAsync(transaction, cancellationToken);
                    return new NormalizedEmailBackfillBatchResult(
                        afterUserId,
                        users.Count,
                        UpdatedCount: 0,
                        IsComplete: false,
                        ConcurrentConflictDetected: true);
                }

                var currentState = NormalizedEmailBackfillFingerprint.ComputeRowState(
                    user.UserID,
                    user.Email,
                    user.NormalizedEmail,
                    expectedNormalizedEmail);
                var matchesPreState = string.Equals(
                    currentState,
                    expectedRow.PreStateFingerprint,
                    StringComparison.Ordinal);
                var matchesPostState = string.Equals(
                    currentState,
                    expectedRow.PostStateFingerprint,
                    StringComparison.Ordinal);
                if (!matchesPreState && !matchesPostState)
                {
                    await RollBackWithoutDisclosureAsync(transaction, cancellationToken);
                    return new NormalizedEmailBackfillBatchResult(
                        afterUserId,
                        users.Count,
                        UpdatedCount: 0,
                        IsComplete: false,
                        ConcurrentConflictDetected: true);
                }

                if (matchesPreState
                    && !matchesPostState
                    && user.NormalizedEmail is null)
                {
                    user.NormalizedEmail = expectedNormalizedEmail;
                    updated++;
                }
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (IdentityDatabaseConflictClassifier.IsUniqueViolation(exception))
            {
                await RollBackWithoutDisclosureAsync(transaction, cancellationToken);
                return new NormalizedEmailBackfillBatchResult(
                    afterUserId,
                    users.Count,
                    UpdatedCount: 0,
                    IsComplete: false,
                    ConcurrentConflictDetected: true);
            }

            return new NormalizedEmailBackfillBatchResult(
                expectedRows.Count == 0
                    ? afterUserId
                    : expectedRows[^1].UserId,
                users.Count,
                updated,
                IsComplete: expectedRows.Count < batchSize,
                ConcurrentConflictDetected: false);
        }
        finally
        {
            // Every execution-strategy attempt must start without tracked values
            // from a rolled-back or unknown-commit transaction.
            _dbContext.ChangeTracker.Clear();
        }
    }

    private static void ValidateExpectedRows(
        int afterUserId,
        int batchSize,
        IReadOnlyList<NormalizedEmailBackfillExpectedRow> expectedRows)
    {
        if (expectedRows.Count > batchSize)
        {
            throw new ArgumentException("Expected rows exceed the bounded batch size.");
        }

        var cursor = afterUserId;
        foreach (var row in expectedRows)
        {
            if (row.UserId <= cursor
                || !NormalizedEmailBackfillFingerprint.TryNormalize(
                    row.PreStateFingerprint,
                    out _)
                || !NormalizedEmailBackfillFingerprint.TryNormalize(
                    row.PostStateFingerprint,
                    out _))
            {
                throw new ArgumentException("Expected row snapshot is invalid.");
            }

            cursor = row.UserId;
        }
    }

    private static async Task RollBackWithoutDisclosureAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch
        {
            // Disposal remains the final cleanup path. Do not surface provider
            // text because it may contain credentials or endpoint metadata.
        }
    }
}
