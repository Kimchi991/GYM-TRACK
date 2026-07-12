# GymTrackPro — Database Analysis & ERD

## Database Technology
- **DBMS:** Microsoft SQL Server
- **ORM:** Entity Framework Core 8 (Code-First)
- **Database Name:** `GymTrackProDB`
- **Migration source:** EF Core migrations in `GymTrackPro.API/Migrations`

---

## Entity Inventory

### 1. `Users` Table
**Entity:** `User.cs`

| Column | Type | Constraints | Notes |
|:--|:--|:--|:--|
| UserID | int | PK, Identity | Internal user ID |
| FirebaseUid | nvarchar(128) | Nullable | Firebase authentication UID |
| MemberID | int | FK → Members, Nullable | Links a GymGoer user to a Member record |
| Username | nvarchar(50) | Required | Unique display name |
| Email | nvarchar(255) | Required | Email address |
| NormalizedEmail | nvarchar(255) | Nullable | Uppercased for lookup |
| PasswordHash | nvarchar(255) | Nullable | Legacy; Firebase auth doesn't use this |
| FirstName | nvarchar(100) | Required | |
| LastName | nvarchar(100) | Required | |
| Role | int | Required | 0=Administrator, 1=Receptionist, 2=GymGoer |
| IsActive | bit | Required, Default=true | |
| EmailVerified | bit | Required, Default=false | |
| CreatedAt | datetime2 | Required | UTC |
| UpdatedAt | datetime2 | Required | UTC |
| LastLoginAt | datetime2 | Nullable | UTC |
| VerificationToken | nvarchar(100) | Nullable | Legacy email verification |
| ResetToken | nvarchar(100) | Nullable | Legacy password reset |
| ResetTokenExpires | datetime2 | Nullable | |

---

### 2. `Members` Table
**Entity:** `Member.cs`

| Column | Type | Constraints | Notes |
|:--|:--|:--|:--|
| MemberID | int | PK, Identity | |
| FirstName | nvarchar(50) | Required | |
| LastName | nvarchar(50) | Required | |
| Gender | nvarchar(10) | Required | |
| BirthDate | datetime2 | Required | |
| PhoneNumber | nvarchar(20) | Required | |
| Email | nvarchar(100) | Nullable | |
| Address | nvarchar(255) | Nullable | |
| EmergencyContact | nvarchar(100) | Required | |
| ProfilePicture | nvarchar(max) | Nullable | Relative URL path to uploaded image |
| QRCode | nvarchar(100) | Required | Unique identifier used for check-in (format: `GTP-XXXXXX`) |
| Status | nvarchar(20) | Required, Default='Active' | Active, Inactive |
| DateRegistered | datetime2 | Required | UTC |
| LastModified | datetime2 | Required | UTC |
| IsDeleted | bit | Required, Default=false | Soft delete flag |

---

### 3. `AttendanceLogs` Table
**Entity:** `Attendance.cs`

| Column | Type | Constraints | Notes |
|:--|:--|:--|:--|
| AttendanceID | int | PK, Identity | |
| MemberID | int | FK → Members, Required | |
| AttendanceDate | date | Required | Gym-local calendar date |
| CheckInTime | datetime2 | Required | UTC timestamp |
| CheckOutTime | datetime2 | Nullable | UTC timestamp |
| Source | nvarchar(32) | Nullable | StaffQr, SelfCheckIn, HistoricalImport, LegacyStaffQr |
| ActorUserID | int | FK → Users, Nullable | Staff who performed the check-in |
| IsVoided | bit | Required | Soft-void flag |
| VoidActorUserID | int | FK → Users, Nullable | Staff who voided the record |
| VoidedAtUtc | datetime2 | Nullable | |
| VoidReason | nvarchar(255) | Nullable | Required when voiding |
| SupersededByAttendanceID | int | FK → AttendanceLogs, Nullable | Self-referential for corrected checkouts |
| RowVersion | rowversion | Optimistic concurrency | |
| LastModified | datetime2 | Required | |

---

### 4. `AttendanceAdjustments` Table
**Entity:** `AttendanceAdjustment.cs`

| Column | Type | Notes |
|:--|:--|:--|
| AttendanceAdjustmentID | bigint | PK |
| AttendanceID | int | FK → AttendanceLogs |
| Kind | int (enum) | AttendanceAdjustmentKind |
| ActorUserID | int | FK → Users |
| BeforeCheckOutTimeUtc / AfterCheckOutTimeUtc | datetime2 | Nullable |
| BeforeIsVoided / AfterIsVoided | bit | Nullable |
| BeforeSupersededByAttendanceID / AfterSupersededByAttendanceID | int | Nullable |
| Reason | nvarchar(255) | |
| OperationID | uniqueidentifier | FK → AttendanceOperations |
| CreatedAtUtc | datetime2 | |

---

### 5. `AttendanceOperations` Table
**Entity:** `AttendanceOperation.cs`

| Column | Type | Notes |
|:--|:--|:--|
| OperationID | uniqueidentifier | PK |
| TargetAttendanceID | int | FK → AttendanceLogs, Nullable |
| OperationType | int (enum) | CheckIn, CheckOut, CorrectCheckout, Void |
| State | int (enum) | Pending, Applied, Failed |
| RequestFingerprint | binary(32) | Request idempotency fingerprint |
| ActorUserID | int | FK → Users |
| OriginalHttpStatus | int | Required |
| OriginalResultCode | nvarchar(64) | Required |
| CreatedAtUtc | datetime2 | |
| CompletedAtUtc | datetime2 | |

---

### 6. `Subscriptions` Table
**Entity:** `Subscription.cs`

| Column | Type | Constraints | Notes |
|:--|:--|:--|:--|
| SubscriptionID | int | PK, Identity | |
| MemberID | int | FK → Members, Required | |
| PlanID | int | FK → MembershipPlans, Required | |
| StartDate | datetime2 | Required | |
| EndDate | datetime2 | Required | |
| Status | nvarchar(20) | Required, Default='Active' | Active, Paused, Cancelled, Expired |
| LastModified | datetime2 | Required | |

---

### 7. `MembershipPlans` Table
**Entity:** `MembershipPlan.cs`

| Column | Type | Constraints | Notes |
|:--|:--|:--|:--|
| PlanID | int | PK, Identity | |
| PlanName | nvarchar(50) | Required | |
| DurationDays | int | Required | |
| Price | decimal(18,2) | Required | |
| Description | nvarchar(255) | Nullable | |
| Status | nvarchar(20) | Required, Default=Active | Active/inactive plan lifecycle state |
| LastModified | datetime2 | Required | |

---

### 8. `MembershipPauses` Table
**Entity:** `MembershipPause.cs`

| Column | Type | Notes |
|:--|:--|:--|
| PauseID | int | PK |
| SubscriptionID | int | FK → Subscriptions |
| Reason | nvarchar(255) | |
| PauseStartDate | datetime2 | |
| PauseEndDate | datetime2 | Nullable |
| DateCreated | datetime2 | |

---

### 9. `Payments` Table
**Entity:** `Payment.cs`

| Column | Type | Constraints | Notes |
|:--|:--|:--|:--|
| PaymentID | int | PK, Identity | |
| MemberID | int | FK → Members, Required | |
| SubscriptionID | int | FK → Subscriptions, Required | |
| Amount | decimal(18,2) | Required | Gross amount |
| Discount | decimal(18,2) | Required, Default=0 | |
| FinalAmount | decimal(18,2) | Required | Amount - Discount |
| PaymentMethod | int (enum) | Required | Cash=0, GCash=1, Maya=2, Card=3, BankTransfer=4 |
| PaymentStatus | int (enum) | Required | Pending=0, Paid=1, Failed=2, Cancelled=3, Refunded=4 |
| ReceiptNumber | nvarchar(50) | Required | Auto-generated sequential receipt |
| ReferenceNumber | nvarchar(100) | Nullable | External reference (e-wallet, bank) |
| DatePaid | datetime2 | Required | UTC |
| LastModified | datetime2 | Required | UTC |
| IsDeleted | bit | Required, Default=false | Soft delete |

---

### 10. `Notifications` Table
**Entity:** `Notification.cs`

| Column | Type | Notes |
|:--|:--|:--|
| NotificationID | int | PK |
| MemberID | int | FK → Members |
| Title | nvarchar(255) | |
| Message | nvarchar(max) | |
| Status | int (enum) | NotificationStatus |
| ScheduledTime | datetime2 | |
| SentTime | datetime2 | Nullable |

---

### 11. `AuditLogs` Table
**Entity:** `AuditLog.cs`

| Column | Type | Notes |
|:--|:--|:--|
| LogID | int | PK |
| UserID | int | FK → Users, Nullable |
| Action | nvarchar(100) | e.g. "Member Deleted", "Payment Refunded" |
| Details | nvarchar(max) | Human-readable event description |
| Timestamp | datetime2 | UTC |
| IPAddress | nvarchar(50) | Client IP |

---

### 12. `SystemSettings` Table
**Entity:** `SystemSetting.cs`

| Column | Type | Notes |
|:--|:--|:--|
| SettingKey | nvarchar(100) | PK, unique setting key |
| SettingValue | nvarchar(max) | String value |
| GroupName | nvarchar(100) | Required, default `General` |
| Description | nvarchar(255) | Nullable |
| LastModified | datetime2 | UTC |

---

### 13. `WalkInVisitors` Table
**Entity:** `WalkInVisitor.cs`

| Column | Type | Notes |
|:--|:--|:--|
| VisitorID | int | PK |
| VisitorName | nvarchar(100) | |
| VisitDate | datetime2 | UTC |
| FeePaid | decimal(18,2) | |
| Purpose | nvarchar(255) | Nullable |

---

### 14. `AccountInvites` Table
**Entity:** `AccountInvite.cs`

| Column | Type | Notes |
|:--|:--|:--|
| AccountInviteID | int | PK |
| TargetMemberID | int | FK → Members, Nullable |
| TargetUserID | int | FK → Users, Nullable |
| TokenHash | binary(32) | Hashed invite token |
| NormalizedEmail | nvarchar(255) | |
| IntendedRole | int (enum) | UserRole |
| Purpose | nvarchar(100) | Description of invite |
| CreatedByUserID | int | FK → Users |
| CreatedAtUtc | datetime2 | |
| ExpiresAtUtc | datetime2 | |
| UsedAtUtc | datetime2 | Nullable |
| RevokedAtUtc | datetime2 | Nullable |
| UsedByFirebaseUid | nvarchar(128) | Nullable |
| RedemptionOperationId | uniqueidentifier | Nullable — idempotency |
| RowVersion | rowversion | Optimistic concurrency |

---

### 15. `MemberProjectionVersions` Table
**Entity:** `MemberProjectionVersion.cs`

| Column | Type | Notes |
|:--|:--|:--|
| MemberID | int | PK, FK → Members (1:1) |
| Version | bigint | Monotonic version counter |
| RowVersion | rowversion | Optimistic concurrency |

---

## Relationships Summary

```
Users ──────────────────────────┐
  │ (1:0..1 via MemberID)       │
  ▼                             │
Members ──────────────────────► AccountInvites
  │ (1:N)                         (TargetMemberID)
  ├──► AttendanceLogs
  ├──► Subscriptions ──────────► MembershipPlans
  │       │ (1:N)
  │       ├──► Payments
  │       └──► MembershipPauses
  ├──► Notifications
  └──► MemberProjectionVersions (1:1)

AttendanceLogs ──────────────► AttendanceAdjustments (1:N)
AttendanceLogs ──────────────► AttendanceOperations (1:N)
AttendanceLogs ──────────────► AttendanceLogs (self-referential: SupersededBy)

Users ──────────────────────► AuditLogs (1:N)
Users ──────────────────────► AttendanceLogs (ActorUserID) (1:N)
Users ──────────────────────► AttendanceLogs (VoidActorUserID) (1:N)
Users ──────────────────────► AccountInvites (CreatedByUserID) (1:N)
```

---

## Cardinality

| Relationship | Type | Notes |
|:--|:--|:--|
| User → Member | 0..1 : 1 | A user may optionally be linked to one member (GymGoer) |
| Member → Subscriptions | 1 : N | A member can have multiple subscriptions over time |
| Subscription → Plan | N : 1 | Many subscriptions reference one plan |
| Subscription → Payments | 1 : N | A subscription can have multiple payment records |
| Subscription → Pauses | 1 : N | A subscription can be paused and resumed multiple times |
| Member → Attendance | 1 : N | A member has many attendance logs |
| Attendance → Adjustments | 1 : N | One log can have multiple correction adjustments |
| Attendance → Operations | 1 : N | One log tracks multiple idempotent operation records |
| Attendance → Attendance (self) | 0..1 : 1 | A corrected checkout creates a new log superseding the old |
| Member → Notifications | 1 : N | |
| User → AuditLogs | 1 : N | |
| Member → ProjectionVersion | 1 : 1 | One durable version per member |
| AccountInvite → Member | N : 0..1 | Invites can be for a specific member or open |

---

## Soft Delete Strategy

| Table | Mechanism |
|:--|:--|
| Members | `IsDeleted = true`, `LastModified` updated |
| MembershipPlans | `Status = "Inactive"`, `LastModified` updated |
| Payments | `IsDeleted = true`, `LastModified` updated |
| AttendanceLogs | `IsVoided = true` (not IsDeleted — different concept) |
| AccountInvites | `RevokedAtUtc` set to current UTC |

---

## Enums

| Enum | Values |
|:--|:--|
| `UserRole` | Administrator=0, Receptionist=1, GymGoer=2 |
| `PaymentMethod` | Cash=0, GCash=1, Maya=2, Card=3, BankTransfer=4 |
| `PaymentStatus` | Pending=0, Paid=1, Failed=2, Cancelled=3, Refunded=4 |
| `NotificationChannel` | InApp, Push |
| `NotificationStatus` | Unread, Read |
| `AttendanceAdjustmentKind` | CheckInCorrection, CheckOutCorrection |
| `AttendanceOperationType` | CheckIn, CheckOut, CorrectCheckout, Void |
| `AttendanceOperationState` | Pending, Applied, Failed |
| `AttendanceSessionState` | CheckedIn, CheckedOut |
| `AttendanceMembershipState` | Active, Expired, Paused, None |
| `MembershipStatus` | Active, Expired, Paused, None |
| `SyncStatus` | Pending, Synced, Failed |
