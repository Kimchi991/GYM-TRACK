# GymTrackPro — Software Architecture Document

## Architecture Pattern

GymTrackPro uses a **Layered Client-Server Architecture** with a mobile frontend and a centralized REST API backend.

```
┌─────────────────────────────────────────────────────┐
│              GymTrackPro.Mobile (.NET MAUI)          │
│         MVVM Pattern (Views + ViewModels)            │
│  ┌──────────┐  ┌──────────┐  ┌────────────────────┐ │
│  │ Views    │  │ViewModels│  │ Services Layer      │ │
│  │ (XAML)   │◄►│  (C#)    │◄►│ ApiService         │ │
│  └──────────┘  └──────────┘  │ FirebaseAuthService │ │
│                               │ LocalDatabaseService│ │
│                               │ SyncService         │ │
│                               └────────────────────┘ │
└────────────────────────┬────────────────────────────┘
                         │ HTTPS / Firebase JWT Bearer
                         ▼
┌─────────────────────────────────────────────────────┐
│         GymTrackPro.API (ASP.NET Core 8)             │
│   ┌────────────────────────────────────────────────┐ │
│   │ Middleware Pipeline                            │ │
│   │  Rate Limiter → Auth (Firebase JWT) →         │ │
│   │  Authorization → Controllers                  │ │
│   └────────────────────────────────────────────────┘ │
│   ┌───────────────┐  ┌──────────────┐              │ │
│   │  Controllers  │  │   Services   │              │ │
│   │  (API Layer)  │◄►│ (Business    │              │ │
│   └───────────────┘  │  Logic)      │              │ │
│                      └──────┬───────┘              │ │
│   ┌───────────────┐         │                      │ │
│   │ Repositories  │◄────────┘                      │ │
│   └───────┬───────┘                                │ │
│           │ EF Core 8                               │ │
│   ┌───────▼───────┐                                │ │
│   │  GymDbContext │                                 │ │
│   └───────────────┘                                │ │
└────────────────────────┬────────────────────────────┘
                         │
                         ▼
              ┌─────────────────┐
              │  SQL Server DB  │
              │  (Docker/Cloud) │
              └─────────────────┘
```

---

## Project Structure

```
GymTrackPro.APP/
├── src/
│   ├── GymTrackPro.API/           # ASP.NET Core 8 REST API
│   │   ├── Authentication/        # Firebase token validation
│   │   ├── Authorization/         # Policy definitions (Policies.cs)
│   │   ├── Controllers/           # 16 controllers
│   │   ├── Data/                  # GymDbContext + migrations
│   │   ├── Middleware/            # (currently empty — was in develop)
│   │   ├── Migrations/            # EF Core migration history
│   │   ├── Repositories/         # Data access layer
│   │   └── Services/             # 32 service files (business logic)
│   │
│   ├── GymTrackPro.Mobile/        # .NET MAUI cross-platform app
│   │   ├── Converters/            # XAML value converters
│   │   ├── Helpers/               # UI helpers
│   │   ├── Models/                # Mobile-specific models
│   │   ├── Platforms/             # Android/iOS platform code
│   │   ├── Resources/             # Fonts, images, styles
│   │   ├── Services/             # 23 service files (HTTP + local)
│   │   ├── ViewModels/           # 17 ViewModels (MVVM)
│   │   └── Views/                # 16 XAML pages
│   │
│   ├── GymTrackPro.Shared/        # Shared class library
│   │   ├── Constants/
│   │   ├── DTOs/                  # 31 DTO files
│   │   ├── Entities/             # 15 domain entities
│   │   ├── Enums/                # 12 enums
│   │   ├── Events/               # Domain event types
│   │   └── Interfaces/           # Service + repository contracts
│   │
│   ├── GymTrackPro.Tests/         # Backend unit/integration tests
│   └── GymTrackPro.Mobile.Tests/  # Mobile unit tests
│
├── docs/                          # Documentation (this folder)
├── scratch/                       # Integration test PowerShell scripts
└── tools/                         # Development tooling
```

---

## Design Patterns Used

| Pattern | Where | Evidence |
|:--|:--|:--|
| **MVVM** | Mobile | `ViewModels/` + `Views/` separation; `BaseViewModel.cs` |
| **Repository Pattern** | API | `Repositories/` folder; `IBaseRepository<T>` interface |
| **Service Layer** | API | All business logic in `Services/`; controllers are thin |
| **Dependency Injection** | Both | `MauiProgram.cs` (mobile); `Program.cs` (API) |
| **Factory Pattern** | API | `ProfilePictureStorage`, `SystemSettingService` |
| **Observer (Events)** | API | `DomainEventPublisher`, `InMemoryEventPublisher` |
| **Decorator/Pipeline** | API | ASP.NET Core middleware pipeline |
| **Strategy** | API | `GymMembershipPolicy` for subscription state transitions |
| **Unit of Work (implicit)** | API | EF Core `DbContext` as unit of work boundary |
| **Projection** | API | `GymGoerProjectionService` — computes derived read model per member |
| **Background Worker** | API | `SubscriptionExpirationWorker`, `NotificationWorker` as `IHostedService` |
| **Optimistic Concurrency** | API | `RowVersion` byte array on `Attendance`, `AccountInvite`, `MemberProjectionVersion` |

---

## Authentication Architecture

```
Mobile                         Firebase                    API
  │                               │                         │
  │── Email/Password ────────────►│                         │
  │◄── Firebase ID Token ─────────│                         │
  │                               │                         │
  │── Firebase ID Token ─────────────────────────────────►  │
  │                               │   (JWT Bearer header)   │
  │                               │         │               │
  │                               │   Firebase JWT Validator│
  │                               │   verifies signature    │
  │                               │   with Firebase public  │
  │                               │   keys                  │
  │                               │         │               │
  │                               │   Policy check:         │
  │                               │   FirebaseOnboarding    │
  │                               │   BackOffice            │
  │                               │   OwnerOnly             │
  │                               │   GymGoerSelf           │
  │                               │   ActiveAppUser         │
  │◄── API JSON Response ─────────────────────────────────  │
```

**Key points:**
- Firebase handles all credential storage and token issuance — the API never stores or validates passwords
- The API validates Firebase ID Tokens using Firebase's public key endpoint
- The API maps Firebase UID → internal `User` record via `SyncUser` endpoint on first login
- Staff users (Admin, Receptionist) are registered via back-office invite code activation (`/api/v1/auth/activate`)
- Gym Goer members are invited via a generated invite code from the members screen

---

## Authorization Policies

*(Derived from `src/GymTrackPro.API/Authorization/Policies.cs`)*

| Policy Name | Allowed Roles | Description |
|:--|:--|:--|
| `FirebaseOnboarding` | Any verified Firebase user | Used for sync-user and activate endpoints |
| `BackOffice` | Administrator, Receptionist | General staff operations |
| `OwnerOnly` | Administrator only | Delete, refund, reports, settings, correct-checkout, void |
| `GymGoerSelf` | GymGoer only | Member self-service endpoints (`/me/...`) |
| `ActiveAppUser` | Any active synced user | Profile retrieval (`/me`) |

---

## Background Workers

| Worker | Schedule | Purpose |
|:--|:--|:--|
| `SubscriptionExpirationWorker` | Runs every hour (derived from implementation) | Queries all subscriptions with `Status = "Active"` and `EndDate < UtcNow`, marks them as `Expired` |
| `NotificationWorker` | Queue-driven (in-memory) | Dequeues notification tasks from `INotificationQueue` and delivers them |

---

## Local Database (Mobile — SQLite)

The mobile app uses **SQLite via sqlite-net-pcl** for:
- Storing sync queue items for offline operations
- Caching user session data

`LocalDatabaseService` manages schema creation and compatibility migration. `LocalDatabaseCompatibilityException` is thrown when schema version is incompatible, triggering a forced logout and re-sync.

---

## API Versioning

All endpoints are prefixed with `/api/v1/`. Deprecated legacy routes (e.g., `POST /attendance/checkin`) include `Deprecation`, `Sunset`, and `Link` response headers pointing to successor routes.
