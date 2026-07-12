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
- Full server tests: **652 passed / 652 total**
- Mobile tests: **73 passed / 73 total**
- Android strict compile: **0 errors / 0 warnings**
- EF pending-model check: **no pending model changes**
- EF migration list: **through
  `20260712050837_AddAttendanceVoidingAndSource`**

These automated results do not mark any manual checkbox below as passed.
LocalDB execution rehearsal was environment-deferred. A fresh final idempotent
script/hash, MonsterASP rehearsal, credential rotation, SQL TLS confirmation,
physical-device/emulator UAT, and handoff to `feature/firebase-auth` remain
pending.

## 1. Setup and migration

- [ ] No secrets, tokens, connection strings, or personal data appear in tracked
  files or captured evidence.
- [ ] The demo database is disposable or has a confirmed pre-rehearsal backup.
- [ ] `20260711204834_StageFirebaseIdentityAndAccountInvites` is applied first.
- [ ] `20260712050837_AddAttendanceVoidingAndSource` is applied second.
- [ ] A fresh final attendance-inclusive idempotent script and SHA-256 were
  generated from the final source; no older or B1-only hash was reused.
- [ ] Attendance safeguards are present after migration: restrictive foreign
  keys, filtered active-session uniqueness, reporting indexes, void-aware
  checkout ordering, and trusted operation/adjustment constraints.
- [ ] Post-migration API startup and database smoke checks pass.

Evidence/result: __________________________________________

## 2. Authentication, bootstrap, and routing

- [ ] The bootstrapped, verified Owner can sign in and receives Owner navigation.
- [ ] A verified Staff account receives staff navigation and cannot access
  Owner-only functions.
- [ ] A user can sign up through Firebase email/password authentication.
- [ ] The verification email and resend-verification flow work.
- [ ] An unverified account cannot activate or enter operational screens.
- [ ] A valid invite activates the intended member once and routes the user to
  `GoerAppShell`.
- [ ] Invalid, revoked, and already-used invites show meaningful failure messages
  and grant no access.
- [ ] A GymGoer cannot access Staff or Owner screens.

Evidence/result: __________________________________________

## 3. Member and invite management

- [ ] Authorized Staff/Owner can create a synthetic member.
- [ ] Staff can view the member's current app-invite status.
- [ ] Staff can generate and revoke an invite using supported UI actions.
- [ ] Invite status is refreshed after activation or revocation.

Evidence/result: __________________________________________

## 4. Private profile and account isolation

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

## 5. Gym-goer dashboard and history

- [ ] Dashboard shows the correct membership status and expiry date.
- [ ] Current attendance state is accurate.
- [ ] Attendance history displays the expected completed visits and source data.
- [ ] Current-month check-ins, streaks, and supported progress indicators match
  the seeded scenario.
- [ ] Digital card/QR data matches the backend member identity and is readable by
  the supported scanner flow.

Evidence/result: __________________________________________

## 6. Online and offline attendance

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

## 7. Staff attendance, payments, and membership lifecycle

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

## 8. Owner analytics

- [ ] Owner can open the attendance summary report.
- [ ] Total visits, average duration, and date-grouped check-ins match the demo
  attendance data.
- [ ] The text/list fallback remains usable if a chart is unavailable.
- [ ] CSV export contains the expected synthetic records and headings.

Evidence/result: __________________________________________

## Final UAT decision

- Failed item numbers: ____________________________________
- Defect references: ______________________________________
- Accepted capstone limitations: ___________________________
- Evidence folder/link: ___________________________________
- Result: [ ] PASS  [ ] FAIL  [ ] PASS WITH NOTED LIMITATIONS
- Tester signature/date: __________________________________
- Capstone lead signature/date: ___________________________
