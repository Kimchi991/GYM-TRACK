namespace GymTrackPro.Shared.Constants;

public static class ErrorCodes
{
    // Auth & Invite
    public const string InviteInvalid = "INVITE_INVALID";
    public const string AccountPendingActivation = "ACCOUNT_PENDING_ACTIVATION";
    public const string IdentityConflict = "IDENTITY_CONFLICT";
    public const string ActivationOperationConflict = "ACTIVATION_OPERATION_CONFLICT";

    // Attendance
    public const string ActiveSessionExists = "ACTIVE_SESSION_EXISTS";
    public const string DailyVisitLimit = "DAILY_VISIT_LIMIT";
    public const string MembershipInactive = "MEMBERSHIP_INACTIVE";
    public const string MembershipPaused = "MEMBERSHIP_PAUSED";
    public const string AttendanceNotFound = "ATTENDANCE_NOT_FOUND";
    public const string AlreadyCheckedOut = "ALREADY_CHECKED_OUT";
    public const string InvalidCheckoutTime = "INVALID_CHECKOUT_TIME";
    public const string AttendanceRequiresOnline = "ATTENDANCE_REQUIRES_ONLINE";
    public const string OperationIdReused = "OPERATION_ID_REUSED";
    public const string InvalidOperationId = "INVALID_OPERATION_ID";
    public const string InvalidAttendanceRange = "INVALID_ATTENDANCE_RANGE";
    public const string InvalidAttendanceReason = "INVALID_ATTENDANCE_REASON";
    public const string InvalidRowVersion = "INVALID_ROW_VERSION";
    public const string InvalidCheckInCode = "INVALID_CHECK_IN_CODE";
    public const string MemberInactive = "MEMBER_INACTIVE";
    public const string AttendanceConflict = "ATTENDANCE_CONFLICT";
    public const string AttendanceConcurrencyConflict = "ATTENDANCE_CONCURRENCY_CONFLICT";
    public const string AttendanceOverlap = "ATTENDANCE_OVERLAP";
    public const string InvalidSupersedingAttendance = "INVALID_SUPERSEDING_ATTENDANCE";
    public const string AttendanceAlreadyVoided = "ATTENDANCE_ALREADY_VOIDED";
    public const string NoActiveSession = "NO_ACTIVE_SESSION";
    public const string AccessForbidden = "ACCESS_FORBIDDEN";
    public const string GymTimezoneInvalid = "GYM_TIMEZONE_INVALID";
    public const string UnsupportedAttendancePreset = "UNSUPPORTED_ATTENDANCE_PRESET";
    public const string AttendanceConfigurationInvalid = "ATTENDANCE_CONFIGURATION_INVALID";

    // Membership & Payments
    public const string MembershipDateInvalid = "MEMBERSHIP_DATE_INVALID";
    public const string MembershipConflict = "MEMBERSHIP_CONFLICT";
    public const string PaymentConflict = "PAYMENT_CONFLICT";
    public const string PaymentInvalid = "PAYMENT_INVALID";
}
