# GymTrackPro — Capstone Presentation Script & Slide Content

> Format: Slide title + bullet points + Speaker notes
> Designed for a 30-minute presentation slot

---

## Slide 1 — Title

**GymTrackPro**
*A Mobile-First Gym Management and Member Self-Service Platform*

- BSIT Capstone Project — [Group Name]
- [School Name] | [Semester, Year]
- Team Members: [Name 1], [Name 2], [Name 3]

**Speaker Notes:**
Welcome, panel. Our capstone project is GymTrackPro — a cross-platform mobile gym management system. I'm [Name], and I'll be walking you through our system today. Our presentation covers the problem we solved, how we built it, and a live demo.

---

## Slide 2 — Problem Statement

**The Problem: Gyms Still Run on Paper**

- Manual logbooks for attendance → error-prone, easy to falsify
- Spreadsheets for payments → no real-time visibility
- No way for members to track their own progress
- Expired memberships discovered too late
- Staff has no mobile tools — everything is PC-dependent

**Speaker Notes:**
Small gyms in the Philippines rely heavily on manual processes. We interviewed gym staff and observed that attendance is recorded in notebooks, payments are tracked in Excel, and there is no self-service for members. These processes are slow, inaccurate, and hard to scale.

---

## Slide 3 — Our Solution

**GymTrackPro: One App, Two Experiences**

- 🏢 **Back-Office App** — for gym staff (Admin + Receptionist)
  - Member registration, QR check-in/out, payments, subscriptions, reports
- 📱 **Member App** — for gym goers
  - Personal dashboard, self check-in, digital membership card, streak tracking, badges

**Speaker Notes:**
Our solution is a single .NET MAUI application that adapts its interface based on the user's role. Staff see the management interface. Members see their personal self-service portal. Both connect to the same backend API, secured by Firebase Authentication.

---

## Slide 4 — Objectives

**What We Set Out to Build:**

1. Secure Firebase-based authentication (Email + Google)
2. Member registry with QR code assignment and photo upload
3. QR code attendance tracking — staff-side and self-service
4. Full membership subscription lifecycle (create, pause, resume, renew, expire)
5. Payment recording with refund support and receipt generation
6. Six financial/operational reports with CSV export
7. Role-based authorization (Administrator, Receptionist, Gym Goer)
8. Gym Goer self-service dashboard with streaks, badges, and digital card
9. Automatic subscription expiration via background worker
10. Configurable gym system settings

**Speaker Notes:**
These are our ten specific objectives. Each one maps directly to a feature in the live system. We'll show most of these in the demo.

---

## Slide 5 — System Architecture

```
[.NET MAUI Mobile App]
     Firebase Auth ──► Firebase ID Token
     ▼ HTTPS Bearer
[ASP.NET Core 8 API] → [GymDbContext] → [SQL Server]
     ↑ Background Workers
     [SubscriptionExpirationWorker]
     [NotificationWorker]
```

**Key Layers:**
- **Mobile:** MVVM Pattern (Views, ViewModels, Services)
- **API:** Controllers → Services → Repositories → EF Core
- **Shared:** DTOs, Entities, Interfaces (used by both mobile and API)

**Speaker Notes:**
Our architecture follows a standard client-server model. The mobile app communicates with the REST API over HTTPS. Authentication is handled entirely by Firebase — we never store passwords. The API uses ASP.NET Core with Entity Framework Core against SQL Server. Two background workers run on the server independently of user requests.

---

## Slide 6 — Technology Stack

| Layer | Technology |
|:--|:--|
| Mobile | .NET MAUI (C#, XAML) |
| Backend | ASP.NET Core 8 |
| Shared Library | .NET 8 Class Library |
| Database | SQL Server |
| ORM | EF Core 8 (Code-First) |
| Authentication | Firebase Auth |
| Local Storage | SQLite |
| QR Scanning | ZXing.Net.Maui |
| Source Control | Git / GitHub |

**Speaker Notes:**
We deliberately chose the .NET ecosystem end-to-end. This let us share code between the mobile app and the API through the Shared project — the same DTO classes, the same entity contracts. Firebase handles all authentication so we never store passwords.

---

## Slide 7 — System Modules & Features

| Module | Features |
|:--|:--|
| Authentication | Firebase login, invite activation, role routing |
| Member Management | Register, update, search, soft-delete, QR assignment, profile picture |
| Attendance | Staff QR scan check-in/out, self-service check-in, void, correction |
| Subscriptions | Enroll, pause, resume, renew (transactional), auto-expire |
| Payments | Record, refund, search, receipt generation |
| Reports | 6 report types × (view + CSV export) |
| Settings | Configurable gym parameters |
| Notifications | In-app notifications, mark read |
| Gym Goer Portal | Dashboard, digital card, progress, attendance history, badges |
| Users | View and manage staff accounts |

**Speaker Notes:**
Here is a complete feature matrix. We have 10 major modules. Each module has full CRUD operations where applicable, with role-based access controls enforced at the API level.

---

## Slide 8 — Database ERD (Simplified)

```
Users ──────────────► Members
                         │
              ┌──────────┼───────────┐
              ▼          ▼           ▼
        AttendanceLogs  Subscriptions  Notifications
                           │
                   ┌───────┴───────┐
                   ▼               ▼
               Payments     MembershipPauses
                   └──► MembershipPlans
```

**Key Facts:**
- 15 database tables
- 12 enums
- Soft delete on Members and Payments; plan deactivation through `Status = Inactive`
- Optimistic concurrency on Attendance, Invites
- One-to-one member projection version table for GymGoer projection metadata

**Speaker Notes:**
Our database has 15 tables. The central entity is Members, which connects to Attendance, Subscriptions, Notifications, and Payments. Each Subscription references a MembershipPlan. We use soft deletes on critical records to preserve financial history.

---

## Slide 9 — User Roles & Permissions

| Feature | Administrator | Receptionist | Gym Goer |
|:--|:--|:--|:--|
| Register Members | ✅ | ✅ | ❌ |
| Manage Subscriptions | ✅ | ✅ | ❌ |
| Process Payments | ✅ | ✅ | ❌ |
| Check In / Out (Staff) | ✅ | ✅ | ❌ |
| Delete Member | ✅ | ❌ | ❌ |
| Refund Payment | ✅ | ❌ | ❌ |
| View Reports | ✅ | ❌ | ❌ |
| Manage Settings | ✅ | ❌ | ❌ |
| Self Check-In | ❌ | ❌ | ✅ |
| View Personal Dashboard | ❌ | ❌ | ✅ |
| Digital Membership Card | ❌ | ❌ | ✅ |

**Speaker Notes:**
We have three roles enforced by five authorization policies in the API. Administrators have full access. Receptionists can handle day-to-day operations. Gym Goers only see their own data.

---

## Slide 10 — System Flow: Check-In

```
[Receptionist opens Attendance Screen]
     ↓
[Scans QR code with camera — ZXing]
     ↓
[POST /api/v1/attendance/check-in { qrCode }]
     ↓
[API: Validates member → subscription → no open session]
     ↓
[INSERT AttendanceLogs + AttendanceOperations]
     ↓
[Return: Check-In confirmed with timestamp]
```

**Speaker Notes:**
The attendance flow is the most frequently used feature. A receptionist opens the attendance page, scans the member's QR code, and the API validates everything: Is the member active? Do they have an active subscription? Are they already checked in? If all checks pass, the attendance record is created.

---

## Slide 11 — Demo Walkthrough

**Demo Sequence:**
1. Login as Administrator (Firebase email auth)
2. Dashboard — show KPI metrics
3. Members — search, view member details
4. Register new member — fill form, upload photo
5. Attendance — scan QR code for check-in
6. Subscriptions — create and renew
7. Payments — process payment, show receipt
8. Reports — show Daily Revenue, export CSV
9. Settings — show configurable parameters
10. Switch to Gym Goer account — show self-service dashboard, digital card, progress

**Speaker Notes:**
We'll now show the actual running app on an Android device. [Start demo here.] I'll narrate each step as we go.

---

## Slide 12 — Testing

**Integration Test Coverage:**
- 8 PowerShell test suites
- ~80+ test cases
- Covers all major API flows end-to-end against live API + Docker SQL Server

**Test Suites:**
- Authentication (login, register, email verify)
- Member Management (CRUD, QR, soft delete, RBAC)
- Attendance (check-in, check-out, void)
- Subscriptions (enroll, pause, resume, renew)
- Payments (process, refund, RBAC)
- Plans (CRUD, deactivate)
- Settings (read, update)
- Notifications (create, mark read)

**Speaker Notes:**
Testing was done through a combination of unit tests in the .NET test projects and end-to-end integration tests via PowerShell scripts. Each script starts a fresh API instance, runs all test cases, and reports pass/fail counts.

---

## Slide 13 — Future Improvements

| Improvement | Priority |
|:--|:--|
| Push Notifications (Firebase Cloud Messaging) | High |
| PDF Report Export | Medium |
| Multi-Gym SaaS Support | High |
| Web Admin Portal | Medium |
| Biometric / Face Check-In | Low |
| Email/SMS Expiration Reminders | High |
| Google Sign-In (complete native config) | Medium |
| Automated Database Backups | High |

**Speaker Notes:**
The Firebase Cloud Messaging infrastructure is already stubbed in the codebase as Phase 10. The multi-gym SaaS foundation is partially built in our develop branch. These are the highest-priority next steps if we were to continue development beyond this capstone.

---

## Slide 14 — Conclusion

**What We Accomplished:**
- ✅ Full-stack mobile gym management system
- ✅ Firebase-authenticated, role-secured API with 50+ endpoints
- ✅ 15-table relational database with proper normalization
- ✅ Dual-experience mobile app (back-office + gym goer)
- ✅ Self-service portal with streaks, badges, and digital card
- ✅ 6 report types with CSV export
- ✅ Background subscription expiration automation
- ✅ Comprehensive integration test suite

**GymTrackPro brings gym management out of the notebook and into the digital age.**

**Speaker Notes:**
To summarize: we built a complete, working gym management system from scratch. It runs on Android, connects to a live API, and is secured by Firebase. Every feature we set out to build in our objectives has been implemented and tested. Thank you.

---

## Slide 15 — Questions

**We are ready for your questions.**

*"Any technical question you have, we are prepared to answer from our implementation."*

**Speaker Notes:**
Panel, we welcome your questions. We have prepared detailed answers for both technical and design questions.
