# Architectural Decisions (ADR)

This document captures the architectural decisions made during the development of GymTrackPro.

---

## 📐 ADR Template Rule

Every major architectural or technology change must be documented using the following template:

```
## ADR [Number]: [Title]
*   **Date:** [YYYY-MM-DD]
*   **Problem:** [What problem are we trying to solve?]
*   **Options Considered:**
    *   *Option A:* [Description, pros, cons]
    *   *Option B:* [Description, pros, cons]
*   **Decision:** [What choice was made and why?]
*   **Reason:** [Key factors leading to the decision]
*   **Consequences:** [What are the side effects, trade-offs, or requirements of this decision?]
```

---

## 🏛️ ADR 1: Solution Project Layout (Compromise Clean Architecture)

*   **Date:** 2026-07-02
*   **Problem:** A full Clean Architecture setup splits the project into 6 distinct C# projects, creating large boilerplate overhead, reference chains, and refactoring frictions for a three-student development team.
*   **Options Considered:**
    *   *Option A (Full Clean Architecture):* Separate into 6 projects (Domain, Application, Infrastructure, Shared, Server Host, Client Host).
    *   *Option B (Layered with Monolithic API/Mobile):* Two projects (API, Mobile). High duplication risk for DTOs and contracts.
    *   *Option C (Simplified Clean Architecture):* Exactly 3 projects: `GymTrackPro.Shared` (shared models, DTOs, enums, validators), `GymTrackPro.API` (backend logic, dbContext), and `GymTrackPro.Mobile` (Presentation, ViewModels, SQLite repositories).
*   **Decision:** **Option C** was chosen.
*   **Reason:** This balances separation of concerns (keeping core models/contracts shared and decoupled from hosts) while keeping project referencing overhead to a minimum.
*   **Consequences:** Shared code (DTOs, common interfaces) compiles into a single dll reference, which both Mobile and Web API include. All UI remains in Mobile, and DB contexts remain in the API.

---

## 🏛️ ADR 2: Tech Stack Validation (Data Persistence & ORM)

*   **Date:** 2026-07-02
*   **Problem:** Choose the server-side database, client-side database, and ORM layer.
*   **Options Considered:**
    *   *Option A (MySQL + SQLite + Dapper):* High raw performance, but requires writing manual SQL and running manual table creation migrations.
    *   *Option B (SQL Server + SQLite + EF Core):* Perfect tooling integration with .NET, faster development due to Entity Framework migration runner, easy offline SQLite mapping.
*   **Decision:** **Option B** (SQL Server hosted on MonsterASP, SQLite local DB, EF Core ORM).
*   **Reason:** This is the most practical stack for a student team. The development speed of EF Core migrations offsets any performance overhead compared to Dapper.
*   **Consequences:** The Web API project will run EF Core with SQL Server. The Mobile project will use a SQLite-net wrapper or EF Core SQLite provider for local storage.

---

## 🏛️ ADR 3: Authentication and Identity Provider

*   **Date:** 2026-07-02
*   **Problem:** Secure system access and support offline logins without adding major identity management integrations.
*   **Options Considered:**
    *   *Option A (Firebase Authentication):* Strong cloud provider, but adds a second user store separate from SQL Server and requires constant internet access for verification (breaks offline-first).
    *   *Option B (ASP.NET Core Identity):* Standard, but introduces high configuration complexity and verbose database schemas.
    *   *Option C (Custom JWT + BCrypt):* API manages username/password records directly in SQL Server, hashes passwords with BCrypt, and issues signed JWTs.
*   **Decision:** **Option C** (Custom JWT + BCrypt).
*   **Reason:** Simplest, cleanest path to manage Custom Roles (Admin, Receptionist) and offline user token storage on mobile.
*   **Consequences:** The development team must implement user password verification and secure token storage (SecureStorage) on the client.

---

## 🏛️ ADR 4: External Services (Firebase)

*   **Date:** 2026-07-02
*   **Problem:** Handle notifications, email validation, and password recovery emails without bloating the core backend.
*   **Options Considered:**
    *   *Option A (Firebase for Auth & Data):* Integrate Firebase Auth/Firestore. High dependency, breaks offline integrity.
    *   *Option B (Supporting Firebase Services):* Keep SQL Server as the single source of truth for auth/business data, and only call Firebase Cloud Messaging (FCM) and email triggers when necessary.
*   **Decision:** **Option B** (Supporting services only).
*   **Reason:** Keeps identity management locally controlled, preserving offline operability.
*   **Consequences:** The Web API must integrate FCM sending client SDKs, and mobile clients must register devices with FCM.

---

## 🏛️ ADR 5: Multi-Tenant Strategy
*   **Date:** 2026-07-10
*   **Problem:** Transition GymTrackPro to support multiple gym business tenants in a single deployment while ensuring secure data separation.
*   **Options Considered:**
    *   *Option A (Database-per-tenant):* High isolation, but high cost, complex deployment, and difficult cross-tenant platform queries.
    *   *Option B (Schema-per-tenant):* Moderate isolation, but high complexity in migrations and schema updates.
    *   *Option C (Shared Database, Shared Schema with GymID):* Low cost, simple migrations, straightforward platform-level analytics, easily supports future multi-branch ownership.
*   **Decision:** **Option C** (Shared Database, Shared Schema with GymID).
*   **Reason:** Fits the constraint of low operational costs and simplicity for deployment, while easily accommodating future multi-branch capabilities.
*   **Consequences:** Developers must ensure tenant boundaries are securely enforced at the database query level to prevent accidental cross-tenant leaks.

---

## 🏛️ ADR 6: Soft Delete Policy
*   **Date:** 2026-07-10
*   **Problem:** Prevent accidental data loss of members, payments, plans, and other operational data while maintaining a clean user experience.
*   **Options Considered:**
    *   *Option A (Physical Delete):* Simple SQL `DELETE` commands, but results in permanent data loss and breaks referential historical statistics.
    *   *Option B (Soft Delete via IsDeleted flags):* Records remain in the database but are marked inactive with deletion metadata (`DeletedAt`, `DeletedBy`).
*   **Decision:** **Option B** (Soft Delete Policy).
*   **Reason:** Preserves historical audit trails and analytical reporting integrity (e.g. keeping payments and membership analytics accurate even if a member is removed).
*   **Consequences:** Operational queries must filter out soft-deleted records, which will be automated using EF Core global filters.

---

## 🏛️ ADR 7: Tenant Isolation Enforcement
*   **Date:** 2026-07-10
*   **Problem:** Ensure that gym tenants cannot read or write data belonging to another gym, preventing leakage.
*   **Options Considered:**
    *   *Option A (Manual service-level filters):* Add `Where(x => x.GymID == currentGymId)` manually to every database query. High risk of human error or omissions.
    *   *Option B (EF Core Global Query Filters):* Define dynamic filters inside `OnModelCreating` to automatically restrict all queries to the tenant context unless explicitly bypassed.
*   **Decision:** **Option B** (EF Core Global Query Filters).
*   **Reason:** Provides a centralized, compile-time safety boundary. Most application code (services/controllers) remains completely unaware of tenant filtering, reducing boilerplate and preventing bugs.
*   **Consequences:** Administrative operations (e.g., Platform Admin queries) must explicitly call `.IgnoreQueryFilters()` to view multi-tenant records.

---

## 🏛️ ADR 8: Settings Separation
*   **Date:** 2026-07-10
*   **Problem:** Support global platform-level system configurations while allowing individual gyms to customize local settings (e.g. QR formats, receipt prefixes).
*   **Options Considered:**
    *   *Option A (Unified table with GymID nullable):* A single table where global settings have `GymID = null` and overrides have `GymID = tenant_id`. Can lead to index collision on composite keys or complex lookup queries.
    *   *Option B (PlatformSetting and GymSetting tables):* Two distinct database models. `PlatformSetting` is platform-scoped (read-only for tenants). `GymSetting` is tenant-scoped and uses a composite key of `(GymID, SettingKey)`.
*   **Decision:** **Option B** (PlatformSetting and GymSetting tables).
*   **Reason:** Clearer database layout, cleaner validation rules, and no risk of key collisions between global platform settings and local tenant overrides.
*   **Consequences:** Services must explicitly fetch from the corresponding table based on whether the config parameter is platform-level or gym-level.
