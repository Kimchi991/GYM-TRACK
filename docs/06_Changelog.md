# Project Changelog

All notable changes to the GymTrackPro project will be documented in this file. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [v2.0.0-rc2] - 2026-07-05

### Added
*   **Mobile App QR Scanner & Generator**: Integrated `ZXing.Net.Maui` to provide a live camera QR code scanner on the Attendance Page, and a QR code generator on the Member Details Page.
*   **Permissions**: Added Camera permission requests to Android (`AndroidManifest.xml`) and iOS (`Info.plist`) for the QR scanner.

---

## [v2.0.0-rc1.rev9] - 2026-07-02

### Added
*   **Dashboard Module**: Implemented real-time system metrics (checked-in headcounts, active memberships, daily/monthly revenue, expirations, and registrations) with hourly foot traffic and revenue by plan distributions.
*   **Reporting & Operations Module**: Built analytical reports for daily/monthly revenue, attendance, sales, expiring memberships, refunds, and cashier activity with date range queries and native CSV exports.
*   **Master E2E Verification Suite**: Added the Dashboard & Reports E2E integration test script, verifying JSON endpoints and CSV export files (totaling 126/126 checks passing).
*   **Documentation**: Authored specs for `17_Dashboard.md`, `18_Reporting.md`, established the Production Hardening & Capstone Readiness roadmap in `19_ProductionHardeningPlan.md`, documented system settings in `20_SystemSettings.md`, and compiled the master architecture reference in `21_SystemArchitecture.md`.

---

## [v2.0.0-rc1.rev8] - 2026-07-02

### Added
*   **Membership Plans Module**: Completed plan name uniqueness check (raises ArgumentException on duplicates) and soft-deactivation (mark as Inactive) via `MembershipPlanService` and `MembershipPlanRepository`.
*   **Payments Module**: Implemented secure transaction receipt generation (`REC-YYMMDDHHMMSS-RAND`), online reference uniqueness checks, positive financial validations, and soft-refund procedures that toggle associated subscriptions to Cancelled.
*   **Membership Lifecycle Module**: Structured subscription state machine (PendingPayment -> Active -> Paused -> Resumed -> Cancelled) with automatic EndDate extensions during pause-resumptions.
*   **E2E Integration Testing & Runner**: Built E2E integration test suites for Membership Plans and Payments/Lifecycle modules. Created a master test runner script (`run_all_tests.ps1`) executing all 5 suites consecutively (110 functional assertions passing successfully).
*   **Documentation Specs**: Authored standard docs for `14_MembershipPlans.md`, `15_Payments.md`, and `16_MembershipLifecycle.md`.

---

## [v2.0.0-rc1.rev7] - 2026-07-02

### Added
*   **Module Documentation Standard:** Formatted all completed modules (`Authentication`, `Member Management`, `Attendance`) around a 7-facet architectural template.
*   **Test Run Verification:** Re-ran all E2E integration test suites (Authentication, Member Management, Attendance), verifying that all 32 integration test scenarios compile and pass successfully.

---

## [v2.0.0-rc1.rev6] - 2026-07-02

### Added
*   **Attendance & QR Check-In Module:** Completed full check-in and check-out workflows in `AttendanceService.cs` and `AttendanceController.cs`.
*   **Active Membership Rule (BR-01):** Implemented validation checking that members checking in have an active subscription within the validity period.
*   **Check-In Limits Rule (BR-02):** Implemented double check-in prevention and strict single-entry check-in limits per calendar day.
*   **Exception Middleware Mapping:** Configured `KeyNotFoundException` mapping to `404 Not Found` responses in the global error handler middleware.
*   **Attendance E2E Integration Testing:** Built a PowerShell test script (`scratch/attendance_integration_test.ps1`) covering 9 E2E scenarios for active/expired subscriptions, daily check-in limits, and check-out logs.
*   **Audit Logging for Attendance:** Captured check-in success, check-in failure reasons, and check-out events in the system activity log.

---

## [v2.0.0-rc1.rev5] - 2026-07-02

### Added
*   **Member Management Module:** Completed full CRUD operations, Base64 profile image filesystem decoding, unique QR check-in string generation (`GTP-*`), and soft-deletion capability.
*   **Paginated Search & Filtering:** Implemented DB-level query filtering (`GET /api/v1/Members/search`) mapping matching strings against names, phones, IDs, or QR codes.
*   **Members E2E Integration Testing:** Built a PowerShell test script covering 15 CRUD lifecycle, search, pagination, validation, and authorization check scenarios.
*   **Audit Logging for Members:** Configured member lifecycle tracking logging events for member creation, modifications, and soft-deletes.

---

## [v2.0.0-rc1.rev4] - 2026-07-02

### Added
*   **Authentication Lifecycle E2E Testing:** Created a PowerShell test runner executing 8 security scenarios verifying registration, lockout, verification, recovery, and role-based permissions.
*   **Audit Logging Integration:** Configured `IAuditService` inside `AuthenticationService` to write records on registration, logins, failures, email verifications, and password resets.
*   **Password Complexity Policy:** Standardized password checks in `RegisterUserDto` via regex validator requiring 8+ chars, upper, lower, numeric, and special characters.
*   **Secure Verification Endpoint:** Refactored `verify-email` API endpoint to accept payload parameters inside JSON request body via `VerifyEmailDto` to prevent query token log leaks.
*   **Nullable Audit Log User Reference:** Shifted `AuditLog.UserID` to a nullable type (`int?`) with database schema update and EF Core migration to support anonymous failed login logging.

### Changed
*   **Self-Registration Role Restriction:** Removed client-side role picker from registration views and DTO, hardcoding backend self-registration to default strictly to `UserRole.Receptionist`.

---

## [v2.0.0-rc1.rev3] - 2026-07-02

### Added
*   **Generic Base Repository:** Implemented `IBaseRepository<T>` and EF Core `BaseRepository<T>` to unify common database CRUD behaviors and minimize code duplication.
*   **Relational Integrity & Soft Deletes:** Implemented database-level RESTRICT delete rules between payments, members, and subscriptions, and added soft-delete support via `IsDeleted` flags for `Member` and `Payment` repositories.
*   **Centralized Exception Handling Middleware:** Added global request exception interceptor `ExceptionHandlingMiddleware` mapping standard exceptions to JSON failure responses.
*   **Standardized API Response Wrapper:** Added `ApiResponse` and `ApiResponse<T>` classes to establish uniform success/failure HTTP payload wrappers.
*   **Hosted Service Placeholder:** Created `SubscriptionExpirationWorker` background HostedService running periodic membership state audits.
*   **Decoupled Services:** Created `INotificationService` and `IEmailService` interfaces and service orchestrators to isolate and decouple downstream provider implementations (Firebase FCM and SMTP).

### Changed
*   **Domain Restructuring:** Cleanly moved all entities from `Shared/Models/` into the new `Shared/Entities/` folder and namespace, keeping them isolated from DTO schemas.
*   **API Versioning:** Updated all API controller route attributes to route requests under the `/api/v1/...` prefix.

---

## [v2.0.0-rc1.scaffold] - 2026-07-02

### Added
*   **Solution Structure:** Created `src/GymTrackPro.slnx` containing three C# projects:
    *   `GymTrackPro.Shared` (Class Library)
    *   `GymTrackPro.API` (Web API)
    *   `GymTrackPro.Mobile` (.NET MAUI Mobile App)
*   **Project References:** Setup reference paths (`GymTrackPro.API` and `GymTrackPro.Mobile` both reference `GymTrackPro.Shared`).
*   **Installed NuGet Packages:**
    *   `GymTrackPro.API`: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.AspNetCore.Authentication.JwtBearer`, and `BCrypt.Net-Next`.
    *   `GymTrackPro.Mobile`: `CommunityToolkit.Mvvm` and `sqlite-net-pcl`.
*   **Build Validation:** Verified solution restore and build with zero errors.

---

## [v2.0.0-rc1.rev1] - 2026-07-02

### Added
*   **GitHub Templates & Workflows:** Created `.github/workflows/build.yml` for C# build verification, pull request templates, and issue templates (bug reports & features).
*   **License File:** Added `LICENSE` (MIT) in the repository root.

### Changed
*   **Agent Guidelines (`docs/01_Agent_Guidelines.md`):** Updated to assign the "Technical Lead" role to the agent, detailing standing review responsibilities and the strict "Never Assume, Always Verify" policy for technology stacks.
*   **Development Roadmap (`docs/02_Development_Roadmap.md`):** Injected **Phase 0: Technology Decisions & Architecture Validation** to lock down databases, ORM, and auth selections before scaffolding code.
*   **System Architecture (`docs/03_Architecture.md`):** Refactored to align with Clean Architecture layers (`Domain`, `Application`, `Infrastructure`, `Shared`, `Client`, `Server`). Removed hard assumptions about ORM and remote database technologies.
*   **Database Specifications (`docs/04_Database.md`):** Adjusted descriptions to keep physical database dialects TBD under Phase 0 evaluation.
*   **Decisions Log (`docs/05_Decisions.md`):** Shifted Database selection, ORM integration, and Authentication providers to **Pending Decisions** under Phase 0. Approved Clean Architecture structural layout and Soft Delete policies.
*   **Project Blueprint (`docs/07_Project_Blueprint.md`):** Updated directory structure definitions to match Clean Architecture layers and classified persistence layers as TBD.

---

## [v2.0.0-rc1.rev2] - 2026-07-02

### Added
*   **Domain Entities & Enums**: Implemented models and enums in `GymTrackPro.Shared` representing users, members, subscriptions, membership plans, pauses, payments, walk-ins, and notifications.
*   **DTO Data Transfer Contracts**: Created transfer structures (Login, Register, Member, Plan, Subscription, Payment, Attendance, Walk-in) in `GymTrackPro.Shared/DTOs`.
*   **Repository & Service Contracts**: Created database and service contracts in `GymTrackPro.Shared/Interfaces`.
*   **Web API Database & Routing**:
    *   Created `GymDbContext` with explicit decimal scales, unique constraint mappings, and relations.
    *   Scaffolded controller endpoints for all sub-modules.
    *   Registered dependencies and set up JWT bearer authentication in `Program.cs`.
*   **Mobile UI & Sync Queue**:
    *   Scaffolded XAML pages and modern viewmodels utilizing C# 11 partial properties.
    *   Configured startup shell routes and flyout items in `AppShell.xaml`.
    *   Scaffolded local offline SQLite database tables, connectivity state listener, and sync queue loop.
*   **Repository Cleanup**: Removed all template artifacts (`WeatherForecastController.cs`, `WeatherForecast.cs`, `MainPage.xaml`, and `MainPage.xaml.cs`).

