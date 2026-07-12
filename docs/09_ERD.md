```mermaid
erDiagram
    Users {
        int UserID PK
        string FirebaseUid
        int MemberID FK
        string Username
        string Email
        string NormalizedEmail
        string FirstName
        string LastName
        int Role
        bool IsActive
        bool EmailVerified
        datetime CreatedAt
        datetime UpdatedAt
        datetime LastLoginAt
    }

    Members {
        int MemberID PK
        string FirstName
        string LastName
        string Gender
        datetime BirthDate
        string PhoneNumber
        string Email
        string Address
        string EmergencyContact
        string ProfilePicture
        string QRCode
        string Status
        datetime DateRegistered
        datetime LastModified
        bool IsDeleted
    }

    MembershipPlans {
        int PlanID PK
        string PlanName
        int DurationDays
        decimal Price
        string Description
        string Status
        datetime LastModified
    }

    Subscriptions {
        int SubscriptionID PK
        int MemberID FK
        int PlanID FK
        datetime StartDate
        datetime EndDate
        string Status
        datetime LastModified
    }

    MembershipPauses {
        int PauseID PK
        int SubscriptionID FK
        string Reason
        datetime PauseStartDate
        datetime PauseEndDate
        datetime DateCreated
    }

    Payments {
        int PaymentID PK
        int MemberID FK
        int SubscriptionID FK
        decimal Amount
        decimal Discount
        decimal FinalAmount
        int PaymentMethod
        int PaymentStatus
        string ReceiptNumber
        string ReferenceNumber
        datetime DatePaid
        datetime LastModified
        bool IsDeleted
    }

    AttendanceLogs {
        int AttendanceID PK
        int MemberID FK
        date AttendanceDate
        datetime CheckInTime
        datetime CheckOutTime
        string Source
        int ActorUserID FK
        bool IsVoided
        int VoidActorUserID FK
        datetime VoidedAtUtc
        string VoidReason
        int SupersededByAttendanceID FK
        datetime LastModified
    }

    AttendanceAdjustments {
        bigint AttendanceAdjustmentID PK
        int AttendanceID FK
        int Kind
        int ActorUserID FK
        datetime BeforeCheckOutTimeUtc
        datetime AfterCheckOutTimeUtc
        bool BeforeIsVoided
        bool AfterIsVoided
        int BeforeSupersededByAttendanceID
        int AfterSupersededByAttendanceID
        string Reason
        uniqueidentifier OperationID FK
        datetime CreatedAtUtc
    }

    AttendanceOperations {
        uniqueidentifier OperationID PK
        int TargetAttendanceID FK
        int OperationType
        int State
        binary RequestFingerprint
        int ActorUserID FK
        int OriginalHttpStatus
        string OriginalResultCode
        datetime CreatedAtUtc
        datetime CompletedAtUtc
    }

    Notifications {
        int NotificationID PK
        int MemberID FK
        string Title
        string Message
        int Status
        datetime ScheduledTime
        datetime SentTime
    }

    AuditLogs {
        int LogID PK
        int UserID FK
        string Action
        string Details
        datetime Timestamp
        string IpAddress
    }

    SystemSettings {
        string SettingKey PK
        string SettingValue
        string GroupName
        string Description
        datetime LastModified
    }

    WalkInVisitors {
        int VisitorID PK
        string VisitorName
        datetime VisitDate
        decimal FeePaid
        string Purpose
    }

    AccountInvites {
        int AccountInviteID PK
        int TargetMemberID FK
        int TargetUserID FK
        binary TokenHash
        string NormalizedEmail
        int IntendedRole
        string Purpose
        int CreatedByUserID FK
        datetime CreatedAtUtc
        datetime ExpiresAtUtc
        datetime UsedAtUtc
        datetime RevokedAtUtc
        string UsedByFirebaseUid
        uniqueidentifier RedemptionOperationId
    }

    MemberProjectionVersions {
        int MemberID PK_FK
        bigint Version
    }

    %% Relationships
    Users ||--o| Members : "linked as GymGoer"
    Members ||--o{ Subscriptions : "enrolled in"
    MembershipPlans ||--o{ Subscriptions : "defines"
    Subscriptions ||--o{ Payments : "paid via"
    Subscriptions ||--o{ MembershipPauses : "paused by"
    Members ||--o{ AttendanceLogs : "has"
    AttendanceLogs ||--o{ AttendanceAdjustments : "adjusted by"
    AttendanceLogs ||--o{ AttendanceOperations : "tracked via"
    AttendanceLogs ||--o| AttendanceLogs : "superseded by"
    Users ||--o{ AttendanceLogs : "acted as staff"
    Users ||--o{ AuditLogs : "generated"
    Members ||--o{ Notifications : "receives"
    Members ||--|| MemberProjectionVersions : "has version"
    Members ||--o{ AccountInvites : "invited via"
    Users ||--o{ AccountInvites : "created by"
```
