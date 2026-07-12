# Capstone Implementation and Demo Guide

## Purpose

This guide defines the verified GymTrackPro capstone scope and the repeatable
demonstration sequence. The capstone target is a controlled academic demo using
the project team's Firebase project and a manually configured MonsterASP SQL
database. Commercial production rollout is outside this acceptance boundary.

The broader [production hardening plan](19_ProductionHardeningPlan.md) remains a
useful future-work reference. Its infrastructure and commercial-scale gates are
not capstone blockers unless a requirement is explicitly repeated here.

## Current verified baseline

The following results were reported from the reviewed capstone implementation:

| Gate | Verified result |
| --- | ---: |
| Server strict build | 0 errors, 0 warnings |
| Full server tests | 652 passed / 652 total |
| Mobile tests | 73 passed / 73 total |
| Android strict compile | 0 errors, 0 warnings |
| EF pending-model check | No pending model changes |
| EF migration list | Listed through `20260712050837_AddAttendanceVoidingAndSource` |

These results establish a source-level baseline. They do not prove that the
pending MonsterASP migration rehearsal or a physical-device demo has passed.
Record those results in the evidence section when they are performed.

The authenticated MAUI gym-goer dashboard now retrieves the current member's
profile image through the bearer-authenticated API client. It does not bind a
public file URI. Missing, invalid, or temporarily unavailable images fall back
to the default profile presentation without blocking the dashboard.

## Architecture boundary

- Firebase Authentication owns email/password signup, login, email verification,
  token issuance, and verification-email resend.
- SQL Server owns application identity and authorization data, including the
  Firebase UID binding, application role, member link, invites, memberships,
  payments, attendance, and reporting data. A Firebase account alone does not
  grant an application role.
- MonsterASP hosts the manually configured SQL Server demo database. The API is
  the only supported application boundary for reading or changing that data.
- Profile media is accessed through authenticated, authorized API behavior. The
  demo must not depend on guessing or directly browsing a public file URL.
- Firebase configuration and database credentials are supplied through local
  configuration, environment variables, user-secrets, or the deployment host's
  secret settings. Real credentials, ID tokens, connection strings, and service
  account material must never be committed or included in screenshots or demo
  evidence.

## Capstone setup

1. Check out the approved `feature/firebase-auth` capstone revision and confirm
   that no unrelated EF migration generator or watcher is modifying the tree.
2. Configure the API with the intended Firebase project identifier and the
   MonsterASP demo connection string through an untracked secret source.
3. Configure the mobile application with the capstone API base address and its
   Firebase client settings. Do not place server credentials in the mobile app.
4. Restrict the Firebase client configuration to the expected Android package,
   signing fingerprint, and/or development hosts as appropriate for the demo.
5. Rotate the MonsterASP database password before the defense if it has ever
   appeared in source, shell history, chat, screenshots, or shared artifacts.
   Update only the secret store and confirm that the old credential no longer
   works. Confirm encrypted SQL transport with the provider; do not accept
   `Encrypt=False` as the final shared-demo configuration without a documented
   provider limitation and explicit capstone risk acceptance.
6. Apply the capstone migrations to a disposable or backed-up demo database in
   this order:
   1. `20260711204834_StageFirebaseIdentityAndAccountInvites`
   2. `20260712050837_AddAttendanceVoidingAndSource`
7. Seed or retain only synthetic demo data. Never use a real member's personal,
   payment, or attendance information.
8. Complete the owner bootstrap with a designated verified Firebase account and
   the matching SQL Administrator record. Keep the one-time token and database
   secret out of command arguments, logs, screenshots, and tracked files.

### Migration rehearsal evidence

This section is intentionally pending. Do not infer a successful MonsterASP
rehearsal from unit tests or local migration generation.

Source-level EF verification completed successfully: EF listed both migrations
through `20260712050837_AddAttendanceVoidingAndSource`, and the pending-model
check reported no pending changes. A disposable LocalDB execution rehearsal was
attempted but is environment-deferred because the available LocalDB instance
could not create the isolated verification database. This is not evidence of a
MonsterASP rehearsal.

The final combined idempotent SQL script and its SHA-256 are **PENDING**. The
final temporary export could not be completed within the verification session's
environment limit. Do not reuse an earlier generated hash or the separately
documented B1-only script hash as evidence for the final attendance-inclusive
script. Generate a fresh script from the final source, calculate its fresh hash,
and review both before applying it to MonsterASP.

- Final idempotent migration script and fresh SHA-256: **PENDING**
- Target demo database identifier (non-secret alias only): **PENDING**
- Rehearsal date/time and operator: **PENDING**
- Backup or disposable-database confirmation: **PENDING**
- B1 migration result: **PENDING**
- Attendance migration result: **PENDING**
- Post-migration smoke-test result: **PENDING**
- Rollback/reset result, if exercised: **PENDING**

The attendance migration source currently includes these reviewed safeguards:

- staged legacy `datetime2` rename, date-only backfill, and preserved nullable
  legacy value without an unproven timezone conversion;
- `NO ACTION`/restrictive Member and attendance-ledger relationships;
- filtered uniqueness for one non-void daily attendance and one non-void open
  session;
- reporting indexes for attendance date, check-in time, and member/check-in;
- void-aware checkout ordering plus operation, adjustment, source, and metadata
  constraints; and
- preflight rejection of invalid active historical rows before migration.

## Exact capstone demo flow

Use synthetic Owner, Staff, and Member identities. Capture the result of each
step in [the UAT checklist](UAT_GymGoer_Checklist.md).

1. **Owner and staff access**
   - Sign in with the bootstrapped, email-verified Owner account and show Owner
     routing and authorized navigation.
   - Sign out, sign in with an email-verified Staff account, and show that Staff
     receives the expected staff shell without Owner-only access.
2. **Member creation and invite**
   - As Owner or authorized Staff, create a synthetic member.
   - Generate an app invite from the member details screen and show its current
     status. Keep the code visible only for the controlled demo.
3. **Signup, verification, and activation**
   - On the gym-goer device, sign up with the invited member's email.
   - Show that an unverified account cannot complete operational access.
   - Demonstrate resend verification, verify the email, sign in again, and
     activate the invite once.
   - Show that invalid, revoked, or already-used invite codes fail with a useful
     message and do not grant access.
4. **Role routing and profile privacy**
   - Confirm the activated GymGoer is routed to the gym-goer shell and cannot
     navigate to staff or Owner functions.
   - Load the member's own profile image through the authenticated experience.
     Confirm another gym-goer cannot retrieve that private image by changing an
     identifier or URL.
5. **Gym-goer dashboard and attendance history**
   - Show current membership status and expiry on the dashboard.
   - Show the current attendance state and attendance history, including the
     date/time and source data exposed by the UI.
6. **Online check-in and check-out**
   - Check in while online and show the updated current state.
   - Attempt the relevant duplicate or invalid transition and show that it is
     rejected without creating a second attendance session.
   - Check out and confirm the completed visit appears in history and the staff
     attendance view.
7. **Offline queue and reconnect sync**
   - Disconnect the gym-goer device, initiate the supported attendance action,
     and show an explicit queued/pending state rather than a false success.
   - Reconnect, trigger or await sync, and show that the queued operation reaches
     the server once, leaves the queue only after acknowledgement, and refreshes
     current/history views without duplication.
8. **Staff attendance operations**
   - From the Staff experience, perform the supported member lookup and
     attendance action.
   - Show the resulting attendance source and state from both staff and gym-goer
     views, including a controlled rejection for an invalid transition.
9. **Payment and membership boundaries**
   - Record a valid payment or renewal and show the resulting membership dates
     and status.
   - Demonstrate controlled rejection of invalid amounts, wholly expired
     coverage, invalid enum/filter input, and duplicate reference data where the
     current capstone implementation supports the boundary.
   - Pause an eligible active membership, show the paused state, then resume it.
     Show that an inactive, deleted, future-only, or otherwise ineligible
     subscription cannot be paused.
10. **Logout and account isolation**
    - Sign out and confirm protected screens cannot be revisited.
    - Sign in as a different synthetic account and confirm the previous account's
      profile, dashboard, attendance, and queued-operation cache is not shown.

## Safe demo reset and rollback

The preferred reset is a fresh disposable capstone database or a provider backup
made immediately before the rehearsal. Keep a small, versioned set of synthetic
demo inputs so the Owner, Staff, Member, invite, membership, payment, and
attendance story can be recreated consistently.

- Do not delete or edit `__EFMigrationsHistory` rows to force a migration.
- Do not run destructive down migrations against a shared or production-like
  database.
- Do not truncate shared tables or manually rewrite identity bindings.
- If a rehearsal fails before the defense, stop the API, preserve the failure
  evidence, and restore the dedicated demo database or create a new disposable
  database. Reapply migrations in the documented order and repeat the smoke test.
- To reset a single synthetic scenario, use normal application operations where
  available or recreate a new synthetic member/account. Do not reuse a consumed
  invite or manually change its terminal state.
- Clear only the test device's application data when resetting mobile state, then
  sign in and sync again. Never copy one account's cache into another account.

## Known limitations and future production work

The following are documented future-hardening items, not claims about the current
capstone implementation:

- Transactional outbox delivery and durable background-event processing.
- Persisted immutable membership/payment quote snapshots.
- Database-enforced global uniqueness for provider/reference identifiers.
- Distributed synchronization, leases, and multi-instance coordination.
- Encryption of all account-scoped local cache data at rest.
- Object storage/CDN-backed profile media and multi-instance file consistency.
- Full payment-provider integration and provider webhook reconciliation.
- Production-scale load, failure, backup-restore, and disaster-recovery rehearsal.

The capstone still must demonstrate correct authorization, controlled failure
states, account isolation, and no tracked secrets. These are functional acceptance
requirements, not deferred infrastructure work.

## Final evidence and decision

- UAT date/time and timezone: ______________________________
- Source revision/commit: _________________________________
- Branch: `feature/firebase-auth`
- API build/test evidence location: ________________________
- Mobile build/test evidence location: _____________________
- Android device/emulator and OS: __________________________
- Firebase project alias (no secrets): _____________________
- MonsterASP database alias (no secrets): __________________
- Migration rehearsal evidence location: __________________
- Screenshots/video location: ______________________________
- Tester(s): ______________________________________________
- Open defects and accepted limitations: ___________________
- Final result: [ ] PASS  [ ] FAIL  [ ] PASS WITH NOTED LIMITATIONS
- Capstone lead sign-off/date: _____________________________

The branch field above is the required handoff destination, not proof that the
current uncommitted integration tree has already been moved there. Branch
handoff, MonsterASP migration rehearsal, credential rotation, and SQL TLS
confirmation remain pending operator actions.
