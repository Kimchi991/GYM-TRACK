using System.Globalization;
using GymTrackPro.API.Maintenance;

namespace GymTrackPro.NormalizedEmailBackfill;

public enum NormalizedEmailBackfillMode
{
    DryRun,
    Confirm
}

public sealed class NormalizedEmailBackfillCommand
{
    public const string ConnectionStringEnvironmentVariable =
        "GYMTRACKPRO_NORMALIZED_EMAIL_BACKFILL_CONNECTION_STRING";
    public const int DefaultBatchSize = 200;
    public const int MaximumBatchSize = 500;

    public required NormalizedEmailBackfillMode Mode { get; init; }
    public required int BatchSize { get; init; }
    public required string ConnectionString { get; init; }
    public string? ExpectedFingerprint { get; init; }

    public override string ToString() =>
        $"NormalizedEmailBackfillCommand Mode={Mode}; BatchSize={BatchSize}; Connection=[REDACTED]";
}

public static class NormalizedEmailBackfillCommandParser
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        Func<string, string?> getEnvironmentVariable,
        out NormalizedEmailBackfillCommand? command)
    {
        command = null;
        string? expectedFingerprint = null;
        var batchSize = NormalizedEmailBackfillCommand.DefaultBatchSize;
        var hasBatchSize = false;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--confirm" when expectedFingerprint is null && index + 1 < args.Count:
                    var candidateFingerprint = args[++index];
                    if (!NormalizedEmailBackfillFingerprint.TryNormalize(
                            candidateFingerprint,
                            out expectedFingerprint))
                    {
                        return false;
                    }

                    break;

                case "--batch-size" when !hasBatchSize && index + 1 < args.Count:
                    hasBatchSize = true;
                    if (!int.TryParse(
                            args[++index],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out batchSize)
                        || batchSize is < 1 or > NormalizedEmailBackfillCommand.MaximumBatchSize)
                    {
                        return false;
                    }

                    break;

                default:
                    // Unknown, duplicate, and connection-bearing switches fail closed.
                    return false;
            }
        }

        ValidatedSqlServerConnection? validatedConnection;
        try
        {
            var connectionString = getEnvironmentVariable(
                NormalizedEmailBackfillCommand.ConnectionStringEnvironmentVariable);
            if (!SqlServerConnectionPolicy.TryCreate(
                    SqlServerConnectionPolicy.ProviderInvariantName,
                    connectionString,
                    "GymTrackPro Normalized Email Backfill",
                    expectedFingerprint is not null
                        ? SqlServerConnectionMode.ReadWrite
                        : SqlServerConnectionMode.ReadOnly,
                    out validatedConnection)
                || validatedConnection is null)
            {
                return false;
            }
        }
        catch
        {
            // Environment-provider failures are collapsed without exposing the
            // environment value or provider exception text.
            return false;
        }

        command = new NormalizedEmailBackfillCommand
        {
            Mode = expectedFingerprint is not null
                ? NormalizedEmailBackfillMode.Confirm
                : NormalizedEmailBackfillMode.DryRun,
            BatchSize = batchSize,
            ConnectionString = validatedConnection.ConnectionString,
            ExpectedFingerprint = expectedFingerprint
        };
        return true;
    }
}

public static class NormalizedEmailBackfillCommandOutput
{
    public const string Rejected = "Normalized-email backfill command rejected.";
    public const string Failed = "Normalized-email backfill command failed.";
    public const string Canceled = "Normalized-email backfill command canceled.";

    public static string Format(NormalizedEmailBackfillResult result)
    {
        var analysis = result.FinalAnalysis ?? result.InitialAnalysis;
        var fingerprint = result.Mode == NormalizedEmailBackfillMode.DryRun
            ? result.InitialAnalysis.SnapshotFingerprint
            : "WITHHELD";
        return string.Format(
            CultureInfo.InvariantCulture,
            "MODE={0}; SCANNED={1}; PENDING={2}; CANONICAL={3}; " +
            "INVALID_SOURCE={4}; EXISTING_MISMATCH={5}; COLLISION_GROUPS={6}; " +
            "COLLISION_ROWS={7}; SNAPSHOT_MISMATCH={8}; CONCURRENT_CONFLICT={9}; " +
            "UPDATED={10}; COMPLETED={11}; FINGERPRINT={12}",
            result.Mode,
            analysis.ScannedCount,
            analysis.PendingCount,
            analysis.AlreadyCanonicalCount,
            analysis.InvalidSourceCount,
            analysis.ExistingMismatchCount,
            analysis.CollisionGroupCount,
            analysis.CollisionRowCount,
            result.SnapshotMismatchDetected ? 1 : 0,
            result.ConcurrentConflictDetected ? 1 : 0,
            result.UpdatedCount,
            result.Completed,
            fingerprint);
    }
}
