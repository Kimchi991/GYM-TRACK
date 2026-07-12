using System.Globalization;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Maintenance;

namespace GymTrackPro.Bootstrap;

public enum BootstrapExecutionMode
{
    DryRun,
    Confirm
}

public sealed class BootstrapCommand
{
    public const string FirebaseIdTokenVariable = "GYMTRACKPRO_BOOTSTRAP_FIREBASE_ID_TOKEN";
    public const string FirebaseProjectIdVariable = "FirebaseAuthentication__ProjectId";
    public const string ConnectionStringVariable = "ConnectionStrings__DefaultConnection";
    public const string EnabledVariable = "OwnerBootstrap__Enabled";
    public const string AllowedEnvironmentVariable = "OwnerBootstrap__AllowedEnvironment";

    public required int UserId { get; init; }
    public required string EnvironmentName { get; init; }
    public required BootstrapExecutionMode Mode { get; init; }
    public required string FirebaseIdToken { get; init; }
    public required string FirebaseProjectId { get; init; }
    public required string ConnectionString { get; init; }

    public override string ToString() =>
        $"BootstrapCommand UserID={UserId}; Environment={EnvironmentName}; Mode={Mode}; SensitiveInputs=[REDACTED]";
}

public static class BootstrapCommandParser
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        Func<string, string?> getEnvironmentVariable,
        out BootstrapCommand? command)
    {
        command = null;
        int? userId = null;
        string? requestedEnvironment = null;
        var dryRun = false;
        var confirm = false;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--user-id" when index + 1 < args.Count && !userId.HasValue:
                    if (!int.TryParse(
                            args[++index],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var parsedUserId)
                        || parsedUserId <= 0)
                    {
                        return false;
                    }

                    userId = parsedUserId;
                    break;

                case "--environment" when index + 1 < args.Count && requestedEnvironment is null:
                    requestedEnvironment = args[++index];
                    break;

                case "--dry-run" when !dryRun:
                    dryRun = true;
                    break;

                case "--confirm" when !confirm:
                    confirm = true;
                    break;

                default:
                    // Unknown, duplicate, and sensitive-value command-line switches all fail
                    // without echoing the argument or its possible value.
                    return false;
            }
        }

        if (!userId.HasValue
            || !IsValidEnvironmentName(requestedEnvironment)
            || dryRun == confirm)
        {
            return false;
        }

        var dotnetEnvironment = getEnvironmentVariable("DOTNET_ENVIRONMENT");
        var aspnetEnvironment = getEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var runtimeEnvironments = new[] { dotnetEnvironment, aspnetEnvironment }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (runtimeEnvironments.Length != 1
            || !string.Equals(
                runtimeEnvironments[0],
                requestedEnvironment,
                StringComparison.Ordinal)
            || !bool.TryParse(getEnvironmentVariable(BootstrapCommand.EnabledVariable), out var enabled)
            || !enabled
            || !string.Equals(
                getEnvironmentVariable(BootstrapCommand.AllowedEnvironmentVariable),
                requestedEnvironment,
                StringComparison.Ordinal))
        {
            return false;
        }

        var firebaseIdToken = getEnvironmentVariable(BootstrapCommand.FirebaseIdTokenVariable);
        var firebaseProjectId = getEnvironmentVariable(BootstrapCommand.FirebaseProjectIdVariable);
        var connectionString = getEnvironmentVariable(BootstrapCommand.ConnectionStringVariable);
        var firebaseOptions = new FirebaseAuthenticationOptions
        {
            ProjectId = firebaseProjectId ?? string.Empty
        };
        if (string.IsNullOrWhiteSpace(firebaseIdToken)
            || firebaseIdToken.Length > FirebaseBootstrapTokenValidator.MaximumTokenCharacters
            || !firebaseOptions.HasValidProjectId
            || !SqlServerConnectionPolicy.TryCreate(
                SqlServerConnectionPolicy.ProviderInvariantName,
                connectionString,
                "GymTrackPro Owner Bootstrap",
                SqlServerConnectionMode.ReadWrite,
                out var validatedConnection)
            || validatedConnection is null)
        {
            return false;
        }

        command = new BootstrapCommand
        {
            UserId = userId.Value,
            EnvironmentName = requestedEnvironment!,
            Mode = dryRun ? BootstrapExecutionMode.DryRun : BootstrapExecutionMode.Confirm,
            FirebaseIdToken = firebaseIdToken!,
            FirebaseProjectId = firebaseProjectId!,
            ConnectionString = validatedConnection.ConnectionString
        };
        return true;
    }

    private static bool IsValidEnvironmentName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 64
        && !value.Any(char.IsControl)
        && !value.Any(char.IsWhiteSpace);
}

public static class BootstrapCommandOutput
{
    public static string Format(OwnerBootstrapResult result) =>
        $"UserID={result.UserId}; Role={result.Role}; WouldApply={result.WouldBind}; Applied={result.Applied}";
}
