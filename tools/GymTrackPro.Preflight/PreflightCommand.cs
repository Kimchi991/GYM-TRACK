using GymTrackPro.API.Maintenance;

namespace GymTrackPro.Preflight;

public sealed class PreflightCommand
{
    internal PreflightCommand(
        string connectionString,
        PreflightStageMode mode)
    {
        ConnectionString = connectionString;
        Mode = mode;
    }

    public string ConnectionString { get; }
    public PreflightStageMode Mode { get; }

    public override string ToString() =>
        $"PreflightCommand Mode={Mode}; Connection=[REDACTED]";
}

public static class PreflightCommandParser
{
    public const string ConnectionStringEnvironmentVariable =
        "GYMTRACKPRO_PREFLIGHT_CONNECTION_STRING";
    public const string ApplicationName = "GymTrackPro Migration Preflight";

    public static bool TryParse(
        string[] args,
        Func<string, string?> getEnvironmentVariable,
        out PreflightCommand? command)
    {
        command = null;
        if (args.Length != 2
            || !string.Equals(args[0], "--mode", StringComparison.Ordinal)
            || !TryParseMode(args[1], out var mode))
        {
            return false;
        }

        try
        {
            var connectionString = getEnvironmentVariable(ConnectionStringEnvironmentVariable);
            if (!SqlServerConnectionPolicy.TryCreate(
                    SqlServerConnectionPolicy.ProviderInvariantName,
                    connectionString,
                    ApplicationName,
                    SqlServerConnectionMode.ReadOnly,
                    out var validatedConnection)
                || validatedConnection is null)
            {
                return false;
            }

            command = new PreflightCommand(validatedConnection.ConnectionString, mode);
            return true;
        }
        catch
        {
            // Environment-provider failures are intentionally collapsed without
            // exposing any environment-supplied connection secret.
            return false;
        }
    }

    private static bool TryParseMode(string value, out PreflightStageMode mode)
    {
        mode = value switch
        {
            "pre-stage" => PreflightStageMode.PreStage,
            "post-stage" => PreflightStageMode.PostStage,
            _ => (PreflightStageMode)(-1)
        };
        return Enum.IsDefined(mode);
    }
}

public static class PreflightCommandOutput
{
    public const string Rejected = "Migration preflight command rejected.";
    public const string Failed = "Migration preflight command failed.";
}
