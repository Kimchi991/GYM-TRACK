using GymTrackPro.API.Maintenance;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class PreflightFixedCatalogQueryTests
{
    [Fact]
    public void Every_hardcoded_catalog_query_passes_the_runtime_validator()
    {
        foreach (var sql in SqlServerPreflightReadOnlyDataSource.GetAllowListedSqlForTesting())
        {
            Assert.True(
                SqlServerPreflightReadOnlyDataSource
                    .IsFixedCatalogQuerySelectOnlyForTesting(sql),
                sql);
        }
    }

    [Fact]
    public void Attendance_queries_target_only_reviewed_legacy_or_final_columns()
    {
        var sql = SqlServerPreflightReadOnlyDataSource.GetAllowListedSqlForTesting();

        Assert.DoesNotContain(sql, statement =>
            statement.Contains("AttendanceDateLocal", StringComparison.Ordinal));
        Assert.Contains(sql, statement =>
            statement.Contains(
                "CAST([AttendanceDate] AS date) AS [GymDate]",
                StringComparison.Ordinal));
        Assert.Contains(sql, statement =>
            statement.Contains(
                "CONVERT(date, [AttendanceDateLegacyDateTime]) <> [AttendanceDate]",
                StringComparison.Ordinal));
        Assert.Contains(sql, statement =>
            statement.Contains(
                "GROUP BY [MemberID], [AttendanceDate]",
                StringComparison.Ordinal));
        Assert.Contains(sql, statement =>
            statement.Contains(
                "[CheckOutTime] <= [CheckInTime]",
                StringComparison.Ordinal)
            && statement.Contains("[IsVoided] = 0", StringComparison.Ordinal));
        Assert.Contains(sql, statement =>
            statement.Contains(
                "[s].[AttendanceDate] <> [a].[AttendanceDate]",
                StringComparison.Ordinal)
            && statement.Contains("[s].[IsVoided] = 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Column_catalog_query_exposes_security_and_storage_metadata_in_reader_order()
    {
        var sql = SqlServerPreflightReadOnlyDataSource.GetAllowListedSqlForTesting()
            .Single(value => value.Contains("[sys].[identity_columns]", StringComparison.Ordinal));
        var orderedFragments = new[]
        {
            "[t].[name]",
            "[c].[name]",
            "[ty].[name]",
            "[c].[max_length]",
            "[c].[precision]",
            "[c].[scale]",
            "[c].[collation_name]",
            "DATABASEPROPERTYEX",
            "[c].[is_nullable]",
            "[c].[default_object_id]",
            "[dc].[definition]",
            "[c].[system_type_id]",
            "[c].[is_identity]",
            "[ic].[seed_value]",
            "[ic].[increment_value]",
            "[ic].[is_not_for_replication]",
            "[c].[is_computed]",
            "[cc].[definition]",
            "[cc].[is_persisted]",
            "[c].[is_rowguidcol]",
            "[c].[is_filestream]",
            "[c].[is_ansi_padded]",
            "[c].[is_sparse]",
            "[c].[is_column_set]",
            "[c].[generated_always_type]",
            "[c].[is_hidden]",
            "[c].[is_masked]",
            "[c].[masking_function]",
            "[c].[encryption_type]",
            "[c].[encryption_type_desc]",
            "[c].[encryption_algorithm_name]",
            "[c].[column_encryption_key_id]",
            "[c].[column_encryption_key_database_name]"
        };

        var cursor = 0;
        foreach (var fragment in orderedFragments)
        {
            var position = sql.IndexOf(fragment, cursor, StringComparison.Ordinal);
            Assert.True(position >= cursor, $"Missing or reordered catalog fragment: {fragment}");
            cursor = position + fragment.Length;
        }

        Assert.Contains("LEFT JOIN [sys].[computed_columns]", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT [Value] FROM [ReadModel]")]
    [InlineData("SELECT (SELECT COUNT_BIG(*) FROM [ReadModel]) AS [Value]")]
    [InlineData("WITH [rows] AS (SELECT [Value] FROM [ReadModel]) SELECT [Value] FROM [rows]")]
    [InlineData("SELECT 'DROP; -- text only', [UPDATE], \"DELETE\" FROM [ReadModel];")]
    [InlineData("SELECT [t].[name] FROM [sys].[tables] AS [t]")]
    [InlineData("SELECT 'Server.Database.Schema.Object', [a.b.c], \"x.y.z\"")]
    public void Conservative_fixed_catalog_shapes_accept_one_read_statement(string sql)
    {
        Assert.True(SqlServerPreflightReadOnlyDataSource
            .IsFixedCatalogQuerySelectOnlyForTesting(sql));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("; SELECT 1")]
    [InlineData("SELECT 1;;")]
    [InlineData("SELECT 1 SELECT 2")]
    [InlineData("SELECT * INTO [Copy] FROM [Users]")]
    [InlineData("WITH [rows] AS (SELECT 1) DELETE FROM [Users]")]
    [InlineData("WITH [rows] AS (SELECT 1)")]
    [InlineData("WITH SELECT 1")]
    [InlineData("SELECT 1 -- hidden second statement")]
    [InlineData("SELECT /* hidden token */ 1")]
    [InlineData("SELECT 1 */")]
    [InlineData("SELECT (1")]
    [InlineData("SELECT 1)")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("SELECT [unterminated")]
    [InlineData("SELECT \"unterminated")]
    public void Fixed_catalog_validator_rejects_multiple_malformed_or_obscured_statements(
        string sql)
    {
        Assert.False(SqlServerPreflightReadOnlyDataSource
            .IsFixedCatalogQuerySelectOnlyForTesting(sql));
    }

    [Theory]
    [InlineData("SELECT * FROM [LinkedServer].[Db].[dbo].[Users]")]
    [InlineData("SELECT * FROM OtherDb.dbo.Users")]
    [InlineData("SELECT * FROM [OtherDb]..[Users]")]
    [InlineData("SELECT * FROM LinkedServer..dbo.Users")]
    [InlineData("SELECT * FROM [Server].Db.\"dbo\".[Users]")]
    [InlineData("SELECT [Server].[Db].[dbo].[Users].[Email]")]
    [InlineData("SELECT * FROM \"Server\".[Database].dbo.[Users]")]
    public void Fixed_catalog_validator_rejects_cross_database_linked_server_and_empty_part_names(
        string sql)
    {
        Assert.False(SqlServerPreflightReadOnlyDataSource
            .IsFixedCatalogQuerySelectOnlyForTesting(sql));
    }

    [Theory]
    [MemberData(nameof(ForbiddenStatements))]
    public void Fixed_catalog_validator_rejects_mutation_admin_security_and_external_tokens(
        string sql)
    {
        Assert.False(SqlServerPreflightReadOnlyDataSource
            .IsFixedCatalogQuerySelectOnlyForTesting(sql));
    }

    public static IEnumerable<object[]> ForbiddenStatements()
    {
        foreach (var token in new[]
                 {
                     "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "EXECUTE",
                     "DROP", "ALTER", "CREATE", "TRUNCATE", "ENABLE", "DISABLE",
                     "GRANT", "REVOKE", "DENY", "IMPERSONATE", "REVERT",
                     "DBCC", "BULK", "OPENROWSET", "OPENDATASOURCE", "OPENQUERY",
                     "OPENXML", "EXTERNAL", "BACKUP", "RESTORE", "KILL",
                     "SHUTDOWN", "RECONFIGURE", "USE", "SET", "WAITFOR",
                     "CHECKPOINT", "BEGIN", "COMMIT", "ROLLBACK", "SAVE", "LOCK",
                     "CALL", "LOAD", "PRINT", "RAISERROR", "THROW", "UPDATETEXT",
                     "WRITETEXT", "NEXT", "INTO"
                 })
        {
            yield return new object[] { $"SELECT 1 {token}" };
        }
    }
}
