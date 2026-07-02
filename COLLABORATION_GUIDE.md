# GymTrackPro Onboarding & Collaboration Guide 🏋️‍♂️

Welcome to the GymTrackPro development team! This guide will help you set up your local environment, understand our architecture, and follow our collaboration standards.

---

## ⚙️ Prerequisites

Before you begin, ensure you have the following installed on your machine:
*   **[.NET 10 SDK](https://dotnet.microsoft.com/download)**
*   **[Visual Studio 2022](https://visualstudio.microsoft.com/)** (v17.10+ recommended) with the following workloads:
    *   ASP.NET and web development
    *   .NET Multi-platform App UI (.NET MAUI) development
*   **[SQL Server Developer/Express Edition](https://www.microsoft.com/en-us/sql-server/sql-server-downloads)**
*   **[PowerShell 7+](https://github.com/PowerShell/PowerShell)** (recommended for running local helper scripts)

---

## 🚀 Local Environment Setup

### 1. Clone the Repository
Clone the repository and check out the `develop` branch (where active integration happens):
```powershell
git clone https://github.com/Kimchi991/GYM-TRACK.git
cd GYM-TRACK
git checkout develop
```

### 2. Configure & Migrate the Database
The API project connects to SQL Server. Follow these steps to initialize it:

1.  Open the solution file `src/GymTrackPro.slnx` in Visual Studio or edit `src/GymTrackPro.API/appsettings.json`.
2.  Locate the connection string named `DefaultConnection` and update it to match your local SQL Server instance (e.g., `Server=(localdb)\\mssqllocaldb;Database=GymTrackProDb;Trusted_Connection=True;MultipleActiveResultSets=true`).
3.  Open the **Package Manager Console** in Visual Studio (or use your terminal) and apply Entity Framework migrations:
    ```powershell
    dotnet ef database update --project src/GymTrackPro.API
    ```

### 3. Seed Default System State
To create the default administrative credentials and initial gym settings, run the seeding script:
```powershell
./scratch/seed_admin.ps1
```
*   **Default Username**: `admin`
*   **Default Password**: `SecurePassword@123`

---

## 💻 Running the Application

### Running the API Backend
From Visual Studio:
1.  Set `GymTrackPro.API` as the Startup Project.
2.  Press **F5** (Debug) or **Ctrl+F5** (Without Debugging).
3.  The API will start at `http://localhost:5221/` and load the Swagger interface.

### Running the Mobile Client (.NET MAUI)
From Visual Studio:
1.  Set `GymTrackPro.Mobile` as the Startup Project.
2.  In the debug dropdown menu, select **Windows Machine** (or select a configured Android Emulator / iOS Simulator).
3.  Press **F5** to compile and run.
4.  Use the `admin` credentials to log in!

---

## 🏛️ Project Architecture Overview

We use a simplified three-project layout under the `src/` directory:

```directory
src/
├── GymTrackPro.Shared/    # Contains Enums, Entities, DTOs, and Service Interfaces
├── GymTrackPro.API/       # ASP.NET Core Web API controllers, repositories, & DB contexts
└── GymTrackPro.Mobile/    # .NET MAUI Client views, view models, and SQLite offline DB
```

### 🔄 Offline-First Synchronization
*   The Mobile client uses an embedded **SQLite** database for offline capability.
*   **`LocalDatabaseService.cs`** manages local tables.
*   **`SyncService.cs`** runs background syncing tasks that queue local edits (marked with `SyncStatus.Pending_*`) and pushes them to the central SQL Server via **`ApiService.cs`** once internet connectivity is detected.

---

## 🌿 Git & Collaboration Workflow

To maintain a clean and stable codebase, please adhere to our branching and commit standards:

### 1. Branching Strategy
*   `main`: Protected branch. Contains stable release-ready code.
*   `develop`: Integration branch. Merge your completed feature branches here.
*   `feature/<username>/<feature-name>`: Active development branches (e.g., `feature/john/member-photo-upload`).

### 2. Committing Changes
Use Conventional Commits for clear history:
*   `feat: <description>` – A new feature (e.g., `feat: add check-in confirmation view`).
*   `fix: <description>` – A bug fix (e.g., `fix: resolve enum deserialization issue`).
*   `chore: <description>` – Code maintenance, dependency updates, configuration changes.

### 3. Submitting Pull Requests
1.  Before submitting a PR, make sure the solution compiles with **0 errors**.
2.  Submit a Pull Request from your feature branch to `develop`.
3.  Fill out the [PR Template](.github/pull_request_template.md).
4.  Ensure your code conforms to the [Coding Standards](docs/08_Coding_Standards.md) and passes the [Definition of Done](docs/09_Definition_of_Done.md).

---

## 🤖 AI Agent Collaborator Guidelines

If you are an AI coding assistant collaborating on this repository, please adhere strictly to these engineering and styling standards:

### 1. Visual Theme & Aesthetics
*   **Color Palette**: The application uses a unified premium "Slate Dark" theme (Background: `#0F172A`, Card Background: `#1E293B`, Borders: `#334155`). Do not introduce generic system colors (e.g., standard red, blue, green).
*   **Card Design**: Use `Border` with `StrokeShape="RoundRectangle 12"` and `Stroke="{StaticResource BorderBrush}"` for consistent card styling.
*   **Gradients**: Use subtle gradients (e.g., `LinearGradientBrush` from slate-800 to slate-900) for key metrics and hero sections.

### 2. Vector Iconography
*   **Centralized Vector Library**: Emojis are deprecated/banned for UI buttons and icons.
*   **PathGeometries**: Refer to `Resources/Styles/Colors.xaml` where centralized `<PathGeometry>` keys are defined (e.g., `IconUser`, `IconCard`, `IconSearch`, `IconRefresh`, `IconTrash`, `IconArrowBack`, `IconLock`, `IconSettings`, `IconCheckCircle`, `IconErrorCircle`).
*   **Usage**: Render them in XAML using `Path` elements:
    ```xml
    <Path Data="{StaticResource IconSearch}" Fill="{StaticResource Primary}" Aspect="Uniform" WidthRequest="18" HeightRequest="18" />
    ```

### 3. Compilation & AOT Compliance
*   **Concrete Geometries**: Always declare geometries as `<PathGeometry Figures="..." />` instead of abstract `<Geometry>` to prevent compiler instantiation errors.
*   **Binding Contexts**: Specify `x:DataType` on all `DataTemplate` and page elements to ensure compile-time type-safety and AOT compatibility.
*   **Navigation & Confirmed Actions**: Follow MVVM rules. Confine UI additions to XAML, using VM commands for screen transitions.

