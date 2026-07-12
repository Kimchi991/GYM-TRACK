using System.Globalization;
using GymTrackPro.API.Services;

namespace GymTrackPro.API.Maintenance;

public interface IMigrationPreflightRunner
{
    Task<MigrationPreflightReport> RunAsync(
        PreflightStageMode mode,
        CancellationToken cancellationToken = default);
}

public sealed class MigrationPreflightRunner : IMigrationPreflightRunner
{
    public const string B1MigrationId =
        "20260711204834_StageFirebaseIdentityAndAccountInvites";
    public const string AttendanceMigrationId =
        "20260712050837_AddAttendanceVoidingAndSource";
    public const string MigrationProductVersion = "10.0.9";

    private static readonly PreflightMigrationMetadata[] PreStageMigrationHistory =
    {
        new("20260701191518_InitialCreate", MigrationProductVersion),
        new("20260701192339_AddUserTokens", MigrationProductVersion),
        new("20260701193502_MakeAuditLogUserNullable", MigrationProductVersion),
        new("20260702013356_AddSystemSettings", MigrationProductVersion)
    };

    private static readonly PreflightMigrationMetadata[] B1StageMigrationHistory =
        PreStageMigrationHistory
            .Append(new PreflightMigrationMetadata(B1MigrationId, MigrationProductVersion))
            .ToArray();

    private static readonly PreflightMigrationMetadata[] AttendanceStageMigrationHistory =
        B1StageMigrationHistory
            .Append(new PreflightMigrationMetadata(AttendanceMigrationId, MigrationProductVersion))
            .ToArray();

    private readonly IPreflightReadOnlyDataSource _dataSource;

    public MigrationPreflightRunner(IPreflightReadOnlyDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<MigrationPreflightReport> RunAsync(
        PreflightStageMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var schema = await _dataSource.InspectSchemaAsync(cancellationToken);
        var findings = new List<MigrationPreflightFinding>();
        var postStage = mode == PreflightStageMode.PostStage;
        var baseSchemaReady = IsBaseSchemaReady(schema, postStage);
        var b1StagingPresent = HasAnyB1StagingMarker(schema);
        var b1SchemaReady = IsB1SchemaFingerprintReady(schema);
        var historyReady = postStage
            ? schema.HasExactMigrationHistory(B1StageMigrationHistory)
                || schema.HasExactMigrationHistory(AttendanceStageMigrationHistory)
            : schema.HasExactMigrationHistory(PreStageMigrationHistory);
        var b1HistoryPresent = schema.HasMigration(B1MigrationId);
        var attendanceHistoryPresent = schema.HasMigration(AttendanceMigrationId);
        var finalAttendanceShapeReady = HasFinalAttendanceQueryShape(schema);

        Add(findings, MigrationPreflightCategories.BaseSchemaInvalid, baseSchemaReady ? 0 : 1);
        Add(findings, MigrationPreflightCategories.MigrationHistoryMismatch, historyReady ? 0 : 1);
        Add(
            findings,
            MigrationPreflightCategories.B1SchemaStateMismatch,
            postStage
                ? b1SchemaReady ? 0 : 1
                : b1StagingPresent ? 1 : 0);
        Add(
            findings,
            MigrationPreflightCategories.IdentitySchemaNotStaged,
            postStage && !b1SchemaReady ? 1 : 0);
        Add(
            findings,
            MigrationPreflightCategories.MigrationHistorySchemaMismatch,
            (b1HistoryPresent && !b1SchemaReady)
                || (attendanceHistoryPresent && !finalAttendanceShapeReady)
                    ? 1
                    : 0);

        var hasUserEmail = schema.HasColumn("Users", "Email");
        var hasNormalizedEmail = schema.HasColumn("Users", "NormalizedEmail");
        var normalization = hasUserEmail
            ? await _dataSource.CountUserEmailNormalizationAsync(
                hasNormalizedEmail,
                cancellationToken)
            : new UserEmailNormalizationCounts(0, 0, 0, 0);
        Add(
            findings,
            MigrationPreflightCategories.MissingNormalizedEmails,
            normalization.MissingNormalizedEmails,
            isBlocking: postStage);
        Add(
            findings,
            MigrationPreflightCategories.InvalidCanonicalEmails,
            normalization.InvalidEmails);
        Add(
            findings,
            MigrationPreflightCategories.CanonicalEmailMismatches,
            normalization.CanonicalMismatches,
            isBlocking: postStage);
        Add(
            findings,
            MigrationPreflightCategories.DuplicateCanonicalEmails,
            normalization.DuplicateCanonicalGroups);
        // Preserve the original machine-readable category while replacing its SQL-only
        // approximation with the exact application normalization algorithm.
        Add(
            findings,
            MigrationPreflightCategories.DuplicateNormalizedEmails,
            normalization.DuplicateCanonicalGroups);

        Add(
            findings,
            MigrationPreflightCategories.DuplicateFirebaseUids,
            schema.HasColumn("Users", "FirebaseUid")
                ? await CountAsync(PreflightCountQuery.DuplicateFirebaseUids, cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.DuplicateMemberLinks,
            schema.HasColumn("Users", "MemberID")
                ? await CountAsync(PreflightCountQuery.DuplicateMemberLinks, cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.InvalidUserRoles,
            schema.HasColumn("Users", "Role")
                ? await CountAsync(PreflightCountQuery.InvalidUserRoles, cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.InvalidUserRoleMemberLinks,
            schema.HasColumns("Users", "Role", "MemberID")
                ? await CountAsync(PreflightCountQuery.InvalidUserRoleMemberLinks, cancellationToken)
                : schema.HasColumn("Users", "Role")
                    ? await CountAsync(PreflightCountQuery.LegacyGymGoerRoles, cancellationToken)
                    : 0);

        var inviteColumnsReady = HasInviteAnomalyColumns(schema);
        Add(
            findings,
            MigrationPreflightCategories.MalformedAccountInvites,
            inviteColumnsReady
                ? await _dataSource.CountMalformedAccountInvitesAsync(cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.DuplicateInviteTokenHashes,
            inviteColumnsReady
                ? await CountAsync(PreflightCountQuery.DuplicateInviteTokenHashes, cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.DuplicateInviteRedemptionOperations,
            inviteColumnsReady
                ? await CountAsync(
                    PreflightCountQuery.DuplicateInviteRedemptionOperations,
                    cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.UnresolvedInvitesForUnavailableMembers,
            inviteColumnsReady
                && schema.HasColumns("Members", "MemberID", "IsDeleted")
                    ? await CountAsync(
                        PreflightCountQuery.UnresolvedInvitesForUnavailableMembers,
                        cancellationToken)
                    : 0,
            isBlocking: postStage);

        var projectionTableExists = schema.HasTable("MemberProjectionVersions");
        var projectionShapeReady = IsProjectionSchemaReady(schema);
        Add(
            findings,
            MigrationPreflightCategories.ProjectionSchemaInvalid,
            postStage
                ? projectionShapeReady ? 0 : 1
                : projectionTableExists ? 1 : 0);
        Add(
            findings,
            MigrationPreflightCategories.MissingProjectionVersions,
            projectionShapeReady
                ? await CountAsync(PreflightCountQuery.MissingProjectionVersions, cancellationToken)
                : schema.HasTable("Members")
                    ? await CountAsync(PreflightCountQuery.AllMembers, cancellationToken)
                    : 0,
            isBlocking: postStage);
        Add(
            findings,
            MigrationPreflightCategories.DuplicateProjectionVersions,
            projectionShapeReady
                ? await CountAsync(PreflightCountQuery.DuplicateProjectionVersions, cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.InvalidProjectionVersions,
            projectionShapeReady
                ? await CountAsync(PreflightCountQuery.InvalidProjectionVersions, cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.DeletedMembersWithActiveUsers,
            schema.HasColumns("Users", "MemberID", "Role", "IsActive")
                && schema.HasColumns("Members", "MemberID", "IsDeleted")
                    ? await CountAsync(
                        PreflightCountQuery.DeletedMembersWithActiveGymGoers,
                        cancellationToken)
                    : 0);

        await AddConfigurationFindingsAsync(
            schema,
            findings,
            postStage,
            cancellationToken);
        await AddAttendanceFindingsAsync(
            schema,
            findings,
            attendanceHistoryPresent,
            cancellationToken);

        Add(
            findings,
            MigrationPreflightCategories.SubscriptionCalendarTimeComponents,
            schema.HasColumns("Subscriptions", "StartDate", "EndDate")
                ? await CountAsync(
                    PreflightCountQuery.SubscriptionCalendarTimeComponents,
                    cancellationToken)
                : 0);

        return new MigrationPreflightReport(mode, findings);
    }

    private async Task AddConfigurationFindingsAsync(
        PreflightSchema schema,
        ICollection<MigrationPreflightFinding> findings,
        bool postStage,
        CancellationToken cancellationToken)
    {
        var configurationReadable = schema.HasColumns(
            "SystemSettings",
            "SettingKey",
            "SettingValue");
        var timezone = configurationReadable
            ? await _dataSource.GetSystemSettingValueAsync(
                TimezoneService.TimezoneSettingKey,
                cancellationToken)
            : null;
        Add(
            findings,
            MigrationPreflightCategories.TimezoneConfigurationInvalid,
            IsValidTimeZone(timezone) ? 0 : 1);

        var staleSessionHours = configurationReadable
            ? await _dataSource.GetSystemSettingValueAsync(
                SystemSettingService.StaleSessionHoursKey,
                cancellationToken)
            : null;
        Add(
            findings,
            MigrationPreflightCategories.StaleSessionConfigurationInvalid,
            IsValidStaleSessionHours(staleSessionHours) ? 0 : 1,
            isBlocking: postStage || staleSessionHours is not null);
    }

    private async Task AddAttendanceFindingsAsync(
        PreflightSchema schema,
        ICollection<MigrationPreflightFinding> findings,
        bool attendanceHistoryPresent,
        CancellationToken cancellationToken)
    {
        var hasAttendanceBase = schema.HasColumns(
            "AttendanceLogs",
            "AttendanceID",
            "MemberID",
            "AttendanceDate",
            "CheckInTime",
            "CheckOutTime");
        var legacyDateShape = HasLegacyAttendanceDateShape(schema);
        var finalQueryShape = HasFinalAttendanceQueryShape(schema);
        var attendanceStateConsistent = attendanceHistoryPresent
            ? finalQueryShape
            : legacyDateShape;
        Add(
            findings,
            MigrationPreflightCategories.AttendanceSchemaReadinessUnknown,
            attendanceStateConsistent ? 0 : 1);

        Add(
            findings,
            MigrationPreflightCategories.AttendanceDateTimeComponents,
            hasAttendanceBase && legacyDateShape
                ? await CountAsync(PreflightCountQuery.AttendanceDateTimeComponents, cancellationToken)
                : 0);

        Add(
            findings,
            MigrationPreflightCategories.AttendanceLocalDatesMissing,
            hasAttendanceBase && legacyDateShape
                ? await CountAsync(PreflightCountQuery.AllAttendanceRows, cancellationToken)
                : 0,
            isBlocking: false);
        Add(
            findings,
            MigrationPreflightCategories.AttendanceLocalDateMismatches,
            hasAttendanceBase && finalQueryShape
                ? await CountAsync(
                    PreflightCountQuery.AttendancePreservedDateMismatches,
                    cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.AttendanceLocalDateDuplicates,
            hasAttendanceBase && finalQueryShape
                ? await CountAsync(PreflightCountQuery.AttendanceDateActiveDuplicates, cancellationToken)
                : hasAttendanceBase && legacyDateShape
                    ? await CountAsync(
                        PreflightCountQuery.AttendanceLegacyDateDuplicates,
                        cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.MultipleOpenAttendanceSessions,
            hasAttendanceBase
                ? await CountAsync(
                    finalQueryShape
                        ? PreflightCountQuery.MultipleOpenActiveAttendanceSessions
                        : PreflightCountQuery.MultipleOpenAttendanceSessions,
                    cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.CheckoutNotAfterCheckin,
            hasAttendanceBase
                ? await CountAsync(
                    finalQueryShape
                        ? PreflightCountQuery.ActiveCheckoutNotAfterCheckin
                        : PreflightCountQuery.CheckoutNotAfterCheckin,
                    cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.AttendanceMissingMembers,
            hasAttendanceBase && schema.HasColumn("Members", "MemberID")
                ? await CountAsync(PreflightCountQuery.AttendanceMissingMembers, cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.AttendanceForDeletedMembers,
            hasAttendanceBase && schema.HasColumns("Members", "MemberID", "IsDeleted")
                ? await CountAsync(PreflightCountQuery.AttendanceForDeletedMembers, cancellationToken)
                : 0,
            isBlocking: false);
        Add(
            findings,
            MigrationPreflightCategories.OpenAttendanceForDeletedMembers,
            hasAttendanceBase && schema.HasColumns("Members", "MemberID", "IsDeleted")
                ? await CountAsync(
                    finalQueryShape
                        ? PreflightCountQuery.OpenActiveAttendanceForDeletedMembers
                        : PreflightCountQuery.OpenAttendanceForDeletedMembers,
                    cancellationToken)
                : 0);
        Add(
            findings,
            MigrationPreflightCategories.InvalidAttendanceSupersession,
            hasAttendanceBase && finalQueryShape
                ? await CountAsync(PreflightCountQuery.InvalidAttendanceSupersession, cancellationToken)
                : 0);
    }

    private static bool IsBaseSchemaReady(
        PreflightSchema schema,
        bool postStage) =>
        HasExactColumn(
            schema,
            "Users",
            "UserID",
            "int",
            4,
            10,
            0,
            false,
            isIdentity: true,
            identitySeed: 1,
            identityIncrement: 1)
        && schema.HasPrimaryKey("Users", "PK_Users", "UserID")
        && HasExactColumn(
            schema,
            "Users",
            "Email",
            "nvarchar",
            255,
            0,
            0,
            false,
            usesDatabaseDefaultCollation: true,
            isAnsiPadded: true)
        && HasExactColumn(
            schema,
            "Users",
            "PasswordHash",
            "nvarchar",
            255,
            0,
            0,
            postStage,
            usesDatabaseDefaultCollation: true,
            isAnsiPadded: true)
        && HasExactColumn(schema, "Users", "Role", "int", 4, 10, 0, false)
        && HasExactColumn(schema, "Users", "IsActive", "bit", 1, 1, 0, false)
        && HasExactColumn(
            schema,
            "Members",
            "MemberID",
            "int",
            4,
            10,
            0,
            false,
            isIdentity: true,
            identitySeed: 1,
            identityIncrement: 1)
        && schema.HasPrimaryKey("Members", "PK_Members", "MemberID")
        && HasExactColumn(schema, "Members", "IsDeleted", "bit", 1, 1, 0, false)
        && HasExactColumn(
            schema,
            "SystemSettings",
            "SettingKey",
            "nvarchar",
            100,
            0,
            0,
            false,
            usesDatabaseDefaultCollation: true,
            isAnsiPadded: true)
        && HasExactColumn(
            schema,
            "SystemSettings",
            "SettingValue",
            "nvarchar",
            -1,
            0,
            0,
            false,
            usesDatabaseDefaultCollation: true,
            isAnsiPadded: true)
        && HasExactColumn(
            schema,
            "AttendanceLogs",
            "AttendanceID",
            "int",
            4,
            10,
            0,
            false,
            isIdentity: true,
            identitySeed: 1,
            identityIncrement: 1)
        && HasExactColumn(schema, "AttendanceLogs", "MemberID", "int", 4, 10, 0, false)
        && (HasLegacyAttendanceDateShape(schema)
            || postStage && HasFinalAttendanceDateShape(schema))
        && HasExactColumn(
            schema,
            "AttendanceLogs",
            "CheckInTime",
            "datetime2",
            8,
            27,
            7,
            false)
        && HasExactColumn(
            schema,
            "AttendanceLogs",
            "CheckOutTime",
            "datetime2",
            8,
            27,
            7,
            true)
        && HasExactColumn(
            schema,
            "__EFMigrationsHistory",
            "MigrationId",
            "nvarchar",
            150,
            0,
            0,
            false,
            usesDatabaseDefaultCollation: true,
            isAnsiPadded: true)
        && schema.HasPrimaryKey(
            "__EFMigrationsHistory",
            "PK___EFMigrationsHistory",
            "MigrationId")
        && HasExactColumn(
            schema,
            "__EFMigrationsHistory",
            "ProductVersion",
            "nvarchar",
            32,
            0,
            0,
            false,
            usesDatabaseDefaultCollation: true,
            isAnsiPadded: true);

    private static bool HasLegacyAttendanceDateShape(PreflightSchema schema) =>
        HasExactColumn(
            schema,
            "AttendanceLogs",
            "AttendanceDate",
            "datetime2",
            8,
            27,
            7,
            false)
        && !schema.HasColumn("AttendanceLogs", "AttendanceDateLegacyDateTime");

    private static bool HasFinalAttendanceDateShape(PreflightSchema schema) =>
        HasExactColumn(
            schema,
            "AttendanceLogs",
            "AttendanceDate",
            "date",
            3,
            10,
            0,
            false)
        && HasExactColumn(
            schema,
            "AttendanceLogs",
            "AttendanceDateLegacyDateTime",
            "datetime2",
            8,
            27,
            7,
            true);

    private static bool HasFinalAttendanceQueryShape(PreflightSchema schema) =>
        HasFinalAttendanceDateShape(schema)
        && HasExactColumn(schema, "AttendanceLogs", "IsVoided", "bit", 1, 1, 0, false)
        && HasExactColumn(
            schema,
            "AttendanceLogs",
            "SupersededByAttendanceID",
            "int",
            4,
            10,
            0,
            true);

    private static bool HasAnyB1StagingMarker(PreflightSchema schema) =>
        schema.HasColumn("Users", "FirebaseUid")
        || schema.HasColumn("Users", "NormalizedEmail")
        || schema.HasColumn("Users", "MemberID")
        || schema.HasTable("AccountInvites")
        || schema.HasTable("MemberProjectionVersions");

    private static bool HasInviteAnomalyColumns(PreflightSchema schema) =>
        schema.HasColumns(
            "AccountInvites",
            "TokenHash",
            "NormalizedEmail",
            "IntendedRole",
            "Purpose",
            "TargetMemberID",
            "TargetUserID",
            "CreatedAtUtc",
            "ExpiresAtUtc",
            "UsedAtUtc",
            "RevokedAtUtc",
            "UsedByFirebaseUid",
            "RedemptionOperationId");

    private static bool IsB1SchemaFingerprintReady(PreflightSchema schema) =>
        IsUserIdentitySchemaReady(schema)
        && IsInviteSchemaReady(schema)
        && IsProjectionSchemaReady(schema);

    private static bool IsUserIdentitySchemaReady(PreflightSchema schema) =>
        HasExactColumn(
            schema,
            "Users",
            "PasswordHash",
            "nvarchar",
            255,
            0,
            0,
            true,
            usesDatabaseDefaultCollation: true,
            isAnsiPadded: true)
        && HasExactColumn(
            schema,
            "Users",
            "FirebaseUid",
            "nvarchar",
            128,
            0,
            0,
            true,
            SqlServerPreflightReadOnlyDataSource.ExactIdentityCollation,
            isAnsiPadded: true)
        && HasExactColumn(schema, "Users", "MemberID", "int", 4, 10, 0, true)
        && HasExactColumn(
            schema,
            "Users",
            "NormalizedEmail",
            "nvarchar",
            255,
            0,
            0,
            true,
            SqlServerPreflightReadOnlyDataSource.ExactIdentityCollation,
            isAnsiPadded: true)
        && schema.HasIndex(
            "Users",
            "UX_Users_FirebaseUid",
            true,
            "[FirebaseUid] IS NOT NULL",
            "FirebaseUid")
        && schema.HasIndex(
            "Users",
            "UX_Users_NormalizedEmail",
            true,
            "[NormalizedEmail] IS NOT NULL",
            "NormalizedEmail")
        && schema.HasIndex(
            "Users",
            "UX_Users_MemberID",
            true,
            "[MemberID] IS NOT NULL",
            "MemberID")
        && schema.HasTrustedCheck("Users", "CK_Users_Role", "[Role] IN (0, 1, 2)")
        && schema.HasTrustedCheck(
            "Users",
            "CK_Users_FirebaseUidNotBlank",
            "[FirebaseUid] IS NULL OR LEN([FirebaseUid]) > 0")
        && schema.HasTrustedCheck(
            "Users",
            "CK_Users_NormalizedEmailNotBlank",
            "[NormalizedEmail] IS NULL OR LEN([NormalizedEmail]) > 0")
        && schema.HasTrustedCheck(
            "Users",
            "CK_Users_RoleMemberLink",
            "([Role] = 2 AND [MemberID] IS NOT NULL) OR ([Role] IN (0, 1) AND [MemberID] IS NULL)")
        && schema.HasForeignKey(
            "Users",
            "FK_Users_Members_MemberID",
            "Members",
            "NO_ACTION",
            new PreflightForeignKeyColumnMetadata("MemberID", "MemberID"));

    private static bool IsInviteSchemaReady(PreflightSchema schema) =>
        HasExactColumn(
            schema,
            "AccountInvites",
            "AccountInviteID",
            "int",
            4,
            10,
            0,
            false,
            isIdentity: true,
            identitySeed: 1,
            identityIncrement: 1)
        && schema.HasPrimaryKey("AccountInvites", "PK_AccountInvites", "AccountInviteID")
        && HasExactColumn(schema, "AccountInvites", "TargetMemberID", "int", 4, 10, 0, true)
        && HasExactColumn(schema, "AccountInvites", "TargetUserID", "int", 4, 10, 0, true)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "TokenHash",
            "binary",
            32,
            0,
            0,
            false,
            isAnsiPadded: true)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "NormalizedEmail",
            "nvarchar",
            255,
            0,
            0,
            false,
            SqlServerPreflightReadOnlyDataSource.ExactIdentityCollation,
            isAnsiPadded: true)
        && HasExactColumn(schema, "AccountInvites", "IntendedRole", "int", 4, 10, 0, false)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "Purpose",
            "nvarchar",
            100,
            0,
            0,
            false,
            usesDatabaseDefaultCollation: true,
            isAnsiPadded: true)
        && HasExactColumn(schema, "AccountInvites", "CreatedByUserID", "int", 4, 10, 0, false)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "CreatedAtUtc",
            "datetime2",
            8,
            27,
            7,
            false)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "ExpiresAtUtc",
            "datetime2",
            8,
            27,
            7,
            false)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "UsedAtUtc",
            "datetime2",
            8,
            27,
            7,
            true)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "RevokedAtUtc",
            "datetime2",
            8,
            27,
            7,
            true)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "UsedByFirebaseUid",
            "nvarchar",
            128,
            0,
            0,
            true,
            SqlServerPreflightReadOnlyDataSource.ExactIdentityCollation,
            isAnsiPadded: true)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "RedemptionOperationId",
            "uniqueidentifier",
            16,
            0,
            0,
            true)
        && HasExactColumn(
            schema,
            "AccountInvites",
            "RowVersion",
            "timestamp",
            8,
            0,
            0,
            false,
            isRowVersion: true)
        && RequiredInviteChecks.All(check => schema.HasTrustedCheck(
            "AccountInvites",
            check.Name,
            check.Definition))
        && schema.HasIndex(
            "AccountInvites",
            "UX_AccountInvites_TokenHash",
            true,
            null,
            "TokenHash")
        && schema.HasIndex(
            "AccountInvites",
            "UX_AccountInvites_RedemptionOperationId",
            true,
            "[RedemptionOperationId] IS NOT NULL",
            "RedemptionOperationId")
        && schema.HasIndex("AccountInvites", "IX_AccountInvites_NormalizedEmail", false, null, "NormalizedEmail")
        && schema.HasIndex("AccountInvites", "IX_AccountInvites_CreatedByUserID", false, null, "CreatedByUserID")
        && schema.HasIndex("AccountInvites", "IX_AccountInvites_TargetMemberID", false, null, "TargetMemberID")
        && schema.HasIndex("AccountInvites", "IX_AccountInvites_TargetUserID", false, null, "TargetUserID")
        && schema.HasForeignKey(
            "AccountInvites",
            "FK_AccountInvites_Members_TargetMemberID",
            "Members",
            "NO_ACTION",
            new PreflightForeignKeyColumnMetadata("TargetMemberID", "MemberID"))
        && schema.HasForeignKey(
            "AccountInvites",
            "FK_AccountInvites_Users_TargetUserID",
            "Users",
            "NO_ACTION",
            new PreflightForeignKeyColumnMetadata("TargetUserID", "UserID"))
        && schema.HasForeignKey(
            "AccountInvites",
            "FK_AccountInvites_Users_CreatedByUserID",
            "Users",
            "NO_ACTION",
            new PreflightForeignKeyColumnMetadata("CreatedByUserID", "UserID"));

    private static readonly RequiredCheck[] RequiredInviteChecks =
    {
        new(
            "CK_AccountInvites_ExactlyOneTarget",
            "([TargetMemberID] IS NOT NULL AND [TargetUserID] IS NULL) OR " +
            "([TargetMemberID] IS NULL AND [TargetUserID] IS NOT NULL)"),
        new(
            "CK_AccountInvites_TargetRole",
            "([TargetMemberID] IS NOT NULL AND [IntendedRole] = 2) OR " +
            "([TargetUserID] IS NOT NULL AND [IntendedRole] IN (0, 1))"),
        new("CK_AccountInvites_ExpiryAfterCreation", "[ExpiresAtUtc] > [CreatedAtUtc]"),
        new("CK_AccountInvites_UsedOrRevoked", "[UsedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL"),
        new(
            "CK_AccountInvites_RedemptionMetadata",
            "([UsedAtUtc] IS NULL AND [UsedByFirebaseUid] IS NULL AND [RedemptionOperationId] IS NULL) OR " +
            "([UsedAtUtc] IS NOT NULL AND [UsedByFirebaseUid] IS NOT NULL " +
            "AND [RedemptionOperationId] IS NOT NULL " +
            "AND [RedemptionOperationId] <> " +
            "CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier))"),
        new(
            "CK_AccountInvites_UsedTimestampAfterCreation",
            "[UsedAtUtc] IS NULL OR [UsedAtUtc] >= [CreatedAtUtc]"),
        new(
            "CK_AccountInvites_UsedBeforeExpiry",
            "[UsedAtUtc] IS NULL OR [UsedAtUtc] < [ExpiresAtUtc]"),
        new(
            "CK_AccountInvites_RevokedTimestampAfterCreation",
            "[RevokedAtUtc] IS NULL OR [RevokedAtUtc] >= [CreatedAtUtc]"),
        new("CK_AccountInvites_NormalizedEmailNotBlank", "LEN([NormalizedEmail]) > 0"),
        new("CK_AccountInvites_PurposeNotBlank", "LEN(LTRIM(RTRIM([Purpose]))) > 0"),
        new(
            "CK_AccountInvites_UsedUidNotBlank",
            "[UsedByFirebaseUid] IS NULL OR LEN([UsedByFirebaseUid]) > 0")
    };

    private static bool IsProjectionSchemaReady(PreflightSchema schema) =>
        HasExactColumn(
            schema,
            "MemberProjectionVersions",
            "MemberID",
            "int",
            4,
            10,
            0,
            false)
        && schema.HasPrimaryKey(
            "MemberProjectionVersions",
            "PK_MemberProjectionVersions",
            "MemberID")
        && HasExactColumn(
            schema,
            "MemberProjectionVersions",
            "Version",
            "bigint",
            8,
            19,
            0,
            false,
            defaultDefinition: "CAST(0 AS bigint)")
        && HasExactColumn(
            schema,
            "MemberProjectionVersions",
            "RowVersion",
            "timestamp",
            8,
            0,
            0,
            false,
            isRowVersion: true)
        && schema.HasTrustedCheck(
            "MemberProjectionVersions",
            "CK_MemberProjectionVersions_VersionRange",
            "[Version] >= 0 AND [Version] <= 2199023255551")
        && schema.HasForeignKey(
            "MemberProjectionVersions",
            "FK_MemberProjectionVersions_Members_MemberID",
            "Members",
            "CASCADE",
            new PreflightForeignKeyColumnMetadata("MemberID", "MemberID"));

    private static bool HasExactColumn(
        PreflightSchema schema,
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
        bool identityNotForReplication = false) =>
        schema.ColumnMatches(PreflightColumnMetadataFactory.Expected(
            table,
            column,
            dataType,
            maximumLength,
            precision,
            scale,
            isNullable,
            collation,
            usesDatabaseDefaultCollation,
            isAnsiPadded,
            defaultDefinition,
            isRowVersion,
            isIdentity,
            identitySeed,
            identityIncrement,
            identityNotForReplication));

    private sealed record RequiredCheck(string Name, string Definition);

    private Task<long> CountAsync(
        PreflightCountQuery query,
        CancellationToken cancellationToken) =>
        _dataSource.CountAsync(query, cancellationToken);

    private static void Add(
        ICollection<MigrationPreflightFinding> findings,
        string category,
        long count,
        bool isBlocking = true)
    {
        findings.Add(new MigrationPreflightFinding(
            category,
            Math.Max(0, count),
            isBlocking));
    }

    private static bool IsValidStaleSessionHours(string? value) =>
        value is not null
        && int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed)
        && parsed is >= 1 and <= SystemSettingService.MaximumStaleSessionHours;

    private static bool IsValidTimeZone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 100
            || value.Any(char.IsControl))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
