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
