# System Architecture Reference

GymTrackPro uses a multi-layered, client-server system architecture designed to support a robust **Offline-First** capability.

For a detailed look, please refer to the main architectural document:
👉 **[Detailed Architecture Log](docs/03_Architecture.md)**

---

## 🏛️ Layered Overview

The codebase is split into distinct functional zones to keep features decoupled and easy to test:

```
               ┌────────────────────────┐
               │    Presentation Layer  │  <-- .NET MAUI Views (XAML Pages)
               └───────────┬────────────┘
                           │
               ┌───────────▼────────────┐
               │    ViewModel Layer     │  <-- ViewModels (CommunityToolkit.Mvvm)
               └───────────┬────────────┘
                           │
               ┌───────────▼────────────┐
               │     Service Layer      │  <-- Business Logic, Validation, Sync
               └───────────┬────────────┘
                           │
               ┌───────────▼────────────┐
               │    Repository Layer    │  <-- DB Access Isolation (SQL / EF Core)
               └────────────────────────┘
```

## 🔌 API & Databases

*   **REST Web API:** An ASP.NET Core service handles the business rules and guards database writes.
*   **MySQL Server DB:** Serves as the central repository and data backup.
*   **SQLite Local DB:** Runs embedded in the mobile application to support offline modifications.
*   **Sync Engine:** Resolves network drops automatically using a synchronization queue and a "newest update wins" logic.
