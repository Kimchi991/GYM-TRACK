namespace GymTrackPro.API.Maintenance;

public enum PreflightStageMode
{
    PreStage,
    PostStage
}

public static class MigrationPreflightCategories
{
    public const string BaseSchemaInvalid = "BASE_SCHEMA_INVALID";
    public const string IdentitySchemaNotStaged = "IDENTITY_SCHEMA_NOT_STAGED";
    public const string B1SchemaStateMismatch = "B1_SCHEMA_STATE_MISMATCH";
    public const string MigrationHistoryMismatch = "MIGRATION_HISTORY_MISMATCH";
    public const string MigrationHistorySchemaMismatch = "MIGRATION_HISTORY_SCHEMA_MISMATCH";
    public const string DuplicateFirebaseUids = "DUPLICATE_FIREBASE_UIDS";
    public const string DuplicateNormalizedEmails = "DUPLICATE_NORMALIZED_EMAILS";
    public const string MissingNormalizedEmails = "MISSING_NORMALIZED_EMAILS";
    public const string InvalidCanonicalEmails = "INVALID_CANONICAL_EMAILS";
    public const string CanonicalEmailMismatches = "CANONICAL_EMAIL_MISMATCHES";
    public const string DuplicateCanonicalEmails = "DUPLICATE_CANONICAL_EMAILS";
    public const string DuplicateMemberLinks = "DUPLICATE_MEMBER_LINKS";
    public const string InvalidUserRoles = "INVALID_USER_ROLES";
    public const string InvalidUserRoleMemberLinks = "INVALID_USER_ROLE_MEMBER_LINKS";
    public const string MalformedAccountInvites = "MALFORMED_ACCOUNT_INVITES";
    public const string DuplicateInviteTokenHashes = "DUPLICATE_INVITE_TOKEN_HASHES";
    public const string DuplicateInviteRedemptionOperations = "DUPLICATE_INVITE_REDEMPTION_OPERATIONS";
    public const string UnresolvedInvitesForUnavailableMembers = "UNRESOLVED_INVITES_FOR_UNAVAILABLE_MEMBERS";
    public const string MissingProjectionVersions = "MISSING_PROJECTION_VERSIONS";
    public const string ProjectionSchemaInvalid = "PROJECTION_SCHEMA_INVALID";
    public const string DuplicateProjectionVersions = "DUPLICATE_PROJECTION_VERSIONS";
    public const string InvalidProjectionVersions = "INVALID_PROJECTION_VERSIONS";
    public const string DeletedMembersWithActiveUsers = "DELETED_MEMBERS_WITH_ACTIVE_USERS";
    public const string TimezoneConfigurationInvalid = "TIMEZONE_CONFIGURATION_INVALID";
    public const string StaleSessionConfigurationInvalid = "STALE_SESSION_CONFIGURATION_INVALID";
    public const string AttendanceDateTimeComponents = "ATTENDANCE_DATE_TIME_COMPONENTS";
    public const string AttendanceLocalDatesMissing = "ATTENDANCE_LOCAL_DATES_MISSING";
    public const string AttendanceLocalDateMismatches = "ATTENDANCE_LOCAL_DATE_MISMATCHES";
    public const string AttendanceLocalDateDuplicates = "ATTENDANCE_LOCAL_DATE_DUPLICATES";
    public const string AttendanceSchemaReadinessUnknown = "ATTENDANCE_SCHEMA_READINESS_UNKNOWN";
    public const string MultipleOpenAttendanceSessions = "MULTIPLE_OPEN_ATTENDANCE_SESSIONS";
    public const string CheckoutNotAfterCheckin = "CHECKOUT_NOT_AFTER_CHECKIN";
    public const string AttendanceMissingMembers = "ATTENDANCE_MISSING_MEMBERS";
    public const string AttendanceForDeletedMembers = "ATTENDANCE_FOR_DELETED_MEMBERS";
    public const string OpenAttendanceForDeletedMembers = "OPEN_ATTENDANCE_FOR_DELETED_MEMBERS";
    public const string InvalidAttendanceSupersession = "INVALID_ATTENDANCE_SUPERSESSION";
    public const string SubscriptionCalendarTimeComponents = "SUBSCRIPTION_CALENDAR_TIME_COMPONENTS";
}

public sealed record MigrationPreflightFinding(
    string Category,
    long Count,
    bool IsBlocking = true);

public sealed record MigrationPreflightReport(
    PreflightStageMode Mode,
    IReadOnlyList<MigrationPreflightFinding> Findings)
{
    public bool HasBlockingFindings => Findings.Any(finding =>
        finding.IsBlocking && finding.Count > 0);
}

public static class MigrationPreflightReportFormatter
{
    public static string Format(MigrationPreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            report.Mode == PreflightStageMode.PreStage
                ? "PREFLIGHT_MODE=PRE_STAGE"
                : "PREFLIGHT_MODE=POST_STAGE",
            report.HasBlockingFindings
                ? "PREFLIGHT_STATUS=BLOCKED"
                : "PREFLIGHT_STATUS=PASS"
        };
        lines.AddRange(report.Findings
            .OrderBy(finding => finding.Category, StringComparer.Ordinal)
            .Select(finding =>
                $"{finding.Category}={finding.Count};SEVERITY=" +
                (finding.IsBlocking ? "BLOCKING" : "INFO")));
        return string.Join(Environment.NewLine, lines);
    }
}
