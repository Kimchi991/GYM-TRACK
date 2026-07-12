using GymTrackPro.API.Authentication;
using GymTrackPro.Bootstrap;
using GymTrackPro.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class BootstrapCommandTests
{
    [Fact]
    public void Parser_requires_non_sensitive_flags_and_explicit_one_run_secret_environment_values()
    {
        var environment = ValidEnvironment();

        var valid = BootstrapCommandParser.TryParse(
            new[] { "--user-id", "7", "--environment", "Production", "--dry-run" },
            key => environment.GetValueOrDefault(key),
            out var command);

        Assert.True(valid);
        Assert.NotNull(command);
        Assert.Equal(7, command!.UserId);
        Assert.Equal("Production", command.EnvironmentName);
        Assert.Equal(BootstrapExecutionMode.DryRun, command.Mode);
        Assert.Equal("header.payload.signature", command.FirebaseIdToken);
        Assert.Equal("gymtrackpro-production", command.FirebaseProjectId);
    }

    [Theory]
    [InlineData("--dry-run", "--confirm")]
    [InlineData("--firebase-id-token", "sensitive-value")]
    [InlineData("--firebase-project-id", "gymtrackpro-production")]
    public void Parser_rejects_conflicting_or_sensitive_command_line_inputs(
        string extraName,
        string extraValue)
    {
        var args = new List<string>
        {
            "--user-id", "7", "--environment", "Production", "--dry-run",
            extraName, extraValue
        };

        var valid = BootstrapCommandParser.TryParse(
            args,
            key => ValidEnvironment().GetValueOrDefault(key),
            out var command);

        Assert.False(valid);
        Assert.Null(command);
    }

    [Fact]
    public void Parser_fails_closed_on_runtime_or_allowed_environment_mismatch()
    {
        var environment = ValidEnvironment();
        environment["DOTNET_ENVIRONMENT"] = "Development";

        var valid = BootstrapCommandParser.TryParse(
            new[] { "--user-id", "7", "--environment", "Production", "--confirm" },
            key => environment.GetValueOrDefault(key),
            out _);

        Assert.False(valid);
    }

    [Fact]
    public void Parser_rejects_missing_token_or_invalid_project_environment_values()
    {
        var missingToken = ValidEnvironment();
        missingToken.Remove(BootstrapCommand.FirebaseIdTokenVariable);
        var invalidProject = ValidEnvironment();
        invalidProject[BootstrapCommand.FirebaseProjectIdVariable] = "project-with-trailing-hyphen-";

        Assert.False(BootstrapCommandParser.TryParse(
            new[] { "--user-id", "7", "--environment", "Production", "--dry-run" },
            key => missingToken.GetValueOrDefault(key),
            out _));
        Assert.False(BootstrapCommandParser.TryParse(
            new[] { "--user-id", "7", "--environment", "Production", "--dry-run" },
            key => invalidProject.GetValueOrDefault(key),
            out _));
    }

    [Theory]
    [InlineData("Server=db;Database=Gym;Encrypt=True")]
    [InlineData("Server=db;Database=Gym;TrustServerCertificate=False")]
    public void Parser_rejects_connection_strings_that_rely_on_transport_defaults(
        string connectionString)
    {
        var environment = ValidEnvironment();
        environment[BootstrapCommand.ConnectionStringVariable] = connectionString;

        Assert.False(BootstrapCommandParser.TryParse(
            new[] { "--user-id", "7", "--environment", "Production", "--dry-run" },
            key => environment.GetValueOrDefault(key),
            out var command));
        Assert.Null(command);
    }

    [Fact]
    public void Command_and_success_output_never_echo_sensitive_inputs_or_connection()
    {
        var environment = ValidEnvironment();
        Assert.True(BootstrapCommandParser.TryParse(
            new[] { "--user-id", "7", "--environment", "Production", "--dry-run" },
            key => environment.GetValueOrDefault(key),
            out var command));
        var success = BootstrapCommandOutput.Format(new OwnerBootstrapResult(
            7,
            UserRole.Administrator,
            WouldBind: true,
            Applied: false));

        foreach (var sensitiveValue in new[]
                 {
                     environment[BootstrapCommand.FirebaseIdTokenVariable],
                     environment[BootstrapCommand.FirebaseProjectIdVariable],
                     environment[BootstrapCommand.ConnectionStringVariable]
                 })
        {
            Assert.DoesNotContain(sensitiveValue, command!.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(sensitiveValue, success, StringComparison.Ordinal);
        }

        Assert.Equal(
            "UserID=7; Role=Administrator; WouldApply=True; Applied=False",
            success);
    }

    [Fact]
    public void Isolated_service_wiring_registers_bootstrap_core_but_no_web_surface()
    {
        var environment = ValidEnvironment();
        Assert.True(BootstrapCommandParser.TryParse(
            new[] { "--user-id", "7", "--environment", "Production", "--dry-run" },
            key => environment.GetValueOrDefault(key),
            out var command));
        var services = new ServiceCollection();
        services.AddBootstrapCore(
            command!,
            options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOwnerBootstrapService>());
        Assert.Equal(
            "Production",
            scope.ServiceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName);
        var options = scope.ServiceProvider.GetRequiredService<IOptions<OwnerBootstrapOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal("Production", options.AllowedEnvironment);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.Name.Contains("Controller", StringComparison.Ordinal));
    }

    [Fact]
    public void Tool_source_has_only_redacted_or_result_console_output()
    {
        var root = TestWorkspace.FindRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "GymTrackPro.Bootstrap",
            "Program.cs"));

        Assert.DoesNotContain("Console.WriteLine(command.FirebaseIdToken", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.WriteLine(firebaseIdentity", program, StringComparison.Ordinal);
        Assert.DoesNotContain("command.ConnectionString)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Console.WriteLine(command", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Operations_runbook_covers_fresh_proof_backup_audit_cleanup_and_recovery()
    {
        var runbook = File.ReadAllText(Path.Combine(
            TestWorkspace.FindRoot(),
            "docs",
            "23_OwnerBootstrapOperationsRunbook.md"));

        foreach (var requirement in new[]
                 {
                     "maintenance window",
                     "database backup",
                     "Force-refresh",
                     "--dry-run",
                     "--confirm",
                     "InitialOwnerFirebaseBound",
                     "Mandatory secret cleanup",
                     "Failure and recovery"
                 })
        {
            Assert.Contains(requirement, runbook, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("eyJhbGci", runbook, StringComparison.Ordinal);
        Assert.Contains("<INJECTED_ONE_TIME_ID_TOKEN>", runbook, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ValidEnvironment() => new(StringComparer.Ordinal)
    {
        ["DOTNET_ENVIRONMENT"] = "Production",
        [BootstrapCommand.EnabledVariable] = "true",
        [BootstrapCommand.AllowedEnvironmentVariable] = "Production",
        [BootstrapCommand.FirebaseIdTokenVariable] = "header.payload.signature",
        [BootstrapCommand.FirebaseProjectIdVariable] = "gymtrackpro-production",
        [BootstrapCommand.ConnectionStringVariable] =
            "Server=bootstrap.invalid;Database=GymTrackPro;Integrated Security=True;" +
            "Encrypt=True;TrustServerCertificate=False"
    };

}
