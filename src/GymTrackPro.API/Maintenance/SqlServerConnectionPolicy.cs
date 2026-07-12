using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace GymTrackPro.API.Maintenance;

public enum SqlServerConnectionMode
{
    ReadWrite,
    ReadOnly
}

/// <summary>
/// Carries a connection string only after it has passed the shared SQL Server
/// transport and endpoint policy. Its string representation is intentionally
/// redacted so routine diagnostics cannot disclose credentials or topology.
/// </summary>
public sealed class ValidatedSqlServerConnection
{
    internal ValidatedSqlServerConnection(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public override string ToString() => "Validated SQL Server connection [REDACTED]";
}

/// <summary>
/// Fail-closed connection policy shared by SQL Server maintenance executables.
/// This validates configuration only; it never opens a database connection.
/// </summary>
public static class SqlServerConnectionPolicy
{
    public const string ProviderInvariantName =
        "Microsoft.EntityFrameworkCore.SqlServer";

    public static bool TryCreate(
        string? providerName,
        string? connectionString,
        string applicationName,
        SqlServerConnectionMode mode,
        out ValidatedSqlServerConnection? validatedConnection)
    {
        validatedConnection = null;
        if (!string.Equals(
                providerName,
                ProviderInvariantName,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(connectionString)
            || !IsValidApplicationName(applicationName)
            || mode is not (SqlServerConnectionMode.ReadWrite
                or SqlServerConnectionMode.ReadOnly))
        {
            return false;
        }

        try
        {
            if (!HasExplicitTransportPolicy(connectionString))
            {
                return false;
            }

            var builder = new SqlConnectionStringBuilder(connectionString);
            var encrypt = builder.Encrypt;
            if (string.IsNullOrWhiteSpace(builder.DataSource)
                || string.IsNullOrWhiteSpace(builder.InitialCatalog)
                || (encrypt != SqlConnectionEncryptOption.Mandatory
                    && encrypt != SqlConnectionEncryptOption.Strict)
                || builder.TrustServerCertificate)
            {
                return false;
            }

            builder.ApplicationIntent = mode == SqlServerConnectionMode.ReadOnly
                ? ApplicationIntent.ReadOnly
                : ApplicationIntent.ReadWrite;
            builder.ApplicationName = applicationName;
            builder.PersistSecurityInfo = false;
            validatedConnection = new ValidatedSqlServerConnection(builder.ConnectionString);
            return true;
        }
        catch
        {
            // Configuration and builder failures are intentionally collapsed to
            // a boolean; exception text can contain credentials or topology.
            return false;
        }
    }

    private static bool IsValidApplicationName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && !value.Any(char.IsControl);

    private static bool HasExplicitTransportPolicy(string connectionString)
    {
        if (!TryCountExplicitTransportKeys(
                connectionString,
                out var encryptKeyCount,
                out var trustServerCertificateKeyCount)
            || encryptKeyCount != 1
            || trustServerCertificateKeyCount != 1)
        {
            return false;
        }

        var explicitValues = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };
        return TryGetSingleExplicitValue(
                explicitValues,
                "ENCRYPT",
                out var encrypt)
            && encrypt is not null
            && (string.Equals(encrypt, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(encrypt, "Mandatory", StringComparison.OrdinalIgnoreCase)
                || string.Equals(encrypt, "Strict", StringComparison.OrdinalIgnoreCase))
            && TryGetSingleExplicitValue(
                explicitValues,
                "TRUSTSERVERCERTIFICATE",
                out var trustServerCertificate)
            && string.Equals(
                trustServerCertificate,
                "False",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCountExplicitTransportKeys(
        string connectionString,
        out int encryptKeyCount,
        out int trustServerCertificateKeyCount)
    {
        encryptKeyCount = 0;
        trustServerCertificateKeyCount = 0;
        var index = 0;
        while (index < connectionString.Length)
        {
            while (index < connectionString.Length
                   && (char.IsWhiteSpace(connectionString[index])
                       || connectionString[index] == ';'))
            {
                index++;
            }

            if (index == connectionString.Length)
            {
                return true;
            }

            var keyStart = index;
            while (index < connectionString.Length
                   && connectionString[index] != '='
                   && connectionString[index] != ';')
            {
                index++;
            }

            if (index == connectionString.Length
                || connectionString[index] != '=')
            {
                return false;
            }

            var key = connectionString[keyStart..index].Trim();
            if (key.Length == 0)
            {
                return false;
            }

            switch (NormalizeKey(key))
            {
                case "ENCRYPT":
                    encryptKeyCount++;
                    break;
                case "TRUSTSERVERCERTIFICATE":
                    trustServerCertificateKeyCount++;
                    break;
            }

            index++;
            while (index < connectionString.Length
                   && char.IsWhiteSpace(connectionString[index]))
            {
                index++;
            }

            if (index < connectionString.Length
                && connectionString[index] is '\'' or '"')
            {
                var delimiter = connectionString[index++];
                var wasClosed = false;
                while (index < connectionString.Length)
                {
                    if (connectionString[index] != delimiter)
                    {
                        index++;
                        continue;
                    }

                    if (index + 1 < connectionString.Length
                        && connectionString[index + 1] == delimiter)
                    {
                        index += 2;
                        continue;
                    }

                    index++;
                    wasClosed = true;
                    break;
                }

                if (!wasClosed)
                {
                    return false;
                }

                while (index < connectionString.Length
                       && char.IsWhiteSpace(connectionString[index]))
                {
                    index++;
                }

                if (index < connectionString.Length
                    && connectionString[index] != ';')
                {
                    return false;
                }
            }
            else
            {
                while (index < connectionString.Length
                       && connectionString[index] != ';')
                {
                    index++;
                }
            }

            if (index < connectionString.Length)
            {
                index++;
            }
        }

        return true;
    }

    private static bool TryGetSingleExplicitValue(
        DbConnectionStringBuilder builder,
        string normalizedKey,
        out string? value)
    {
        value = null;
        var matchingKeys = builder.Keys
            .Cast<string>()
            .Where(key => string.Equals(
                NormalizeKey(key),
                normalizedKey,
                StringComparison.Ordinal))
            .ToArray();
        if (matchingKeys.Length != 1)
        {
            return false;
        }

        value = Convert.ToString(
                builder[matchingKeys[0]],
                CultureInfo.InvariantCulture)
            ?.Trim();
        return value is not null;
    }

    private static string NormalizeKey(string key) =>
        string.Concat(key.Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
}
