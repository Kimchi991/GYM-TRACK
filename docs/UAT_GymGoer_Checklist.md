# Gym Goer UAT Checklist

## 1. Authentication and Registration
- [ ] User can sign up via Firebase Authentication using email and password.
- [ ] User receives a verification email.
- [ ] User cannot access operational back-office features (Receptionist / Admin views).
- [ ] User with a valid Invite Code can activate their app access via the `ActivateInviteAsync` flow.

## 2. Gym Goer Navigation (Role-based)
- [ ] Upon login, if role is `GymGoer`, user is redirected to `GoerAppShell`.
- [ ] `GoerAppShell` displays Dashboard, Progress, and Digital Card tabs.
- [ ] Gym Owner / Receptionist are directed to standard `AppShell`.

## 3. Gym Goer Dashboard
- [ ] Dashboard shows active membership status and expiry date.
- [ ] Dashboard shows current month's check-ins and streaks.
- [ ] Offline mode works correctly: displays data loaded from the encrypted SQLite local cache.
- [ ] Re-sync works when connection is restored.

## 4. Gym Goer Digital Card
- [ ] Displays a QR code representing the user's `Member.QRCode` or `MemberID`.
- [ ] QR code accurately matches the backend and is readable by the GymTrackPro scanner.

## 5. Gym Goer Progress & Badges
- [ ] User can select a month and view check-in history.
- [ ] Streaks (current and longest) are correctly fetched from the server.
- [ ] Badges correctly unlock according to server-side rules (First Visit, 3-Day Streak, Weekend Warrior).

## 6. App Access Invite (Staff / Owner)
- [ ] Staff can view the current invite status for a Member on the `MemberDetailsPage`.
- [ ] Staff can Generate a new Invite Code.
- [ ] Staff can Revoke an existing Invite Code.

## 7. Owner Analytics
- [ ] Owner can select "Owner Attendance Summary" from Reports.
- [ ] Owner views a summarized text-fallback or list representing Total Visits, Average Duration, and Check-Ins grouped by date.
- [ ] Owner can export this summary to CSV.

## 8. Security & Edge Cases
- [ ] Android backup correctly excludes SQLite cache (`backup_rules.xml` and `data_extraction_rules.xml`).
- [ ] Unverified Firebase users cannot bypass the `Activate` invite screen.
- [ ] Trying to use an invalid or already-used invite code shows an appropriate error message.
