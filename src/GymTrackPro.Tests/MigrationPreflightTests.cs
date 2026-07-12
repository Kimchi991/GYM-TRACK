using System.Text.RegularExpressions;
using GymTrackPro.API.Maintenance;
using GymTrackPro.Preflight;
using Microsoft.Data.SqlClient;

namespace GymTrackPro.Tests;

public sealed class MigrationPreflightTests
{
    [Fact]
    public async Task Runner_reports_only_controlled_categories_and_counts()
    {
        var source = new FakePreflightDataSource(CreateStagedSchema())
        {
            Timezone = "Asia/Manila",
            StaleSessionHours = "16"
        };
        source.Counts[PreflightCountQuery.DuplicateFirebaseUids] = 2;
        source.MalformedInviteCount = 3;
        source.Counts[PreflightCountQuery.InvalidUserRoles] = 6;
        source.Counts[PreflightCountQuery.DuplicateInviteTokenHashes] = 7;
        source.Counts[PreflightCountQuery.DuplicateInviteRedemptionOperations] = 8;
        source.Counts[PreflightCountQuery.InvalidProjectionVersions] = 9;
        source.Counts[PreflightCountQuery.MissingProjectionVersions] = 4;
        source.Counts[PreflightCountQuery.MultipleOpenAttendanceSessions] = 5;
        var runner = new MigrationPreflightRunner(source);

        var report = await runner.RunAsync(PreflightStageMode.PostStage);
        var output = MigrationPreflightReportFormatter.Format(report);

        Assert.True(report.HasBlockingFindings);
        Assert.Equal(2, Find(report, MigrationPreflightCategories.DuplicateFirebaseUids));
        Assert.Equal(3, Find(report, MigrationPreflightCategories.MalformedAccountInvites));
        Assert.Equal(6, Find(report, MigrationPreflightCategories.InvalidUserRoles));
        Assert.Equal(7, Find(report, MigrationPreflightCategories.DuplicateInviteTokenHashes));
        Assert.Equal(8, Find(report, MigrationPreflightCategories.DuplicateInviteRedemptionOperations));
        Assert.Equal(9, Find(report, MigrationPreflightCategories.InvalidProjectionVersions));
        Assert.Equal(4, Find(report, MigrationPreflightCategories.MissingProjectionVersions));
        Assert.Equal(5, Find(report, MigrationPreflightCategories.MultipleOpenAttendanceSessions));
        Assert.DoesNotContain("@", output, StringComparison.Ordinal);
        Assert.DoesNotContain("uid-", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_staged_tables_are_reported_without_querying_them()
    {
        var schema = CreatePreStageSchema();
        var source = new FakePreflightDataSource(schema);
        source.NormalizationCounts = new UserEmailNormalizationCounts(0, 0, 0, 1);
        source.Counts[PreflightCountQuery.AllMembers] = 7;
        source.Counts[PreflightCountQuery.AllAttendanceRows] = 8;
        var report = await new MigrationPreflightRunner(source).RunAsync(
            PreflightStageMode.PreStage);

        Assert.Equal(0, Find(report, MigrationPreflightCategories.IdentitySchemaNotStaged));
        Assert.Equal(1, Find(report, MigrationPreflightCategories.DuplicateNormalizedEmails));
        Assert.Equal(7, Find(report, MigrationPreflightCategories.MissingProjectionVersions));
        Assert.Equal(8, Find(report, MigrationPreflightCategories.AttendanceLocalDatesMissing));
        Assert.False(report.Findings.Single(finding =>
            finding.Category == MigrationPreflightCategories.MissingProjectionVersions).IsBlocking);
        Assert.False(report.Findings.Single(finding =>
            finding.Category == MigrationPreflightCategories.AttendanceLocalDatesMissing).IsBlocking);
        Assert.Equal(0, source.MalformedInviteValidationCalls);
        Assert.DoesNotContain(PreflightCountQuery.DuplicateFirebaseUids, source.ExecutedQueries);
    }

    [Fact]
    public async Task Invalid_configuration_values_become_counts_not_output_values()
    {
        var source = new FakePreflightDataSource(CreateStagedSchema())
        {
            Timezone = "private-server-value",
            StaleSessionHours = "connection-secret-value"
        };

        var report = await new MigrationPreflightRunner(source).RunAsync(
            PreflightStageMode.PostStage);
        var output = MigrationPreflightReportFormatter.Format(report);

        Assert.Equal(1, Find(report, MigrationPreflightCategories.TimezoneConfigurationInvalid));
        Assert.Equal(1, Find(report, MigrationPreflightCategories.StaleSessionConfigurationInvalid));
        Assert.DoesNotContain(source.Timezone, output, StringComparison.Ordinal);
        Assert.DoesNotContain(source.StaleSessionHours, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_invite_storage_still_runs_row_integrity_count()
    {
        var columns = CreateStagedSchemaColumns().ToList();
        columns.RemoveAll(column =>
            column.Table == "AccountInvites" && column.Column == "TokenHash");
        columns.Add(Column("AccountInvites", "TokenHash", "char", 44));
        var source = new FakePreflightDataSource(CreateStagedSchemaWithColumns(columns))
        {
            Timezone = "Asia/Manila",
            StaleSessionHours = "16"
        };
        source.MalformedInviteCount = 9;

        var report = await new MigrationPreflightRunner(source).RunAsync(
            PreflightStageMode.PostStage);

        Assert.Equal(1, Find(report, MigrationPreflightCategories.IdentitySchemaNotStaged));
        Assert.Equal(1, Find(report, MigrationPreflightCategories.MigrationHistorySchemaMismatch));
        Assert.Equal(9, Find(report, MigrationPreflightCategories.MalformedAccountInvites));
        Assert.Equal(1, source.MalformedInviteValidationCalls);
    }

    [Fact]
    public async Task Expected_pre_migration_backfill_counts_are_informational()
    {
        var schema = CreatePreStageSchema();
        var source = new FakePreflightDataSource(schema)
        {
            Timezone = "Asia/Manila",
            StaleSessionHours = null
        };
        source.Counts[PreflightCountQuery.AllMembers] = 12;

        var report = await new MigrationPreflightRunner(source).RunAsync(
            PreflightStageMode.PreStage);
        var output = MigrationPreflightReportFormatter.Format(report);

        Assert.False(report.HasBlockingFindings);
        Assert.Equal(12, Find(report, MigrationPreflightCategories.MissingProjectionVersions));
        Assert.Contains("PREFLIGHT_STATUS=PASS", output, StringComparison.Ordinal);
        Assert.Contains("SEVERITY=INFO", output, StringComparison.Ordinal);
        Assert.False(report.Findings.Single(finding =>
            finding.Category == MigrationPreflightCategories.StaleSessionConfigurationInvalid).IsBlocking);
    }

    [Fact]
    public async Task Exact_pre_and_post_stage_fixtures_pass_only_in_their_declared_mode()
    {
        var preSource = ValidSource(CreatePreStageSchema());
        var preReport = await new MigrationPreflightRunner(preSource).RunAsync(
            PreflightStageMode.PreStage);
        Assert.False(preReport.HasBlockingFindings);
        Assert.Contains("PREFLIGHT_MODE=PRE_STAGE", MigrationPreflightReportFormatter.Format(preReport));

        var postSource = ValidSource(CreateStagedSchema());
        var postReport = await new MigrationPreflightRunner(postSource).RunAsync(
            PreflightStageMode.PostStage);
        Assert.False(postReport.HasBlockingFindings);
        Assert.Contains("PREFLIGHT_MODE=POST_STAGE", MigrationPreflightReportFormatter.Format(postReport));

        var wrongModeReport = await new MigrationPreflightRunner(ValidSource(CreateStagedSchema()))
            .RunAsync(PreflightStageMode.PreStage);
        Assert.Equal(1, Find(wrongModeReport, MigrationPreflightCategories.MigrationHistoryMismatch));
        Assert.Equal(1, Find(wrongModeReport, MigrationPreflightCategories.B1SchemaStateMismatch));
    }

    [Fact]
    public async Task Post_stage_rejects_missing_B1_or_unexpected_newer_migration_history_rows()
    {
        var missingB1Report = await new MigrationPreflightRunner(
                ValidSource(CreateStagedSchemaWithHistory(CreatePreStageHistory())))
            .RunAsync(PreflightStageMode.PostStage);

        Assert.Equal(1, Find(
            missingB1Report,
            MigrationPreflightCategories.MigrationHistoryMismatch));

        var unexpectedHistory = CreatePostStageHistory()
            .Append(Migration("20260713000000_UnexpectedFutureMigration"))
            .ToArray();
        var unexpectedHistoryReport = await new MigrationPreflightRunner(
                ValidSource(CreateStagedSchemaWithHistory(unexpectedHistory)))
            .RunAsync(PreflightStageMode.PostStage);

        Assert.Equal(1, Find(
            unexpectedHistoryReport,
            MigrationPreflightCategories.MigrationHistoryMismatch));
    }

    [Theory]
    [InlineData("wrong-check")]
    [InlineData("legacy-redemption-check")]
    [InlineData("missing-index")]
    [InlineData("missing-foreign-key")]
    [InlineData("wrong-default")]
    [InlineData("invalid-rowversion")]
    [InlineData("column-wrong-data-type")]
    [InlineData("column-wrong-max-length")]
    [InlineData("column-wrong-precision")]
    [InlineData("column-wrong-scale")]
    [InlineData("column-wrong-nullability")]
    [InlineData("column-wrong-explicit-collation")]
    [InlineData("column-wrong-default-collation")]
    [InlineData("column-extra-default")]
    [InlineData("column-missing-default")]
    [InlineData("column-nonidentity")]
    [InlineData("column-identity-seed")]
    [InlineData("column-identity-increment")]
    [InlineData("column-identity-not-for-replication")]
    [InlineData("column-computed")]
    [InlineData("column-computed-persisted")]
    [InlineData("column-computed-definition")]
    [InlineData("column-rowguid")]
    [InlineData("column-filestream")]
    [InlineData("column-ansi-padding")]
    [InlineData("column-sparse")]
    [InlineData("column-column-set")]
    [InlineData("column-generated-always")]
    [InlineData("column-hidden")]
    [InlineData("column-masked")]
    [InlineData("column-masking-function")]
    [InlineData("column-encryption-type")]
    [InlineData("column-encryption-description")]
    [InlineData("column-encryption-algorithm")]
    [InlineData("column-encryption-key-id")]
    [InlineData("column-encryption-key-database")]
    [InlineData("pk-extra-key")]
    [InlineData("pk-wrong-schema")]
    [InlineData("pk-wrong-table")]
    [InlineData("pk-wrong-name")]
    [InlineData("pk-descending-key")]
    [InlineData("pk-included-column")]
    [InlineData("pk-disabled")]
    [InlineData("pk-hypothetical")]
    [InlineData("pk-ignore-duplicate-keys")]
    [InlineData("pk-wrong-type")]
    [InlineData("pk-not-unique")]
    [InlineData("pk-not-primary")]
    [InlineData("pk-unique-constraint")]
    [InlineData("pk-filtered")]
    [InlineData("index-extra-key")]
    [InlineData("index-wrong-schema")]
    [InlineData("index-wrong-table")]
    [InlineData("index-wrong-name")]
    [InlineData("index-descending-key")]
    [InlineData("index-included-column")]
    [InlineData("index-disabled")]
    [InlineData("index-hypothetical")]
    [InlineData("index-ignore-duplicate-keys")]
    [InlineData("index-wrong-type")]
    [InlineData("index-not-unique")]
    [InlineData("index-primary-flag")]
    [InlineData("index-unique-constraint")]
    [InlineData("index-filter-flag")]
    [InlineData("index-filter-definition")]
    [InlineData("duplicate-index-object")]
    [InlineData("foreign-key-extra-pair")]
    [InlineData("foreign-key-wrong-parent-table")]
    [InlineData("foreign-key-wrong-name")]
    [InlineData("foreign-key-wrong-parent-column")]
    [InlineData("foreign-key-wrong-principal-column")]
    [InlineData("foreign-key-wrong-parent-schema")]
    [InlineData("foreign-key-wrong-principal-schema")]
    [InlineData("foreign-key-wrong-principal-table")]
    [InlineData("foreign-key-wrong-delete-action")]
    [InlineData("foreign-key-wrong-update-action")]
    [InlineData("foreign-key-disabled")]
    [InlineData("foreign-key-untrusted")]
    [InlineData("foreign-key-not-for-replication")]
    [InlineData("duplicate-foreign-key-object")]
    public async Task Post_stage_rejects_every_required_schema_fingerprint_drift(string fault)
    {
        var report = await new MigrationPreflightRunner(
                ValidSource(CreateBrokenPostStageSchema(fault)))
            .RunAsync(PreflightStageMode.PostStage);

        Assert.Equal(1, Find(report, MigrationPreflightCategories.B1SchemaStateMismatch));
        Assert.Equal(1, Find(report, MigrationPreflightCategories.MigrationHistorySchemaMismatch));
    }

    [Fact]
    public void Composite_index_fingerprint_requires_exact_order_direction_and_cardinality()
    {
        var exact = Index("Example", "IX_Example_Composite", false, null, "FirstKey", "SecondKey");
        var exactPrimaryKey = PrimaryKey(
            "Example",
            "PK_Example",
            "FirstKey",
            "SecondKey");

        Assert.True(new PreflightSchema(Array.Empty<PreflightColumnMetadata>(), indexes: new[] { exact })
            .HasIndex("Example", "IX_Example_Composite", false, null, "FirstKey", "SecondKey"));
        Assert.True(new PreflightSchema(
                Array.Empty<PreflightColumnMetadata>(),
                indexes: new[] { exactPrimaryKey })
            .HasPrimaryKey("Example", "PK_Example", "FirstKey", "SecondKey"));

        var reordered = exact with { KeyColumns = exact.KeyColumns.Reverse().ToArray() };
        var descending = exact with
        {
            KeyColumns = new[]
            {
                new PreflightIndexKeyColumnMetadata("FirstKey", true),
                new PreflightIndexKeyColumnMetadata("SecondKey", false)
            }
        };
        var extra = exact with
        {
            KeyColumns = exact.KeyColumns
                .Append(new PreflightIndexKeyColumnMetadata("ThirdKey", false))
                .ToArray()
        };
        var included = exact with { IncludedColumns = new[] { "Payload" } };
        var reorderedPrimaryKey = exactPrimaryKey with
        {
            KeyColumns = exactPrimaryKey.KeyColumns.Reverse().ToArray()
        };

        Assert.False(new PreflightSchema(Array.Empty<PreflightColumnMetadata>(), indexes: new[] { reordered })
            .HasIndex("Example", "IX_Example_Composite", false, null, "FirstKey", "SecondKey"));
        Assert.False(new PreflightSchema(Array.Empty<PreflightColumnMetadata>(), indexes: new[] { descending })
            .HasIndex("Example", "IX_Example_Composite", false, null, "FirstKey", "SecondKey"));
        Assert.False(new PreflightSchema(Array.Empty<PreflightColumnMetadata>(), indexes: new[] { extra })
            .HasIndex("Example", "IX_Example_Composite", false, null, "FirstKey", "SecondKey"));
        Assert.False(new PreflightSchema(Array.Empty<PreflightColumnMetadata>(), indexes: new[] { included })
            .HasIndex("Example", "IX_Example_Composite", false, null, "FirstKey", "SecondKey"));
        Assert.False(new PreflightSchema(
                Array.Empty<PreflightColumnMetadata>(),
                indexes: new[] { reorderedPrimaryKey })
            .HasPrimaryKey("Example", "PK_Example", "FirstKey", "SecondKey"));
    }

    [Fact]
    public void Composite_foreign_key_fingerprint_requires_exact_ordered_column_pairs()
    {
        var exact = new PreflightForeignKeyMetadata(
            "dbo",
            "Child",
            "FK_Child_Parent",
            "dbo",
            "Parent",
            "NO_ACTION",
            "NO_ACTION",
            false,
            true,
            false,
            new[]
            {
                new PreflightForeignKeyColumnMetadata("ParentKeyA", "KeyA"),
                new PreflightForeignKeyColumnMetadata("ParentKeyB", "KeyB")
            });
        var expectedPairs = exact.Columns.ToArray();

        Assert.True(new PreflightSchema(Array.Empty<PreflightColumnMetadata>(), foreignKeys: new[] { exact })
            .HasForeignKey("Child", "FK_Child_Parent", "Parent", "NO_ACTION", expectedPairs));

        var reordered = exact with { Columns = exact.Columns.Reverse().ToArray() };
        var extra = exact with
        {
            Columns = exact.Columns
                .Append(new PreflightForeignKeyColumnMetadata("ParentKeyC", "KeyC"))
                .ToArray()
        };

        Assert.False(new PreflightSchema(Array.Empty<PreflightColumnMetadata>(), foreignKeys: new[] { reordered })
            .HasForeignKey("Child", "FK_Child_Parent", "Parent", "NO_ACTION", expectedPairs));
        Assert.False(new PreflightSchema(Array.Empty<PreflightColumnMetadata>(), foreignKeys: new[] { extra })
            .HasForeignKey("Child", "FK_Child_Parent", "Parent", "NO_ACTION", expectedPairs));
    }

    [Fact]
    public async Task Pre_stage_blocks_legacy_gym_goer_role_before_member_link_column_exists()
    {
        var source = ValidSource(CreatePreStageSchema());
        source.Counts[PreflightCountQuery.LegacyGymGoerRoles] = 2;

        var report = await new MigrationPreflightRunner(source).RunAsync(
            PreflightStageMode.PreStage);

        Assert.Equal(2, Find(report, MigrationPreflightCategories.InvalidUserRoleMemberLinks));
        Assert.Contains(PreflightCountQuery.LegacyGymGoerRoles, source.ExecutedQueries);
    }

    [Fact]
    public async Task Post_stage_normalization_and_unresolved_invite_counts_are_blocking()
    {
        var source = ValidSource(CreateStagedSchema());
        source.NormalizationCounts = new UserEmailNormalizationCounts(3, 2, 4, 1);
        source.Counts[PreflightCountQuery.UnresolvedInvitesForUnavailableMembers] = 5;

        var report = await new MigrationPreflightRunner(source).RunAsync(
            PreflightStageMode.PostStage);

        Assert.Equal(3, Find(report, MigrationPreflightCategories.MissingNormalizedEmails));
        Assert.Equal(2, Find(report, MigrationPreflightCategories.InvalidCanonicalEmails));
        Assert.Equal(4, Find(report, MigrationPreflightCategories.CanonicalEmailMismatches));
        Assert.Equal(1, Find(report, MigrationPreflightCategories.DuplicateCanonicalEmails));
        Assert.Equal(5, Find(
            report,
            MigrationPreflightCategories.UnresolvedInvitesForUnavailableMembers));
        Assert.True(report.HasBlockingFindings);
    }

    [Fact]
    public async Task Attendance_anomalies_select_active_queries_and_preserve_closed_deleted_history()
    {
        var schema = CreatePostStageSchemaWithAttendanceStaging();
        var source = ValidSource(schema);
        source.Counts[PreflightCountQuery.MultipleOpenActiveAttendanceSessions] = 2;
        source.Counts[PreflightCountQuery.ActiveCheckoutNotAfterCheckin] = 3;
        source.Counts[PreflightCountQuery.AttendanceMissingMembers] = 4;
        source.Counts[PreflightCountQuery.AttendanceForDeletedMembers] = 5;
        source.Counts[PreflightCountQuery.OpenActiveAttendanceForDeletedMembers] = 1;
        source.Counts[PreflightCountQuery.InvalidAttendanceSupersession] = 6;

        var report = await new MigrationPreflightRunner(source).RunAsync(
            PreflightStageMode.PostStage);

        Assert.Equal(2, Find(report, MigrationPreflightCategories.MultipleOpenAttendanceSessions));
        Assert.Equal(3, Find(report, MigrationPreflightCategories.CheckoutNotAfterCheckin));
        Assert.Equal(4, Find(report, MigrationPreflightCategories.AttendanceMissingMembers));
        Assert.Equal(5, Find(report, MigrationPreflightCategories.AttendanceForDeletedMembers));
        Assert.False(report.Findings.Single(finding =>
            finding.Category == MigrationPreflightCategories.AttendanceForDeletedMembers).IsBlocking);
        Assert.Equal(1, Find(report, MigrationPreflightCategories.OpenAttendanceForDeletedMembers));
        Assert.Equal(6, Find(report, MigrationPreflightCategories.InvalidAttendanceSupersession));
    }

    [Fact]
    public void Exact_invite_row_count_predicate_enforces_redemption_and_canonical_security_metadata()
    {
        var now = new DateTime(2026, 7, 12, 1, 0, 0, DateTimeKind.Utc);
        var valid = new PreflightAccountInviteRow(
            new byte[32],
            "MEMBER@EXAMPLE.TEST",
            2,
            "Mobile access",
            10,
            null,
            now,
            now.AddHours(1),
            null,
            null,
            null,
            null);

        Assert.False(SqlServerPreflightReadOnlyDataSource.IsMalformedAccountInvite(valid));
        var complete = valid with
        {
            UsedAtUtc = now,
            UsedByFirebaseUid = "valid-used-uid",
            RedemptionOperationId = Guid.NewGuid()
        };
        Assert.False(SqlServerPreflightReadOnlyDataSource.IsMalformedAccountInvite(complete));
        Assert.True(SqlServerPreflightReadOnlyDataSource.IsMalformedAccountInvite(
            complete with { RedemptionOperationId = Guid.Empty }));
        foreach (var partial in new[]
                 {
                     (Used: true, Uid: false, Operation: false),
                     (Used: false, Uid: true, Operation: false),
                     (Used: false, Uid: false, Operation: true),
                     (Used: true, Uid: true, Operation: false),
                     (Used: true, Uid: false, Operation: true),
                     (Used: false, Uid: true, Operation: true)
                 })
        {
            var partialRow = valid with
            {
                UsedAtUtc = partial.Used ? now : null,
                UsedByFirebaseUid = partial.Uid ? "valid-used-uid" : null,
                RedemptionOperationId = partial.Operation ? Guid.NewGuid() : null
            };
            Assert.True(
                SqlServerPreflightReadOnlyDataSource.IsMalformedAccountInvite(partialRow),
                $"Partial state Used={partial.Used}, Uid={partial.Uid}, Operation={partial.Operation}");
        }

        Assert.True(SqlServerPreflightReadOnlyDataSource.IsMalformedAccountInvite(
            valid with { UsedByFirebaseUid = "uid-with space" }));
        Assert.True(SqlServerPreflightReadOnlyDataSource.IsMalformedAccountInvite(
            valid with { NormalizedEmail = "\u212A@EXAMPLE.TEST" }));
        Assert.True(SqlServerPreflightReadOnlyDataSource.IsMalformedAccountInvite(
            valid with { Purpose = " Mobile access " }));
        Assert.True(SqlServerPreflightReadOnlyDataSource.IsMalformedAccountInvite(
            valid with { TokenHash = Convert.ToBase64String(new byte[32]) }));
    }

    [Fact]
    public void Sql_catalog_is_structurally_select_only()
    {
        var mutation = new Regex(
            @"\b(INSERT|UPDATE|DELETE|MERGE|EXEC(?:UTE)?|DROP|ALTER|CREATE|TRUNCATE)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var sql in SqlServerPreflightReadOnlyDataSource.GetAllowListedSqlForTesting())
        {
            Assert.StartsWith("SELECT", sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
            Assert.False(mutation.IsMatch(sql), sql);
        }

        Assert.DoesNotContain(
            typeof(IPreflightReadOnlyDataSource).GetMethods(),
            method => method.Name.Contains("Save", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Write", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Command_accepts_connection_only_from_environment_and_sets_read_intent()
    {
        const string secret = "Server=db.example.test;Database=Gym;User Id=reader;" +
            "Password=secret-value;Encrypt=True;TrustServerCertificate=False";
        var preStageArgs = new[] { "--mode", "pre-stage" };

        Assert.True(PreflightCommandParser.TryParse(
            preStageArgs,
            key => key == PreflightCommandParser.ConnectionStringEnvironmentVariable
                ? secret
                : null,
            out var command));
        var parsed = new SqlConnectionStringBuilder(command!.ConnectionString);
        Assert.Equal(ApplicationIntent.ReadOnly, parsed.ApplicationIntent);
        Assert.Equal("GymTrackPro Migration Preflight", parsed.ApplicationName);
        Assert.False(parsed.PersistSecurityInfo);
        Assert.Equal(PreflightStageMode.PreStage, command.Mode);
        Assert.True(PreflightCommandParser.TryParse(
            new[] { "--mode", "post-stage" },
            _ => secret,
            out var postStage));
        Assert.Equal(PreflightStageMode.PostStage, postStage!.Mode);

        Assert.False(PreflightCommandParser.TryParse(
            new[] { "--connection", secret },
            _ => secret,
            out _));
        Assert.False(PreflightCommandParser.TryParse(
            Array.Empty<string>(),
            _ => secret,
            out _));
        Assert.False(PreflightCommandParser.TryParse(
            new[] { "--mode", "unknown" },
            _ => secret,
            out _));
        Assert.False(PreflightCommandParser.TryParse(
            preStageArgs,
            _ => null,
            out _));

        Assert.False(PreflightCommandParser.TryParse(
            preStageArgs,
            _ => "Server=db.example.test;Database=Gym;User Id=reader;Password=secret-value;Encrypt=False",
            out _));
        Assert.False(PreflightCommandParser.TryParse(
            preStageArgs,
            _ => "Server=db.example.test;Database=Gym;User Id=reader;Password=secret-value;Encrypt=True;TrustServerCertificate=True",
            out _));
        Assert.False(PreflightCommandParser.TryParse(
            preStageArgs,
            _ => "not-a-valid-connection-string-with-secret-value",
            out _));
        Assert.DoesNotContain("secret-value", PreflightCommandOutput.Rejected, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", PreflightCommandOutput.Failed, StringComparison.Ordinal);
    }

    private static long Find(MigrationPreflightReport report, string category) =>
        report.Findings.Single(finding => finding.Category == category).Count;

    private static PreflightSchema CreateStagedSchema() => new(
        CreateStagedSchemaColumns(),
        CreateStagedChecks(),
        CreateStagedForeignKeys(),
        CreateStagedIndexes(),
        CreatePostStageHistory());

    private static PreflightSchema CreateStagedSchemaWithHistory(
        IReadOnlyList<PreflightMigrationMetadata> migrations) => new(
        CreateStagedSchemaColumns(),
        CreateStagedChecks(),
        CreateStagedForeignKeys(),
        CreateStagedIndexes(),
        migrations);

    private static PreflightSchema CreateStagedSchemaWithColumns(
        IReadOnlyList<PreflightColumnMetadata> columns) => new(
        columns,
        CreateStagedChecks(),
        CreateStagedForeignKeys(),
        CreateStagedIndexes(),
        CreatePostStageHistory());

    private static PreflightSchema CreatePreStageSchema() => new(
        CreatePreStageColumns(),
        indexes: CreatePreStageIndexes(),
        migrations: CreatePreStageHistory());

    private static IReadOnlyList<PreflightColumnMetadata> CreatePreStageColumns() => new[]
    {
        Column(
            "Users",
            "UserID",
            "int",
            isNullable: false,
            isIdentity: true,
            identitySeed: 1,
            identityIncrement: 1),
        Column("Users", "Email", "nvarchar", 255, isNullable: false),
        Column("Users", "PasswordHash", "nvarchar", 255, isNullable: false),
        Column("Users", "Role", "int", isNullable: false),
        Column("Users", "IsActive", "bit", isNullable: false),
        Column(
            "Members",
            "MemberID",
            "int",
            isNullable: false,
            isIdentity: true,
            identitySeed: 1,
            identityIncrement: 1),
        Column("Members", "IsDeleted", "bit", isNullable: false),
        Column("SystemSettings", "SettingKey", "nvarchar", 100, isNullable: false),
        Column("SystemSettings", "SettingValue", "nvarchar", -1, isNullable: false),
        Column(
            "AttendanceLogs",
            "AttendanceID",
            "int",
            isNullable: false,
            isIdentity: true,
            identitySeed: 1,
            identityIncrement: 1),
        Column("AttendanceLogs", "MemberID", "int", isNullable: false),
        Column("AttendanceLogs", "AttendanceDate", "datetime2", isNullable: false),
        Column("AttendanceLogs", "CheckInTime", "datetime2", isNullable: false),
        Column("AttendanceLogs", "CheckOutTime", "datetime2", isNullable: true),
        Column("Subscriptions", "StartDate", "datetime2", isNullable: false),
        Column("Subscriptions", "EndDate", "datetime2", isNullable: false),
        Column("__EFMigrationsHistory", "MigrationId", "nvarchar", 150, isNullable: false),
        Column("__EFMigrationsHistory", "ProductVersion", "nvarchar", 32, isNullable: false)
    };

    private static IReadOnlyList<PreflightColumnMetadata> CreateStagedSchemaColumns() =>
        CreatePreStageColumns()
            .Where(column => !string.Equals(
                column.Column,
                "PasswordHash",
                StringComparison.OrdinalIgnoreCase))
            .Append(Column("Users", "PasswordHash", "nvarchar", 255, isNullable: true))
            .Append(Column(
                "Users",
                "FirebaseUid",
                "nvarchar",
                128,
                SqlServerPreflightReadOnlyDataSource.ExactIdentityCollation,
                isNullable: true))
            .Append(Column("Users", "MemberID", "int", isNullable: true))
            .Append(Column(
                "Users",
                "NormalizedEmail",
                "nvarchar",
                255,
                SqlServerPreflightReadOnlyDataSource.ExactIdentityCollation,
                isNullable: true))
            .Concat(new[]
            {
                Column(
                    "AccountInvites",
                    "AccountInviteID",
                    "int",
                    isNullable: false,
                    isIdentity: true,
                    identitySeed: 1,
                    identityIncrement: 1),
                Column("AccountInvites", "TargetMemberID", "int", isNullable: true),
                Column("AccountInvites", "TargetUserID", "int", isNullable: true),
                Column("AccountInvites", "TokenHash", "binary", 32, isNullable: false),
                Column("AccountInvites", "NormalizedEmail", "nvarchar", 255, SqlServerPreflightReadOnlyDataSource.ExactIdentityCollation, false),
                Column("AccountInvites", "IntendedRole", "int", isNullable: false),
                Column("AccountInvites", "Purpose", "nvarchar", 100, isNullable: false),
                Column("AccountInvites", "CreatedByUserID", "int", isNullable: false),
                Column("AccountInvites", "CreatedAtUtc", "datetime2", isNullable: false),
                Column("AccountInvites", "ExpiresAtUtc", "datetime2", isNullable: false),
                Column("AccountInvites", "UsedAtUtc", "datetime2", isNullable: true),
                Column("AccountInvites", "RevokedAtUtc", "datetime2", isNullable: true),
                Column("AccountInvites", "UsedByFirebaseUid", "nvarchar", 128, SqlServerPreflightReadOnlyDataSource.ExactIdentityCollation, true),
                Column("AccountInvites", "RedemptionOperationId", "uniqueidentifier", isNullable: true),
                Column("AccountInvites", "RowVersion", "timestamp", 8, isNullable: false, isRowVersion: true),
                Column("MemberProjectionVersions", "MemberID", "int", isNullable: false),
                Column("MemberProjectionVersions", "Version", "bigint", isNullable: false, defaultDefinition: "CAST(0 AS bigint)"),
                Column("MemberProjectionVersions", "RowVersion", "timestamp", 8, isNullable: false, isRowVersion: true)
            })
            .ToArray();

    private static IReadOnlyList<PreflightCheckConstraintMetadata> CreateStagedChecks() => new[]
    {
        Check("Users", "CK_Users_Role", "[Role] IN (0, 1, 2)"),
        Check("Users", "CK_Users_FirebaseUidNotBlank", "[FirebaseUid] IS NULL OR LEN([FirebaseUid]) > 0"),
        Check("Users", "CK_Users_NormalizedEmailNotBlank", "[NormalizedEmail] IS NULL OR LEN([NormalizedEmail]) > 0"),
        Check("Users", "CK_Users_RoleMemberLink", "([Role] = 2 AND [MemberID] IS NOT NULL) OR ([Role] IN (0, 1) AND [MemberID] IS NULL)"),
        Check("AccountInvites", "CK_AccountInvites_ExactlyOneTarget", "([TargetMemberID] IS NOT NULL AND [TargetUserID] IS NULL) OR ([TargetMemberID] IS NULL AND [TargetUserID] IS NOT NULL)"),
        Check("AccountInvites", "CK_AccountInvites_TargetRole", "([TargetMemberID] IS NOT NULL AND [IntendedRole] = 2) OR ([TargetUserID] IS NOT NULL AND [IntendedRole] IN (0, 1))"),
        Check("AccountInvites", "CK_AccountInvites_ExpiryAfterCreation", "[ExpiresAtUtc] > [CreatedAtUtc]"),
        Check("AccountInvites", "CK_AccountInvites_UsedOrRevoked", "[UsedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL"),
        Check(
            "AccountInvites",
            "CK_AccountInvites_RedemptionMetadata",
            "([UsedAtUtc] IS NULL AND [UsedByFirebaseUid] IS NULL AND [RedemptionOperationId] IS NULL) OR " +
            "([UsedAtUtc] IS NOT NULL AND [UsedByFirebaseUid] IS NOT NULL " +
            "AND [RedemptionOperationId] IS NOT NULL " +
            "AND [RedemptionOperationId] <> " +
            "CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier))"),
        Check("AccountInvites", "CK_AccountInvites_UsedTimestampAfterCreation", "[UsedAtUtc] IS NULL OR [UsedAtUtc] >= [CreatedAtUtc]"),
        Check("AccountInvites", "CK_AccountInvites_UsedBeforeExpiry", "[UsedAtUtc] IS NULL OR [UsedAtUtc] < [ExpiresAtUtc]"),
        Check("AccountInvites", "CK_AccountInvites_RevokedTimestampAfterCreation", "[RevokedAtUtc] IS NULL OR [RevokedAtUtc] >= [CreatedAtUtc]"),
        Check("AccountInvites", "CK_AccountInvites_NormalizedEmailNotBlank", "LEN([NormalizedEmail]) > 0"),
        Check("AccountInvites", "CK_AccountInvites_PurposeNotBlank", "LEN(LTRIM(RTRIM([Purpose]))) > 0"),
        Check("AccountInvites", "CK_AccountInvites_UsedUidNotBlank", "[UsedByFirebaseUid] IS NULL OR LEN([UsedByFirebaseUid]) > 0"),
        Check("MemberProjectionVersions", "CK_MemberProjectionVersions_VersionRange", "[Version] >= 0 AND [Version] <= 2199023255551")
    };

    private static IReadOnlyList<PreflightForeignKeyMetadata> CreateStagedForeignKeys() => new[]
    {
        ForeignKey("Users", "FK_Users_Members_MemberID", "MemberID", "Members", "MemberID", "NO_ACTION"),
        ForeignKey("AccountInvites", "FK_AccountInvites_Members_TargetMemberID", "TargetMemberID", "Members", "MemberID", "NO_ACTION"),
        ForeignKey("AccountInvites", "FK_AccountInvites_Users_TargetUserID", "TargetUserID", "Users", "UserID", "NO_ACTION"),
        ForeignKey("AccountInvites", "FK_AccountInvites_Users_CreatedByUserID", "CreatedByUserID", "Users", "UserID", "NO_ACTION"),
        ForeignKey("MemberProjectionVersions", "FK_MemberProjectionVersions_Members_MemberID", "MemberID", "Members", "MemberID", "CASCADE")
    };

    private static IReadOnlyList<PreflightIndexMetadata> CreatePreStageIndexes() => new[]
    {
        PrimaryKey("Users", "PK_Users", "UserID"),
        PrimaryKey("Members", "PK_Members", "MemberID"),
        PrimaryKey("__EFMigrationsHistory", "PK___EFMigrationsHistory", "MigrationId")
    };

    private static IReadOnlyList<PreflightIndexMetadata> CreateStagedIndexes() =>
        CreatePreStageIndexes()
            .Concat(new[]
            {
                PrimaryKey("AccountInvites", "PK_AccountInvites", "AccountInviteID"),
                PrimaryKey("MemberProjectionVersions", "PK_MemberProjectionVersions", "MemberID"),
                Index("Users", "UX_Users_FirebaseUid", true, "[FirebaseUid] IS NOT NULL", "FirebaseUid"),
                Index("Users", "UX_Users_NormalizedEmail", true, "[NormalizedEmail] IS NOT NULL", "NormalizedEmail"),
                Index("Users", "UX_Users_MemberID", true, "[MemberID] IS NOT NULL", "MemberID"),
                Index("AccountInvites", "UX_AccountInvites_TokenHash", true, null, "TokenHash"),
                Index("AccountInvites", "UX_AccountInvites_RedemptionOperationId", true, "[RedemptionOperationId] IS NOT NULL", "RedemptionOperationId"),
                Index("AccountInvites", "IX_AccountInvites_NormalizedEmail", false, null, "NormalizedEmail"),
                Index("AccountInvites", "IX_AccountInvites_CreatedByUserID", false, null, "CreatedByUserID"),
                Index("AccountInvites", "IX_AccountInvites_TargetMemberID", false, null, "TargetMemberID"),
                Index("AccountInvites", "IX_AccountInvites_TargetUserID", false, null, "TargetUserID")
            })
            .ToArray();

    private static IReadOnlyList<PreflightMigrationMetadata> CreatePreStageHistory() => new[]
    {
        Migration("20260701191518_InitialCreate"),
        Migration("20260701192339_AddUserTokens"),
        Migration("20260701193502_MakeAuditLogUserNullable"),
        Migration("20260702013356_AddSystemSettings")
    };

    private static IReadOnlyList<PreflightMigrationMetadata> CreatePostStageHistory() =>
        CreatePreStageHistory()
            .Append(Migration(MigrationPreflightRunner.B1MigrationId))
            .ToArray();

    private static PreflightSchema CreateBrokenPostStageSchema(string fault)
    {
        var columns = CreateStagedSchemaColumns().ToList();
        var checks = CreateStagedChecks().ToList();
        var foreignKeys = CreateStagedForeignKeys().ToList();
        var indexes = CreateStagedIndexes().ToList();
        switch (fault)
        {
            case "wrong-check":
            {
                var index = checks.FindIndex(check =>
                    check.Name == "CK_AccountInvites_UsedOrRevoked");
                checks[index] = checks[index] with
                {
                    Definition = "[UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL"
                };
                break;
            }
            case "legacy-redemption-check":
            {
                var index = checks.FindIndex(check =>
                    check.Name == "CK_AccountInvites_RedemptionMetadata");
                checks[index] = checks[index] with
                {
                    Name = "CK_AccountInvites_UsedMetadataComplete",
                    Definition =
                        "([UsedAtUtc] IS NULL AND [UsedByFirebaseUid] IS NULL AND " +
                        "[RedemptionOperationId] IS NULL) OR " +
                        "([UsedAtUtc] IS NOT NULL AND [UsedByFirebaseUid] IS NOT NULL AND " +
                        "[RedemptionOperationId] IS NOT NULL)"
                };
                break;
            }
            case "missing-index":
                indexes.RemoveAll(index => index.Name == "UX_Users_FirebaseUid");
                break;
            case "missing-foreign-key":
                foreignKeys.RemoveAll(key => key.Name == "FK_Users_Members_MemberID");
                break;
            case "wrong-default":
            {
                var index = columns.FindIndex(column =>
                    column.Table == "MemberProjectionVersions" && column.Column == "Version");
                columns[index] = columns[index] with { DefaultDefinition = "CAST(1 AS bigint)" };
                break;
            }
            case "invalid-rowversion":
            {
                var index = columns.FindIndex(column =>
                    column.Table == "AccountInvites" && column.Column == "RowVersion");
                columns[index] = columns[index] with { IsRowVersion = false };
                break;
            }
            case "column-wrong-data-type":
                ReplaceColumn(columns, "AccountInvites", "RedemptionOperationId", value => value with
                {
                    DataType = "nvarchar"
                });
                break;
            case "column-wrong-max-length":
                ReplaceColumn(columns, "AccountInvites", "TokenHash", value => value with
                {
                    MaximumLength = 31
                });
                break;
            case "column-wrong-precision":
                ReplaceColumn(columns, "AccountInvites", "CreatedAtUtc", value => value with
                {
                    Precision = 26
                });
                break;
            case "column-wrong-scale":
                ReplaceColumn(columns, "AccountInvites", "CreatedAtUtc", value => value with
                {
                    Scale = 6
                });
                break;
            case "column-wrong-nullability":
                ReplaceColumn(columns, "AccountInvites", "IntendedRole", value => value with
                {
                    IsNullable = true
                });
                break;
            case "column-wrong-explicit-collation":
                ReplaceColumn(columns, "AccountInvites", "NormalizedEmail", value => value with
                {
                    Collation = "SQL_Latin1_General_CP1_CI_AS"
                });
                break;
            case "column-wrong-default-collation":
                ReplaceColumn(columns, "AccountInvites", "Purpose", value => value with
                {
                    UsesDatabaseDefaultCollation = false
                });
                break;
            case "column-extra-default":
                ReplaceColumn(columns, "AccountInvites", "IntendedRole", value => value with
                {
                    HasDefault = true,
                    DefaultDefinition = "0"
                });
                break;
            case "column-missing-default":
                ReplaceColumn(columns, "MemberProjectionVersions", "Version", value => value with
                {
                    HasDefault = false,
                    DefaultDefinition = null
                });
                break;
            case "column-nonidentity":
                ReplaceColumn(columns, "AccountInvites", "AccountInviteID", value => value with
                {
                    IsIdentity = false,
                    IdentitySeed = null,
                    IdentityIncrement = null
                });
                break;
            case "column-identity-seed":
                ReplaceColumn(columns, "AccountInvites", "AccountInviteID", value => value with
                {
                    IdentitySeed = 2
                });
                break;
            case "column-identity-increment":
                ReplaceColumn(columns, "AccountInvites", "AccountInviteID", value => value with
                {
                    IdentityIncrement = 2
                });
                break;
            case "column-identity-not-for-replication":
                ReplaceColumn(columns, "AccountInvites", "AccountInviteID", value => value with
                {
                    IdentityNotForReplication = true
                });
                break;
            case "column-computed":
                ReplaceColumn(columns, "AccountInvites", "Purpose", value => value with
                {
                    IsComputed = true,
                    ComputedDefinition = "[NormalizedEmail]"
                });
                break;
            case "column-computed-persisted":
                ReplaceColumn(columns, "AccountInvites", "Purpose", value => value with
                {
                    IsComputed = true,
                    ComputedDefinition = "[NormalizedEmail]",
                    IsPersisted = true
                });
                break;
            case "column-computed-definition":
                ReplaceColumn(columns, "AccountInvites", "Purpose", value => value with
                {
                    ComputedDefinition = "[NormalizedEmail]"
                });
                break;
            case "column-rowguid":
                ReplaceColumn(columns, "AccountInvites", "RedemptionOperationId", value => value with
                {
                    IsRowGuid = true
                });
                break;
            case "column-filestream":
                ReplaceColumn(columns, "AccountInvites", "TokenHash", value => value with
                {
                    IsFileStream = true
                });
                break;
            case "column-ansi-padding":
                ReplaceColumn(columns, "AccountInvites", "TokenHash", value => value with
                {
                    IsAnsiPadded = false
                });
                break;
            case "column-sparse":
                ReplaceColumn(columns, "AccountInvites", "TargetMemberID", value => value with
                {
                    IsSparse = true
                });
                break;
            case "column-column-set":
                ReplaceColumn(columns, "AccountInvites", "Purpose", value => value with
                {
                    IsColumnSet = true
                });
                break;
            case "column-generated-always":
                ReplaceColumn(columns, "AccountInvites", "CreatedAtUtc", value => value with
                {
                    GeneratedAlwaysType = 1
                });
                break;
            case "column-hidden":
                ReplaceColumn(columns, "AccountInvites", "UsedAtUtc", value => value with
                {
                    IsHidden = true
                });
                break;
            case "column-masked":
                ReplaceColumn(columns, "AccountInvites", "NormalizedEmail", value => value with
                {
                    IsMasked = true,
                    MaskingFunction = "email()"
                });
                break;
            case "column-masking-function":
                ReplaceColumn(columns, "AccountInvites", "NormalizedEmail", value => value with
                {
                    MaskingFunction = "email()"
                });
                break;
            case "column-encryption-type":
                ReplaceColumn(columns, "AccountInvites", "NormalizedEmail", value => value with
                {
                    EncryptionType = 1
                });
                break;
            case "column-encryption-description":
                ReplaceColumn(columns, "AccountInvites", "NormalizedEmail", value => value with
                {
                    EncryptionTypeDescription = "DETERMINISTIC"
                });
                break;
            case "column-encryption-algorithm":
                ReplaceColumn(columns, "AccountInvites", "NormalizedEmail", value => value with
                {
                    EncryptionAlgorithmName = "AEAD_AES_256_CBC_HMAC_SHA_256"
                });
                break;
            case "column-encryption-key-id":
                ReplaceColumn(columns, "AccountInvites", "NormalizedEmail", value => value with
                {
                    ColumnEncryptionKeyId = 1
                });
                break;
            case "column-encryption-key-database":
                ReplaceColumn(columns, "AccountInvites", "NormalizedEmail", value => value with
                {
                    ColumnEncryptionKeyDatabaseName = "ExternalKeys"
                });
                break;
            case "pk-extra-key":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    KeyColumns = value.KeyColumns
                        .Append(new PreflightIndexKeyColumnMetadata("TargetMemberID", false))
                        .ToArray()
                });
                break;
            case "pk-wrong-schema":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with { Schema = "security" });
                break;
            case "pk-wrong-table":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with { Table = "Users" });
                break;
            case "pk-wrong-name":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    Name = "PK_AccountInvites_Drift"
                });
                break;
            case "pk-descending-key":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    KeyColumns = new[]
                    {
                        new PreflightIndexKeyColumnMetadata("AccountInviteID", true)
                    }
                });
                break;
            case "pk-included-column":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    IncludedColumns = new[] { "TokenHash" }
                });
                break;
            case "pk-disabled":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with { IsDisabled = true });
                break;
            case "pk-hypothetical":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    IsHypothetical = true
                });
                break;
            case "pk-ignore-duplicate-keys":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    IgnoreDuplicateKeys = true
                });
                break;
            case "pk-wrong-type":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    Type = "NONCLUSTERED"
                });
                break;
            case "pk-not-unique":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with { IsUnique = false });
                break;
            case "pk-not-primary":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    IsPrimaryKey = false
                });
                break;
            case "pk-unique-constraint":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    IsUniqueConstraint = true
                });
                break;
            case "pk-filtered":
                ReplaceIndex(indexes, "PK_AccountInvites", value => value with
                {
                    HasFilter = true,
                    Filter = "[AccountInviteID] IS NOT NULL"
                });
                break;
            case "index-extra-key":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with
                {
                    KeyColumns = value.KeyColumns
                        .Append(new PreflightIndexKeyColumnMetadata("UserID", false))
                        .ToArray()
                });
                break;
            case "index-wrong-schema":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with { Schema = "security" });
                break;
            case "index-wrong-table":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with { Table = "Members" });
                break;
            case "index-wrong-name":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with
                {
                    Name = "UX_Users_FirebaseUid_Drift"
                });
                break;
            case "index-descending-key":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with
                {
                    KeyColumns = new[]
                    {
                        new PreflightIndexKeyColumnMetadata("FirebaseUid", true)
                    }
                });
                break;
            case "index-included-column":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with
                {
                    IncludedColumns = new[] { "UserID" }
                });
                break;
            case "index-disabled":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with
                {
                    IsDisabled = true
                });
                break;
            case "index-hypothetical":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with
                {
                    IsHypothetical = true
                });
                break;
            case "index-ignore-duplicate-keys":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with
                {
                    IgnoreDuplicateKeys = true
                });
                break;
            case "index-wrong-type":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with { Type = "CLUSTERED" });
                break;
            case "index-not-unique":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with { IsUnique = false });
                break;
            case "index-primary-flag":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with { IsPrimaryKey = true });
                break;
            case "index-unique-constraint":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with
                {
                    IsUniqueConstraint = true
                });
                break;
            case "index-filter-flag":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with { HasFilter = false });
                break;
            case "index-filter-definition":
                ReplaceIndex(indexes, "UX_Users_FirebaseUid", value => value with
                {
                    Filter = "[FirebaseUid] IS NULL"
                });
                break;
            case "duplicate-index-object":
                indexes.Add(indexes.Single(index => index.Name == "UX_Users_FirebaseUid"));
                break;
            case "foreign-key-extra-pair":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    Columns = value.Columns
                        .Append(new PreflightForeignKeyColumnMetadata("UserID", "MemberID"))
                        .ToArray()
                });
                break;
            case "foreign-key-wrong-parent-table":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    ParentTable = "Members"
                });
                break;
            case "foreign-key-wrong-name":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    Name = "FK_Users_Members_MemberID_Drift"
                });
                break;
            case "foreign-key-wrong-parent-column":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    Columns = new[]
                    {
                        new PreflightForeignKeyColumnMetadata("UserID", "MemberID")
                    }
                });
                break;
            case "foreign-key-wrong-principal-column":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    Columns = new[]
                    {
                        new PreflightForeignKeyColumnMetadata("MemberID", "IsDeleted")
                    }
                });
                break;
            case "foreign-key-wrong-parent-schema":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    ParentSchema = "security"
                });
                break;
            case "foreign-key-wrong-principal-schema":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    PrincipalSchema = "security"
                });
                break;
            case "foreign-key-wrong-principal-table":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    PrincipalTable = "Users"
                });
                break;
            case "foreign-key-wrong-delete-action":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    DeleteAction = "CASCADE"
                });
                break;
            case "foreign-key-wrong-update-action":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    UpdateAction = "CASCADE"
                });
                break;
            case "foreign-key-disabled":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    IsDisabled = true
                });
                break;
            case "foreign-key-untrusted":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    IsTrusted = false
                });
                break;
            case "foreign-key-not-for-replication":
                ReplaceForeignKey(foreignKeys, "FK_Users_Members_MemberID", value => value with
                {
                    IsNotForReplication = true
                });
                break;
            case "duplicate-foreign-key-object":
                foreignKeys.Add(foreignKeys.Single(key => key.Name == "FK_Users_Members_MemberID"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }

        return new PreflightSchema(
            columns,
            checks,
            foreignKeys,
            indexes,
            CreatePostStageHistory());
    }

    private static void ReplaceColumn(
        IList<PreflightColumnMetadata> columns,
        string table,
        string column,
        Func<PreflightColumnMetadata, PreflightColumnMetadata> replacement)
    {
        var position = columns.ToList().FindIndex(value =>
            value.Table == table && value.Column == column);
        columns[position] = replacement(columns[position]);
    }

    private static void ReplaceIndex(
        IList<PreflightIndexMetadata> indexes,
        string name,
        Func<PreflightIndexMetadata, PreflightIndexMetadata> replacement)
    {
        var position = indexes.ToList().FindIndex(index => index.Name == name);
        indexes[position] = replacement(indexes[position]);
    }

    private static void ReplaceForeignKey(
        IList<PreflightForeignKeyMetadata> foreignKeys,
        string name,
        Func<PreflightForeignKeyMetadata, PreflightForeignKeyMetadata> replacement)
    {
        var position = foreignKeys.ToList().FindIndex(key => key.Name == name);
        foreignKeys[position] = replacement(foreignKeys[position]);
    }

    private static PreflightSchema CreatePostStageSchemaWithAttendanceStaging()
    {
        var columns = CreateStagedSchemaColumns()
            .Append(Column("AttendanceLogs", "AttendanceDateLocal", "date", isNullable: true))
            .Append(Column("AttendanceLogs", "IsVoided", "bit", isNullable: false))
            .Append(Column("AttendanceLogs", "SupersededByAttendanceID", "int", isNullable: true))
            .ToArray();
        return CreateStagedSchemaWithColumns(columns);
    }

    private static FakePreflightDataSource ValidSource(PreflightSchema schema) => new(schema)
    {
        Timezone = "Asia/Manila",
        StaleSessionHours = schema.HasTable("AccountInvites") ? "16" : null
    };

    private static PreflightColumnMetadata Column(
        string table,
        string column,
        string dataType,
        int? maximumLength = null,
        string? collation = null,
        bool isNullable = true,
        string? defaultDefinition = null,
        bool isRowVersion = false,
        bool isIdentity = false,
        decimal? identitySeed = null,
        decimal? identityIncrement = null,
        bool identityNotForReplication = false)
    {
        var shape = dataType.ToUpperInvariant() switch
        {
            "INT" => (Length: 4, Precision: (byte)10, Scale: (byte)0, Ansi: false),
            "BIGINT" => (Length: 8, Precision: (byte)19, Scale: (byte)0, Ansi: false),
            "BIT" => (Length: 1, Precision: (byte)1, Scale: (byte)0, Ansi: false),
            "DATETIME2" => (Length: 8, Precision: (byte)27, Scale: (byte)7, Ansi: false),
            "DATE" => (Length: 3, Precision: (byte)10, Scale: (byte)0, Ansi: false),
            "UNIQUEIDENTIFIER" => (Length: 16, Precision: (byte)0, Scale: (byte)0, Ansi: false),
            "TIMESTAMP" => (Length: 8, Precision: (byte)0, Scale: (byte)0, Ansi: false),
            "BINARY" or "VARBINARY" or "CHAR" or "VARCHAR" or "NCHAR" or "NVARCHAR" =>
                (Length: maximumLength ?? throw new ArgumentException(
                    "The exact length is required for character and binary columns.",
                    nameof(maximumLength)), Precision: (byte)0, Scale: (byte)0, Ansi: true),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null)
        };
        var usesDatabaseDefaultCollation = shape.Ansi
            && collation is null
            && !string.Equals(dataType, "binary", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dataType, "varbinary", StringComparison.OrdinalIgnoreCase);
        var metadata = PreflightColumnMetadataFactory.Expected(
            table,
            column,
            dataType,
            maximumLength ?? shape.Length,
            shape.Precision,
            shape.Scale,
            isNullable,
            collation,
            usesDatabaseDefaultCollation,
            shape.Ansi,
            defaultDefinition,
            isRowVersion,
            isIdentity,
            identitySeed,
            identityIncrement,
            identityNotForReplication);
        return usesDatabaseDefaultCollation
            ? metadata with { Collation = "SQL_Latin1_General_CP1_CI_AS" }
            : metadata;
    }

    private static PreflightCheckConstraintMetadata Check(
        string table,
        string name,
        string definition) => new(table, name, definition, true, true);

    private static PreflightForeignKeyMetadata ForeignKey(
        string table,
        string name,
        string column,
        string principalTable,
        string principalColumn,
        string deleteAction) =>
        new(
            "dbo",
            table,
            name,
            "dbo",
            principalTable,
            deleteAction,
            "NO_ACTION",
            false,
            true,
            false,
            new[] { new PreflightForeignKeyColumnMetadata(column, principalColumn) });

    private static PreflightIndexMetadata PrimaryKey(
        string table,
        string name,
        params string[] columns) =>
        new(
            "dbo",
            table,
            name,
            "CLUSTERED",
            true,
            true,
            false,
            false,
            null,
            false,
            false,
            false,
            columns.Select(column => new PreflightIndexKeyColumnMetadata(column, false)).ToArray(),
            Array.Empty<string>());

    private static PreflightIndexMetadata Index(
        string table,
        string name,
        bool isUnique,
        string? filter,
        params string[] columns) =>
        new(
            "dbo",
            table,
            name,
            "NONCLUSTERED",
            isUnique,
            false,
            false,
            filter is not null,
            filter,
            false,
            false,
            false,
            columns.Select(column => new PreflightIndexKeyColumnMetadata(column, false)).ToArray(),
            Array.Empty<string>());

    private static PreflightMigrationMetadata Migration(string id) =>
        new(id, MigrationPreflightRunner.MigrationProductVersion);

    private sealed class FakePreflightDataSource : IPreflightReadOnlyDataSource
    {
        private readonly PreflightSchema _schema;

        public FakePreflightDataSource(PreflightSchema schema)
        {
            _schema = schema;
        }

        public Dictionary<PreflightCountQuery, long> Counts { get; } = new();

        public List<PreflightCountQuery> ExecutedQueries { get; } = new();

        public string? Timezone { get; set; }

        public string? StaleSessionHours { get; set; }

        public UserEmailNormalizationCounts NormalizationCounts { get; set; } =
            new(0, 0, 0, 0);

        public long MalformedInviteCount { get; set; }

        public int MalformedInviteValidationCalls { get; private set; }

        public Task<PreflightSchema> InspectSchemaAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_schema);

        public Task<long> CountAsync(
            PreflightCountQuery query,
            CancellationToken cancellationToken = default)
        {
            ExecutedQueries.Add(query);
            return Task.FromResult(Counts.GetValueOrDefault(query));
        }

        public Task<UserEmailNormalizationCounts> CountUserEmailNormalizationAsync(
            bool normalizedEmailColumnExists,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NormalizationCounts);

        public Task<long> CountMalformedAccountInvitesAsync(
            CancellationToken cancellationToken = default)
        {
            MalformedInviteValidationCalls++;
            return Task.FromResult(MalformedInviteCount);
        }

        public Task<string?> GetSystemSettingValueAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(key == "Timezone" ? Timezone : StaleSessionHours);
    }
}
