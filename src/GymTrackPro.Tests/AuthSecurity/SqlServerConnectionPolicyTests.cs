using GymTrackPro.API.Maintenance;
using Microsoft.Data.SqlClient;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class SqlServerConnectionPolicyTests
{
    private const string ValidConnection =
        "Server=private-db.example.test;Database=PrivateCatalog;" +
        "User Id=private-user;Password=private-password;" +
        "Encrypt=True;TrustServerCertificate=False";

    [Theory]
    [InlineData(SqlServerConnectionMode.ReadOnly, ApplicationIntent.ReadOnly)]
    [InlineData(SqlServerConnectionMode.ReadWrite, ApplicationIntent.ReadWrite)]
    public void Valid_policy_hardens_intent_application_name_and_secret_persistence(
        SqlServerConnectionMode mode,
        ApplicationIntent expectedIntent)
    {
        Assert.True(SqlServerConnectionPolicy.TryCreate(
            SqlServerConnectionPolicy.ProviderInvariantName,
            ValidConnection,
            "GymTrackPro Policy Test",
            mode,
            out var validated));

        var parsed = new SqlConnectionStringBuilder(validated!.ConnectionString);
        Assert.Equal(expectedIntent, parsed.ApplicationIntent);
        Assert.Equal("GymTrackPro Policy Test", parsed.ApplicationName);
        Assert.False(parsed.PersistSecurityInfo);
        Assert.False(parsed.TrustServerCertificate);
        Assert.True(parsed.Encrypt == SqlConnectionEncryptOption.Mandatory
                    || parsed.Encrypt == SqlConnectionEncryptOption.Strict);
        Assert.DoesNotContain("private-password", validated.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-db", validated.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateCatalog", validated.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Encrypt=True;TrustServerCertificate=False")]
    [InlineData("Encrypt=Mandatory;TrustServerCertificate=False")]
    [InlineData("Encrypt=Strict;TrustServerCertificate=False")]
    [InlineData("Encrypt=True;Trust Server Certificate=False")]
    [InlineData("eNcRyPt=tRuE;tRuStSeRvErCeRtIfIcAtE=fAlSe;;;")]
    [InlineData("Encrypt=\"True\";TrustServerCertificate='False';")]
    public void Explicit_required_or_strict_encryption_with_explicit_certificate_validation_is_accepted(
        string transportPolicy)
    {
        Assert.True(SqlServerConnectionPolicy.TryCreate(
            SqlServerConnectionPolicy.ProviderInvariantName,
            $"Server=strict.example.test;Database=Gym;Integrated Security=True;{transportPolicy}",
            "GymTrackPro Strict Test",
            SqlServerConnectionMode.ReadOnly,
            out _));
    }

    [Fact]
    public void Quoted_values_are_scanned_without_treating_tls_looking_password_text_as_keys()
    {
        Assert.True(SqlServerConnectionPolicy.TryCreate(
            SqlServerConnectionPolicy.ProviderInvariantName,
            "Server=db;Database=Gym;Password=\"secret;Encrypt=False;" +
            "TrustServerCertificate=True\";Encrypt=True;TrustServerCertificate=False;;;",
            "GymTrackPro Quote Test",
            SqlServerConnectionMode.ReadOnly,
            out _));

        Assert.False(SqlServerConnectionPolicy.TryCreate(
            SqlServerConnectionPolicy.ProviderInvariantName,
            "Server=db;Database=Gym;" +
            "Password=\"Encrypt=True;TrustServerCertificate=False\"",
            "GymTrackPro Quote Test",
            SqlServerConnectionMode.ReadOnly,
            out _));
    }

    [Theory]
    [InlineData("Encrypt=True;Encrypt=False;TrustServerCertificate=False")]
    [InlineData("Encrypt=False;Encrypt=True;TrustServerCertificate=False")]
    [InlineData("Encrypt=True;Encrypt=True;TrustServerCertificate=False")]
    [InlineData("encrypt=True;ENCRYPT=True;TrustServerCertificate=False")]
    [InlineData("Encrypt=True;TrustServerCertificate=True;TrustServerCertificate=False")]
    [InlineData("Encrypt=True;TrustServerCertificate=False;TrustServerCertificate=True")]
    [InlineData("Encrypt=True;TrustServerCertificate=False;TrustServerCertificate=False")]
    [InlineData("Encrypt=True;trustservercertificate=False;TRUSTSERVERCERTIFICATE=False")]
    [InlineData("Encrypt=True;TrustServerCertificate=False;Trust Server Certificate=False")]
    public void Repeated_or_alias_duplicate_transport_keys_are_rejected_even_when_final_value_is_safe(
        string transportPolicy)
    {
        Assert.False(SqlServerConnectionPolicy.TryCreate(
            SqlServerConnectionPolicy.ProviderInvariantName,
            $"Server=db;Database=Gym;{transportPolicy}",
            "GymTrackPro Duplicate Test",
            SqlServerConnectionMode.ReadOnly,
            out _));
    }

    [Theory]
    [InlineData("Other.Provider", "Server=db;Database=Gym;Encrypt=True;TrustServerCertificate=False")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Database=Gym;Encrypt=True;TrustServerCertificate=False")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Server=db;Encrypt=True;TrustServerCertificate=False")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Server=db;Database=Gym;Encrypt=False;TrustServerCertificate=False")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Server=db;Database=Gym;Encrypt=Optional;TrustServerCertificate=False")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Server=db;Database=Gym;Encrypt=True;TrustServerCertificate=True")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Server=db;Database=Gym;TrustServerCertificate=False")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Server=db;Database=Gym;Encrypt=True")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Server=db;Database=Gym;Encrypt=Yes;TrustServerCertificate=False")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "Server=db;Database=Gym;Encrypt=True;TrustServerCertificate=No")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "not-a-connection-string")]
    public void Invalid_provider_endpoint_or_transport_is_rejected_without_diagnostics(
        string provider,
        string connectionString)
    {
        Assert.False(SqlServerConnectionPolicy.TryCreate(
            provider,
            connectionString,
            "GymTrackPro Policy Test",
            SqlServerConnectionMode.ReadOnly,
            out var validated));
        Assert.Null(validated);
    }

    [Fact]
    public void Undefined_mode_and_invalid_application_name_fail_closed()
    {
        Assert.False(SqlServerConnectionPolicy.TryCreate(
            SqlServerConnectionPolicy.ProviderInvariantName,
            ValidConnection,
            "GymTrackPro Policy Test",
            (SqlServerConnectionMode)99,
            out _));
        Assert.False(SqlServerConnectionPolicy.TryCreate(
            SqlServerConnectionPolicy.ProviderInvariantName,
            ValidConnection,
            "bad\r\napplication",
            SqlServerConnectionMode.ReadOnly,
            out _));
    }

    [Fact]
    public void Bootstrap_parser_rejects_unsafe_transport_without_exposing_configuration()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_ENVIRONMENT"] = "Production",
            [GymTrackPro.Bootstrap.BootstrapCommand.EnabledVariable] = "true",
            [GymTrackPro.Bootstrap.BootstrapCommand.AllowedEnvironmentVariable] = "Production",
            [GymTrackPro.Bootstrap.BootstrapCommand.FirebaseIdTokenVariable] = "header.payload.signature",
            [GymTrackPro.Bootstrap.BootstrapCommand.FirebaseProjectIdVariable] = "gymtrackpro-production",
            [GymTrackPro.Bootstrap.BootstrapCommand.ConnectionStringVariable] =
                "Server=private-source;Database=private-catalog;Password=private-password;" +
                "Encrypt=False;TrustServerCertificate=False"
        };

        Assert.False(GymTrackPro.Bootstrap.BootstrapCommandParser.TryParse(
            new[] { "--user-id", "7", "--environment", "Production", "--dry-run" },
            key => environment.GetValueOrDefault(key),
            out var command));
        Assert.Null(command);
        var output = GymTrackPro.Bootstrap.BootstrapCommandOutput.Format(
            new GymTrackPro.API.Authentication.OwnerBootstrapResult(
                7,
                GymTrackPro.Shared.Enums.UserRole.Administrator,
                WouldBind: false,
                Applied: false));
        foreach (var sensitive in new[]
                 {
                     "private-source",
                     "private-catalog",
                     "private-password"
                 })
        {
            Assert.DoesNotContain(sensitive, output, StringComparison.Ordinal);
        }
    }
}
