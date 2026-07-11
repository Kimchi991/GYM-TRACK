# Implementation Plan - GymTrackPro 2.0 Phase 1 (SaaS Foundation)

This implementation plan details the evolution of GymTrackPro into a multi-tenant SaaS application, updated to address the architectural feedback on entity isolation boundaries, subscription contracts, settings separation, and relationships.

---

## 🏛️ Tenant Boundary Rule

To prevent accidental cross-tenant access and maintain absolute boundary clarity as the system grows, every entity belongs to one of three scopes:

| Scope | Description | Entities (Phase 1) |
| :--- | :--- | :--- |
| **Platform-scoped** | Shared across the entire SaaS instance, managed by Platform Admins. | `Gym`, `SubscriptionPlan`, `GymSubscription`, `PlatformSetting` |
| **Gym-scoped (Tenant)** | Belongs to exactly one Gym tenant. Enforced via non-nullable `GymID` and global query filters. | `Member`, `Attendance`, `MembershipPlan`, `Subscription`, `MembershipPause`, `Payment`, `Notification`, `WalkInVisitor`, `GymSetting`, `GymInvitation` |
| **User-scoped** | Belongs to a single authenticated user account. | `User`, `AuditLog` |

---

## ❗ Architectural Updates Integrated

1.  **Gym/Subscription Separation**: Split subscription contract properties out of the core `Gym` entity into `GymSubscription` and `SubscriptionPlan` entities, controlled by a new `SubscriptionStatus` enum.
2.  **Gym Profile Expansion**: Added profile metadata fields (operating hours, permit details, social links, logo/cover photo URLs, etc.) directly into `Gym` to reserve capacity for public profiles.
3.  **User ↔ Member Mapping**: Linked the existing `Member` entity to the `User` table using a nullable `UserID` foreign key. This allows receptionists to register offline members while enabling members to bind their authenticated accounts to their gym profile.
4.  **Settings Separation**: Replaced generic `SystemSettings` with `PlatformSetting` (global, key-indexed) and `GymSetting` (tenant-scoped, composite key of `(GymID, SettingKey)`).
5.  **Gym Invitation Audit Trail**: Introduced a formal `GymInvitation` table to track, audit, and accept staff registration tokens.
6.  **Soft Delete Support**: Ensured that all soft-deleted entities track `IsDeleted`, `DeletedAt`, and `DeletedBy` fields.
7.  **Future Module Folder Stubs**: Added structural directory stubs for Phase 2–4 modules (WorkoutPrograms, Challenges, Rewards, Leaderboards, Achievements, Events) in both API and Mobile layers.

---

## 🔄 Revised Execution Sequence

We will implement Phase 1 sequentially to ensure foundational infrastructures are stable before layering onboarding flows:

```
1. Tenant Infrastructure ──> 2. Role System ──> 3. Subscription System ──> 4. Gym Onboarding ──> 5. Platform Admin
```

---

## Proposed Changes

### 🏗️ 1. Shared Contracts & Core Enums (`GymTrackPro.Shared`)

#### [NEW] [SubscriptionStatus.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Enums/SubscriptionStatus.cs)
- Enum defining: `Trial`, `Active`, `GracePeriod`, `Suspended`, `Expired`, `Cancelled`.

#### [MODIFY] [UserRole.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Enums/UserRole.cs)
- Expand `UserRole` to support: `PlatformAdmin`, `GymOwner`, `Manager`, `Receptionist`, `Trainer`, `FinanceStaff`, and `GymGoer`.

#### [NEW] [Gym.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/Gym.cs)
- Define the gym tenant metadata, including business credentials, operating hours, amenities, social handles, and branding URLs. Supports soft deletion (`IsDeleted`, `DeletedAt`, `DeletedBy`).

#### [NEW] [SubscriptionPlan.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/SubscriptionPlan.cs)
- Platform-scoped subscription models: `PlanID`, `Name`, `Price`, `MaxMembers`, `Description`, `BillingCycleMonths`.

#### [NEW] [GymSubscription.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/GymSubscription.cs)
- Platform-scoped gym contract linking: `GymID`, `PlanID`, `SubscriptionStatus`, `StartedAt`, `ExpiresAt`, `TrialEndsAt`.

#### [NEW] [PlatformSetting.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/PlatformSetting.cs)
- Platform-level SaaS configuration properties.

#### [NEW] [GymSetting.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/GymSetting.cs)
- Tenant-level overrides with a composite key of `(GymID, SettingKey)`.

#### [NEW] [GymInvitation.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/GymInvitation.cs)
- Tenant invitation manager: `GymID`, `Role`, `Email`, `Token`, `ExpiresAt`, `AcceptedAt`, `CreatedBy`.

#### [MODIFY] Shared Entities
Refactor existing entities to enforce tenant-isolation scopes and user relationships:
- [User.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/User.cs): Add `int? GymID` (nullable).
- [AuditLog.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/AuditLog.cs): Add `int? GymID` (nullable).
- [Member.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/Member.cs): Add `int GymID` (non-nullable), `int? UserID` (FK to User), `IsDeleted`, `DeletedAt`, `DeletedBy`.
- [Attendance.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/Attendance.cs): Add `int GymID` (non-nullable).
- [MembershipPlan.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/MembershipPlan.cs): Add `int GymID` (non-nullable), `IsDeleted`, `DeletedAt`, `DeletedBy`.
- [Subscription.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/Subscription.cs): Add `int GymID` (non-nullable).
- [MembershipPause.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/MembershipPause.cs): Add `int GymID` (non-nullable).
- [Payment.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/Payment.cs): Add `int GymID` (non-nullable), `IsDeleted`, `DeletedAt`, `DeletedBy`.
- [WalkInVisitor.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/WalkInVisitor.cs): Add `int GymID` (non-nullable).
- [Notification.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/Notification.cs): Add `int GymID` (non-nullable).
- [DELETE] [SystemSetting.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.Shared/Entities/SystemSetting.cs)

---

### 🌐 2. Backend API Infrastructure (`GymTrackPro.API`)

#### [MODIFY] [GymDbContext.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.API/Data/GymDbContext.cs)
- Register `DbSets` for `Gyms`, `GymSubscriptions`, `SubscriptionPlans`, `PlatformSettings`, `GymSettings`, and `GymInvitations`.
- Define composite key mapping for `GymSetting`.
- Inject `ITenantProvider` to apply dynamic global query filters to all tenant-scoped DB sets based on the resolved `GymID` and `IsPlatformAdmin` flags.

#### [NEW] [TenantResolverMiddleware.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.API/Middleware/TenantResolverMiddleware.cs)
- Intercepts requests post-Authentication, extracts verified user details from `GymDbContext` (bypassing query filters for authorization purposes), and registers the client's `GymID` and `Role` inside scoped `TenantState`.

#### [NEW] [TenantProvider.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.API/Services/TenantProvider.cs)
- Reads from scoped `TenantState` to dynamically provide the `GymID` context to EF Core and internal services.

#### [NEW] [OnboardingController.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.API/Controllers/OnboardingController.cs)
- Expose endpoints to register the gym owner user account, record business information, select a subscription plan, and initialize the gym tenant.

#### [NEW] [PlatformAdminController.cs](file:///d:/DEV/DEV/IT123P.GymTracker.APP/src/GymTrackPro.API/Controllers/PlatformAdminController.cs)
- Exposes administrative capabilities (listing all registered tenants, manually updating subscription states, or suspending tenants).

---

### 📂 3. Future Module Folder Stubs
We will introduce folder stubs containing `.gitkeep` files in `src/GymTrackPro.API/Services/` and `src/GymTrackPro.API/Controllers/` to set the architectural layout for later phases:
- `WorkoutPrograms/`
- `Challenges/`
- `Rewards/`
- `Leaderboards/`
- `Achievements/`
- `Events/`

---

## Verification Plan

### Automated Tests
- Run EF Core migration commands:
  `dotnet ef migrations add EvolveToSaaS --project src/GymTrackPro.API`
- Update `appsettings.Development.json` to route local development and testing to the Docker SQL Server (`localhost,14333`).
- Run the PowerShell integration testing suite:
  `scratch/run_all_tests.ps1`
- Confirm that tenant queries for Gym A return zero results for Gym B's plans, members, and settings.

### Manual Verification
- Simulate registering a new client owner: verify that they can create a gym, select a pricing tier, and view their dashboard under the restricted `GymOwner` role.
