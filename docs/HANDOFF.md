# GymTrackPro — AI Handoff Document
> **Last updated:** 2026-07-11  
> **Branch:** `develop`  
> **Purpose:** Context handoff for the next AI session or developer continuing this work.

---

## 📌 What This Project Is

**GymTrackPro** is evolving from a single-gym management app into a **multi-tenant SaaS platform**.

The existing application (member management, attendance, payments, subscriptions, notifications, analytics) is fully functional. The current work is implementing the **SaaS foundation (Phase 1)** on top of it — without breaking existing functionality.

---

## ✅ What Has Been Completed

### Phase 1 — Multi-Tenant SaaS Foundation (IMPLEMENTED)

All of the following have been built and are in the codebase on `develop`:

#### Shared Layer (`GymTrackPro.Shared`)
- `UserRole.cs` — expanded to: `PlatformAdmin`, `GymOwner`, `Manager`, `Receptionist`, `Trainer`, `FinanceStaff`, `GymGoer`
- `SubscriptionStatus.cs` — new enum: `Trial`, `Active`, `GracePeriod`, `Suspended`, `Expired`, `Cancelled`
- `Gym.cs` — gym tenant entity (metadata, branding, operating hours, soft delete)
- `SubscriptionPlan.cs` — platform-scoped SaaS pricing tiers
- `GymSubscription.cs` — contract linking a Gym to a SubscriptionPlan
- `PlatformSetting.cs` — global platform-level configuration
- `GymSetting.cs` — per-tenant configuration overrides (composite key: `GymID + SettingKey`)
- `GymInvitation.cs` — staff invite audit trail
- `TenantState.cs` — scoped service holding `GymID` and `UserRole` per HTTP request
- `ITenantProvider.cs` — interface for injecting tenant context
- `SaaSOnboardingDtos.cs` — DTOs for the onboarding flow
- **Deleted:** `SystemSetting.cs` (replaced by `PlatformSetting` + `GymSetting`)
- **Modified:** `User`, `Member`, `Attendance`, `MembershipPlan`, `Subscription`, `MembershipPause`, `Payment`, `WalkInVisitor`, `Notification`, `AuditLog` — all now have `GymID` field for tenant isolation

#### API Layer (`GymTrackPro.API`)
- `GymDbContext.cs` — updated with new `DbSet`s, composite key for `GymSetting`, and **global query filters** scoped by `GymID` (via `ITenantProvider`)
- `TenantResolverMiddleware.cs` — intercepts authenticated requests, resolves user's `GymID` + `Role` from DB, injects into `TenantState`, and adds correct role claims to `ClaimsPrincipal`
- `TenantProvider.cs` — reads from `TenantState` to supply `GymID` to EF Core filters
- `OnboardingController.cs` — gym owner self-registration, gym profile creation, subscription plan selection
- `PlatformAdminController.cs` — platform admin operations (list tenants, manage subscriptions, suspend gyms)
- `Program.cs` — middleware pipeline updated: `UseAuthentication → TenantResolverMiddleware → UseAuthorization`
- EF Core migration: `20260710153518_EvolveToSaaS.cs`
- Future module stubs: `WorkoutPrograms/`, `Challenges/`, `Rewards/`, `Leaderboards/`, `Achievements/`, `Events/`

#### Default Seed Data (in `GymDbContext.OnModelCreating`)
- **Default Gym** (ID=1): "Default Gym" — used for local development and integration tests
- **Default SubscriptionPlans**: Standard (₱99/mo) and Premium (₱199/mo)
- **Default GymSubscription**: Gym ID=1 linked to Standard plan, active for 10 years
- **Default GymSettings** for Gym ID=1 (10 settings: GymName, QRPrefix, Currency, Timezone, etc.)

---

## ⚠️ What Is In Progress / Remaining

### Integration Test Stabilization

The `scratch/` folder contains PowerShell E2E integration test scripts. They were written to validate the SaaS implementation. Most are passing; some still have known bugs:

| Test Suite | File | Status | Known Issue |
|---|---|---|---|
| `auth` | `auth_integration_test.ps1` | ✅ 8/8 | None |
| `attendance` | `attendance_integration_test.ps1` | ✅ Stable | None |
| `plans` | `plans_integration_test.ps1` | ✅ Stable | None |
| `notifications` | `notifications_integration_test.ps1` | ✅ Stable | None |
| `members` | `members_integration_test.ps1` | ⚠️ 14/15 | See below |
| `payments` | `payments_integration_test.ps1` | ⚠️ Fixed | RBAC case fixed (see below) |
| `settings` | `settings_integration_test.ps1` | ⚠️ Fixed | Date range fixed |
| `ops_analytics` | `ops_analytics_integration_test.ps1` | ⚠️ Fixed | All date ranges fixed |

#### Members Test — Remaining Bug

**Failing test:** `Soft Delete Member - Allow Admin`  
**Error:** `404 Not Found`

**Root cause identified (not yet fixed):**  
In `members_integration_test.ps1` line 89:
```powershell
$updateRecep = "SET QUOTED_IDENTIFIER ON; UPDATE Users SET EmailVerified=1, Role=1 WHERE Username='recep_user';"
```
`Role=1` sets `recep_user` to **GymOwner** (not Receptionist which is `Role=3`).  
GymOwner gets the `"Administrator"` claim via `TenantResolverMiddleware`, which means the receptionist's DELETE request **succeeds** (200 OK), soft-deleting Charlie.  
Then when the admin tries to delete already-deleted Charlie → 404.

**Fix needed (one line):**  
Change `Role=1` to `Role=3` on line 89 of `scratch/members_integration_test.ps1`.

#### Payments Test — Fixed (verify)
Case 12 `RBAC: Block Receptionist from Refund` was trying to refund an already-refunded payment.  
Fixed by creating a fresh subscription + payment specifically for the RBAC test.

#### Settings & Ops Analytics Tests — Fixed (verify)
All date ranges were hardcoded to `2026-07-01 to 2026-07-03`.  
Fixed by replacing with dynamic `$startDate`/`$endDate` variables (today-1 to today+1).

---

## 🏗️ Architecture Overview

```
TenantResolverMiddleware
  └── Reads JWT email claim
  └── Queries Users table (IgnoreQueryFilters) to get GymID + Role
  └── Sets TenantState { GymID, UserRole }
  └── Adds ClaimsPrincipal role claims (including "Administrator" for PlatformAdmin/GymOwner)

GymDbContext
  └── CurrentGymID = TenantProvider.GetTenantId()     ← from TenantState
  └── IsPlatformAdmin = TenantProvider.IsPlatformAdmin() ← true if UserRole == "PlatformAdmin"
  └── Global Query Filters applied to ALL tenant-scoped DbSets

TenantState (Scoped per request)
  └── GymID: int?
  └── UserRole: string?
  └── IsPlatformAdmin: bool (computed → UserRole == "PlatformAdmin")
```

### Key Gotcha: `IsPlatformAdmin` bypass is NOT applied to `Member`, `MembershipPlan`, `Subscription`, etc.
Only `Gym`, `User`, and `AuditLog` global filters use `IsPlatformAdmin ||` bypass.
This is intentional: PlatformAdmins do not directly interact with gym-level records.

### Key Gotcha: `ApplyTenantId()` defaults to GymID=1
When a new entity is saved and has no GymID set (anonymous/unscoped request), `GymID` defaults to `1` (the default gym). This means all locally registered users and members in tests end up in Gym 1.

---

## 🧪 How to Run Tests

### Prerequisites
- Docker container `mssql_container_gym` running SQL Server on port `1433`
- Database: `GymTrackProDB`
- API project compiles: `dotnet build src/GymTrackPro.API`

### Run all test suites
```powershell
# From project root:
powershell -ExecutionPolicy Bypass -File scratch/run_all_tests.ps1
```

### Run individual suite
```powershell
powershell -ExecutionPolicy Bypass -File scratch/members_integration_test.ps1
```

### DB connection (for manual queries)
```bash
docker exec -t mssql_container_gym /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P YourStrongPass@123 -C -d GymTrackProDB
```

---

## 📋 Implementation Plan (Reference)

See [docs/implementation_plan_phase1.md](./implementation_plan_phase1.md) — or the Antigravity artifact for full detail.

The plan is complete and approved. Phase 1 is fully implemented. No items were removed.

---

## 🔜 Next Steps (for the next AI/developer)

1. **Fix the members test** — change `Role=1` to `Role=3` in `scratch/members_integration_test.ps1` line 89
2. **Run `run_all_tests.ps1`** — verify all suites pass clean
3. **Verify `NotificationService.cs`** — confirm `IgnoreQueryFilters()` fix for background workers is working in notifications tests
4. **Phase 2 planning** — WorkoutPrograms, Challenges, Leaderboards modules (stubs already in place)

---

## 📁 Key File Locations

| File | Purpose |
|---|---|
| `scratch/run_all_tests.ps1` | Master test runner — runs all suites sequentially |
| `src/GymTrackPro.API/Middleware/TenantResolverMiddleware.cs` | Core tenant resolution logic |
| `src/GymTrackPro.API/Data/GymDbContext.cs` | EF Core context with global query filters |
| `src/GymTrackPro.API/Services/TenantProvider.cs` | Supplies GymID to EF Core |
| `src/GymTrackPro.Shared/Entities/TenantState.cs` | Scoped tenant context per request |
| `src/GymTrackPro.API/Services/NotificationService.cs` | Has `IgnoreQueryFilters()` fix for background workers |
| `src/GymTrackPro.API/Controllers/OnboardingController.cs` | Gym self-registration flow |
| `src/GymTrackPro.API/Controllers/PlatformAdminController.cs` | Platform admin operations |
| `docs/05_Decisions.md` | Architecture decision records |
