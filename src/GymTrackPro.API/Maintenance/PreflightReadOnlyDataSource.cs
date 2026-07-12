using System.Data;
using System.Data.Common;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.API.Maintenance;

public enum PreflightCountQuery
{
    DuplicateFirebaseUids,
    DuplicateMemberLinks,
    InvalidUserRoles,
    LegacyGymGoerRoles,
    DuplicateInviteTokenHashes,
    DuplicateInviteRedemptionOperations,
    UnresolvedInvitesForUnavailableMembers,
    MissingProjectionVersions,
    AllMembers,
    DuplicateProjectionVersions,
    InvalidProjectionVersions,
    DeletedMembersWithActiveGymGoers,
    InvalidUserRoleMemberLinks,
    AttendanceDateTimeComponents,
    AllAttendanceRows,
    AttendanceLocalDatesMissing,
    AttendanceLocalDateMismatches,
    AttendanceLocalDateDuplicates,
    AttendanceLocalDateActiveDuplicates,
    MultipleOpenAttendanceSessions,
    MultipleOpenActiveAttendanceSessions,
    CheckoutNotAfterCheckin,
    ActiveCheckoutNotAfterCheckin,
    AttendanceMissingMembers,
    AttendanceForDeletedMembers,
    OpenAttendanceForDeletedMembers,
    OpenActiveAttendanceForDeletedMembers,
    InvalidAttendanceSupersession,
    SubscriptionCalendarTimeComponents
}

public sealed record PreflightColumnMetadata(
    string Table,
    string Column,
    string DataType,
    int MaximumLength,
    byte Precision,
    byte Scale,
    string? Collation,
    bool UsesDatabaseDefaultCollation,
    bool IsNullable,
    bool HasDefault,
    string? DefaultDefinition,
    bool IsRowVersion,
    bool IsIdentity,
    decimal? IdentitySeed,
    decimal? IdentityIncrement,
    bool IdentityNotForReplication,
    bool IsComputed,
    string? ComputedDefinition,
    bool IsPersisted,
    bool IsRowGuid,
    bool IsFileStream,
    bool IsAnsiPadded,
    bool IsSparse,
    bool IsColumnSet,
    byte GeneratedAlwaysType,
    bool IsHidden,
    bool IsMasked,
    string? MaskingFunction,
    byte? EncryptionType,
    string? EncryptionTypeDescription,
    string? EncryptionAlgorithmName,
    int? ColumnEncryptionKeyId,
    string? ColumnEncryptionKeyDatabaseName);

internal static class PreflightColumnMetadataFactory
{
    public static PreflightColumnMetadata Expected(
        string table,
        string column,
        string dataType,
        int maximumLength,
        byte precision,
        byte scale,
        bool isNullable,
        string? collation = null,
        bool usesDatabaseDefaultCollation = false,
        bool isAnsiPadded = false,
        string? defaultDefinition = null,
        bool isRowVersion = false,
        bool isIdentity = false,
        decimal? identitySeed = null,
        decimal? identityIncrement = null,
        bool identityNotForReplication = false)
    {
        if (isIdentity != (identitySeed.HasValue && identityIncrement.HasValue)
            || !isIdentity && identityNotForReplication
            || collation is not null && usesDatabaseDefaultCollation)
        {
            throw new ArgumentException("The expected column metadata is inconsistent.");
        }

        return new PreflightColumnMetadata(
            table,
            column,
            dataType,
            maximumLength,
            precision,
            scale,
            collation,
            usesDatabaseDefaultCollation,
            isNullable,
            defaultDefinition is not null,
            defaultDefinition,
            isRowVersion,
            isIdentity,
            identitySeed,
            identityIncrement,
            identityNotForReplication,
            IsComputed: false,
            ComputedDefinition: null,
            IsPersisted: false,
            IsRowGuid: false,
            IsFileStream: false,
            IsAnsiPadded: isAnsiPadded,
            IsSparse: false,
            IsColumnSet: false,
            GeneratedAlwaysType: 0,
            IsHidden: false,
            IsMasked: false,
            MaskingFunction: null,
            EncryptionType: null,
            EncryptionTypeDescription: null,
            EncryptionAlgorithmName: null,
            ColumnEncryptionKeyId: null,
            ColumnEncryptionKeyDatabaseName: null);
    }
}

public sealed record PreflightCheckConstraintMetadata(
    string Table,
    string Name,
    string Definition,
    bool IsEnabled,
    bool IsTrusted);

public sealed record PreflightForeignKeyColumnMetadata(
    string ParentColumn,
    string PrincipalColumn);

public sealed record PreflightForeignKeyMetadata(
    string ParentSchema,
    string ParentTable,
    string Name,
    string PrincipalSchema,
    string PrincipalTable,
    string DeleteAction,
    string UpdateAction,
    bool IsDisabled,
    bool IsTrusted,
    bool IsNotForReplication,
    IReadOnlyList<PreflightForeignKeyColumnMetadata> Columns);

public sealed record PreflightIndexKeyColumnMetadata(
    string Column,
    bool IsDescending);

public sealed record PreflightIndexMetadata(
    string Schema,
    string Table,
    string Name,
    string Type,
    bool IsUnique,
    bool IsPrimaryKey,
    bool IsUniqueConstraint,
    bool HasFilter,
    string? Filter,
    bool IgnoreDuplicateKeys,
    bool IsDisabled,
    bool IsHypothetical,
    IReadOnlyList<PreflightIndexKeyColumnMetadata> KeyColumns,
    IReadOnlyList<string> IncludedColumns);

public sealed record PreflightMigrationMetadata(
    string MigrationId,
    string ProductVersion);

public sealed record UserEmailNormalizationCounts(
    long MissingNormalizedEmails,
    long InvalidEmails,
    long CanonicalMismatches,
    long DuplicateCanonicalGroups);

internal sealed record PreflightAccountInviteRow(
    object? TokenHash,
    string? NormalizedEmail,
    int IntendedRole,
    string? Purpose,
    int? TargetMemberId,
    int? TargetUserId,
    DateTime? CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    DateTime? UsedAtUtc,
    DateTime? RevokedAtUtc,
    string? UsedByFirebaseUid,
    Guid? RedemptionOperationId);

public sealed class PreflightSchema
{
    private readonly Dictionary<string, Dictionary<string, PreflightColumnMetadata>> _tables;
    private readonly IReadOnlyList<PreflightCheckConstraintMetadata> _checks;
    private readonly IReadOnlyList<PreflightForeignKeyMetadata> _foreignKeys;
    private readonly IReadOnlyList<PreflightIndexMetadata> _indexes;
    private readonly IReadOnlyDictionary<string, PreflightMigrationMetadata> _migrations;

    public PreflightSchema(
        IEnumerable<PreflightColumnMetadata> columns,
        IEnumerable<PreflightCheckConstraintMetadata>? checks = null,
        IEnumerable<PreflightForeignKeyMetadata>? foreignKeys = null,
        IEnumerable<PreflightIndexMetadata>? indexes = null,
        IEnumerable<PreflightMigrationMetadata>? migrations = null)
    {
        _tables = columns
            .GroupBy(column => column.Table, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    column => column.Column,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        _checks = checks?.ToArray() ?? Array.Empty<PreflightCheckConstraintMetadata>();
        _foreignKeys = foreignKeys?.ToArray() ?? Array.Empty<PreflightForeignKeyMetadata>();
        _indexes = indexes?.ToArray() ?? Array.Empty<PreflightIndexMetadata>();
        _migrations = (migrations ?? Array.Empty<PreflightMigrationMetadata>())
            .ToDictionary(item => item.MigrationId, StringComparer.Ordinal);
    }

    public bool HasTable(string table) => _tables.ContainsKey(table);

    public bool HasColumn(string table, string column) =>
        _tables.TryGetValue(table, out var columns) && columns.ContainsKey(column);

    public bool HasColumns(string table, params string[] columns) =>
        columns.All(column => HasColumn(table, column));

    public bool ColumnMatches(PreflightColumnMetadata expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!_tables.TryGetValue(expected.Table, out var columns)
            || !columns.TryGetValue(expected.Column, out var actual))
        {
            return false;
        }

        return string.Equals(
                actual.DataType,
                expected.DataType,
                StringComparison.OrdinalIgnoreCase)
            && actual.MaximumLength == expected.MaximumLength
            && actual.Precision == expected.Precision
            && actual.Scale == expected.Scale
            && CollationMatches(actual, expected)
            && actual.IsNullable == expected.IsNullable
            && actual.HasDefault == expected.HasDefault
            && SqlShapeEquals(actual.DefaultDefinition, expected.DefaultDefinition)
            && actual.IsRowVersion == expected.IsRowVersion
            && actual.IsIdentity == expected.IsIdentity
            && actual.IdentitySeed == expected.IdentitySeed
            && actual.IdentityIncrement == expected.IdentityIncrement
            && actual.IdentityNotForReplication == expected.IdentityNotForReplication
            && actual.IsComputed == expected.IsComputed
            && SqlShapeEquals(actual.ComputedDefinition, expected.ComputedDefinition)
            && actual.IsPersisted == expected.IsPersisted
            && actual.IsRowGuid == expected.IsRowGuid
            && actual.IsFileStream == expected.IsFileStream
            && actual.IsAnsiPadded == expected.IsAnsiPadded
            && actual.IsSparse == expected.IsSparse
            && actual.IsColumnSet == expected.IsColumnSet
            && actual.GeneratedAlwaysType == expected.GeneratedAlwaysType
            && actual.IsHidden == expected.IsHidden
            && actual.IsMasked == expected.IsMasked
            && string.Equals(
                actual.MaskingFunction,
                expected.MaskingFunction,
                StringComparison.Ordinal)
            && actual.EncryptionType == expected.EncryptionType
            && string.Equals(
                actual.EncryptionTypeDescription,
                expected.EncryptionTypeDescription,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                actual.EncryptionAlgorithmName,
                expected.EncryptionAlgorithmName,
                StringComparison.Ordinal)
            && actual.ColumnEncryptionKeyId == expected.ColumnEncryptionKeyId
            && string.Equals(
                actual.ColumnEncryptionKeyDatabaseName,
                expected.ColumnEncryptionKeyDatabaseName,
                StringComparison.OrdinalIgnoreCase);
    }

    public bool HasPrimaryKey(
        string table,
        string name,
        params string[] keyColumns) =>
        HasExactIndex(
            table,
            name,
            "CLUSTERED",
            isUnique: true,
            isPrimaryKey: true,
            filter: null,
            keyColumns);

    public bool HasTrustedCheck(string table, string name, string? definition = null) =>
        _checks.Any(check =>
            string.Equals(check.Table, table, StringComparison.OrdinalIgnoreCase)
            && string.Equals(check.Name, name, StringComparison.OrdinalIgnoreCase)
            && check.IsEnabled
            && check.IsTrusted
            && (definition is null || SqlShapeEquals(check.Definition, definition)));

    public bool HasForeignKey(
        string parentTable,
        string name,
        string principalTable,
        string deleteAction,
        params PreflightForeignKeyColumnMetadata[] columns)
    {
        var candidates = _foreignKeys
            .Where(foreignKey =>
                string.Equals(foreignKey.ParentSchema, "dbo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(foreignKey.ParentTable, parentTable, StringComparison.OrdinalIgnoreCase)
                && string.Equals(foreignKey.Name, name, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            return false;
        }

        var candidate = candidates[0];
        return string.Equals(candidate.PrincipalSchema, "dbo", StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.PrincipalTable, principalTable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.DeleteAction, deleteAction, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.UpdateAction, "NO_ACTION", StringComparison.OrdinalIgnoreCase)
            && !candidate.IsDisabled
            && candidate.IsTrusted
            && !candidate.IsNotForReplication
            && candidate.Columns.Count == columns.Length
            && candidate.Columns.Zip(columns).All(pair =>
                string.Equals(
                    pair.First.ParentColumn,
                    pair.Second.ParentColumn,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    pair.First.PrincipalColumn,
                    pair.Second.PrincipalColumn,
                    StringComparison.OrdinalIgnoreCase));
    }

    public bool HasIndex(
        string table,
        string name,
        bool isUnique,
        string? filter,
        params string[] keyColumns) =>
        HasExactIndex(
            table,
            name,
            "NONCLUSTERED",
            isUnique,
            isPrimaryKey: false,
            filter,
            keyColumns);

    private bool HasExactIndex(
        string table,
        string name,
        string type,
        bool isUnique,
        bool isPrimaryKey,
        string? filter,
        IReadOnlyList<string> keyColumns)
    {
        var candidates = _indexes
            .Where(index =>
                string.Equals(index.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(index.Table, table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(index.Name, name, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            return false;
        }

        var candidate = candidates[0];
        return string.Equals(candidate.Type, type, StringComparison.OrdinalIgnoreCase)
            && candidate.IsUnique == isUnique
            && candidate.IsPrimaryKey == isPrimaryKey
            && !candidate.IsUniqueConstraint
            && candidate.HasFilter == (filter is not null)
            && SqlShapeEquals(candidate.Filter, filter)
            && !candidate.IgnoreDuplicateKeys
            && !candidate.IsDisabled
            && !candidate.IsHypothetical
            && candidate.IncludedColumns.Count == 0
            && candidate.KeyColumns.Count == keyColumns.Count
            && candidate.KeyColumns.Zip(keyColumns).All(pair =>
                !pair.First.IsDescending
                && string.Equals(
                    pair.First.Column,
                    pair.Second,
                    StringComparison.OrdinalIgnoreCase));
    }

    public bool HasMigration(string migrationId) => _migrations.ContainsKey(migrationId);

    public bool HasExactMigrationHistory(params PreflightMigrationMetadata[] expected) =>
        _migrations.Count == expected.Length
        && expected.All(item =>
            _migrations.TryGetValue(item.MigrationId, out var actual)
            && string.Equals(actual.ProductVersion, item.ProductVersion, StringComparison.Ordinal));

    private static bool CollationMatches(
        PreflightColumnMetadata actual,
        PreflightColumnMetadata expected)
    {
        if (expected.Collation is not null)
        {
            return string.Equals(
                actual.Collation,
                expected.Collation,
                StringComparison.OrdinalIgnoreCase);
        }

        return expected.UsesDatabaseDefaultCollation
            ? actual.UsesDatabaseDefaultCollation
                && !string.IsNullOrWhiteSpace(actual.Collation)
            : actual.Collation is null;
    }

    private static bool SqlShapeEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(
            NormalizeSqlShape(left),
            NormalizeSqlShape(right),
            StringComparison.Ordinal);
    }

    private static string NormalizeSqlShape(string value)
    {
        var compact = new string(value
            .Where(character => !char.IsWhiteSpace(character))
            .Select(char.ToUpperInvariant)
            .ToArray());
        while (HasOneOuterParenthesisPair(compact))
        {
            compact = compact[1..^1];
        }

        return compact;
    }

    private static bool HasOneOuterParenthesisPair(string value)
    {
        if (value.Length < 2 || value[0] != '(' || value[^1] != ')')
        {
            return false;
        }

        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            depth += value[index] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0
            };
            if (depth == 0 && index != value.Length - 1)
            {
                return false;
            }
        }

        return depth == 0;
    }
}

public interface IPreflightReadOnlyDataSource
{
    Task<PreflightSchema> InspectSchemaAsync(CancellationToken cancellationToken = default);

    Task<long> CountAsync(
        PreflightCountQuery query,
        CancellationToken cancellationToken = default);

    Task<UserEmailNormalizationCounts> CountUserEmailNormalizationAsync(
        bool normalizedEmailColumnExists,
        CancellationToken cancellationToken = default);

    Task<long> CountMalformedAccountInvitesAsync(
        CancellationToken cancellationToken = default);

    Task<string?> GetSystemSettingValueAsync(
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes an allow-listed catalog of SELECT-only SQL. It intentionally does not
/// expose GymDbContext.SaveChanges or arbitrary SQL to the preflight runner.
/// </summary>
public sealed class SqlServerPreflightReadOnlyDataSource : IPreflightReadOnlyDataSource
{
    public const string ExactIdentityCollation = "Latin1_General_100_BIN2";

    private sealed record PreflightForeignKeyRow(
        int ObjectId,
        string ParentSchema,
        string ParentTable,
        string Name,
        string PrincipalSchema,
        string PrincipalTable,
        string DeleteAction,
        string UpdateAction,
        bool IsDisabled,
        bool IsTrusted,
        bool IsNotForReplication,
        int Ordinal,
        string ParentColumn,
        string PrincipalColumn);

    private sealed record PreflightIndexRow(
        int ObjectId,
        int IndexId,
        string Schema,
        string Table,
        string Name,
        string Type,
        bool IsUnique,
        bool IsPrimaryKey,
        bool IsUniqueConstraint,
        bool HasFilter,
        string? Filter,
        bool IgnoreDuplicateKeys,
        bool IsDisabled,
        bool IsHypothetical,
        string Column,
        int KeyOrdinal,
        bool IsDescending,
        bool IsIncluded,
        int IndexColumnId);

    private const string ColumnSchemaSql =
        "SELECT [t].[name], [c].[name], [ty].[name], " +
        "CASE WHEN [c].[max_length] = -1 THEN -1 " +
        "WHEN [ty].[name] IN (N'nchar', N'nvarchar') THEN [c].[max_length] / 2 " +
        "ELSE [c].[max_length] END, [c].[precision], [c].[scale], [c].[collation_name], " +
        "CAST(CASE WHEN [c].[collation_name] = CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), N'Collation')) " +
        "THEN 1 ELSE 0 END AS bit), [c].[is_nullable], " +
        "CAST(CASE WHEN [c].[default_object_id] = 0 THEN 0 ELSE 1 END AS bit), [dc].[definition], " +
        "CAST(CASE WHEN [c].[system_type_id] = 189 THEN 1 ELSE 0 END AS bit), " +
        "[c].[is_identity], CONVERT(decimal(38, 0), [ic].[seed_value]), " +
        "CONVERT(decimal(38, 0), [ic].[increment_value]), " +
        "CAST(COALESCE([ic].[is_not_for_replication], 0) AS bit), " +
        "[c].[is_computed], [cc].[definition], CAST(COALESCE([cc].[is_persisted], 0) AS bit), " +
        "[c].[is_rowguidcol], [c].[is_filestream], [c].[is_ansi_padded], " +
        "[c].[is_sparse], [c].[is_column_set], [c].[generated_always_type], " +
        "[c].[is_hidden], [c].[is_masked], [c].[masking_function], " +
        "[c].[encryption_type], [c].[encryption_type_desc], [c].[encryption_algorithm_name], " +
        "[c].[column_encryption_key_id], [c].[column_encryption_key_database_name] " +
        "FROM [sys].[tables] AS [t] " +
        "INNER JOIN [sys].[schemas] AS [s] ON [s].[schema_id] = [t].[schema_id] " +
        "INNER JOIN [sys].[columns] AS [c] ON [c].[object_id] = [t].[object_id] " +
        "INNER JOIN [sys].[types] AS [ty] ON [ty].[user_type_id] = [c].[user_type_id] " +
        "LEFT JOIN [sys].[default_constraints] AS [dc] ON [dc].[object_id] = [c].[default_object_id] " +
        "LEFT JOIN [sys].[identity_columns] AS [ic] ON [ic].[object_id] = [c].[object_id] " +
        "AND [ic].[column_id] = [c].[column_id] " +
        "LEFT JOIN [sys].[computed_columns] AS [cc] ON [cc].[object_id] = [c].[object_id] " +
        "AND [cc].[column_id] = [c].[column_id] " +
        "WHERE [s].[name] = N'dbo'";

    private const string CheckConstraintSchemaSql =
        "SELECT [t].[name], [cc].[name], [cc].[definition], [cc].[is_disabled], [cc].[is_not_trusted] " +
        "FROM [sys].[check_constraints] AS [cc] " +
        "INNER JOIN [sys].[tables] AS [t] ON [t].[object_id] = [cc].[parent_object_id] " +
        "INNER JOIN [sys].[schemas] AS [s] ON [s].[schema_id] = [t].[schema_id] " +
        "WHERE [s].[name] = N'dbo'";

    private const string ForeignKeySchemaSql =
        "SELECT [fk].[object_id], [ds].[name], [dt].[name], [fk].[name], " +
        "[ps].[name], [pt].[name], [fk].[delete_referential_action_desc], " +
        "[fk].[update_referential_action_desc], [fk].[is_disabled], " +
        "[fk].[is_not_trusted], [fk].[is_not_for_replication], " +
        "[fkc].[constraint_column_id], [dc].[name], [pc].[name] " +
        "FROM [sys].[foreign_keys] AS [fk] " +
        "INNER JOIN [sys].[foreign_key_columns] AS [fkc] ON [fkc].[constraint_object_id] = [fk].[object_id] " +
        "INNER JOIN [sys].[tables] AS [dt] ON [dt].[object_id] = [fk].[parent_object_id] " +
        "INNER JOIN [sys].[schemas] AS [ds] ON [ds].[schema_id] = [dt].[schema_id] " +
        "INNER JOIN [sys].[columns] AS [dc] ON [dc].[object_id] = [dt].[object_id] AND [dc].[column_id] = [fkc].[parent_column_id] " +
        "INNER JOIN [sys].[tables] AS [pt] ON [pt].[object_id] = [fk].[referenced_object_id] " +
        "INNER JOIN [sys].[schemas] AS [ps] ON [ps].[schema_id] = [pt].[schema_id] " +
        "INNER JOIN [sys].[columns] AS [pc] ON [pc].[object_id] = [pt].[object_id] AND [pc].[column_id] = [fkc].[referenced_column_id] " +
        "WHERE [ds].[name] = N'dbo' " +
        "ORDER BY [fk].[object_id], [fkc].[constraint_column_id]";

    private const string IndexSchemaSql =
        "SELECT [i].[object_id], [i].[index_id], [s].[name], [t].[name], " +
        "[i].[name], [i].[type_desc], [i].[is_unique], [i].[is_primary_key], " +
        "[i].[is_unique_constraint], [i].[has_filter], [i].[filter_definition], " +
        "[i].[ignore_dup_key], [i].[is_disabled], [i].[is_hypothetical], " +
        "[c].[name], [ic].[key_ordinal], [ic].[is_descending_key], " +
        "[ic].[is_included_column], [ic].[index_column_id] " +
        "FROM [sys].[indexes] AS [i] " +
        "INNER JOIN [sys].[tables] AS [t] ON [t].[object_id] = [i].[object_id] " +
        "INNER JOIN [sys].[schemas] AS [s] ON [s].[schema_id] = [t].[schema_id] " +
        "INNER JOIN [sys].[index_columns] AS [ic] ON [ic].[object_id] = [i].[object_id] AND [ic].[index_id] = [i].[index_id] " +
        "INNER JOIN [sys].[columns] AS [c] ON [c].[object_id] = [t].[object_id] AND [c].[column_id] = [ic].[column_id] " +
        "WHERE [s].[name] = N'dbo' AND [i].[name] IS NOT NULL " +
        "ORDER BY [i].[object_id], [i].[index_id], [ic].[index_column_id]";

    private const string MigrationHistorySql =
        "SELECT [MigrationId], [ProductVersion] FROM [__EFMigrationsHistory]";

    private const string PreStageUserEmailsSql =
        "SELECT [Email], CAST(NULL AS nvarchar(255)) AS [NormalizedEmail] FROM [Users]";

    private const string PostStageUserEmailsSql =
        "SELECT [Email], [NormalizedEmail] FROM [Users]";

    private const string AccountInviteValidationSql =
        "SELECT [TokenHash], [NormalizedEmail], [IntendedRole], [Purpose], " +
        "[TargetMemberID], [TargetUserID], [CreatedAtUtc], [ExpiresAtUtc], " +
        "[UsedAtUtc], [RevokedAtUtc], [UsedByFirebaseUid], [RedemptionOperationId] " +
        "FROM [AccountInvites]";

    private const string SettingSql =
        "SELECT TOP (1) [SettingValue] FROM [SystemSettings] WHERE [SettingKey] = @settingKey";

    private static readonly IReadOnlyDictionary<PreflightCountQuery, string> CountSql =
        new Dictionary<PreflightCountQuery, string>
        {
            [PreflightCountQuery.DuplicateFirebaseUids] =
                "SELECT COUNT_BIG(*) FROM (SELECT [FirebaseUid] COLLATE Latin1_General_100_BIN2 AS [Value] " +
                "FROM [Users] WHERE [FirebaseUid] IS NOT NULL GROUP BY [FirebaseUid] COLLATE Latin1_General_100_BIN2 " +
                "HAVING COUNT_BIG(*) > 1) AS [DuplicateGroups]",
            [PreflightCountQuery.DuplicateMemberLinks] =
                "SELECT COUNT_BIG(*) FROM (SELECT [MemberID] FROM [Users] WHERE [MemberID] IS NOT NULL " +
                "GROUP BY [MemberID] HAVING COUNT_BIG(*) > 1) AS [DuplicateGroups]",
            [PreflightCountQuery.InvalidUserRoles] =
                "SELECT COUNT_BIG(*) FROM [Users] WHERE [Role] NOT IN (0, 1, 2)",
            [PreflightCountQuery.LegacyGymGoerRoles] =
                "SELECT COUNT_BIG(*) FROM [Users] WHERE [Role] = 2",
            [PreflightCountQuery.DuplicateInviteTokenHashes] =
                "SELECT COUNT_BIG(*) FROM (SELECT [TokenHash] FROM [AccountInvites] " +
                "GROUP BY [TokenHash] HAVING COUNT_BIG(*) > 1) AS [DuplicateGroups]",
            [PreflightCountQuery.DuplicateInviteRedemptionOperations] =
                "SELECT COUNT_BIG(*) FROM (SELECT [RedemptionOperationId] FROM [AccountInvites] " +
                "WHERE [RedemptionOperationId] IS NOT NULL GROUP BY [RedemptionOperationId] " +
                "HAVING COUNT_BIG(*) > 1) AS [DuplicateGroups]",
            [PreflightCountQuery.UnresolvedInvitesForUnavailableMembers] =
                "SELECT COUNT_BIG(*) FROM [AccountInvites] AS [i] LEFT JOIN [Members] AS [m] " +
                "ON [m].[MemberID] = [i].[TargetMemberID] WHERE [i].[TargetMemberID] IS NOT NULL " +
                "AND [i].[UsedAtUtc] IS NULL AND [i].[RevokedAtUtc] IS NULL " +
                "AND ([m].[MemberID] IS NULL OR [m].[IsDeleted] = 1)",
            [PreflightCountQuery.MissingProjectionVersions] =
                "SELECT COUNT_BIG(*) FROM [Members] AS [m] LEFT JOIN [MemberProjectionVersions] AS [v] " +
                "ON [v].[MemberID] = [m].[MemberID] WHERE [v].[MemberID] IS NULL",
            [PreflightCountQuery.AllMembers] =
                "SELECT COUNT_BIG(*) FROM [Members]",
            [PreflightCountQuery.DuplicateProjectionVersions] =
                "SELECT COUNT_BIG(*) FROM (SELECT [MemberID] FROM [MemberProjectionVersions] " +
                "GROUP BY [MemberID] HAVING COUNT_BIG(*) > 1) AS [DuplicateGroups]",
            [PreflightCountQuery.InvalidProjectionVersions] =
                "SELECT COUNT_BIG(*) FROM [MemberProjectionVersions] WHERE [Version] < 0 " +
                "OR [Version] > 2199023255551",
            [PreflightCountQuery.DeletedMembersWithActiveGymGoers] =
                "SELECT COUNT_BIG(*) FROM [Users] AS [u] INNER JOIN [Members] AS [m] " +
                "ON [m].[MemberID] = [u].[MemberID] WHERE [m].[IsDeleted] = 1 " +
                "AND [u].[Role] = 2 AND [u].[IsActive] = 1",
            [PreflightCountQuery.InvalidUserRoleMemberLinks] =
                "SELECT COUNT_BIG(*) FROM [Users] WHERE ([Role] = 2 AND [MemberID] IS NULL) " +
                "OR ([Role] IN (0, 1) AND [MemberID] IS NOT NULL)",
            [PreflightCountQuery.AttendanceDateTimeComponents] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] WHERE CAST([AttendanceDate] AS time) <> CAST('00:00:00' AS time)",
            [PreflightCountQuery.AllAttendanceRows] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs]",
            [PreflightCountQuery.AttendanceLocalDatesMissing] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] WHERE [AttendanceDateLocal] IS NULL",
            [PreflightCountQuery.AttendanceLocalDateMismatches] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] WHERE [AttendanceDateLocal] IS NOT NULL " +
                "AND CAST([AttendanceDate] AS date) <> [AttendanceDateLocal]",
            [PreflightCountQuery.AttendanceLocalDateDuplicates] =
                "SELECT COUNT_BIG(*) FROM (SELECT [MemberID], [AttendanceDateLocal] FROM [AttendanceLogs] " +
                "WHERE [AttendanceDateLocal] IS NOT NULL GROUP BY [MemberID], [AttendanceDateLocal] " +
                "HAVING COUNT_BIG(*) > 1) AS [DuplicateGroups]",
            [PreflightCountQuery.AttendanceLocalDateActiveDuplicates] =
                "SELECT COUNT_BIG(*) FROM (SELECT [MemberID], [AttendanceDateLocal] FROM [AttendanceLogs] " +
                "WHERE [AttendanceDateLocal] IS NOT NULL AND [IsVoided] = 0 " +
                "GROUP BY [MemberID], [AttendanceDateLocal] HAVING COUNT_BIG(*) > 1) AS [DuplicateGroups]",
            [PreflightCountQuery.MultipleOpenAttendanceSessions] =
                "SELECT COUNT_BIG(*) FROM (SELECT [MemberID] FROM [AttendanceLogs] " +
                "WHERE [CheckOutTime] IS NULL GROUP BY [MemberID] HAVING COUNT_BIG(*) > 1) AS [DuplicateGroups]",
            [PreflightCountQuery.MultipleOpenActiveAttendanceSessions] =
                "SELECT COUNT_BIG(*) FROM (SELECT [MemberID] FROM [AttendanceLogs] " +
                "WHERE [CheckOutTime] IS NULL AND [IsVoided] = 0 GROUP BY [MemberID] " +
                "HAVING COUNT_BIG(*) > 1) AS [DuplicateGroups]",
            [PreflightCountQuery.CheckoutNotAfterCheckin] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] WHERE [CheckOutTime] IS NOT NULL " +
                "AND [CheckOutTime] <= [CheckInTime]",
            [PreflightCountQuery.ActiveCheckoutNotAfterCheckin] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] WHERE [CheckOutTime] IS NOT NULL " +
                "AND [CheckOutTime] <= [CheckInTime] AND [IsVoided] = 0",
            [PreflightCountQuery.AttendanceMissingMembers] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] AS [a] LEFT JOIN [Members] AS [m] " +
                "ON [m].[MemberID] = [a].[MemberID] WHERE [m].[MemberID] IS NULL",
            [PreflightCountQuery.AttendanceForDeletedMembers] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] AS [a] INNER JOIN [Members] AS [m] " +
                "ON [m].[MemberID] = [a].[MemberID] WHERE [m].[IsDeleted] = 1",
            [PreflightCountQuery.OpenAttendanceForDeletedMembers] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] AS [a] INNER JOIN [Members] AS [m] " +
                "ON [m].[MemberID] = [a].[MemberID] WHERE [m].[IsDeleted] = 1 " +
                "AND [a].[CheckOutTime] IS NULL",
            [PreflightCountQuery.OpenActiveAttendanceForDeletedMembers] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] AS [a] INNER JOIN [Members] AS [m] " +
                "ON [m].[MemberID] = [a].[MemberID] WHERE [m].[IsDeleted] = 1 " +
                "AND [a].[CheckOutTime] IS NULL AND [a].[IsVoided] = 0",
            [PreflightCountQuery.InvalidAttendanceSupersession] =
                "SELECT COUNT_BIG(*) FROM [AttendanceLogs] AS [a] LEFT JOIN [AttendanceLogs] AS [s] " +
                "ON [s].[AttendanceID] = [a].[SupersededByAttendanceID] " +
                "WHERE [a].[SupersededByAttendanceID] IS NOT NULL AND ([s].[AttendanceID] IS NULL " +
                "OR [a].[SupersededByAttendanceID] = [a].[AttendanceID] OR [s].[MemberID] <> [a].[MemberID] " +
                "OR [a].[IsVoided] = 0)",
            [PreflightCountQuery.SubscriptionCalendarTimeComponents] =
                "SELECT COUNT_BIG(*) FROM [Subscriptions] WHERE CAST([StartDate] AS time) <> CAST('00:00:00' AS time) " +
                "OR CAST([EndDate] AS time) <> CAST('00:00:00' AS time) OR [EndDate] < [StartDate]"
        };

    private static readonly HashSet<string> ForbiddenFixedCatalogTokens = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "ALTER",
        "BACKUP",
        "BEGIN",
        "BULK",
        "CALL",
        "CHECKPOINT",
        "COMMIT",
        "CREATE",
        "DBCC",
        "DELETE",
        "DENY",
        "DISABLE",
        "DROP",
        "ENABLE",
        "EXEC",
        "EXECUTE",
        "EXTERNAL",
        "GRANT",
        "IMPERSONATE",
        "INSERT",
        "INTO",
        "KILL",
        "LOAD",
        "LOCK",
        "MERGE",
        "NEXT",
        "OPENDATASOURCE",
        "OPENQUERY",
        "OPENROWSET",
        "OPENXML",
        "PRINT",
        "RAISERROR",
        "RECONFIGURE",
        "RESTORE",
        "REVERT",
        "REVOKE",
        "ROLLBACK",
        "SAVE",
        "SET",
        "SHUTDOWN",
        "THROW",
        "TRUNCATE",
        "UPDATE",
        "UPDATETEXT",
        "USE",
        "WAITFOR",
        "WRITETEXT"
    };

    private readonly GymDbContext _dbContext;

    static SqlServerPreflightReadOnlyDataSource()
    {
        foreach (var sql in GetAllowListedSqlForTesting())
        {
            ValidateSelectOnlySql(sql);
        }
    }

    public SqlServerPreflightReadOnlyDataSource(GymDbContext dbContext)
    {
        _dbContext = dbContext;
        if (!string.Equals(
                dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.SqlServer",
                StringComparison.Ordinal))
        {
            throw new NotSupportedException("Migration preflight requires SQL Server.");
        }
    }

    public static IReadOnlyCollection<string> GetAllowListedSqlForTesting() =>
        new[]
        {
            ColumnSchemaSql,
            CheckConstraintSchemaSql,
            ForeignKeySchemaSql,
            IndexSchemaSql,
            MigrationHistorySql,
            PreStageUserEmailsSql,
            PostStageUserEmailsSql,
            AccountInviteValidationSql,
            SettingSql
        }.Concat(CountSql.Values).ToArray();

    internal static bool IsFixedCatalogQuerySelectOnlyForTesting(string? sql) =>
        IsFixedCatalogQuerySelectOnly(sql);

    public async Task<PreflightSchema> InspectSchemaAsync(
        CancellationToken cancellationToken = default)
    {
        var columns = new List<PreflightColumnMetadata>();
        var checks = new List<PreflightCheckConstraintMetadata>();
        var foreignKeyRows = new List<PreflightForeignKeyRow>();
        var indexRows = new List<PreflightIndexRow>();
        var migrations = new List<PreflightMigrationMetadata>();
        await UseOpenConnectionAsync(async connection =>
        {
            {
                await using var command = CreateReadCommand(connection, ColumnSchemaSql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    columns.Add(new PreflightColumnMetadata(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        Convert.ToInt32(
                            reader.GetValue(3),
                            System.Globalization.CultureInfo.InvariantCulture),
                        Convert.ToByte(
                            reader.GetValue(4),
                            System.Globalization.CultureInfo.InvariantCulture),
                        Convert.ToByte(
                            reader.GetValue(5),
                            System.Globalization.CultureInfo.InvariantCulture),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.GetBoolean(7),
                        reader.GetBoolean(8),
                        reader.GetBoolean(9),
                        reader.IsDBNull(10) ? null : reader.GetString(10),
                        reader.GetBoolean(11),
                        reader.GetBoolean(12),
                        reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                        reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                        reader.GetBoolean(15),
                        reader.GetBoolean(16),
                        reader.IsDBNull(17) ? null : reader.GetString(17),
                        reader.GetBoolean(18),
                        reader.GetBoolean(19),
                        reader.GetBoolean(20),
                        reader.GetBoolean(21),
                        reader.GetBoolean(22),
                        reader.GetBoolean(23),
                        Convert.ToByte(
                            reader.GetValue(24),
                            System.Globalization.CultureInfo.InvariantCulture),
                        reader.GetBoolean(25),
                        reader.GetBoolean(26),
                        reader.IsDBNull(27) ? null : reader.GetString(27),
                        reader.IsDBNull(28)
                            ? null
                            : Convert.ToByte(
                                reader.GetValue(28),
                                System.Globalization.CultureInfo.InvariantCulture),
                        reader.IsDBNull(29) ? null : reader.GetString(29),
                        reader.IsDBNull(30) ? null : reader.GetString(30),
                        reader.IsDBNull(31) ? null : reader.GetInt32(31),
                        reader.IsDBNull(32) ? null : reader.GetString(32)));
                }
            }

            {
                await using var command = CreateReadCommand(connection, CheckConstraintSchemaSql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    checks.Add(new PreflightCheckConstraintMetadata(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        !reader.GetBoolean(3),
                        !reader.GetBoolean(4)));
                }
            }

            {
                await using var command = CreateReadCommand(connection, ForeignKeySchemaSql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    foreignKeyRows.Add(new PreflightForeignKeyRow(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.GetString(6),
                        reader.GetString(7),
                        reader.GetBoolean(8),
                        !reader.GetBoolean(9),
                        reader.GetBoolean(10),
                        reader.GetInt32(11),
                        reader.GetString(12),
                        reader.GetString(13)));
                }
            }

            {
                await using var command = CreateReadCommand(connection, IndexSchemaSql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    indexRows.Add(new PreflightIndexRow(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.GetBoolean(6),
                        reader.GetBoolean(7),
                        reader.GetBoolean(8),
                        reader.GetBoolean(9),
                        reader.IsDBNull(10) ? null : reader.GetString(10),
                        reader.GetBoolean(11),
                        reader.GetBoolean(12),
                        reader.GetBoolean(13),
                        reader.GetString(14),
                        Convert.ToInt32(reader.GetValue(15), System.Globalization.CultureInfo.InvariantCulture),
                        reader.GetBoolean(16),
                        reader.GetBoolean(17),
                        Convert.ToInt32(reader.GetValue(18), System.Globalization.CultureInfo.InvariantCulture)));
                }
            }

            if (columns.Any(column =>
                    string.Equals(column.Table, "__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(column.Column, "MigrationId", StringComparison.OrdinalIgnoreCase))
                && columns.Any(column =>
                    string.Equals(column.Table, "__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(column.Column, "ProductVersion", StringComparison.OrdinalIgnoreCase)))
            {
                await using var command = CreateReadCommand(connection, MigrationHistorySql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    migrations.Add(new PreflightMigrationMetadata(
                        reader.GetString(0),
                        reader.GetString(1)));
                }
            }
        }, cancellationToken);

        var foreignKeys = foreignKeyRows
            .GroupBy(row => row.ObjectId)
            .Select(group =>
            {
                var first = group.First();
                return new PreflightForeignKeyMetadata(
                    first.ParentSchema,
                    first.ParentTable,
                    first.Name,
                    first.PrincipalSchema,
                    first.PrincipalTable,
                    first.DeleteAction,
                    first.UpdateAction,
                    first.IsDisabled,
                    first.IsTrusted,
                    first.IsNotForReplication,
                    group.OrderBy(row => row.Ordinal)
                        .Select(row => new PreflightForeignKeyColumnMetadata(
                            row.ParentColumn,
                            row.PrincipalColumn))
                        .ToArray());
            })
            .ToArray();
        var indexes = indexRows
            .GroupBy(row => (row.ObjectId, row.IndexId))
            .Select(group =>
            {
                var first = group.First();
                return new PreflightIndexMetadata(
                    first.Schema,
                    first.Table,
                    first.Name,
                    first.Type,
                    first.IsUnique,
                    first.IsPrimaryKey,
                    first.IsUniqueConstraint,
                    first.HasFilter,
                    first.Filter,
                    first.IgnoreDuplicateKeys,
                    first.IsDisabled,
                    first.IsHypothetical,
                    group.Where(row => row.KeyOrdinal > 0)
                        .OrderBy(row => row.KeyOrdinal)
                        .Select(row => new PreflightIndexKeyColumnMetadata(
                            row.Column,
                            row.IsDescending))
                        .ToArray(),
                    group.Where(row => row.IsIncluded)
                        .OrderBy(row => row.IndexColumnId)
                        .Select(row => row.Column)
                        .ToArray());
            })
            .ToArray();
        return new PreflightSchema(columns, checks, foreignKeys, indexes, migrations);
    }

    public async Task<long> CountAsync(
        PreflightCountQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!CountSql.TryGetValue(query, out var sql))
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        object? value = null;
        await UseOpenConnectionAsync(async connection =>
        {
            await using var command = CreateReadCommand(connection, sql);
            value = await command.ExecuteScalarAsync(cancellationToken);
        }, cancellationToken);
        return value is null or DBNull
            ? 0
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<UserEmailNormalizationCounts> CountUserEmailNormalizationAsync(
        bool normalizedEmailColumnExists,
        CancellationToken cancellationToken = default)
    {
        long missing = 0;
        long invalid = 0;
        long mismatched = 0;
        var canonicalCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var sql = normalizedEmailColumnExists ? PostStageUserEmailsSql : PreStageUserEmailsSql;

        await UseOpenConnectionAsync(async connection =>
        {
            await using var command = CreateReadCommand(connection, sql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var rawEmail = reader.IsDBNull(0) ? null : reader.GetString(0);
                var storedNormalizedEmail = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (!normalizedEmailColumnExists || string.IsNullOrWhiteSpace(storedNormalizedEmail))
                {
                    missing = checked(missing + 1);
                }

                if (!EmailNormalization.TryCanonicalize(
                        rawEmail,
                        out _,
                        out var normalizedEmail))
                {
                    invalid = checked(invalid + 1);
                    continue;
                }

                if (normalizedEmailColumnExists
                    && !string.Equals(storedNormalizedEmail, normalizedEmail, StringComparison.Ordinal))
                {
                    mismatched = checked(mismatched + 1);
                }

                canonicalCounts[normalizedEmail] = checked(
                    canonicalCounts.GetValueOrDefault(normalizedEmail) + 1);
            }
        }, cancellationToken);

        return new UserEmailNormalizationCounts(
            missing,
            invalid,
            mismatched,
            canonicalCounts.LongCount(item => item.Value > 1));
    }

    public async Task<long> CountMalformedAccountInvitesAsync(
        CancellationToken cancellationToken = default)
    {
        long malformed = 0;
        await UseOpenConnectionAsync(async connection =>
        {
            await using var command = CreateReadCommand(connection, AccountInviteValidationSql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new PreflightAccountInviteRow(
                    reader.IsDBNull(0) ? null : reader.GetValue(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2)
                        ? int.MinValue
                        : Convert.ToInt32(
                            reader.GetValue(2),
                            System.Globalization.CultureInfo.InvariantCulture),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetGuid(11));
                if (IsMalformedAccountInvite(row))
                {
                    malformed = checked(malformed + 1);
                }
            }
        }, cancellationToken);

        return malformed;
    }

    internal static bool IsMalformedAccountInvite(PreflightAccountInviteRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var hasCompleteUsedMetadata =
            (!row.UsedAtUtc.HasValue
                && row.UsedByFirebaseUid is null
                && !row.RedemptionOperationId.HasValue)
            || (row.UsedAtUtc.HasValue
                && row.UsedByFirebaseUid is not null
                && row.RedemptionOperationId.HasValue
                && row.RedemptionOperationId.Value != Guid.Empty);
        var hasExactlyOneTarget = row.TargetMemberId.HasValue != row.TargetUserId.HasValue;
        var targetRoleIsValid = row.TargetMemberId.HasValue
            ? row.IntendedRole == 2
            : row.TargetUserId.HasValue && row.IntendedRole is 0 or 1;
        var normalizedEmailIsCanonical = EmailNormalization.TryNormalize(
                row.NormalizedEmail,
                out var recomputedNormalizedEmail)
            && string.Equals(
                row.NormalizedEmail,
                recomputedNormalizedEmail,
                StringComparison.Ordinal);
        var canonicalPurpose = row.Purpose is null
            ? null
            : row.Purpose.Trim().Normalize(System.Text.NormalizationForm.FormKC);
        var purposeIsCanonical = row.Purpose is not null
            && row.Purpose.Length is >= 1 and <= 100
            && !row.Purpose.Any(char.IsControl)
            && string.Equals(row.Purpose, canonicalPurpose, StringComparison.Ordinal);
        var usedUidIsValid = row.UsedByFirebaseUid is null
            || FirebaseIdentityValidation.TryValidateUid(row.UsedByFirebaseUid);

        return row.TokenHash is not byte[] { Length: 32 }
            || !normalizedEmailIsCanonical
            || !purposeIsCanonical
            || !hasExactlyOneTarget
            || !targetRoleIsValid
            || !row.CreatedAtUtc.HasValue
            || !row.ExpiresAtUtc.HasValue
            || row.CreatedAtUtc.HasValue
                && row.ExpiresAtUtc.HasValue
                && row.ExpiresAtUtc.Value <= row.CreatedAtUtc.Value
            || row.UsedAtUtc.HasValue
                && row.CreatedAtUtc.HasValue
                && row.ExpiresAtUtc.HasValue
                && (row.UsedAtUtc.Value < row.CreatedAtUtc.Value
                    || row.UsedAtUtc.Value >= row.ExpiresAtUtc.Value)
            || row.RevokedAtUtc.HasValue
                && row.CreatedAtUtc.HasValue
                && row.RevokedAtUtc.Value < row.CreatedAtUtc.Value
            || row.UsedAtUtc.HasValue && row.RevokedAtUtc.HasValue
            || !hasCompleteUsedMetadata
            || !usedUidIsValid;
    }

    public async Task<string?> GetSystemSettingValueAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        object? value = null;
        await UseOpenConnectionAsync(async connection =>
        {
            await using var command = CreateReadCommand(connection, SettingSql);
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@settingKey";
            parameter.DbType = DbType.String;
            parameter.Size = 100;
            parameter.Value = key;
            command.Parameters.Add(parameter);
            value = await command.ExecuteScalarAsync(cancellationToken);
        }, cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private async Task UseOpenConnectionAsync(
        Func<DbConnection, Task> operation,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await operation(connection);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static DbCommand CreateReadCommand(DbConnection connection, string sql)
    {
        ValidateSelectOnlySql(sql);
        var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        return command;
    }

    private static void ValidateSelectOnlySql(string sql)
    {
        if (!IsFixedCatalogQuerySelectOnly(sql))
        {
            throw new InvalidOperationException(
                "The migration preflight SQL catalog contains a non-read-only statement.");
        }
    }

    private static bool IsFixedCatalogQuerySelectOnly(string? sql)
    {
        // Defense in depth for this executable's immutable, hardcoded query
        // catalog only. This intentionally conservative scanner is not a
        // general-purpose SQL parser or an authorization boundary.
        if (string.IsNullOrWhiteSpace(sql)
            || !TryTokenizeFixedCatalogQuery(sql, out var tokens)
            || tokens.Count == 0
            || HasExternalMultipartIdentifier(sql)
            || tokens.Any(token => ForbiddenFixedCatalogTokens.Contains(token.Value)))
        {
            return false;
        }

        var rootTokens = tokens.Where(token => token.Depth == 0).ToArray();
        if (rootTokens.Length == 0)
        {
            return false;
        }

        var rootSelectCount = rootTokens.Count(token =>
            string.Equals(token.Value, "SELECT", StringComparison.OrdinalIgnoreCase));
        if (string.Equals(
                rootTokens[0].Value,
                "SELECT",
                StringComparison.OrdinalIgnoreCase))
        {
            return rootSelectCount == 1
                && !rootTokens.Any(token => string.Equals(
                    token.Value,
                    "WITH",
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(
                rootTokens[0].Value,
                "WITH",
                StringComparison.OrdinalIgnoreCase)
            || rootSelectCount != 1
            || rootTokens.Count(token => string.Equals(
                token.Value,
                "WITH",
                StringComparison.OrdinalIgnoreCase)) != 1)
        {
            return false;
        }

        var rootSelectIndex = Array.FindIndex(rootTokens, token => string.Equals(
            token.Value,
            "SELECT",
            StringComparison.OrdinalIgnoreCase));
        return rootSelectIndex > 0
            && rootTokens.Take(rootSelectIndex).Any(token => string.Equals(
                token.Value,
                "AS",
                StringComparison.OrdinalIgnoreCase))
            && tokens.Any(token => token.Depth > 0 && string.Equals(
                token.Value,
                "SELECT",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryTokenizeFixedCatalogQuery(
        string sql,
        out IReadOnlyList<FixedCatalogToken> tokens)
    {
        var collected = new List<FixedCatalogToken>();
        tokens = collected;
        var depth = 0;
        var index = 0;
        while (index < sql.Length)
        {
            var current = sql[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '\'' || current == '"')
            {
                if (!TrySkipQuotedValue(sql, ref index, current))
                {
                    return false;
                }

                continue;
            }

            if (current == '[')
            {
                if (!TrySkipBracketedIdentifier(sql, ref index))
                {
                    return false;
                }

                continue;
            }

            if ((current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
                || (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
                || (current == '*' && index + 1 < sql.Length && sql[index + 1] == '/'))
            {
                return false;
            }

            if (current == '(')
            {
                depth++;
                index++;
                continue;
            }

            if (current == ')')
            {
                if (depth == 0)
                {
                    return false;
                }

                depth--;
                index++;
                continue;
            }

            if (current == ';')
            {
                return depth == 0
                    && sql.Skip(index + 1).All(char.IsWhiteSpace);
            }

            if (char.IsLetter(current) || current == '_')
            {
                var start = index++;
                while (index < sql.Length
                       && (char.IsLetterOrDigit(sql[index]) || sql[index] == '_'))
                {
                    index++;
                }

                collected.Add(new FixedCatalogToken(
                    sql[start..index].ToUpperInvariant(),
                    depth));
                continue;
            }

            index++;
        }

        return depth == 0;
    }

    private static bool HasExternalMultipartIdentifier(string sql)
    {
        var index = 0;
        while (index < sql.Length)
        {
            if (sql[index] == '\'')
            {
                TrySkipQuotedValue(sql, ref index, '\'');
                continue;
            }

            if (!TrySkipIdentifierPart(sql, ref index))
            {
                index++;
                continue;
            }

            var cursor = index;
            var separatorCount = 0;
            while (cursor < sql.Length)
            {
                while (cursor < sql.Length && char.IsWhiteSpace(sql[cursor]))
                {
                    cursor++;
                }

                if (cursor >= sql.Length || sql[cursor] != '.')
                {
                    break;
                }

                separatorCount++;
                if (separatorCount >= 2)
                {
                    return true;
                }

                cursor++;
                while (cursor < sql.Length && char.IsWhiteSpace(sql[cursor]))
                {
                    cursor++;
                }

                if (!TrySkipIdentifierPart(sql, ref cursor))
                {
                    // An immediate second dot represents an empty identifier
                    // part (for example server..object). Leave it for the next
                    // iteration so it counts as another qualification boundary.
                    if (cursor < sql.Length && sql[cursor] == '.')
                    {
                        continue;
                    }

                    break;
                }
            }

            index = Math.Max(index, cursor);
        }

        return false;
    }

    private static bool TrySkipIdentifierPart(string sql, ref int index)
    {
        if (index >= sql.Length)
        {
            return false;
        }

        if (sql[index] == '[')
        {
            return TrySkipBracketedIdentifier(sql, ref index);
        }

        if (sql[index] == '"')
        {
            return TrySkipQuotedValue(sql, ref index, '"');
        }

        if (!char.IsLetter(sql[index])
            && sql[index] != '_'
            && sql[index] != '#'
            && sql[index] != '@')
        {
            return false;
        }

        index++;
        while (index < sql.Length
               && (char.IsLetterOrDigit(sql[index])
                   || sql[index] == '_'
                   || sql[index] == '$'
                   || sql[index] == '#'
                   || sql[index] == '@'))
        {
            index++;
        }

        return true;
    }

    private static bool TrySkipQuotedValue(
        string sql,
        ref int index,
        char delimiter)
    {
        index++;
        while (index < sql.Length)
        {
            if (sql[index] != delimiter)
            {
                index++;
                continue;
            }

            if (index + 1 < sql.Length && sql[index + 1] == delimiter)
            {
                index += 2;
                continue;
            }

            index++;
            return true;
        }

        return false;
    }

    private static bool TrySkipBracketedIdentifier(string sql, ref int index)
    {
        index++;
        while (index < sql.Length)
        {
            if (sql[index] != ']')
            {
                index++;
                continue;
            }

            if (index + 1 < sql.Length && sql[index + 1] == ']')
            {
                index += 2;
                continue;
            }

            index++;
            return true;
        }

        return false;
    }

    private readonly record struct FixedCatalogToken(string Value, int Depth);
}
