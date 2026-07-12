# GymTrackPro Capstone UAT Checklist

Use synthetic accounts and data. Record evidence for every failed or blocked item;
do not mark an item passed from code inspection alone. Follow the complete sequence
in [24_CapstoneImplementationAndDemoGuide.md](24_CapstoneImplementationAndDemoGuide.md).

## Test context

- Date/time and timezone: _________________________________
- Branch and source revision: _____________________________
- API environment: _______________________________________
- Android device/emulator and OS: _________________________
- Firebase project alias (no secrets): ____________________
- MonsterASP database alias (no secrets): _________________
- Tester: ________________________________________________

## Verified source baseline before UAT

- Server strict build: **0 errors / 0 warnings**
- Full server tests: **663 passed / 663 total**
- Focused backend Staff/auth tests: **49 passed / 49 total**
- Mobile tests: **86 passed / 86 total**
- Android strict compile: **0 errors / 0 warnings**
- EF pending-model check: **no pending model changes**
- EF migration list: **through
  `20260712050837_AddAttendanceVoidingAndSource`**

These automated results do not mark any manual checkbox below as passed.
The operator reports both migrations applied/tracked on MonsterASP and the prior
missing-column errors cleared. That remote status is not independently verified
by these automated results. A fresh final idempotent script/hash, backup
evidence, credential rotation, SQL TLS confirmation, physical-device/emulator
UAT, final source commit, and merge to `feature/firebase-auth` remain pending.

## 1. Setup and migration

- [ ] No secrets, tokens, connection strings, or personal data appear in tracked
  files or captured evidence.
- [ ] The demo database is disposable or has a confirmed pre-rehearsal backup.
- [ ] Read-only `__EFMigrationsHistory` verification shows
  `20260711204834_StageFirebaseIdentityAndAccountInvites` before
  `20260712050837_AddAttendanceVoidingAndSource`.
- [ ] No migration SQL is reapplied and no schema, UID binding, or
  `__EFMigrationsHistory` row is manually edited during normal UAT.
- [ ] A fresh final attendance-inclusive idempotent script and SHA-256 were
  generated from the final source; no older or B1-only hash was reused.
- [ ] Attendance safeguards are present after migration: restrictive foreign
  keys, filtered active-session uniqueness, reporting indexes, void-aware
  checkout ordering, and trusted operation/adjustment constraints.
- [ ] Post-migration API startup and database smoke checks pass.

Evidence/result: __________________________________________

## 2. Owner bootstrap and routing

- [ ] The bootstrapped, verified Owner can sign in and receives Owner navigation.
- [ ] The existing Owner bypasses activation; no second Owner or Owner invite is
  created.

Evidence/result: __________________________________________

## 3. Receptionist/Cashier provisioning and activation

- [ ] From the Owner UI, **Add Receptionist** creates one Receptionist profile and
  one-time invite through the Owner-only application flow.
- [ ] The Receptionist registers in Firebase using the exact profile/invite email;
  no role-selection control is available.
- [ ] The verification email and resend-verification flow work.
- [ ] An unverified account cannot activate or enter operational screens.
- [ ] The valid Staff invite activates once with role `Receptionist`.
- [ ] The Receptionist reaches the shared back-office shell; no distinct Staff
  shell is expected.
- [ ] Receptionist operations work while an Owner-only action is denied.
- [ ] A mismatched, revoked, or already-used Staff invite grants no access.
- [ ] If the create response is deliberately interrupted, the tester checks for
  an already-created profile before retrying and uses the Owner-only existing-user
  revoke/reissue flow when needed. This recovery case is nonblocking if the
  normal flow passes, but its result/limitation is recorded.

Evidence/result: __________________________________________

## 4. Member, GymGoer registration, and invite management

- [ ] Authorized Staff/Owner creates a synthetic member before Firebase signup,
  using the exact email the GymGoer will register.
- [ ] Staff can view, generate, and revoke the member's invite using supported UI
  actions.
- [ ] The GymGoer registers through Firebase email/password with that exact email;
  no role-selection control is available.
- [ ] Email verification and resend work, and an unverified GymGoer cannot
  activate or enter operational screens.
- [ ] A valid Member invite activates once, grants `GymGoer`, and routes to
  `GoerAppShell`.
- [ ] Invalid, revoked, and already-used invites show meaningful failure messages
  and grant no access.
- [ ] An email mismatch is rejected without creating an application binding.
- [ ] A GymGoer cannot access Staff or Owner screens.
- [ ] Invite status is refreshed after activation or revocation.

Evidence/result: __________________________________________

## 5. Private profile and account isolation

- [ ] The signed-in user can load the permitted profile image through the
  authenticated MAUI API flow, with no public profile URI.
- [ ] Another GymGoer cannot retrieve that private profile by changing an ID or
  direct URL.
- [ ] Logout prevents returning to protected screens.
- [ ] A second account does not see the first account's profile, dashboard,
  attendance data, or queued offline operations.
- [ ] Android backup rules exclude account-scoped local application data as
  configured by the project.

Evidence/result: __________________________________________

## 6. Gym-goer dashboard and history

- [ ] Dashboard shows the correct membership status and expiry date.
- [ ] Current attendance state is accurate.
- [ ] Attendance history displays the expected completed visits and source data.
- [ ] Current-month check-ins, streaks, and supported progress indicators match
  the seeded scenario.
- [ ] Digital card/QR data matches the backend member identity and is readable by
  the supported scanner flow.

Evidence/result: __________________________________________

## 7. Online and offline attendance

- [ ] Online check-in creates one current attendance session.
- [ ] Duplicate/invalid check-in is rejected without creating another session.
- [ ] Online check-out closes the session and refreshes history.
- [ ] While disconnected, a supported action displays an explicit queued/pending
  state and does not claim server success.
- [ ] After reconnect, sync submits the queued operation once and removes it only
  after server acknowledgement.
- [ ] Repeated reconnect/sync does not duplicate the attendance operation.
- [ ] Staff attendance view and GymGoer history agree after synchronization.

Evidence/result: __________________________________________

## 8. Staff attendance, payments, and membership lifecycle

- [ ] Staff can locate a member and perform the supported attendance operation.
- [ ] Invalid attendance transitions return a controlled message.
- [ ] A valid payment/renewal produces the expected membership dates and state.
- [ ] Invalid amounts and wholly expired coverage are rejected.
- [ ] Invalid enum or filter input produces a controlled validation response.
- [ ] Duplicate reference data is rejected where covered by the current capstone
  service boundary.
- [ ] An eligible active membership can be paused and resumed.
- [ ] Inactive, deleted, future-only, or otherwise ineligible subscriptions cannot
  be paused.

Evidence/result: __________________________________________

## 9. Owner analytics

- [ ] Owner can open the attendance summary report.
- [ ] Total visits, average duration, and date-grouped check-ins match the demo
  attendance data.
- [ ] The text/list fallback remains usable if a chart is unavailable.
- [ ] CSV export contains the expected synthetic records and headings.

Evidence/result: __________________________________________

## 10. Application-layer defect evidence

Do not change MonsterASP schema, Firebase UID bindings, or migration-history rows
while triaging an application defect.

- [ ] Source revision/build configuration: ______________________________
- [ ] Device/emulator, OS, and network state: ___________________________
- [ ] Application role and screen: _____________________________________
- [ ] Reproduction steps and expected/actual result: ____________________
- [ ] Sanitized exception/stack location: _______________________________
- [ ] API route, HTTP status, safe error code, correlation ID: __________
- [ ] Suspected layer: [ ] XAML  [ ] ViewModel  [ ] Navigation  [ ] DI
  [ ] Controller  [ ] Service  [ ] Authorization
- [ ] Fix/retest result or accepted limitation: _________________________

## Final UAT decision

- Failed item numbers: ____________________________________
- Defect references: ______________________________________
- Accepted capstone limitations: ___________________________
- Evidence folder/link: ___________________________________
- Result: [ ] PASS  [ ] FAIL  [ ] PASS WITH NOTED LIMITATIONS
- Tester signature/date: __________________________________
- Capstone lead signature/date: ___________________________
