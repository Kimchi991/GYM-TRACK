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
| Full server tests | 663 passed / 663 total |
| Focused backend staff/auth tests | 49 passed / 49 total |
| Mobile tests | 86 passed / 86 total |
| Android strict compile | 0 errors, 0 warnings |
| EF pending-model check | No pending model changes |
| EF migration list | Listed through `20260712050837_AddAttendanceVoidingAndSource` |

These results establish a source-level baseline. They do not independently
validate the remote MonsterASP database or prove that a physical-device demo has
passed. The database status below is operator-reported and must be paired with
the requested evidence before final sign-off.

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

1. Use `solsolplan` as the UAT source branch. Commit `9d78179` is the prior
   reviewed baseline; the final source revision is **PENDING** because the new
   Receptionist provisioning changes are not yet committed. Record the final
   revision before UAT. The merge destination remains `feature/firebase-auth`.
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
6. For the current MonsterASP demo database, perform read-only verification that
   these migrations are already present in `__EFMigrationsHistory`, in order:
   1. `20260711204834_StageFirebaseIdentityAndAccountInvites`
   2. `20260712050837_AddAttendanceVoidingAndSource`
   Do not reapply their raw SQL, remove columns, or edit migration-history rows
   during normal UAT. Apply migrations only when creating a fresh disposable or
   restored database, after completing the documented backup and preflight.
7. Seed or retain only synthetic demo data. Never use a real member's personal,
   payment, or attendance information.
8. The one-time Owner bootstrap is **operator-reported complete**. Use the
   existing verified Owner account. Do not create a second Owner and do not use
   an invite for the Owner. Keep the Owner email, Firebase UID, tokens, and
   database secrets out of documentation, logs, screenshots, and tracked files.

### MonsterASP migration status and evidence

The operator reports that both migrations were applied to MonsterASP, the
attendance migration was recorded in `__EFMigrationsHistory`, and the prior
missing-column dashboard errors were cleared. This is the current UAT baseline,
not an independent database validation by the review agents. Normal UAT must
read-verify this state and must not repair, reapply, or rewrite it.

Source-level EF verification completed successfully: EF listed both migrations
through `20260712050837_AddAttendanceVoidingAndSource`, and the pending-model
check reported no pending changes. A disposable LocalDB execution rehearsal was
attempted but is environment-deferred because the available LocalDB instance
could not create the isolated verification database. This is not evidence of a
MonsterASP rehearsal.

The final combined idempotent SQL script and its SHA-256 remain **PENDING**.
Do not reuse an earlier generated hash or the separately documented B1-only
script hash as evidence for the final attendance-inclusive script. Generate and
review a fresh artifact from the final committed source for archival/recovery
evidence; do not execute it again against the already-migrated UAT database.

- Final idempotent migration script and fresh SHA-256: **PENDING**
- Target demo database identifier (non-secret alias only): **PENDING**
- Remote execution date/time and operator: **PENDING**
- Backup or disposable-database confirmation: **PENDING**
- B1 migration result: **OPERATOR-REPORTED APPLIED/TRACKED**
- Attendance migration result: **OPERATOR-REPORTED APPLIED/TRACKED**
- Missing-column dashboard errors: **OPERATOR-REPORTED CLEARED**
- Read-only history/schema verification: **PENDING**
- Post-migration application smoke-test evidence: **PENDING**
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

1. **Existing Owner access**
   - Sign in with the bootstrapped, email-verified Owner account and show Owner
     routing and authorized navigation.
   - Confirm the Owner bypasses activation. Do not create another Owner and do
     not issue or redeem an Owner invite.
2. **Receptionist/Cashier provisioning and activation**
   - As Owner, open **Add Receptionist** and create the Receptionist profile and
     one-time invite through the supported UI. The underlying Owner-only route is
     `POST /api/v1/users/staff`; the operation does not create Firebase
     credentials.
   - On the Receptionist device, register the exact email used for that profile,
     complete Firebase email verification, and activate the invite once. There
     is no user-selectable role; the invite grants the `Receptionist` role.
   - Confirm the Receptionist reaches the shared back-office shell. There is no
     separate Staff shell. Demonstrate that an Owner-only action is denied while
     authorized Cashier/Staff operations remain available.
   - If the create request's response is interrupted, do not blindly submit a
     second Staff profile. First verify whether the profile/invite was committed;
     if it was, revoke/reissue the invite through the Owner-only existing-user
     invite operation. Treat this recovery scenario as nonblocking when the
     normal successful flow passes, but record its evidence or limitation.
3. **Member creation and invite**
   - As Owner or authorized Staff, create a synthetic member.
   - Use the exact email that the GymGoer will register in Firebase.
   - Generate an app invite from the member details screen and show its current
     status. Keep the code visible only for the controlled demo.
4. **GymGoer signup, verification, and activation**
   - On the gym-goer device, sign up with the invited member's email.
   - Confirm the registration UI offers no role selection; the Member invite is
     the only authority that grants the `GymGoer` role.
   - Show that an unverified account cannot complete operational access.
   - Demonstrate resend verification, verify the email, sign in again, and
     activate the invite once.
   - Show that invalid, revoked, or already-used invite codes fail with a useful
     message and do not grant access.
5. **Role routing and profile privacy**
   - Confirm the activated GymGoer is routed to the gym-goer shell and cannot
     navigate to staff or Owner functions.
   - Load the member's own profile image through the authenticated experience.
     Confirm another gym-goer cannot retrieve that private image by changing an
     identifier or URL.
6. **Gym-goer dashboard and attendance history**
   - Show current membership status and expiry on the dashboard.
   - Show the current attendance state and attendance history, including the
     date/time and source data exposed by the UI.
7. **Online check-in and check-out**
   - Check in while online and show the updated current state.
   - Attempt the relevant duplicate or invalid transition and show that it is
     rejected without creating a second attendance session.
   - Check out and confirm the completed visit appears in history and the staff
     attendance view.
8. **Offline queue and reconnect sync**
   - Disconnect the gym-goer device, initiate the supported attendance action,
     and show an explicit queued/pending state rather than a false success.
   - Reconnect, trigger or await sync, and show that the queued operation reaches
     the server once, leaves the queue only after acknowledgement, and refreshes
     current/history views without duplication.
9. **Staff attendance operations**
   - From the Staff experience, perform the supported member lookup and
     attendance action.
   - Show the resulting attendance source and state from both staff and gym-goer
     views, including a controlled rejection for an invalid transition.
10. **Payment and membership boundaries**
   - Record a valid payment or renewal and show the resulting membership dates
     and status.
   - Demonstrate controlled rejection of invalid amounts, wholly expired
     coverage, invalid enum/filter input, and duplicate reference data where the
     current capstone implementation supports the boundary.
   - Pause an eligible active membership, show the paused state, then resume it.
     Show that an inactive, deleted, future-only, or otherwise ineligible
     subscription cannot be paused.
11. **Logout and account isolation**
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

## Application-layer defect triage

The current MonsterASP schema is frozen during normal UAT. Investigate reported
failures in MAUI/XAML, ViewModels, navigation, dependency injection, API
controllers, services, validation, and authorization. Do not mutate schema,
manually change Firebase UID bindings, or edit `__EFMigrationsHistory` while
triaging an application defect.

Record this evidence for every defect without including secrets or personal
identifiers:

- final source revision and build configuration;
- device/emulator model, OS, and network state;
- signed-in application role and screen;
- exact reproduction steps and expected/actual behavior;
- sanitized exception type, message, and stack trace;
- API route, HTTP status, safe error code, and correlation ID;
- screenshot/video or sanitized log location;
- suspected layer: MAUI/XAML, ViewModel, navigation, DI, controller, service, or
  authorization; and
- disposition, retest result, and accepted capstone limitation, if any.

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
- UAT source branch: `solsolplan`
- Prior reviewed baseline: `9d78179`
- Final source revision/commit: **PENDING** __________________
- Merge destination: `feature/firebase-auth`
- API build/test evidence location: ________________________
- Mobile build/test evidence location: _____________________
- Android device/emulator and OS: __________________________
- Firebase project alias (no secrets): _____________________
- MonsterASP database alias (no secrets): __________________
- Remote migration evidence location: _____________________
- Screenshots/video location: ______________________________
- Tester(s): ______________________________________________
- Open defects and accepted limitations: ___________________
- Final result: [ ] PASS  [ ] FAIL  [ ] PASS WITH NOTED LIMITATIONS
- Capstone lead sign-off/date: _____________________________

The migrations and Owner binding are operator-reported complete on MonsterASP;
their supporting evidence is still required. Final source commit, branch merge,
credential rotation, SQL TLS confirmation, and physical-device UAT remain
pending operator actions.
