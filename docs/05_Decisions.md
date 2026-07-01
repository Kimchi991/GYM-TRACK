# Architectural Decisions (ADR)

This document captures the architectural decisions made during the design of GymTrackPro, including context, alternatives considered, and justifications.

---

## ⏳ PENDING DECISION 1: Database Engine Selection (Phase 0)

*   **Status:** Pending Evaluation (Phase 0)
*   **Context:** GymTrackPro requires a central database engine (server-side) and a lightweight local database engine (client-side).
*   **Candidates Under Evaluation:**
    *   *Server Databases:* MySQL, PostgreSQL, Microsoft SQL Server.
    *   *Client Databases:* SQLite, LiteDB.
*   **Action Plan:** Compare memory footings, multi-user concurrency rules, hosting pricing (for server DBs), and compatibility with Xamarin/MAUI environments.

---

## ⏳ PENDING DECISION 2: Data Access Layer & ORM (Phase 0)

*   **Status:** Pending Evaluation (Phase 0)
*   **Context:** The API and client application require data access technologies to execute queries and manage database transactions.
*   **Candidates Under Evaluation:**
    *   *Entity Framework Core:* Heavy but feature-rich, handles migrations, easy relationships, but slower for bulk writes.
    *   *Dapper:* Extremely fast micro-ORM, utilizes raw SQL, requires manual query mappings and migrations.
    *   *ADO.NET (Raw SQL):* Highest performance, maximum control, but generates verbose, hard-to-maintain code.
*   **Action Plan:** Build a simple performance benchmark and inspect readability for a student team.

---

## ⏳ PENDING DECISION 3: Authentication Strategy (Phase 0)

*   **Status:** Pending Evaluation (Phase 0)
*   **Context:** Users need to log in securely, and sessions must support offline verification.
*   **Candidates Under Evaluation:**
    *   *Custom JWT:* Plain Web API generating stateless tokens, client stores tokens and roles securely.
    *   *ASP.NET Core Identity:* Integrated user manager with email recovery, password hashing, and cookie/token support built-in.
    *   *External Identity Providers:* Auth0, Firebase Auth (high dependency risk, online-only).
*   **Action Plan:** Weigh custom implementation complexity against the security and features provided by ASP.NET Core Identity.

---

## 🏛️ APPROVED DECISION 4: Client Architecture

*   **Status:** **APPROVED**
*   **Decision:** Build the Mobile application using the **MVVM (Model-View-ViewModel)** pattern via the `CommunityToolkit.Mvvm` package.
*   **Justification:** Separates user interface (XAML) from business logic, supports native data-binding, and is the standard industry pattern for .NET MAUI development.

---

## 🏛️ APPROVED DECISION 5: Core Solution Separation (Clean Architecture)

*   **Status:** **APPROVED**
*   **Decision:** Scaffold the codebase into distinct layers: Domain, Application, Infrastructure, Shared, Client Host, and Server Host.
*   **Justification:** Prevents business logic pollution, isolates persistence decisions, enables shared components (DTOs, contracts) across client and server, and maintains code quality for a multi-developer team.

---

## 🏛️ APPROVED DECISION 6: Soft Deletes for Transactional Data

*   **Status:** **APPROVED**
*   **Decision:** Enforce Soft Delete / Deactivation flags on Members, Plans, and Payments.
*   **Justification:** Protects database reference integrity, prevents cascade failures, and ensures financial and attendance report logs remain accurate even when members are deactivated.
