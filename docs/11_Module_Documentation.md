# GymTrackPro — Module Documentation

> Each system module documented with: purpose, actors, data flow, business rules, and related endpoints.

---

## Module 1: Authentication & Identity

### Purpose
Manages user identity, login, email verification, and account activation using Firebase Authentication as the identity provider and the internal SQL `Users` table for role and profile data.

### Actors
- All roles (Administrator, Receptionist, Gym Goer)
- Firebase Authentication (external service)

### Key Files
- `API/Controllers/AuthController.cs`
- `API/Controllers/MeController.cs`
- `API/Services/AuthenticationService.cs`
- `API/Authentication/FirebaseClaimTypes.cs`
- `Mobile/Services/FirebaseAuthService.cs`
- `Shared/Entities/User.cs`
- `Shared/Entities/AccountInvite.cs`

### User Login Flow
1. User enters email + password in mobile app
2. `FirebaseAuthService.LoginAsync()` calls Firebase SDK to sign in
3. Firebase returns a signed ID Token (JWT, 1-hour validity)
4. `ApiService.SyncUserWithBackendAsync()` sends the token to `POST /api/v1/auth/sync-user`
5. API validates the token using Firebase's public keys
6. API finds or creates the internal `User` record
7. Response contains the user's role, which determines which shell (AppShell vs GoerAppShell) the app navigates to

### Staff Onboarding Flow (Invite)
1. Admin generates a staff invite via back-office (not a current mobile screen — note: derived from implementation, `AccountInvite` with `IntendedRole = Receptionist`)
2. Invite token hash stored in `AccountInvites` table
3. New staff member registers on Firebase (or uses existing account)
4. Calls `POST /api/v1/auth/activate` with the invite code
5. API validates token, links Firebase UID to User record, sets role

### Member Onboarding Flow (App Invite)
1. Receptionist creates member record via Members module
2. Receptionist taps "Generate App Invite" on Member Details page
3. Staff enters member's email, calls `POST /api/v1/members/{id}/app-invite`
4. `AccountInvite` record created with `IntendedRole = GymGoer`, 24-hour expiry
5. Plaintext invite code shown once on screen
6. Staff shares code with member
7. Member downloads app, logs in with Firebase, enters code
8. `POST /api/v1/auth/activate` links member's Firebase UID to their User record

### Business Rules
- A Firebase UID can only link to one internal User record
- Invite codes expire after 24 hours (derived from implementation)
- A used or revoked invite cannot be reused (`UsedAtUtc` or `RevokedAtUtc` set)
- `RedemptionOperationId` (GUID) ensures idempotent activation — retrying the same activation returns the same result
- Email must be verified by Firebase before certain operations are allowed (controlled by `FirebaseOnboarding` policy)

---

## Module 2: Member Management

### Purpose
Maintains the registry of gym members, their personal information, QR codes, profile pictures, and soft-delete lifecycle.

### Actors
- Administrator, Receptionist (write access)
- Administrator only (delete access)

### Key Files
- `API/Controllers/MembersController.cs`
- `API/Services/MemberService.cs` (IMemberService)
- `Shared/Entities/Member.cs`
- `Shared/DTOs/CreateMemberDto.cs`, `UpdateMemberDto.cs`, `MemberResponseDto.cs`
- `Mobile/ViewModels/MembersViewModel.cs`, `MemberDetailsViewModel.cs`

### Business Rules
- Phone number and email (if provided) must be unique across non-deleted members
- QR code is generated as `GTP-XXXXXX` (6 random alphanumeric characters); uniqueness checked at creation
- Profile picture is stored server-side as a file; the `ProfilePicture` column stores the relative URL path
- Soft delete: `IsDeleted = true` + `LastModified` updated. Deleted members do not appear in search or listing results but their attendance and payment records are preserved
- Members with `IsDeleted = true` cannot be check-in targets
- Status values: `Active`, `Inactive`

### QR Code Process
1. Staff provides member data
2. API generates random 6-character alphanumeric suffix
3. Prefixes with `GTP-` → e.g., `GTP-A3BX9K`
4. Checks `Members.QRCode` for collision; regenerates if needed
5. Stores final code; member is given a physical QR card

---

## Module 3: Attendance Management

### Purpose
Records and manages member gym visits with check-in and check-out timestamps. Supports staff-side QR scanning and member-side self check-in. Includes admin-only void and checkout correction capabilities.

### Actors
- Administrator, Receptionist (staff check-in/out)
- Administrator only (void, correct checkout)
- Gym Goer (self check-in/out via member portal)

### Key Files
- `API/Controllers/AttendanceController.cs`
- `API/Controllers/MeAttendanceController.cs`
- `API/Services/AttendanceService.cs`
- `Shared/Entities/Attendance.cs`
- `Shared/Entities/AttendanceOperation.cs`
- `Shared/Entities/AttendanceAdjustment.cs`

### Check-In Business Rules
1. QR code must match a non-deleted member
2. Member must have at least one `Active` subscription
3. No existing open session (`CheckOutTime IS NULL AND IsVoided = false`)
4. `Source` is set to `StaffQr` for staff check-in or `SelfCheckIn` for member self-check-in
5. `ActorUserID` records which staff user performed the action (required for staff-side; derived from JWT claims for self-service)

### Idempotency
- `AttendanceOperation` stores a `OperationId` (GUID) for each check-in/out
- Self-service requests from mobile generate a random GUID per tap
- If the same `OperationId` arrives twice (network retry), the API returns the existing result without creating a duplicate log

### Void Business Rules
- Only Administrators can void (`OwnerOnly` policy)
- `VoidReason` is required
- `VoidActorUserID` and `VoidedAtUtc` are recorded
- Voided records remain in the DB for audit purposes
- Voided records do not count toward active session checks

### Correct Checkout Rules
- Only Administrators can correct checkout (`OwnerOnly` policy)
- A corrected checkout creates a NEW `AttendanceLogs` record superseding the original
- Original record's `SupersededByAttendanceID` is set to the new record's ID
- `AttendanceAdjustment` entry records the before/after times and the reason

### Legacy Routes
- `POST /checkin` and `POST /{id}/checkout` are deprecated with sunset headers. They continue to function but return `Deprecation: true` and `Sunset: Tue, 12 Jan 2027` response headers pointing to successor routes.

---

## Module 4: Membership Subscriptions

### Purpose
Manages the lifecycle of a member's membership plan subscription — enrollment, pausing, resuming, renewal, and auto-expiration.

### Key Files
- `API/Controllers/SubscriptionsController.cs`
- `API/Services/SubscriptionService.cs`
- `Shared/Entities/Subscription.cs`
- `Shared/Entities/MembershipPause.cs`
- `API/Workers/SubscriptionExpirationWorker.cs` (derived from implementation)

### Status Values
| Status | Meaning |
|:--|:--|
| Active | Subscription is valid; member can check in |
| Paused | Clock frozen; member temporarily suspended |
| Cancelled | Cancelled by admin (typically via refund) |
| Expired | EndDate passed; worker marked as Expired |

### Business Rules
- `StartDate` is set to the current date at enrollment time
- `EndDate = StartDate + Plan.DurationDays`
- A member can have multiple subscriptions historically; only `Active` ones are checked during check-in validation
- Pause records the reason in `MembershipPauses`; the subscription timer is frozen while paused (derived from implementation)
- Renewal atomically creates a new subscription AND a payment in a single database transaction (via `RenewSubscriptionAsync`)
- The `SubscriptionExpirationWorker` polls on a schedule, finds subscriptions where `EndDate < UtcNow AND Status = Active`, and batch-updates them to `Expired`

---

## Module 5: Payments

### Purpose
Records financial transactions between members and the gym. Supports multiple payment methods, partial discounts, and admin-only refunds with linked subscription cancellation.

### Key Files
- `API/Controllers/PaymentsController.cs`
- `API/Services/PaymentService.cs`
- `Shared/Entities/Payment.cs`
- `Shared/DTOs/CreatePaymentDto.cs`, `PaymentResponseDto.cs`

### Business Rules
- `ReceiptNumber` is auto-generated as a sequential string (format derived from implementation)
- `FinalAmount = Amount - Discount` (always non-negative)
- `PaymentStatus` starts as `Paid` for direct payments (derived from implementation)
- Refund: sets `PaymentStatus = Refunded` and corresponding subscription `Status = Cancelled`
- Refund is restricted to `OwnerOnly` policy
- Payment records use soft delete (`IsDeleted`) to preserve financial history in reports even after a member is deleted

### Payment Methods
Cash, GCash, Maya, Card, BankTransfer

---

## Module 6: Membership Plans

### Purpose
Defines the available plan types (name, duration, price) that members can subscribe to.

### Key Files
- `API/Controllers/PlansController.cs`
- `Shared/Entities/MembershipPlan.cs`

### Business Rules
- Plans are deactivated by setting `Status` to `Inactive`, preserving existing subscription references
- Plan price is stored as `decimal(18,2)` for financial precision
- `DurationDays` defines how many days the subscription lasts from enrollment date

---

## Module 7: Reports & Analytics

### Purpose
Generates financial and operational reports from live database data. All reports are available as JSON responses and as CSV file downloads.

### Reports

| Report | Source | Key Fields |
|:--|:--|:--|
| Daily Revenue | Payments grouped by day | Date, TransactionCount, GrossAmount, TotalDiscount, NetAmount |
| Monthly Revenue | Payments grouped by month | Month, TransactionCount, GrossAmount, TotalDiscount, NetAmount |
| Attendance | AttendanceLogs with member and plan info | AttendanceID, MemberName, PlanName, CheckInTime, CheckOutTime |
| Membership Sales | Payments joined with member, plan | MemberName, PlanName, Amount, Discount, FinalAmount, DatePaid, PaymentMethod |
| Expiring Memberships | Subscriptions WHERE EndDate < NOW + N days | MemberName, PlanName, StartDate, EndDate, Status |
| Refunds | Payments WHERE Status = Refunded | PaymentID, MemberName, ReceiptNumber, RefundedAmount, DateRefunded |
| Cashier Activity | AuditLogs for payment/subscription actions | Username, Action, Details, Timestamp, IpAddress |
| Attendance Summary | AttendanceLogs grouped by day/week/month | GymDate, Label, VisitCount |

### CSV Injection Prevention
All report exports use `CsvCellEncoder.Encode()` which:
1. Detects leading formula-injection characters: `=`, `+`, `-`, `@`
2. Prepends a single quote (`'`) to neutralize the formula
3. Wraps all values in double quotes with internal quotes escaped

---

## Module 8: Gym Goer Self-Service Portal

### Purpose
Provides gym members with a private mobile experience to view their membership status, track personal attendance, check in and out independently, view their digital membership card, and monitor monthly progress and badge achievements.

### Key Files
- `API/Controllers/MeController.cs`
- `API/Controllers/MeAttendanceController.cs`
- `API/Services/GymGoerProjectionService.cs` (derived from implementation)
- `Shared/DTOs/GymGoerDtos.cs`
- `Mobile/Views/GoerDashboardPage.xaml`
- `Mobile/Views/GoerDigitalCardPage.xaml`
- `Mobile/Views/GoerProgressPage.xaml`
- `Mobile/ViewModels/GoerDashboardViewModel.cs`

### Dashboard Data (`GoerDashboardDto`)
| Field | Source |
|:--|:--|
| MembershipStatus | Active subscription status |
| CurrentSession | Open attendance record (if any) |
| CurrentMonthMinutes | Sum of (CheckOutTime - CheckInTime) for current month |
| CurrentMonthDurationSeconds | Same, in seconds |
| VisitCount | Total distinct attendance days in current month |
| CurrentStreak | Consecutive days with at least one visit (ending today) |
| LongestStreak | Maximum consecutive-day streak historically |
| UnlockedBadges | Badge IDs earned (compatibility field, deprecated 2027-01-12) |
| Badges | Structured badge list with eligibility and unlock date |
| Timezone | Gym's configured timezone |

### Projection Versioning and Mobile Cache
- Each member's projection mutation version is stored in `MemberProjectionVersions`
- Attendance mutations increment that version
- The API recomputes the projection for online dashboard requests and includes `DataVersion`, `ContentETag`, and `CacheFreshUntilUtc` in `ProjectionMetadataDto`
- The mobile app saves the most recently successful dashboard and recent attendance in SQLite for offline or temporarily unavailable-server fallback

### Self-Service Check-In Security
- Only members with `GymGoerSelf` policy (role = GymGoer) can access `/me/attendance`
- Member ID is resolved from the Firebase UID via `Users.MemberID` — no way for one member to check in for another
- Idempotency GUID prevents double check-ins from retried network requests

---

## Module 9: System Settings

### Purpose
Allows the Administrator to configure operational parameters of the gym system through a key-value settings store.

### Key Files
- `API/Controllers/SettingsController.cs`
- `API/Services/SystemSettingService.cs`
- `Shared/Entities/SystemSetting.cs`
- `Shared/DTOs/SystemSettingDto.cs`

### Example Settings (Derived from implementation)
- `GymName` — Display name of the gym
- `Timezone` — IANA timezone identifier for date calculations (e.g., `Asia/Manila`)
- `Currency` — Currency code for financial display (e.g., `PHP`)
- Additional settings as needed without code changes

---

## Module 10: Notifications

### Purpose
Creates and tracks in-app notifications for gym members. Notifications are generated by system events (e.g., subscription expiring) and displayed in the Notifications screen.

### Key Files
- `API/Controllers/NotificationsController.cs`
- `API/Services/NotificationService.cs`
- `Shared/Entities/Notification.cs`
- `Mobile/Views/NotificationsPage.xaml`

### Business Rules
- `Channel` can be `InApp` or `Push` (Push is not yet delivered — FCM stub)
- `Status` starts as `Unread`; marked as `Read` via `PUT /api/v1/notifications/{id}/read`
- Notifications are linked to members (`MemberID`)
- Staff can view notifications for all members or filter by member
