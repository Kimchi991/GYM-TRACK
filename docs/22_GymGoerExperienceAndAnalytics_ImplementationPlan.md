# Gym Goer Experience and Attendance Analytics Implementation Plan

## Document control

| Field | Value |
|---|---|
| Target repository | GymTrackPro |
| Required branch | `feature/firebase-auth` |
| Plan date | 2026-07-12 |
| Status | Approval-ready implementation plan; coding remains blocked by section 18 decisions and WP0 gates |
| Source draft | `C:\Users\velas\Downloads\implementation_plan.md` |
| Cloud system of record | ASP.NET Core API and Microsoft SQL Server on MonsterASP |
| Identity provider | Firebase Authentication for email/password sign-up, login, verification, reset, and token issuance |
| Local storage | SQLite cache in the .NET MAUI app; never the authority for identity, roles, attendance, progress, or badges |

This plan replaces the source draft. It preserves every requested outcome - a Gym Goer experience, role-based navigation, offline dashboard data, strict check-in/check-out, time-based progress, streaks and badges, and an owner attendance graph - while correcting gaps that would otherwise make the feature insecure or internally inconsistent.

## 1. Executive decision

The feature is feasible, but the draft must not be implemented as written. The safe target architecture is:

1. Firebase proves who signed in. It does not decide application roles or which member record the caller may access.
2. SQL Server on MonsterASP remains the source of truth for users, roles, the User-to-Member link, memberships, attendance, progress calculations, and badge eligibility.
3. The API fully validates Firebase ID tokens, resolves the database user from the Firebase UID, adds application authorization context, and enforces role and record ownership on every endpoint.
4. Firebase sign-up is public, but GymTrackPro application activation is invite-only. A Member invite can activate only a `GymGoer`; public sign-up can never produce operational staff access.
5. The current internal `Administrator = 0` enum/wire value is retained for v1 to protect old-client JSON compatibility and is displayed to users as “Gym Owner.” `Receptionist = 1` remains and `GymGoer = 2` is added.
6. Gym Goers use self-scoped `/api/v1/me/...` endpoints. Those endpoints derive `MemberID` from the authenticated server-side link and never accept a caller-selected member ID.
7. Existing staffed QR check-in remains the v1 check-in mechanism: the Gym Goer displays their digital membership QR code and staff scans it. This plan does not add an insecure static-gym-QR or remote self-check-in mechanism.
8. Check-in and check-out writes are online-only and use server time. Offline mode is read-only for Gym Goer history, progress, badges, membership status, and QR card data.
9. Streaks, duration totals, and badge eligibility are calculated on the server. The mobile app caches the returned snapshot; it does not recalculate authoritative values.
10. Cached attendance data must not be stored in plaintext. Each account gets a distinct AES-GCM data-encryption key kept in MAUI `SecureStorage`; cache payloads and authenticated metadata are account-bound, minimized, and cryptographically discarded on logout.
11. The owner graph is a server-aggregated, gym-timezone-aware attendance trend, not client aggregation of an unbounded attendance list.
12. Multi-gym, multi-branch, trainer, workout recommendation, nutrition, wearable integration, rewards redemption, and social leaderboards are outside this feature. The formal project specification explicitly keeps multi-branch support out of scope.

## 2. Strict assessment of the source draft

| Severity | Draft problem | Repository evidence | Required correction |
|---|---|---|---|
| P0 | A newly synchronized Firebase user is assigned `Receptionist`. | `AuthenticationService.SyncUserAsync` defaults to `UserRole.Receptionist`. | Unknown users get no app access; only a valid member invite may create/bind a `GymGoer`; owner/staff roles require controlled provisioning. |
| P0 | Firebase UID is received but discarded. | `SyncUserAsync(firebaseUid, email)` never persists `firebaseUid`; `User` has no UID field. | Add unique `FirebaseUid` and use it as the stable identity link after one-time onboarding. |
| P0 | Firebase issuer and audience validation are disabled. | `Program.cs` sets `ValidateIssuer = false` and `ValidateAudience = false`. | Validate signature, lifetime, exact issuer, and exact project audience from environment-specific configuration. |
| P0 | API role authorization cannot work with current Firebase tokens. | Owner routes require `Administrator`, while the Firebase ID token has no SQL role claim. | Resolve the SQL user on authenticated requests and add/test database-backed application claims or policies. |
| P0 | Any authenticated Firebase account can reach many back-office endpoints. | Most controllers use only `[Authorize]`; no `GymGoer` denial policy or ownership check exists. | Apply explicit policies to every controller/action before enabling member accounts. |
| P0 | A live-looking MonsterASP SQL credential is tracked in source. | `src/GymTrackPro.API/appsettings.json` contains a full production-style connection string. | Treat it as compromised, rotate it, use MonsterASP environment variables, remove it from tracked configuration, and scan history. Never reproduce it in tickets or logs. |
| P0 | Registration claims email verification but does not complete a verified backend profile flow. | The mobile view model creates a Firebase user, ignores collected username/name values, and the API unconditionally marks the SQL user verified. | Send a Firebase verification email, reject unverified ID tokens, then redeem an authorized invite whose normalized email and target match; email alone never binds a Member or privileged User. |
| P1 | The role names in the draft do not exist. | `UserRole` contains only `Administrator` and `Receptionist`. | Retain `Administrator = 0` as the internal/wire owner role for compatibility, display “Gym Owner” in UI, preserve `Receptionist = 1`, and add `GymGoer = 2`. |
| P1 | No User-to-Member link exists. | `User` and `Member` are unrelated; matching arbitrary `memberId` would be insecure. | Add a nullable, unique `User.MemberID` foreign key and a controlled linking workflow. |
| P1 | `LocalDatabaseService` is incorrectly marked new. | It already exists and creates `SyncQueue` and `LocalMember`. | Modify it with versioned local migrations and encrypted account-scoped cache tables. |
| P1 | The draft calculates streaks on the client. | The draft assigns streak calculation to `GoerDashboardViewModel`. | Calculate on the API using gym-local attendance dates; cache only the result. |
| P1 | Check-in and checkout already exist, but the draft treats them as new. | Current routes are `POST attendance/checkin` and `POST attendance/{id}/checkout`. | Evolve the current service and routes; do not break staff clients without a versioned transition. |
| P1 | Duplicate prevention is race-prone. | Service code loads records and checks in memory; there is no database uniqueness constraint for a local day or open session. | Add database constraints/indexes and map uniqueness failures to stable 409 responses. |
| P1 | “No auto-checkout” has no missed-checkout recovery. | An open record blocks all future check-ins indefinitely. | Add an owner-only audited correction workflow; do not silently auto-close a session. |
| P1 | UTC dates conflict with the configured Manila timezone. | Attendance and dashboard code use `DateTime.UtcNow.Date`; settings specify `Asia/Manila`. | Store instants in UTC but derive `AttendanceDate`, periods, streaks, and graph buckets in the configured gym timezone. |
| P1 | The requested owner graph is missing from the proposed changes. | The API already returns `CheckInsByHour`, but the owner view model and XAML do not render it. | Define and implement a bounded attendance-summary contract plus an accessible native MAUI graph. |
| P1 | The SQLite security question is left unresolved. | Current SQLite connection has no application-layer encryption and Android backup behavior is not addressed. | Encrypt cached payloads, keep the key in `SecureStorage`, exclude the cache from backup, minimize fields, and wipe it on logout/account change. |
| P1 | Firebase session refresh is not designed. | Only the short-lived ID token is saved; Firebase ID tokens expire in about one hour. | Persist the refresh session securely, refresh before API calls, retry one 401 once, and force reauthentication when refresh fails. |
| P2 | Startup and logout can leave inconsistent session state. | Startup initialization runs in an unawaited `Task.Run`; logout removes a different storage key than `ApiService` uses. | Add one awaited session/bootstrap coordinator and one authoritative logout path. |
| P2 | Automated tests no longer represent the branch. | PowerShell scripts call removed custom `/auth/register` and `/auth/login` endpoints; no unit-test projects exist. | Replace auth setup with a test authentication handler and Firebase Emulator coverage; add API and mobile service/view-model tests. |
| P2 | Documentation contradicts the branch. | architecture, roadmap, auth docs, ADRs, and agent guidelines still state that Firebase is not the identity provider. | Supersede the Firebase ADR and update all affected specifications in the same delivery. |

## 3. Current-state baseline

### 3.1 Confirmed reusable implementation

- The solution is already split into Shared, API, and MAUI Mobile projects.
- SQL Server via EF Core is already configured for the MonsterASP database.
- Firebase email/password login, registration, and password-reset calls are partially integrated in the mobile project.
- The API already accepts a Firebase bearer token on `auth/sync-user`.
- Attendance already supports a staff QR check-in, open-session detection, daily-limit detection, and checkout.
- Dashboard metrics already calculate today's hourly check-ins, but mobile does not display them.
- Reports already expose raw attendance rows and CSV export for administrators.
- SQLite, connectivity detection, and a sync queue are scaffolded, but attendance caching and actual queued upload processing are not implemented.
- The existing digital QR data can be reused; a second QR identity scheme is not needed.

### 3.2 Baseline verification result

On 2026-07-12, `dotnet build src/GymTrackPro.slnx --no-restore` succeeded for the API, Shared library, Android, iOS, Mac Catalyst, and Windows targets with 0 errors and 7 warnings.

Warnings that affect this feature:

- Nullable-reference warnings exist in dashboard and report aggregation code.
- The current SQLite native library raises an Android 16 16 KB page-size compatibility warning.
- A camera dependency also raises an Android 16 16 KB page-size warning.

These warnings are not evidence of a failed baseline, but the SQLite/native compatibility warnings must be resolved or explicitly supported before a production mobile release.

### 3.3 Scope conflict with the formal specification

`docs/00_Project_Specification.pdf` defines only Administrator and Receptionist application accounts, while members interact through QR attendance and a digital card. Adding a logged-in Gym Goer and using Firebase Authentication are therefore architectural changes, not small UI additions. They require:

- an ADR that supersedes the current custom-JWT/Firebase-support-only decisions;
- an updated role and permission matrix;
- a new User-to-Member identity link;
- explicit offline-auth limitations; and
- updates to authentication, navigation, attendance, database, testing, and deployment documentation.

## 4. Locked product and architecture decisions

These are the recommended implementation assumptions. Changing one requires updating this plan before coding.

### 4.1 Product boundary

- One GymTrackPro installation represents one gym facility.
- Deployment invariant: one API process/configuration and its one SQL database serve exactly one gym; this release is not multi-tenant SaaS.
- `GymOwner` is the product/display name for the current internal `Administrator` role, not a multi-tenant owner. The enum/wire value remains `Administrator` in v1.
- `Receptionist` remains a back-office role.
- `GymGoer` is the member-facing role.
- Multi-branch and multi-tenant data isolation are deferred to a separate epic because they would require adding a gym/tenant key across nearly every table and query.

### 4.2 Identity boundary

- Firebase owns credentials, email verification, password reset, ID tokens, and refresh tokens.
- SQL Server owns app activation, role, member link, profile, and permissions.
- JWT bearer validation uses `MapInboundClaims = false`, accepts only Firebase RS256 ID tokens for the configured project, validates exact issuer/audience/signature/lifetime with at most two minutes clock skew, reads explicit `sub`, `email`, and `email_verified` claims, and ignores any token-supplied role/custom access claim.
- Missing Firebase project/issuer/audience configuration fails API startup; it never disables validation as a fallback.
- V1 application revocation is SQL-authoritative: `User.IsActive` is checked on every business request, so disabling the SQL User blocks app access immediately. No Firebase Admin revocation network check is placed on the request path. Firebase-only deletion/revocation can leave an already issued ID token cryptographically valid until its normal expiry, but it still cannot bypass an inactive SQL User; this limitation is documented and tested.
- Operational disablement updates SQL `IsActive` first. If the project owner also disables/deletes the Firebase account through the Firebase Console or a future Admin tool, that happens second; a Firebase-side failure never re-enables SQL access.
- The Firebase client API key is an app/project identifier, not an authorization secret, but it must be environment-specific and restricted to the intended Firebase APIs and applications.
- The mobile app never connects directly to the MonsterASP database.
- The API never trusts role, user ID, member ID, or timestamps supplied by the mobile client.

### 4.3 Account provisioning and member linking

Email matching alone is not accepted as an account-linking mechanism. The v1 workflow uses a single-use, expiring invitation stored in SQL:

1. Staff creates or updates the Member record with the member's controlled email address.
2. Staff generates a Gym Goer app invite for that exact Member. The API stores only a hash of the random invite token, its intended email and role, expiry, creator, and unused status.
3. The Gym Goer signs up through Firebase with the invited email and completes Firebase email verification.
4. The mobile onboarding request sends the invite token while the Firebase ID token proves the caller's UID and verified email.
5. In one SQL transaction, the API validates the token hash, expiry, unused state, intended role, email match, target Member state, and lack of an existing User/Member link.
6. The API creates or binds the SQL User as `GymGoer`, persists the immutable Firebase UID, links the Member, and marks the invite used.
7. A retry by the same Firebase UID for the same already-linked target returns the existing successful profile. Reuse by another UID, expired tokens, email mismatches, or a UID bound elsewhere return one generic client denial and a detailed internal security audit category.
8. Once linked, Firebase UID is authoritative. Later email changes never silently relink the account.

Owner and receptionist records are pre-provisioned in SQL with their role, then bound through a staff invite with the same one-time controls. There is no uninvited or self-service owner/receptionist application-activation path; invited staff redeem `/auth/activate` like other invited identities.

Initial-owner bootstrap is an out-of-band maintenance operation, never a standing public endpoint:

1. Put the existing API in maintenance mode, back up the database, and identify exactly one independently verified legacy `Administrator` record.
2. That owner creates and verifies the intended Firebase account. An operator retrieves its UID and verified email from the Firebase Console through an authorized admin session.
3. A one-time, environment-gated bootstrap command binds that UID only when SQL role is `Administrator`, SQL/Firebase normalized emails match, both UID/User links are empty, and no owner has already been bootstrapped. It records an audit entry and returns no credentials.
4. The command refuses a second use and is disabled in normal web startup. The hardened UID-policy API is deployed and smoke-tested before maintenance mode is removed.

A Firebase user without a valid invite may see only a neutral “Ask your gym to activate app access” onboarding state. It receives no SQL role, member link, dashboard, or business API access.

Invite implementation defaults:

- Generate at least 32 cryptographically random bytes and encode them for safe manual entry/link transport.
- Store only a SHA-256 or stronger one-way token hash, bind the row to purpose/role/target, and compare candidate hashes in constant time after lookup.
- Default expiry is 72 hours; creating a replacement revokes the previous unused invite for the same target.
- Rate-limit redemption by Firebase UID, client/IP partition, and a non-reversible token-hash prefix; return one non-enumerating client error for invalid/expired/used/mismatched tokens.
- Deliver the plaintext token once through an explicit staff copy/handoff or approved email channel. Never put it in a URL query string, analytics event, referrer, log, notification preview, or persistent client clipboard history under app control.
- Enforce exactly one target type: a Member for `GymGoer`, or a pre-provisioned User for internal `Administrator`/`Receptionist`.

### 4.4 Attendance interaction

- A Gym Goer displays their membership QR code in the `QR Check-in` tab.
- A Gym Owner or Receptionist scans it using the existing attendance screen.
- Either staff or the Gym Goer may perform a manual checkout through an authorized endpoint, but the server selects the open session and records server UTC time.
- Offline check-in and checkout are not included. The mobile app must clearly say that an internet connection is required for attendance writes.
- A forgotten checkout stays open. Only an owner may correct it, supplying a corrected time and mandatory reason. The original and corrected values are audited.
- The daily check-in rule remains one visit per gym-local calendar day.

### 4.5 Progress and gamification rules

- A valid visit is an attendance row accepted by the server.
- Visit days use the persisted gym-local `AttendanceDate`, not the device clock.
- Monthly time is the sum of each completed session's overlap with the selected gym-local month boundaries converted to UTC. A cross-month session is split across the two months for duration totals, while its visit day remains the local check-in date.
- Open sessions are shown separately and excluded from completed totals until checkout.
- A current streak is the number of consecutive local calendar days ending today, or ending yesterday when the member has not yet visited today. If the latest visit is older than yesterday, current streak is zero.
- Longest streak is the maximum consecutive-day run in the member's history.
- `First Visit` unlocks after one valid visit; its derived unlock date is the first accepted visit date.
- `3-Day Streak` unlocks when longest streak first reaches three days; its derived unlock date is the third day of the earliest qualifying run.
- `Weekend Warrior` unlocks after visits on both Saturday and Sunday in the same gym-local Monday-to-Sunday week; its derived unlock date is that Sunday.
- Current streak is calculated as of today across all history. Longest streak and badge eligibility are all-time even when the progress endpoint selects one month.
- Badge eligibility is derived on demand from authoritative attendance for v1 and returns a `BadgeRuleVersion`. No badge-award table, rewards, points, leaderboard, or redemption system is added.
- “Unlocked” is reversible in v1: voiding/superseding attendance can make a badge disappear after refresh. Permanent award history is a future enhancement and this behavior requires explicit approval in section 18.

### 4.6 Local-cache security decision

Plaintext attendance caching is rejected. Attendance timestamps reveal personal movement patterns and can leak across device backups or account changes.

The approved v1 pattern is application-layer encrypted cache envelopes rather than an unverified SQLCipher integration:

- Each signed-in account uses a separate cache database file whose filename contains only a non-reversible account identifier, never raw UID/email.
- Each account receives an independent 256-bit data-encryption key (DEK) stored under a namespaced, versioned `SecureStorage` key. No account shares a DEK.
- Every write uses AES-GCM with a new 96-bit CSPRNG nonce and 128-bit authentication tag. A unique `(account scope, key version, nonce)` constraint/retry prevents nonce reuse under concurrency.
- Authenticated associated data (AAD) binds the immutable account scope, cache key, envelope/schema version, DEK version, server data version, fetched time, and expiry/stale metadata. Moving a valid ciphertext to another account/key or editing its freshness metadata must fail authentication.
- Envelope replacement is atomic. The app accepts only non-decreasing server data versions, keeping the last accepted version in namespaced secure state to detect local rollback.
- The cache does not store names, email addresses, bearer tokens, refresh tokens, or raw API errors in plaintext.
- The QR/digital-card payload is included only inside the encrypted account envelope.
- The ID/refresh session stays in `SecureStorage`, never SQLite.
- A missing/corrupt key or failed authentication tag causes cache deletion and online refetch; it never falls back to plaintext.
- Logout, account switch, terminal remote disable, or identity-link conflict first stops cache workers and closes connections, then destroys the account DEK and removes that account's DB, WAL, and SHM files. Destroying the DEK is the cryptographic-erasure boundary.
- Key rotation increments the DEK version and rewrites a fresh account database; old keys are removed only after successful atomic migration.
- Android backup configuration excludes the account DB/WAL/SHM and MAUI SecureStorage preferences through both legacy `fullBackupContent` and API 31+ `dataExtractionRules`. iOS/Mac Catalyst cache files are marked excluded from backup and Keychain persistence/reinstall behavior is documented and tested.
- The app verifies encryption with known-plaintext, tag/ciphertext mutation, AAD/metadata mutation, cross-account/key swap, nonce-collision retry, rollback, key rotation, concurrent write, crash consistency, and DB/WAL/SHM remnant tests.
- The existing plaintext-capable `LocalMember` table must be audited. If any workflow populates it with PII, that workflow must migrate to protected storage or remain disabled; adding encrypted Gym Goer envelopes does not excuse a second plaintext PII cache in the same app.
- The current `SyncService` must fail closed. It may not delete a queued operation until a real API acknowledgement is recorded; the present TODO/delete behavior must not be enabled for production writes.

This design uses built-in .NET cryptography, keeps SQLite as requested, avoids queryable plaintext, and avoids making release depend on an unproven SQLCipher/native package. The underlying SQLite package still requires the Android 16 compatibility gate.

## 5. Roles and authorization matrix

`[Authorize]` alone is not sufficient. Every endpoint must use one of the named policies below, and services/repositories must preserve object-level scope.

| Capability | Gym Owner (`Administrator`) | Receptionist | GymGoer |
|---|---:|---:|---:|
| Owner dashboard and attendance graph | Yes | No | No |
| Member list/profile management | Yes | Yes | No |
| Plan create/update/delete | Yes | No | No |
| Plan read for operations | Yes | Yes | No |
| Subscription and payment operations | Yes | Yes | No |
| Staff QR check-in and staff checkout | Yes | Yes | No |
| Create/revoke Gym Goer invite for a Member | Yes | Yes | No |
| Create/revoke owner/receptionist invite | Yes | No | No |
| Attendance correction | Yes | No | No |
| Full attendance/report export | Yes | No | No |
| System settings update | Yes | No | No |
| Own member card/membership | No special `/me` access required | No special `/me` access required | Own record only |
| Own attendance/current session/progress/badges | No special `/me` access required | No special `/me` access required | Own record only |
| Own manual checkout | No special `/me` access required | No special `/me` access required | Own open session only |

Named policies:

- `ActiveAppUser`: valid Firebase token, known SQL User, Firebase UID match, verified email, and `IsActive = true`.
- `BackOffice`: `ActiveAppUser` plus internal `Administrator` or `Receptionist`.
- `OwnerOnly`: `ActiveAppUser` plus internal `Administrator`.
- `GymGoerSelf`: `ActiveAppUser` plus `GymGoer` and a non-null linked `MemberID`.
- `FirebaseOnboarding`: valid Firebase ID token and verified email; allowed only on the sync/link endpoint before an app User exists.

Implementation rule: introduce central `AppClaimTypes` and `ICurrentUserContext`. Existing code that parses `ClaimTypes.NameIdentifier` as an integer must be migrated because the Firebase subject is a string UID, not the SQL `UserID`.

### 5.1 Controller/action policy inventory

The contract-freeze packet must enumerate each action, but the minimum frozen mapping is:

| Controller/action group | Required policy | Resource rule |
|---|---|---|
| Auth activate/sync compatibility | `FirebaseOnboarding` | Activate only the invite target; sync only an already-bound UID. |
| Operational dashboard metrics | `BackOffice` | Single-gym operational data; available to owner and receptionist. |
| Owner attendance summary graph | `OwnerOnly` | Single-gym owner aggregate. |
| Members list/get/create/update | `BackOffice` | Back-office only; no Gym Goer access. |
| Member delete/deactivate | `OwnerOnly` unless the existing approved matrix says deactivate is staff-safe | Preserve related history. |
| Member Gym Goer invite create/revoke | `BackOffice` | Target must be the requested eligible Member. |
| Plans read | `BackOffice` | Operational read only. |
| Plans create/update/delete | `OwnerOnly` | Existing owner behavior preserved. |
| Subscriptions create/read/pause/resume/renew | `BackOffice` | Requested Member must exist and be eligible. |
| Payments create/read | `BackOffice` | Requested Member/subscription relationship validated. |
| Payment refund | `OwnerOnly` | Existing owner-only behavior preserved. |
| Staff attendance read/check-in/check-out | `BackOffice` | Staff route; requested record/member must exist. |
| Attendance correct/void/supersede | `OwnerOnly` | Reason, non-overlap, immutable adjustment, audit. |
| Reports and exports | `OwnerOnly` | Owner-only for this release. |
| Settings read | `BackOffice` | Receptionist receives approved non-secret settings only. |
| Settings update | `OwnerOnly` | Timezone becomes immutable after attendance without a dedicated migration. |
| Existing notifications controller | `BackOffice` | Gym Goer notifications require future `/me/notifications`; generic IDs are not exposed. |
| Staff User invite create/revoke/bootstrap administration | `OwnerOnly` | Target SQL role is pre-provisioned; no role in request. |
| `/me` profile/card/dashboard/attendance/progress | `GymGoerSelf` | MemberID comes only from current-user context. |

HTTP outcomes are frozen:

- Missing, malformed, wrong-project, invalid-signature, or expired ID token -> 401.
- Valid token but unprovisioned, inactive, unverified, or role-denied identity -> 403 with a stable non-enumerating code.
- A foreign or hidden resource ID -> 404, not an existence-revealing 403.
- State or operation-fingerprint conflict -> 409.
- Validation/range failure -> 400 with structured field errors.
- Rate limit -> 429.

Add a reflection/endpoint-metadata integration test that fails when a business action is anonymous, has only bare `[Authorize]`, or lacks one approved named policy. Agent A defines/freeze policies; each controller's file owner applies them; Agent A red-teams the completed inventory.

## 6. Target data design

### 6.1 Server schema

| Table/entity | Change | Integrity rule |
|---|---|---|
| `Users` | Retain `Administrator = 0` internally and display “Gym Owner”; preserve `Receptionist = 1`; add `GymGoer = 2`; add nullable `FirebaseUid`, `MemberID`, and required backfilled `NormalizedEmail`; make `PasswordHash` nullable in the first migration; phase out remaining credential fields later. | Unique filtered index on `FirebaseUid`; unique filtered index on `MemberID`; staged unique filtered index on `NormalizedEmail`; FK `Users.MemberID -> Members.MemberID` with restrict delete; trusted CHECK requires `GymGoer` to have a Member and every back-office role to have no Member. |
| `AccountInvites` | New one-time activation record with concrete nullable `TargetMemberID` and `TargetUserID`, token hash, normalized email, intended role, purpose, creator, expiry, redemption operation ID/UID, and used/revoked timestamps. | Check constraints require exactly one target and require redemption metadata to be either entirely null or entirely populated with a non-empty operation GUID; Member target requires `GymGoer`; User target requires its pre-provisioned SQL role; unique token hash and redemption operation ID; replacement transaction revokes every prior unused/unrevoked invite because a filtered index cannot depend on current time. |
| `AttendanceLogs` | Keep UTC `CheckInTime`, nullable UTC `CheckOutTime`, and convert gym-local `AttendanceDate` to .NET `DateOnly`/SQL `date` through a staged backfill; add source, actor IDs, `IsVoided`, void actor/time/reason, optional `SupersededByAttendanceID`, and row version. Duration remains derived. | Unique active `(MemberID, AttendanceDate)` filtered by `IsVoided = 0`; unique open-session index filtered by `IsVoided = 0 AND CheckOutTime IS NULL`; constraint permits checkout-before-check-in historical rows only when voided; indexes on `AttendanceDate`, `CheckInTime`, and `(MemberID, CheckInTime)`. Use restrict delete. |
| `AttendanceAdjustments` | Immutable record for checkout corrections/void/supersede actions: AttendanceID, adjustment type, before/after values, reason, actor, and UTC timestamp. | Append-only; restrict FKs; original anomaly/correction evidence is never deleted or overwritten without a corresponding adjustment. |
| `AttendanceOperations` | Idempotency ledger: GUID operation ID, actor UserID, operation type, canonical request fingerprint, target AttendanceID, original HTTP/result code, state, and created/completed UTC timestamps. | Operation ID is globally unique; mutation and completed ledger state commit in one transaction; conflicting fingerprint returns 409; retain at least 30 days. |
| `SystemSettings` | Keep `Timezone = Asia/Manila`; add `StaleSessionHours = 16` as a validated review-only threshold. | API validates timezone/threshold and fails startup/health checks on invalid values rather than silently using device local time; normal settings cannot change timezone after attendance exists. |
| Badge tables | No change in v1. | Badges are derived; no duplicate or award migration needed. |

`PasswordHash` must become nullable in the first migration because new invite-based Firebase Users have no local password. Do not drop it, `VerificationToken`, `ResetToken`, or `ResetTokenExpires` in that migration. Existing hashes remain for compatibility/forensics, new code stops authenticating against them, and a later cleanup migration removes obsolete credential fields only after Firebase rollout and rollback compatibility are proven.

When a Member invite creates a Gym Goer User, required legacy fields are populated deterministically: `Username = "member-{MemberID}"`, email comes from the verified Firebase token after the same normalization used by the invite, first/last names come from the linked Member, role is `GymGoer`, `PasswordHash = null`, and active/verified state comes from the approved onboarding rules. The client supplies none of those authority fields.

Email normalization is one shared server function used for invite creation, Firebase activation, uniqueness, and tests: trim outer whitespace, apply Unicode Form KC, uppercase with invariant culture, reject embedded control/whitespace characters, and enforce the 255-character User limit. The Firebase verified email is the login/notification address. After UID binding, a newly verified Firebase email may update `User.Email` only if normalized uniqueness checks pass; it never changes role or MemberID. `Member.Email` remains the gym's contact record until changed through a separate authorized profile workflow. A conflict blocks the email update and requires owner resolution.

### 6.2 Local schema

Modify the existing `LocalDatabaseService`; do not create a second database service.

| Local table/model | Purpose | Plaintext fields |
|---|---|---|
| `LocalCacheEnvelope` | Encrypted snapshots for `goer-dashboard`, `goer-progress`, `attendance-history`, and `digital-card` inside a separate per-account cache database. | Local ID, hashed account scope, cache key, envelope/schema version, DEK version, server data version, authenticated fetched/expiry metadata, nonce, tag, ciphertext. |
| `LocalCacheState` | Records latest successful refresh and last error category without sensitive payloads. | Hashed account scope, cache key, last success UTC, stale flag. |
| Existing `SyncQueue` | Remains for the broader offline-first roadmap. | It must not queue Gym Goer check-in or checkout in this feature. |

Use a versioned local migration runner. `CreateTableAsync` alone is not adequate once shipped schemas evolve. Migrations must be additive when possible, run transactionally, and clear only the affected cache when an incompatible envelope schema is encountered.

## 7. API contract plan

All routes remain under `/api/v1`. Responses keep the existing `ApiResponse` wrapper but add a stable machine-readable error code so mobile behavior does not depend on English text.

### 7.1 Authentication and session

| Method and route | Policy | Purpose |
|---|---|---|
| `POST /auth/activate` | `FirebaseOnboarding` | Redeem `{ inviteCode, operationId }`, bind the verified Firebase UID to the invite target transactionally, and return the profile. Same UID/target replay returns the original success; another UID or fingerprint conflicts generically. |
| `POST /auth/sync-user` | `FirebaseOnboarding` during compatibility only | If the UID is already linked, return its current profile. If unlinked, return `ACCOUNT_PENDING_ACTIVATION`. Never create a User, choose a role, bind by email, or redeem an invite. Remove after supported clients use `/auth/activate` and `/me`. |
| `GET /me` | `ActiveAppUser` | Return current SQL user ID, role, member link/onboarding state, display name, and API capability flags. |

`UserResponseDto.Token` is obsolete because the API does not mint a second application token in this plan. The mobile app sends a refreshed Firebase ID token. The sync response must include `UserID`, role, `MemberID` when linked, `IsActive`, onboarding state, and capabilities.

### 7.2 Invite management and activation

| Method and route | Policy | Purpose |
|---|---|---|
| `POST /members/{memberId}/app-invite` | `BackOffice` | Create/replace a Gym Goer invite for an active, non-deleted, unlinked Member with a valid email; return the plaintext code once. |
| `GET /members/{memberId}/app-invite/status` | `BackOffice` | Return unused/used/revoked/expired status and timestamps without token/hash. |
| `DELETE /members/{memberId}/app-invite` | `BackOffice` | Revoke the Member's current unused invite. |
| `POST /users/{userId}/app-invite` | `OwnerOnly` | Create/replace an invite for a pre-provisioned owner/receptionist User whose role is already fixed in SQL. |
| `GET /users/{userId}/app-invite/status` | `OwnerOnly` | Return staff-invite status without token/hash. |
| `DELETE /users/{userId}/app-invite` | `OwnerOnly` | Revoke the staff User's current unused invite. |

The Member Details back-office UI must expose status plus generate/replace (“resend”), copy-once, and revoke actions without ever retrieving the code again after its one-time response. “Active invite” means unused and unrevoked; expiry is checked in service logic, and replacement first revokes any unused/unrevoked row because SQL filtered indexes cannot depend on current time. Staff-invite UI may remain an owner-only operational/bootstrap tool until the formal User Management module is implemented.

Member soft deletion is one serializable identity operation. It acquires locks in the documented `AccountInvites` target range -> `Members` row -> linked `Users` range order, revokes every unused/unrevoked member invite (including an already expired unresolved row), deactivates linked Gym Goer SQL identities, and writes the audit record in the same transaction. A failure rolls back all four effects; back-office identities are never deactivated through this path.

Public client codes are deliberately coarse: `INVITE_INVALID`, `ACCOUNT_PENDING_ACTIVATION`, `IDENTITY_CONFLICT`, and `ACTIVATION_OPERATION_CONFLICT`. Detailed internal audit categories distinguish expired, revoked, used, email-normalization mismatch, ineligible target, rate limit, and another-UID replay without revealing whether an arbitrary email, UID, Member, or User exists.

### 7.3 Gym Goer self-service

| Method and route | Purpose | Rules |
|---|---|---|
| `GET /me/dashboard` | One cacheable snapshot for membership, current attendance state, current-month minutes, visit count, streaks, badges, timezone, and generated time. | Server derives MemberID; bounded response; no client role/member fields. |
| `GET /me/digital-card` | Member ID/card data, current membership status/expiry, and QR value. | Return only the caller's linked member. |
| `GET /me/attendance/current` | Current open session only. | Return 200 with the open server session or 200 with a documented `CheckedOut` state and null session; never ambiguously substitute the most recent closed visit. |
| `GET /me/attendance?from=&to=&page=&pageSize=` | Paginated attendance history. | Default 30 days; maximum 366-day range; maximum page size 100. |
| `POST /me/attendance/checkout` | Checkout the caller's current open session. | Body contains a GUID `operationId`; server finds the open row; server UTC time; return updated session. |
| `GET /me/progress?month=YYYY-MM` | Monthly duration and visits plus all-time current/longest streak and badge eligibility. | Month interpreted in gym timezone; reject malformed values; return `BadgeRuleVersion`; no points. |

There is intentionally no `POST /me/attendance/checkin` in v1. A remotely callable self-check-in without rotating facility proof or trusted proximity controls would make attendance easy to falsify.

### 7.4 Staff attendance and recovery

Introduce structured v2-style routes while retaining the current raw routes as deprecated adapters for one compatibility release. Both route sets call the same constrained service; the mobile app migrates to the structured routes before the adapters are removed.

| Method and route | Policy | Change |
|---|---|---|
| `POST /attendance/check-in` | `BackOffice` | New structured staff request containing QR code and operation ID; database constraint handling, gym-local date, and structured errors. |
| `POST /attendance/{id}/check-out` | `BackOffice` | New structured request containing operation ID; return updated DTO; use server time; support safe retry. |
| `POST /attendance/{id}/correct-checkout` | `OwnerOnly` | Body contains operation ID, corrected UTC time, and required reason; validate time/non-overlap; append immutable adjustment; audit old/new values and actor. |
| `POST /attendance/{id}/void` | `OwnerOnly` | Body contains operation ID, required reason, and optional canonical/superseding attendance ID; preserve the row, append adjustment, and exclude it from active rules/analytics. |
| `GET /attendance/member/{memberId}` | `BackOffice` | Remain staff-only; Gym Goers use `/me/attendance`. |

Deprecated compatibility adapters:

- `POST /attendance/checkin` accepts the existing raw QR JSON string.
- `POST /attendance/{id}/checkout` accepts the existing empty body.

Adapters remain protected by database constraints but cannot promise client-level idempotent replay without an operation ID. They emit deprecation telemetry/headers, are never used by the new Gym Goer flow, and are removed only after the supported mobile version has migrated.

Stable error codes include `ACTIVE_SESSION_EXISTS`, `DAILY_VISIT_LIMIT`, `MEMBERSHIP_INACTIVE`, `MEMBERSHIP_PAUSED`, `ATTENDANCE_NOT_FOUND`, `ALREADY_CHECKED_OUT`, `INVALID_CHECKOUT_TIME`, and `ATTENDANCE_REQUIRES_ONLINE`.

### 7.5 Owner attendance analytics

| Method and route | Policy | Purpose |
|---|---|---|
| `GET /reports/attendance/summary?from=&to=&bucket=day` | `OwnerOnly` | Return zero-filled, server-aggregated attendance counts for the owner graph. |

The v1 graph contract is frozen to normalized local bucket date/label and accepted-visit count, plus range total, calendar-bucket average, requested range, gym timezone, and generated UTC time. Average divides the total by every calendar bucket, including zero days. SQL returns grouped non-voided counts and the bounded service layer zero-fills missing dates. Completed-session/minute series are out of scope until their daily allocation and UI are separately approved. Default range is the last 7 gym-local days; presets are 7, 30, and 90 days; hard maximum is 366 days.

Keep `DashboardMetricsDto.CheckInsByHour`, correct it to gym-local hour buckets, and render it only if the product wants a “today by hour” secondary graph. Do not silently use UTC hours or leave the field unused.

## 8. Server-side calculation rules

### 8.1 Time handling

- Inject a clock abstraction for deterministic tests.
- Read the gym timezone from validated settings through one timezone service.
- Once any attendance exists, normal settings APIs cannot change the gym timezone. A timezone change requires a dedicated historical-date migration/reconciliation plan.
- Store event instants as UTC.
- Compute and persist `AttendanceDate` from server UTC converted to the gym timezone at check-in.
- Use half-open API ranges (`from` inclusive, `to` exclusive) internally to avoid end-of-day and subsecond errors.
- Do not group dates after materializing an unbounded table into memory. Aggregate and filter in SQL where practical.

### 8.2 Duration

- Duration is derived as `CheckOutTime - CheckInTime`; do not store a second mutable duration column in v1.
- Reject checkout before check-in.
- Exclude open sessions from completed minutes.
- Period totals query non-voided completed sessions satisfying `CheckInTime < periodEndUtc AND CheckOutTime > periodStartUtc`, then calculate `overlapStart = max(CheckInTime, periodStartUtc)`, `overlapEnd = min(CheckOutTime, periodEndUtc)`, and integer `seconds = max(0, overlapEnd - overlapStart)`. Return `long` seconds and round only for display.
- A non-voided accepted check-in counts as a visit for visit totals, streaks, and badge eligibility even while its checkout remains open. Only completed time waits for checkout.
- Every progress, badge, report, occupancy, and graph query explicitly excludes voided attendance rows.
- Flag implausibly long open sessions for owner correction; do not auto-close them.
- Default `StaleSessionHours` to 16 as a review threshold only. `MembersCheckedInCount` means all non-voided open sessions and is labeled “Open sessions”; `StaleOpenSessionCount` is the subset older than the threshold; a separate gym-local “Visits today” count uses `AttendanceDate`. No threshold mutates attendance.
- A corrected checkout must not be in the future, before check-in, or overlap the member's next non-voided session. It appends an immutable adjustment and immediately affects duration/report aggregates. A void/supersede adjustment can affect visits, streaks, badges, and graphs.

### 8.3 Concurrency and idempotency

- Database uniqueness, not an in-memory pre-check, is the final duplicate guard.
- Retain friendly pre-checks for user experience, then catch unique-constraint failures and return the same 409 error.
- Checkout/correction updates use a transaction and row-version or affected-row check.
- Every structured attendance mutation body contains one GUID `operationId`; no `Idempotency-Key` header is used. The client persists the same pending GUID across timeout, app restart, and safe retry until a terminal response.
- The API stores a canonical request fingerprint in `AttendanceOperations`. Same operation ID, actor, operation type, and fingerprint returns the original HTTP/result payload; any differing fingerprint returns 409 `OPERATION_ID_REUSED`. The operation ledger and mutation commit in one transaction.
- Fingerprints hash a canonical representation of non-secret effective inputs and target identifiers. Raw QR values, invite codes, tokens, or reasons containing sensitive text are never stored in the ledger.
- Audit success, failure category, correction, and actor without logging bearer tokens or the full QR value.

## 9. Mobile architecture and user experience

### 9.1 Session lifecycle

Replace the current split Firebase/API token handling with one `IAppSessionService` or equivalent coordinator.

Required behavior:

1. Registration creates the Firebase account, sends a verification email, and retains only the session material needed to finish verification/onboarding.
2. The client requires a refreshed Firebase token showing verified email before redeeming an invite.
3. The frozen candidate is an upgrade from unofficial `FirebaseAuthentication.net` 3.7.2 to stable 4.1.0 with a custom versioned `SecureStorage` credential repository. WP0 must prove .NET 10 Android/Windows/iOS/Mac Catalyst behavior, verification email, newest-refresh-token persistence, emulator/test-project operation, and serialization compatibility before acceptance. If 4.1.0 fails, implementation stops for an ADR amendment rather than silently keeping the 2021 package or inventing a partial REST client mid-workstream.
4. App-owned keys use the namespace `GymTrackPro:{environment}:{firebaseProjectHash}:{accountHash}:{purpose}:v{schemaVersion}`; the account hash is derived from immutable UID and no email appears in a key. Store only UID/account selector, newest refresh credential, credential schema, last validated/trusted times, and cache-DEK metadata. Short-lived ID tokens remain in memory when practical. A five-minute expiry skew triggers proactive refresh and concurrent refresh calls are coalesced. Account switch fully tears down the prior namespace before activating another.
5. An authenticated HTTP handler obtains a current ID token before API calls. After 401 it may refresh and retry exactly once only for GET/HEAD/OPTIONS or a POST whose frozen contract contains a server-enforced operation ID (invite redemption, structured check-in, structured checkout). It buffers/clones the request safely and never blindly replays another mutation.
6. Refresh failures are classified. Terminal failures such as invalid refresh credential, revoked session, deleted/disabled Firebase user, or confirmed SQL deactivation clear session and cache. Timeout, DNS, offline, and transient service failures preserve the local session/cache and enter offline read-only mode.
7. A second 401 after refresh is terminal for remote access and cannot loop.
8. On cold start, an awaited bootstrap page initializes SQLite, restores the Firebase session, calls `GET /me` when online, and selects the permitted shell.
9. Offline cached access is allowed for at most 72 hours after the last successful server validation, and never beyond a known membership expiry. Store `lastServerValidatedAtUtc`, `offlineValidUntilUtc`, and last observed trusted server time in namespaced secure state. If device time moves backward before the trusted time or the window expires, require online validation before showing account content.
10. Cached membership/role state is labeled “as of” and can never authorize a write or prove current facility eligibility; the staff/server check-in path remains authoritative.
11. Logout is centralized and clears only the app-owned Firebase/session keys, API headers, in-memory user context, per-account DEK, and per-account cache files.
12. Maintain an explicit owned-key/file manifest; do not call broad `SecureStorage.RemoveAll` or delete unrelated app data.

Do not keep the current fire-and-forget startup `Task.Run`, mismatched `firebase_token`/`auth_token` keys, or separate logout implementations.

### 9.2 Registration and onboarding UX

The current form collects data that is discarded. Replace it with an honest flow:

- Invite code
- Invited email
- Password and confirmation
- Password policy hint matching the Firebase project policy: at least 8 characters, upper, lower, number, and special character
- Verification-sent state with resend and “I have verified” refresh action
- Activation result: linked Gym Goer, linked staff, pending gym activation, expired invite, or conflict requiring staff help

Member name/profile data comes from the pre-existing SQL Member record. Staff profile data comes from the pre-provisioned SQL User. The client does not submit a role, MemberID, UserID, or owner flag.

Firebase password reset remains an email-link flow. Remove or retire the current manual `ResetPasswordPage` unless full Firebase action-code deep-link handling is separately planned and tested.

### 9.3 Root navigation and shells

Do not assign `Application.Current.MainPage` directly from a view model. Use an injected root-navigation/session coordinator on the UI thread.

- internal `Administrator` (displayed as Gym Owner) -> existing back-office `AppShell`, with owner features visible.
- `Receptionist` -> existing back-office `AppShell`, with owner-only tabs/actions hidden and API policies still enforced.
- `GymGoer` -> new `GoerAppShell`.
- Pending/unlinked/disabled -> onboarding or access-denied page, never a business shell.

The shell is a usability layer, not security. Deep links and manually entered routes must be guarded and the server remains authoritative.

### 9.4 Gym Goer shell

| Tab | Page responsibilities | Offline behavior |
|---|---|---|
| My Dashboard | Greeting; membership status/expiry; current checked-in state; current-month completed time; visit count; current streak; derived unlocked/locked badges; last updated time. | Show encrypted cached snapshot and a clear stale/offline banner. |
| QR Check-in | Digital membership card and QR, membership validity, current attendance state, and online-only checkout action. Explain that staff scans the code. | Show encrypted cached card; disable checkout and show “internet required.” |
| My Progress | Selected month, completed hours/minutes, visits, current/longest streak, daily activity summary, paginated history. | Show cached snapshot/history range and last updated time. |
| Settings | Read-only account/member summary, cache status, refresh, support text, privacy/cache explanation, and logout. | Logout and local cache removal must still work. |

Required states on every page: first load, refreshing, content, no data, offline/stale, unlinked, inactive membership, API forbidden, token-expired/re-authentication, and unexpected error with correlation ID.

Accessibility requirements:

- Do not communicate attendance/badge state by color alone.
- Provide semantic labels for QR/card state, badges, chart points, and buttons.
- Touch targets are at least platform guidance size.
- Dynamic type does not clip totals, badges, or navigation.
- The owner chart has a textual summary/list alternative.

### 9.5 Owner attendance graph

Add an `AttendanceTrend` collection and selected 7/30/90-day preset to the owner dashboard view model. The dashboard calls the bounded summary endpoint and displays:

- graph title and exact local date range;
- one point/bar per local day, including zero days;
- total visits and average visits per day;
- last generated/refreshed time;
- empty, loading, retry, and forbidden states; and
- an accessible textual point list.

Prefer a native MAUI `GraphicsView` or a simple bindable XAML bar visualization. Do not add a chart package until a short compatibility/license/AOT review proves it works on all targeted .NET 10 platforms. The graph must not block the dashboard if rendering fails.

## 10. Concrete file and component map

The names below are proposed implementation targets. An implementer may refine names while preserving ownership and contracts.

### 10.1 Shared project

Modify:

- `Enums/UserRole.cs`
- `Entities/User.cs`
- `Entities/Attendance.cs`
- `DTOs/UserResponseDto.cs`
- `DTOs/AttendanceDto.cs`
- `DTOs/DashboardMetricsDto.cs`
- `DTOs/ReportDtos.cs`
- attendance, authentication, dashboard, report, and repository interfaces affected by the new contracts

Add:

- `Entities/AccountInvite.cs`
- `DTOs/CurrentUserDto.cs`
- `DTOs/GymGoerDashboardDto.cs`
- `DTOs/GymGoerProgressDto.cs`
- `DTOs/AttendanceTrendDto.cs`
- request DTOs for invite redemption, idempotent attendance writes, and checkout correction
- stable error-code support in the API response contract

Do not expose EF entities directly through new mobile API methods.

### 10.2 API project

Modify:

- `Program.cs`
- `Controllers/AuthController.cs`
- `Controllers/AttendanceController.cs`
- `Controllers/DashboardController.cs`
- `Controllers/ReportsController.cs`
- every existing business controller's authorization attributes/policies
- `Services/AuthenticationService.cs`
- `Services/AttendanceService.cs`
- `Services/DashboardService.cs`
- `Services/ReportsService.cs`
- `Repositories/UserRepository.cs`
- `Repositories/AttendanceRepository.cs`
- `Data/GymDbContext.cs`
- `Middleware/ExceptionHandlingMiddleware.cs`
- `appsettings.json` and environment configuration binding

Add:

- authentication/authorization constants, requirements, handlers, and current-user context
- identity/profile `Controllers/MeController.cs`
- `Controllers/MeDashboardController.cs`, `MeAttendanceController.cs`, and `MeProgressController.cs` sharing the `/api/v1/me/...` route prefix without file ownership overlap
- Gym Goer projection/progress service
- invite repository/service or focused methods in existing user services
- centralized gym timezone service and clock abstraction
- compatibility-preserving EF migration, reviewed SQL script, preflight anomaly queries, and rollback notes
- one-time `tools/GymTrackPro.Bootstrap` owner-binding command; it is never hosted as an HTTP endpoint

### 10.3 Mobile project

Modify:

- `MauiProgram.cs`
- `App.xaml.cs`
- `AppShell.xaml` and code-behind
- `Services/IFirebaseAuthService.cs`
- `Services/FirebaseAuthService.cs`
- `Services/IApiService.cs`
- `Services/ApiService.cs`
- `Services/ILocalDatabaseService.cs`
- `Services/LocalDatabaseService.cs`
- `ViewModels/LoginViewModel.cs`
- `ViewModels/RegisterViewModel.cs`
- `ViewModels/DashboardViewModel.cs`
- `ViewModels/MemberDetailsViewModel.cs` and `Views/MemberDetailsPage.xaml` for Member app-invite generation/revocation
- existing logout entry points
- Android manifest/backup/cleartext configuration and corresponding iOS backup/privacy configuration
- environment-specific API/Firebase configuration

Add:

- one session coordinator and authenticated HTTP handler
- one root-navigation/bootstrap service and loading page
- encrypted cache service, encryption service, cache envelope models, and local migration runner
- `GoerAppShell.xaml`
- Gym Goer dashboard, QR/digital-card, progress, and settings views/view models
- owner attendance graph control/drawable and accessible fallback

Remove or retire after migration:

- fake/manual reset UI that is incompatible with the chosen Firebase email-link flow
- unused profile inputs or Google-login method until their complete flows are explicitly implemented
- duplicate token storage and logout code paths

### 10.4 Tests and documentation

Add test projects outside the three runtime projects, then include them in `GymTrackPro.slnx`:

- `tests/GymTrackPro.API.Tests`
- `tests/GymTrackPro.Mobile.Tests`

Update or replace the obsolete `scratch` authentication and attendance scripts. They must target a local/test database and Firebase emulator/test project only, never production MonsterASP.

Documentation to update in the same change set:

- `README.md`
- `ARCHITECTURE.md`
- `docs/01_Agent_Guidelines.md`
- `docs/02_Development_Roadmap.md`
- `docs/03_Architecture.md`
- `docs/04_Database.md`
- `docs/05_Decisions.md`
- `docs/06_Changelog.md`
- `docs/07_Project_Blueprint.md`
- `docs/09_Definition_of_Done.md`
- `docs/11_Authentication.md`
- `docs/13_AttendanceManagement.md`
- `docs/17_Dashboard.md`
- `docs/18_Reporting.md`
- `docs/21_SystemArchitecture.md`
- the formal project specification/version history if it remains the governing document

## 11. Work packages and implementation order

No Gym Goer page may be enabled before WP0-WP3 pass their gates.

### WP0 - Containment, decisions, and compatibility spikes

Dependencies: none.

Tasks:

- Rotate the exposed MonsterASP SQL password immediately; verify the old credential fails.
- Put `ConnectionStrings__DefaultConnection` in MonsterASP environment configuration and replace tracked values with non-secret placeholders.
- Scan Git history and deployment artifacts for the old credential; record remediation without placing the secret in logs.
- Configure exact Firebase Project ID/issuer/audience per environment and restrict the Firebase client key to Firebase APIs/app identifiers.
- Ship a minimal security hotfix before feature/schema work: exact Firebase validation, `MapInboundClaims = false`, verified-email requirement, no unknown-user creation, no automatic Receptionist assignment, `IsActive` enforcement for existing SQL users, and generic production errors. Transitional lookup may recognize only an independently reviewed pre-existing SQL user by verified normalized email; it cannot create or permanently link an identity.
- Build, smoke-test, and tag that hotfix plus legacy attendance adapters as the exact rollback artifact `firebase-auth-hardened-compat-v1`. The insecure current `c948b9e` binary is never an approved rollback target.
- Remove obsolete custom-JWT secret configuration if no longer used.
- Require HTTPS release API URLs; remove broad Android cleartext allowance for release.
- Fail Release build/startup when the API URL is missing, HTTP, localhost/loopback, or redirects authentication across a host boundary. Verify the merged Android Release manifest disables cleartext, iOS/Mac Catalyst contain no production ATS exception, and bearer headers are never forwarded on cross-host redirects.
- Return generic production 500 responses with a correlation ID; do not expose raw exception messages.
- Configure trusted forwarded headers/networks for the actual MonsterASP proxy before HTTPS decisions, authentication, client-IP logging, and rate limiting. Validate `AllowedHosts`; make the intended global limiter global; partition invite/onboarding limits by verified UID plus correctly resolved client IP.
- Record the actual MonsterASP tier, database quota/used size, SQL version, app memory limit, backup/restore capability, and provider-approved internal/public connection-string/TLS behavior.
- Write ADRs for Firebase/SQL identity boundaries, the “Administrator displayed as Gym Owner” compatibility choice, single-gym scope, invite linking, online-only attendance writes, timezone semantics, and encrypted cache.
- Spike the Firebase refresh lifecycle on Android/Windows and the cache/SQLite stack on Android 16 16 KB page-size environments. Also build/smoke iOS and Mac Catalyst.
- Record the seven baseline warnings and classify each as fixed, accepted with evidence, or release-blocking.

Exit gate:

- No live server credential remains in tracked configuration.
- Wrong-project Firebase tokens are rejected.
- Unknown Firebase users can no longer be auto-created or assigned Receptionist by the deployed compatibility API.
- `firebase-auth-hardened-compat-v1` is reproducible, retained, and passes legacy attendance smoke tests against the compatibility-preserving schema.
- Release configuration refuses HTTP API base URLs.
- Architecture decisions are approved.
- The chosen Firebase session and SQLite versions pass the target-platform spike.

### WP1 - Identity schema, invite provisioning, and migration safety

Dependencies: WP0 decisions.

Tasks:

- Make enum values explicit while preserving wire/storage compatibility: `Administrator = 0`, `Receptionist = 1`, `GymGoer = 2`; add a UI display-name mapping from Administrator to “Gym Owner.”
- Add nullable `FirebaseUid` and `MemberID` to User with filtered uniqueness and restrict FK.
- Add concrete `AccountInvite` target/redemption fields and transactional single-use constraints.
- Make `PasswordHash` nullable but retain all legacy credential columns for the first compatibility release.
- Inventory all current Users. Treat Firebase-created receptionists as untrusted until the owner verifies them; do not automatically preserve elevated access merely because email matches.
- Create/test the one-time initial-owner bootstrap command and controlled staff/member invite procedures. Do not deploy UID-only policies until the owner UID is bound and independently verified during maintenance.
- Add BackOffice Member-invite endpoints and owner-only staff-invite endpoints; expose one-time Member invite generation/revocation in the existing Member Details flow.
- Generate and review the EF migration and idempotent production SQL script.

Exit gate:

- Migration succeeds on an empty database and a sanitized production-like backup.
- Existing role numeric values are preserved.
- A new invited Gym Goer can be inserted with `PasswordHash = null` and deterministic required User fields.
- Invite replay/concurrency creates at most one link.
- The initial owner is UID-bound, active, and can pass a local/hardened-policy smoke test before the new API replaces the compatibility build.
- Ambiguous or previously auto-created privileged accounts remain disabled/pending review.

### WP2 - Server authentication, current-user context, and authorization lockdown

Dependencies: WP1 schema.

Tasks:

- Preserve the exact Firebase validation shipped in WP0; add UID-backed provisioning and database authorization without relaxing it.
- Implement onboarding and active-app-user policies.
- Resolve SQL User by Firebase UID and load role, active state, and optional MemberID into a typed current-user context.
- Redeem invites transactionally; never bind a privileged record by email alone.
- Replace every integer parse of the Firebase name identifier.
- Apply the complete policy matrix to every existing controller and sensitive action.
- Add object-level authorization so resource IDs cannot cross the caller's allowed scope.
- Replace unsafe production exception detail with stable errors and correlation IDs.

Exit gate:

- Anonymous/malformed/invalid tokens return 401; valid unprovisioned/inactive/unverified/role-denied callers return 403; hidden foreign resources return 404; state/fingerprint conflicts return 409.
- Endpoint-metadata tests find no bare `[Authorize]` or accidental anonymous business action.
- A Gym Goer cannot reach any member list, finance, global attendance, report, or settings endpoint.
- Audit records contain the correct internal SQL UserID.

### WP3 - Attendance integrity, timezone, and recovery

Dependencies: WP2 current-user context; migration preflight.

Tasks:

- Stage candidate gym-local SQL `date` values from UTC check-ins, then audit candidate same-day duplicates, multiple open sessions, checkout-before-check-in values, and implausibly stale sessions.
- Resolve anomalies by void/supersede plus immutable adjustment records; never silently delete history.
- Add AttendanceOperations, actor/source/void/supersede metadata, row version, filtered constraints, and indexes.
- Make the gym timezone service authoritative for local visit dates and report periods.
- Refactor staff check-in/checkout to transaction-safe, idempotent behavior with structured request DTOs and errors.
- Retain the old raw attendance routes as constrained compatibility adapters for one release; move the current mobile staff screen to the structured routes before removal.
- Add owner-only checkout correction with mandatory reason and audit.
- Reject correction times in the future, before check-in, or overlapping the next non-voided session; give adjustments their own operation ID.
- Correct the current dashboard label/count semantics and surface a stale-open-session count.
- Correct inclusive end-date report behavior by using half-open ranges.

Exit gate:

- Concurrent check-in tests produce exactly one row.
- Checkout cannot precede check-in and safe retries do not double-mutate.
- Manila midnight/day/month boundary tests pass.
- Voided rows remain preserved but are excluded from constraints, occupancy, progress, badges, reports, and graphs.
- No stale open session is silently counted as a completed duration.

### WP4 - Gym Goer API projections, progress, and badges

Dependencies: WP2 and WP3.

Tasks:

- Implement self-scoped profile, dashboard, digital-card, current-attendance, history, checkout, and progress endpoints.
- Calculate period overlap durations, visit days, current/longest streaks, and v1 badge eligibility server-side.
- Paginate history and bound all date ranges.
- Include generated time, timezone, schema/version, and cache metadata in projections.
- Ensure QR and other identifiers never appear in logs.

Exit gate:

- Changing route/query IDs cannot expose another member because self endpoints accept no MemberID.
- Progress examples and boundary tests return deterministic results.
- A cached snapshot can be safely versioned and refreshed without client-side recalculation.

### WP5 - Mobile session, role routing, and encrypted cache

Dependencies: frozen WP2/WP4 contracts. UI may start earlier against fakes after contract freeze.

Tasks:

- Implement refresh-aware Firebase session coordination and authenticated HTTP calls.
- Implement verification and invite onboarding.
- Replace startup race and centralize logout/account switch.
- Implement versioned SQLite cache envelopes and AES-GCM encryption with a `SecureStorage` key.
- Add cache-first/read-through behavior, 72-hour maximum offline validation, stale indicators, connectivity refresh, per-account DEK/file isolation, rollback detection, and corrupt-cache recovery.
- Add root navigation for owner/receptionist/goer/pending states.
- Harden release transport and backup configuration.

Exit gate:

- Session remains usable across ID-token expiry through a valid refresh.
- Terminal refresh failure logs out once and clears app-owned account data; transient network failure preserves read-only cache within the offline validation window.
- Known plaintext is absent from DB/WAL/SHM, and tag, AAD, metadata, account/key swap, nonce-reuse, rollback, and rotation tests pass.
- Account B cannot read account A's cache.
- Offline attendance writes remain disabled.

### WP6 - Gym Goer pages

Dependencies: WP4 and WP5.

Tasks:

- Build `GoerAppShell` and four requested tabs.
- Reuse/extract the existing digital membership QR presentation where safe.
- Add all loading, empty, offline, stale, inactive, unlinked, error, and reauthentication states.
- Add accessible badge and progress presentation.
- Make online checkout clear, confirmable, idempotent, and refresh the current state/cache on success.

Exit gate:

- A Gym Goer can complete the defined online and offline UAT journeys without seeing a back-office screen.
- Membership, attendance, duration, streak, and badge values reconcile with API responses.
- Logout clears the cached dashboard/card/history.

### WP7 - Owner attendance graph

Dependencies: WP3 aggregation semantics; may run parallel with WP5/WP6.

Tasks:

- Add SQL-side, timezone-aware, zero-filled daily summary for 7/30/90-day presets.
- Bind the collection in owner `DashboardViewModel`.
- Render a native accessible graph and text fallback.
- Keep the owner dashboard responsive while graph data loads or fails.
- Either render the existing today-by-hour metric correctly or remove it from the public contract; do not leave dead fields.

Exit gate:

- Graph totals match raw attendance for the same half-open local range.
- Zero-visit days render as zero, not missing.
- Gym Goer and Receptionist receive 403 for the owner graph endpoint.

### WP8 - Test replacement, documentation, and release

Dependencies: WP0-WP7.

Tasks:

- Add automated API and mobile test projects.
- Replace destructive legacy auth setup with a test authentication handler and Firebase Emulator/test-project flows.
- Update governing docs and ADRs.
- Execute migration rehearsal, performance checks, security regression, platform UAT, and MonsterASP smoke tests.
- Roll out behind feature flags to a small pilot before broad enablement.

Exit gate: every Definition of Done item in section 15 is satisfied.

## 12. Verification plan

### 12.1 Unit tests

Identity and authorization:

- Exact role ordinal/name mapping.
- Invite token hashing, expiry, revocation, email/role/target validation, and single use.
- Invite entropy/format, purpose binding, Unicode/case normalization, generic error behavior, rate-limit partitioning, replacement revocation, and same-UID response-loss replay.
- Unknown Firebase identity receives no operational role.
- UID is immutable after binding.
- Active/inactive and linked/unlinked policy outcomes.
- Current-user context maps Firebase UID to the correct SQL UserID and MemberID.

Attendance and time:

- Check-in local date at Manila midnight boundaries, including 15:59:59 UTC and 16:00:00 UTC.
- Membership active, future, expired, paused, deleted, and boundary dates.
- Open session, daily visit limit, invalid QR, and invalid checkout ordering.
- Completed duration uses exact UTC instants and display rounding happens only at presentation.
- Cross-midnight and cross-month period overlap.
- Current streak ending today/yesterday, broken streak, duplicates, leap day, year boundary, and longest streak.
- All three badge rules, including correction-driven recomputation.
- Zero-filled daily trend buckets and half-open date ranges.

Mobile services/view models:

- ID-token refresh before expiry and coalescing of concurrent refresh requests.
- One safe retry after 401; no infinite retry.
- Session/bootstrap state transitions and role routing.
- AES-GCM encrypt/decrypt, wrong key, modified ciphertext/tag, missing SecureStorage key, and schema-version mismatch.
- Per-account DEK separation, nonce uniqueness/collision retry, canonical AAD, cross-account/cache-key envelope swap, freshness mutation, rollback, rotation, atomic/crash-interrupted write, and DB/WAL/SHM remnants.
- Cache-first load, online refresh, stale banner, offline fallback, account isolation, and logout purge.
- Checkout disabled offline and correct error-code-to-message mapping.

### 12.2 API integration tests

Use `WebApplicationFactory` or the .NET 10 equivalent with a disposable SQL Server database compatible with production behavior. Use two distinct authentication test layers:

- Real JwtBearer validator tests use an in-process OIDC metadata/JWKS server with generated RSA keys (or an equivalent direct validator harness) to exercise issuer, audience, algorithm, signature, lifetime, clock skew, and unmapped Firebase claims.
- A fake authenticated-user handler is used only for policy/resource matrix tests after token validation; it cannot claim to test JWT validation.

Authentication cases:

- Valid exact-project token.
- Wrong audience, wrong issuer, non-RS256 algorithm, invalid signature, expired token beyond two-minute skew, malformed token, missing subject, missing email, and unverified email.
- `MapInboundClaims = false` preserves explicit Firebase claims and token-supplied role/custom claims never grant SQL access.
- Unprovisioned identity can access onboarding only.
- Invite redemption is idempotent under concurrent requests.
- Same email with another UID cannot inherit an account.
- Disabled SQL user is denied despite a valid Firebase token.
- Public signup cannot become owner/receptionist.
- Invite brute-force/rate-limit behavior, revoke-versus-redeem races, generic-error enumeration, delivery token leakage controls, expired cleanup, same-UID replay after commit/response loss, different-UID replay, and operation-fingerprint conflict.

Authorization/resource cases:

- Full role/endpoint matrix from section 5.
- Gym Goer cannot alter a route/query/body ID to access another member.
- Gym Goer cannot access owner dashboard, member directory, payments, subscriptions, reports, or settings.
- Receptionist cannot access owner graph, correction, plan mutation, or system settings mutation.
- Back-office access remains functional for permitted roles.
- Audit rows contain the internal actor, action, outcome, and correlation ID without token/QR leakage.
- Spoofed untrusted forwarded headers do not change client identity/IP; trusted MonsterASP proxy headers do; invite/onboarding rate limits partition as designed; invalid Host is rejected.

Attendance/data cases:

- 20-50 simultaneous check-ins for one member result in one attendance row and deterministic conflict responses.
- Same operation ID replay returns the same result; same key with different payload conflicts.
- Concurrent checkout succeeds once and safe retries are deterministic.
- Database constraints still protect direct/repository-level concurrent writes.
- Owner correction requires role and reason and preserves an audit trail.
- Multiple corrections append multiple adjustments; future/overlapping correction and reused adjustment operation IDs are rejected.
- Progress, history pagination, range caps, and report end-day inclusion.
- Every affected query excludes voided rows and a void/supersede recomputes visit/streak/badge/graph outputs.
- Owner graph reconciles to raw attendance for the identical timezone/range.

### 12.3 Firebase integration tests

Use Firebase Authentication Emulator where the chosen client library supports it, plus a dedicated non-production Firebase project for the final platform smoke tests.

Cover:

- sign-up;
- verification email/action;
- login before/after verification;
- password reset email;
- ID-token refresh after one hour or simulated expiry;
- v1 revocation behavior: SQL `IsActive` denies every API request immediately; Firebase-only revocation/deletion is detected on token expiry/refresh and cannot bypass SQL disable;
- logout/cold start; and
- environment isolation between development, test, and production projects.

The emulator does not reproduce production anti-abuse/rate limits, so production smoke testing remains required.

### 12.4 Migration tests

- Generate and inspect an idempotent EF migration script.
- Apply to an empty database.
- Apply to a sanitized production-like backup with representative Users and AttendanceLogs.
- Run preflight anomaly reports before constraints.
- Verify role ordinals and all row counts before/after.
- Verify the script cannot elevate auto-created receptionists implicitly.
- Apply/rehearse retry behavior without duplicating invites or indexes.
- Verify the approved compatibility API can run against the migrated schema during rollback.
- Verify `firebase-auth-hardened-compat-v1`, not `c948b9e`, against the migrated schema and legacy attendance routes.
- Verify candidate local-date collision staging, void/supersede preservation, SQL `date` mapping, and timezone-change rejection after attendance exists.
- Generate a separate cleanup migration only after the compatibility window; do not combine credential-column removal with the initial feature migration.

### 12.5 Performance and capacity tests

- Seed at least 10,000 Members and 100,000 realistically distributed Attendance rows, including open, voided, adjusted, and boundary sessions, unless the actual production tier requires a higher agreed dataset.
- On the target/staging-equivalent configuration, require p95 server latency under 500 ms for 7/30/90-day visit graphs, under 1,000 ms for 366-day graph and monthly progress, and under 500 ms for a 100-row history page, measured after warm-up with 20 concurrent clients.
- API timeout is 10 seconds. Graph responses remain below 64 KB and paginated JSON responses below 256 KB.
- Require bounded/indexed SQL with no unbounded materialization; capture reviewed query plans for graph, current session, and progress.
- Graph failure/timeout cannot block the rest of the owner dashboard; it shows an independent retry state.
- The feature adds no more than 50 MB p95 process working-set above the measured baseline during the 20-client test.
- Database plus index growth after the representative seed must leave usage below the 70% warning threshold for the actual plan. Alert at 70%, critical at 85%, and block rollout/upgrade capacity by 90%.
- Maintenance rollout uses a write freeze, giving an RPO target of zero for the migration window; verify the selected backup can meet a 60-minute restore/recovery target or record/approve a different provider-tested RTO before deployment.

### 12.6 Manual platform and accessibility matrix

Required before release:

- Android and Windows: full sign-up, verify, invite, login, role routing, QR display/scan, check-in, checkout, progress, owner graph, offline cache, token refresh, and logout.
- iOS and Mac Catalyst: build plus smoke flow; full flow where devices/build signing are available.
- Android restore/reinstall and device account-switch behavior.
- iOS/Mac Catalyst cache-backup exclusion and Keychain reinstall behavior where signed devices are available; Windows backup/roaming behavior is tested or recorded as an explicitly accepted release risk.
- Airplane mode before launch, during refresh, and during checkout.
- Local midnight and month-boundary test with a controlled server clock/test environment.
- Dynamic type, screen reader labels, color contrast, keyboard/focus behavior on desktop, and chart text fallback.

### 12.7 Baseline and regression commands

Implementation verification must include, at minimum:

- restore and build the full solution;
- run all API and mobile unit/integration tests;
- generate/validate the migration script without applying it to production;
- scan tracked files for secrets and release HTTP URLs;
- publish the API in Release for the MonsterASP target;
- build Release mobile targets available in CI; and
- execute non-destructive production smoke tests after deployment.

No destructive `scratch` script may point at the MonsterASP production database.

## 13. Database migration, rollout, and rollback

### 13.1 Pre-deployment containment

1. Rotate the exposed SQL password and verify all deployed/local consumers use the new environment value.
2. Remove the credential from the tracked file and inspect Git history, publish profiles, logs, and shared archives.
3. Confirm the production Firebase project ID and application restrictions.
4. Inventory current SQL Users, especially records with empty password hashes and `Receptionist` created after Firebase integration.
5. Disable or quarantine accounts whose staff status cannot be independently verified.
6. Confirm MonsterASP database quota, automatic backup availability, manual restore procedure, SQL version, API runtime, and maintenance window.
7. Deploy and verify `firebase-auth-hardened-compat-v1` before schema migration; retain its package, configuration manifest, and checksums as the only approved rollback build.
8. Prepare a mandatory rehearsal target: either a second hosted SQL database or a sanitized local SQL Server restore. “No staging database” is not permission to test on production.

### 13.2 Server migration sequence

1. Rehearse the exact idempotent SQL script and backfill tool on the mandatory rehearsal database; review `__EFMigrationsHistory`, row counts, constraints, and rollback-build compatibility.
2. Enter a production write freeze/maintenance mode. Take a dated, restorable backup and record row counts/checksums.
3. Apply the reviewed idempotent SQL script exactly once and verify the expected `__EFMigrationsHistory` row before any application restart.
4. Preserve enum/wire storage: numeric 0 and JSON `Administrator` remain the internal owner role, numeric 1/`Receptionist` remain unchanged, and new `GymGoer` uses 2. Make `PasswordHash` nullable.
5. Add a nullable staging/local-date column mapped to SQL `date`. Use a reviewed .NET backfill tool with the validated `Asia/Manila` `TimeZoneInfo`; do not assume SQL Server accepts the IANA ID in `AT TIME ZONE`.
6. Detect candidate local-date collisions, multiple open sessions, checkout-before-check-in, deleted-member history, and duplicate identity links using the staged dates. Preserve old values.
7. Resolve anomalies through owner-approved void/supersede and immutable adjustment records. Never delete a conflicting row to make an index pass.
8. Make the final `AttendanceDate` authoritative SQL `date`, migrate staged values, then add filtered unique/check constraints and query indexes. Lock ordinary timezone changes.
9. Run the out-of-band initial-owner bootstrap while still in maintenance mode. Verify that the UID-bound owner passes the new policy suite locally against production configuration.
10. Deploy the UID-backed hardened API and invite endpoints with Gym Goer/graph/write-v2 flags off. Smoke-test the owner account before removing maintenance mode.
11. Run security, legacy-adapter, concurrency, migration-history, and reconciliation tests.
12. Issue controlled pilot invites, enable the Gym Goer API for those accounts, then deploy the mobile feature.
13. Enable the owner graph after range totals reconcile.
14. Observe the pilot for at least seven days with no unresolved P0/P1 incident before broad rollout.

### 13.3 Local database migration

1. Upgrade/validate the SQLite native dependency for supported Android page sizes before changing the shipped schema.
2. Inventory the existing common SQLite DB, `LocalMember`, and `SyncQueue.SerializedData`. If SyncQueue is nonempty, block automatic upgrade until its unsynced intent is reviewed; never silently delete or pretend-upload it.
3. After approved resolution, stop connections/workers and remove the obsolete plaintext DB plus WAL/SHM files. Recreate the common operational schema with the unsafe TODO/delete sync path disabled.
4. Initialize one versioned encrypted-envelope cache DB per account and generate its independent DEK under the app-owned SecureStorage namespace.
5. Do not convert legacy plaintext Member/queue payloads into the Gym Goer cache. Re-fetch minimal encrypted projections.
6. On incompatible schema or key loss, close and cryptographically discard only the owned per-account cache/DEK; never delete server data or session-independent user files.

### 13.4 Feature flags and pilot

Recommended server configuration flags:

- `Features__GymGoerExperience`
- `Features__OwnerAttendanceGraph`
- `Features__AttendanceWriteV2`

Pilot order:

1. Development/test Firebase and local SQL.
2. Mandatory second hosted test DB or sanitized local SQL Server restore.
3. Production owner plus a small set of explicitly invited members.
4. All verified members after at least one observation window with no P0/P1 incidents.

### 13.5 Rollback

- Disable feature flags first.
- Roll back only to the retained `firebase-auth-hardened-compat-v1` API/mobile-compatible artifact while retaining the compatibility-preserving migrated schema. Never deploy `c948b9e` or another build that disables exact validation, active-user enforcement, or the no-auto-provision rule.
- Never roll back exact issuer/audience validation, active-user enforcement, or the rotated secret.
- Do not run destructive down migrations against production merely to remove unused additive columns.
- If a migration caused confirmed data corruption, stop writes, preserve evidence, and restore the verified backup according to the runbook; reconcile any post-backup writes deliberately.
- Cleanup/drop legacy credential columns only in a later release after rollback compatibility is no longer required.

## 14. Operations, monitoring, and risk controls

### 14.1 MonsterASP deployment configuration

- Use `ConnectionStrings__DefaultConnection` in the MonsterASP control panel environment settings.
- Configure Firebase Project ID and feature flags as environment values.
- Use the HTTPS MonsterASP API URL in Release mobile configuration; localhost HTTP remains development-only.
- Configure forwarded headers only for known MonsterASP proxy addresses/networks and run that processing before HTTPS scheme decisions, authentication context, rate-limit IP partitioning, and audit IP capture. Reject untrusted forwarded values.
- Configure and smoke-test provider/edge throttling for unauthenticated and activation traffic. The bounded in-process limiter protects one API process but is not a distributed-DDoS control; production release remains blocked until the MonsterASP edge policy is verified against the trusted forwarded client address.
- Set a production `AllowedHosts` allowlist and make the final mobile base URI point directly to the HTTPS API host with no redirect dependency. Cross-host redirects are not followed with bearer headers.
- Apply production EF changes as the one reviewed idempotent SQL script during the write-freeze window; do not auto-migrate on API startup. Verify `__EFMigrationsHistory` immediately after application.
- Use a MonsterASP topology whose SQL certificate chain can be validated by the maintenance runtime. Every maintenance connection string must explicitly contain `Encrypt=True` (or `Encrypt=Mandatory`/`Strict`) and `TrustServerCertificate=False`; omissions, optional/false encryption, and certificate bypass are rejected. If the available endpoint cannot satisfy that policy, block rollout and coordinate a provider-approved trusted endpoint instead of bypassing certificate validation.
- Record actual quota and used size before rollout. Alert at 70%, declare critical at 85%, and block expansion/require a plan upgrade by 90%; do not wait for SQL writes to fail at quota.
- Restrict remote database access to migration/administration windows when operationally possible.
- Keep the mobile client entirely behind the API; never distribute database credentials.

### 14.2 Logs and metrics

Server logs/metrics:

- authentication failures by safe reason category;
- onboarding/invite conflicts and redemption rate;
- 401/403/409/429 rates by endpoint and role without user secrets;
- check-in/out latency and database constraint conflicts;
- stale open-session count and correction count;
- Gym Goer projection and owner graph latency/error rate;
- SQL retry/failure and database capacity; and
- correlation IDs for unexpected errors.

Mobile telemetry/logging, if enabled:

- session refresh success/failure category;
- cache hit/stale/corrupt/purge outcomes;
- API connectivity category; and
- page failure category and correlation ID.

Never log passwords, invite tokens, Firebase ID/refresh tokens, SQL credentials, full QR codes, encrypted cache keys, or raw sensitive response bodies.

### 14.3 Risk register

| Risk | Likelihood/impact | Mitigation | Release owner |
|---|---|---|---|
| Exposed SQL credential is abused | High/Critical until rotated | Immediate rotation, environment secrets, history/artifact scan, DB/audit review. | Project owner |
| Public signup creates operational access | Current/Critical | Disable current auto-receptionist provisioning; invite-only app activation; policy lockdown. | Identity workstream |
| Wrong Firebase project token accepted | Current/Critical | Exact issuer/audience/signature/lifetime validation and negative tests. | Identity workstream |
| Gym Goer accesses another member or finance data | High/Critical without policies | Self-derived MemberID, endpoint policy matrix, object-level tests. | Identity/API workstreams |
| Concurrent requests create duplicate visits | Medium/High | Unique database constraints, transactions, body operation IDs, concurrency tests. | Attendance workstream |
| Forgotten checkout corrupts occupancy/progress | High/High | Stale-session visibility and owner-only audited correction; exclude open duration. | Attendance workstream |
| UTC/local boundary gives wrong day/streak | High/Medium | One gym timezone service, persisted local visit date, boundary tests. | Attendance workstream |
| Cached personal data leaks across accounts/backups | Medium/High | AES-GCM envelopes, SecureStorage key, backup exclusions, account partition and purge. | Mobile workstream |
| Firebase token expires after one hour | Certain/High | Refresh-aware central session service and one-retry logic. | Mobile auth workstream |
| SQLite/camera native package fails Android release | Medium/High | Dependency upgrade/spike and Android 16 16 KB page-size validation. | Mobile workstream |
| Owner graph overloads limited host | Medium/Medium | SQL aggregation, indexes, range caps, no unbounded materialization, load test. | Analytics workstream |
| Docs/tests continue to describe removed custom auth | High/Medium | ADR/doc update and replacement test setup are release-gated. | QA/docs workstream |
| Scope expands into multi-tenant SaaS mid-feature | Medium/High | Single-gym ADR and separate multi-tenant epic/change control. | Project owner |

## 15. Definition of Done

The feature is complete only when every item below is true.

Security and identity:

- [ ] Exposed MonsterASP credentials are rotated and absent from tracked/release files.
- [ ] Firebase tokens validate exact project issuer/audience, signature, and lifetime.
- [ ] Unverified, unknown, disabled, and unlinked identities cannot reach business endpoints.
- [ ] Public Firebase signup without an authorized activation cannot create internal `Administrator` or `Receptionist` access.
- [ ] Firebase UID is unique/immutable and app role/member link come from SQL.
- [ ] Invite tokens are hashed, expiring, revocable, single-use, and audited.
- [ ] Invite same-UID response-loss replay is safe; another-UID/fingerprint replay is denied generically; create/status/replace/revoke controls are authorized.
- [ ] Every endpoint uses the approved policy and ownership rules.
- [ ] The initial owner is bound out of band before UID policies go live, and `firebase-auth-hardened-compat-v1` is the tested rollback target.
- [ ] Production errors expose correlation IDs, not internal exception details.

Attendance and analytics:

- [ ] Database constraints enforce one open session and one gym-local visit per day.
- [ ] Check-in/out/correction/void use server rules and body GUID operation IDs and are idempotent under retries/app restart.
- [ ] Checkout cannot precede check-in.
- [ ] Missed checkout has an owner-only reasoned correction flow; no auto-checkout was introduced.
- [ ] Voided/superseded anomalies and every correction remain preserved through append-only adjustments and are excluded/recomputed consistently.
- [ ] Progress duration, streak, and badge rules match section 4.5 and automated tests.
- [ ] Gym Goer self endpoints accept no arbitrary MemberID.
- [ ] Owner graph is timezone-correct, zero-filled, range-bounded, and reconciled.
- [ ] Reports include the full selected end day through half-open range semantics.

Mobile and offline:

- [ ] Startup/session restore is awaited and deterministic.
- [ ] Firebase ID-token refresh works across expiry and concurrent API calls.
- [ ] One centralized logout clears tokens, headers, context, and cache.
- [ ] Owner, receptionist, goer, pending, and denied routing is correct.
- [ ] All four requested Gym Goer tabs and required UI states exist.
- [ ] Offline mode is cache-only; attendance write controls are disabled.
- [ ] Cache payloads use per-account AES-256-GCM DEKs, unique nonces, authenticated metadata/AAD, rollback/key-rotation checks, platform backup exclusions, and cryptographic purge on logout.
- [ ] Offline cached access expires after the approved 72-hour/trusted-time window and transient network failure does not masquerade as terminal revocation.
- [ ] Legacy `LocalMember`, `SyncQueue.SerializedData`, DB/WAL/SHM, and restore artifacts contain no unresolved plaintext PII.
- [ ] Accessibility requirements and chart fallback pass manual review.

Quality and operations:

- [ ] Full solution and Release targets build with 0 errors; all warnings are fixed or documented with evidence and no release-blocking warning remains.
- [ ] New automated tests pass; obsolete auth setup cannot run against production.
- [ ] Migration succeeds on empty and production-like databases and is reviewed as a script.
- [ ] Production migration uses the rehearsed idempotent SQL script during write freeze and verifies `__EFMigrationsHistory`; candidate local-date collisions are resolved before filtered indexes.
- [ ] MonsterASP backup/restore, environment settings, HTTPS endpoint, health/smoke, and capacity checks are documented and verified.
- [ ] All conflicting architecture/auth/database/attendance/dashboard/report docs and ADRs are updated.
- [ ] Feature flags, pilot results, monitoring, and rollback steps are verified.

## 16. Delegation plan for implementation agents

This section defines the future coding delegation. The planning agents used to review this document were not authorized to implement it.

### 16.1 Contract-freeze packet owned by the lead/integrator

Before parallel work, the lead freezes:

- role enum values and policy names;
- User/Member/Invite/Attendance schema fields and migration order;
- all `/auth`, `/me`, staff-attendance, and owner-summary contracts;
- stable error codes;
- timezone, duration, streak, and badge rules;
- feature flags; and
- file ownership below.

The lead alone owns cross-workstream merge hotspots:

- `GymTrackPro.API/Program.cs`
- `GymTrackPro.API/Data/GymDbContext.cs`
- EF migration/model snapshot
- `GymTrackPro.Mobile/MauiProgram.cs`
- `GymTrackPro.Mobile/App.xaml.cs`
- root shell integration
- solution/test-project registration
- ADRs and final documentation reconciliation

Before agents begin, the lead lands compile-safe Shared DTO/error-code/interface stubs and the common API/mobile test fixtures. Agents extend only their owned contract folders; `ApiResponse`, shared error codes, test host/auth fixtures, and solution wiring remain lead-owned.

### 16.2 Path-level ownership and external actions

| Owner | Exclusive paths/components | Handoff-only areas |
|---|---|---|
| Lead/integrator | `API/Program.cs`, `API/Data/GymDbContext.cs`, migrations/snapshot/scripts, Shared `ApiResponse`/common contract stubs, `Mobile/MauiProgram.cs`, `Mobile/App.xaml.cs`, existing `AppShell.*`, solution registration, common test fixtures, bootstrap tool, final docs | Applies DI, root-shell, proxy, platform, and policy registrations described by agents. |
| Agent A | User/AccountInvite identity contracts and services, AuthController, identity `MeController`, auth requirements/handlers/current-user context, identity/security tests | Defines named policies; file owners apply them to their controllers; lead registers them. |
| Agent B | Attendance/adjustment/operation contracts, AttendanceController/service/repository, `MeDashboardController`, `MeAttendanceController`, `MeProgressController`, dashboard/report attendance queries, data tests | Supplies schema/index/query changes to lead for DbContext/migration; applies frozen policies to owned controllers. |
| Agent C | New session/cache/HTTP-handler classes, `GoerAppShell.*`, Gym Goer pages/view models, owner graph mobile control, Member Details invite UI, platform manifest/config drafts, mobile tests | Supplies DI/root routing and manifest diffs; lead applies/integrates existing App/AppShell/MauiProgram and release configuration. |

External-state accountability:

- Project owner: approve section 18, rotate MonsterASP credential, confirm tier/quota/backups, authorize maintenance, control Firebase Console/app restrictions, verify the initial owner identity, and approve the invite delivery channel.
- Lead/integrator: execute the one-time owner bootstrap with the project owner present, apply the reviewed migration, configure/verify `firebase-auth-hardened-compat-v1`, control signing/CI secrets, and verify backup/restore and published artifacts.
- Agent C prepares platform backup/transport configuration evidence; the lead signs off and applies external signing/store settings.
- Invite delivery is manual one-time staff handoff unless the project owner separately approves and configures an email channel; no agent assumes authority to send real invites during implementation/testing.

Agents submit focused changes or handoff notes for these hotspots; they do not edit them concurrently.

### 16.3 Agent A - Identity and authorization

Scope: WP0 identity portions, WP1 entity/repository input, WP2, and identity/security tests.

Primary ownership:

- auth controller/service/repository
- User, AccountInvite, current-user contracts
- authentication requirements/handlers/claims/current-user context
- named-policy specification, endpoint inventory, and security test matrix; each controller owner applies the frozen policy
- exception-response hardening

Explicit exclusions: attendance algorithms, owner graph, MAUI pages/cache, final `Program.cs` merge, and EF migration file.

Handoff must include policy registration requirements, schema changes for the lead, all touched controller policies, and negative security test evidence.

### 16.4 Agent B - Attendance, progress, and server analytics

Scope: WP3, WP4, WP7 server portion, reporting corrections, and data tests.

Primary ownership:

- Attendance entity/DTO/interface input
- attendance controller/service/repository
- `MeDashboardController`, `MeAttendanceController`, and `MeProgressController` actions and projection service
- dashboard/report attendance queries and summary contract
- timezone/clock services
- concurrency, idempotency, progress, badge, report, and graph tests

Explicit exclusions: auth policies/claims internals, mobile files, final `GymDbContext`/migration merge.

Handoff must include proposed indexes/constraints, anomaly/preflight requirements, query plans/performance evidence, and exact registration hooks.

### 16.5 Agent C - Mobile session, cache, and UX

Scope: WP5, WP6, WP7 mobile portion, and mobile tests.

Primary ownership:

- Firebase session abstraction and authenticated HTTP handler
- ApiService contracts after freeze
- startup/session state machine handoff
- encrypted SQLite cache and local migrations
- all Gym Goer shells/pages/view models
- existing Member Details invite generation/revocation UI
- owner graph renderer/text fallback
- platform transport/backup configuration
- mobile unit tests and UAT checklist

Explicit exclusions: API schema/policies, EF migration, final `MauiProgram`/App/root-shell merge.

Handoff must include DI registrations, route registrations, platform configuration changes, cache threat-model evidence, and screenshots/UAT results when implementation is authorized.

### 16.6 Lead/integrator and QA wave

After A/B/C complete independently:

1. Lead integrates Shared contracts, `Program.cs`, `GymDbContext`, EF migration, `MauiProgram`, App/root shells, feature flags, and docs.
2. Agent A red-teams authorization and invite/session abuse cases.
3. Agent B rehearses migration, concurrency, timezone, reconciliation, and MonsterASP query load.
4. Agent C runs cross-platform cache/session/accessibility UAT.
5. Lead resolves findings, runs the complete verification matrix, and decides pilot readiness.

This split keeps each agent at a bounded context, minimizes duplicate repository reading, and avoids concurrent edits to the highest-conflict files.

### 16.7 Critical path and safe parallelism

Critical path:

`WP0 -> contract freeze -> WP1 -> WP2 -> WP3 -> WP4 -> WP5/WP6 integration -> WP8 release gate`

Safe parallel work after contracts are frozen:

- Identity policy implementation and attendance query refactoring can proceed in parallel if Shared/DbContext hotspots remain lead-owned.
- Mobile views/cache can proceed against frozen fake contracts while server endpoints are built.
- Owner graph server work can proceed with Gym Goer mobile work.
- Test authors can build fixtures and negative matrices as soon as contracts are frozen.

Unsafe parallel work:

- Multiple agents editing `Program.cs`, `GymDbContext`, migrations, `MauiProgram`, App startup, or root shells.
- UI guessing unfinished DTOs/error codes.
- Database constraints added before anomaly/backfill review.
- Gym Goer routes enabled before authorization lockdown.

## 17. Primary technical references

- [Firebase: verify ID tokens](https://firebase.google.com/docs/auth/admin/verify-id-tokens)
- [Firebase: manage sessions and token lifetime](https://firebase.google.com/docs/auth/admin/manage-sessions)
- [Firebase Authentication Emulator](https://firebase.google.com/docs/emulator-suite/connect_auth)
- [Firebase API-key usage and restrictions](https://firebase.google.com/docs/projects/api-keys)
- [FirebaseAuthentication.net repository (unofficial client dependency)](https://github.com/step-up-labs/firebase-authentication-dotnet)
- [FirebaseAuthentication.net 4.1.0 candidate package](https://www.nuget.org/packages/FirebaseAuthentication.net/4.1.0)
- [.NET MAUI SecureStorage](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage)
- [ASP.NET Core policy-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [EF Core production migration guidance](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [MonsterASP environment variables for connection strings/secrets](https://help.monsterasp.net/books/development/page/environment-variables-as-configuration-store)
- [MonsterASP ASP.NET Core with MSSQL deployment](https://help.monsterasp.net/books/deploy/page/how-to-deploy-net-core-web-application-with-mssql-using-visual-studio)
- [MonsterASP MSSQL TLS guidance](https://help.monsterasp.net/books/databases/page/secure-connection-ssltls-to-mssql)
- [MonsterASP free-tier limits](https://www.monsterasp.net/ASP.NET-Freehosting/)

## 18. Approval checkpoint

Before coding, the project owner should explicitly approve these five scope choices:

1. Single-gym release with internal/wire `Administrator` retained and displayed as “Gym Owner”; multi-tenant SaaS is deferred.
2. Invite-only app activation; verified email alone does not link a Member or privileged User.
3. Staff-scanned member QR and online-only attendance writes; no v1 remote self-check-in.
4. Encrypted, read-only Gym Goer cache with the 72-hour maximum offline-access window; no plaintext attendance cache and no offline attendance queue.
5. V1 badges are server-derived, rule-versioned, and reversible after attendance void/supersede; if badges must be permanent achievements, add `MemberBadgeAwards` before implementation instead.

Once approved, implementation begins with WP0 containment and contract freeze, not with the new Shell or dashboard UI.
