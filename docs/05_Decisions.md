# Architectural Decisions (ADR)

This document captures the architectural decisions made during the design of GymTrackPro, including context, alternatives considered, and justifications.

---

## 🏛️ ADR 1: Technology Stack Selection

*   **Decision:** Use **.NET MAUI** for the Mobile application, **ASP.NET Core Web API** for the backend, **MySQL** for the central database, and **SQLite** for the local client database.
*   **Context:** GymTrackPro is a cross-platform application that runs on front-desk computers (Windows/macOS) and mobile devices (Android/iOS).
*   **Alternatives Considered:**
    *   *Flutter + Node.js + MongoDB:* Higher learning curve for a team with C# knowledge.
    *   *React Native + Express + PostgreSQL:* JavaScript ecosystems offer less compile-time safety compared to C#.
*   **Justification:**
    *   .NET MAUI allows writing a single C# codebase for Windows, Android, macOS, and iOS, integrating natively with desktop operating systems.
    *   ASP.NET Core provides high-performance Web APIs, dependency injection, and native ORM integrations (Entity Framework Core).
    *   SQLite is lightweight, requires no installation, and is standard for mobile device local storage.
    *   C# throughout the entire stack (Mobile & API) simplifies team learning and enables code reuse (e.g., shared DTOs/Models).

---

## 🔄 ADR 2: Offline-First Synchronization Strategy

*   **Decision:** Build a custom sync-queue in SQLite and resolve database conflicts using a **"Newest Update Wins"** policy based on `LastModified` timestamps.
*   **Context:** Gyms frequently experience flaky internet connections. Receptionists must be able to register members, check in attendees, and record payments offline.
*   **Alternatives Considered:**
    *   *Real-time Database Sync (e.g. Firebase):* Incompatible with custom relational databases (MySQL) and requires expensive cloud integrations.
    *   *Bidirectional Syncing Engine (e.g. CouchDB):* Over-engineered for a student capstone project.
*   **Justification:**
    *   Writing to SQLite first ensures the UI is responsive and operations never block.
    *   The sync queue keeps a clean list of changes to upload when network connectivity is detected.
    *   "Newest Update Wins" is a simple, deterministic resolution strategy that keeps the synchronizer code easy to write, read, and test.

---

## 🗑️ ADR 3: Data Deletion Rules (Soft Deactivations)

*   **Decision:** Enforce **Soft Delete** or **Deactivation** for core system entities (Members, Payments, Subscriptions, Users).
*   **Context:** Accidental deletions could lead to loss of attendance history, auditing records, and payment receipts.
*   **Alternatives Considered:**
    *   *Hard Deletes:* Deleting records directly via `DELETE FROM ...`. High risk of database reference crashes (FK violations).
*   **Justification:**
    *   Setting `Status = 'Inactive'` or `IsActive = false` preserves relational integrity.
    *   Ensures historical revenue and attendance reports remain accurate even if a member is no longer active in the gym.

---

## 🔑 ADR 4: Security and Session Management

*   **Decision:** Store passwords using **BCrypt** hashing on the server, issue **JSON Web Tokens (JWT)** for session validation, and store tokens securely on the device.
*   **Context:** GymTrackPro stores sensitive member details and financial records.
*   **Alternatives Considered:**
    *   *Basic Auth:* Insecure as credentials must be sent with every single request.
    *   *Session Cookies:* Harder to configure and manage securely across cross-platform native apps compared to JWT.
*   **Justification:**
    *   JWTs are standard, stateless, and contain role attributes (RBAC) to enforce permissions on both the client (UI rendering) and server (endpoint access).
    *   Secure storage on the native device (via MAUI SecureStorage) prevents cross-site token theft.
