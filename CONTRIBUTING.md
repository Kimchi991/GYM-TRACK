# Contributing to GymTrackPro

Welcome to the GymTrackPro project! To ensure our codebase remains clean, maintainable, and stable, all developers should adhere to the following collaboration and development standards.

---

## 🌳 Git Branching Strategy

We follow a structured branching model:

*   **`main` (Production):** Always represents a production-ready, stable state of the application. Code is merged here only after complete integration testing.
*   **`develop` (Integration):** The main sandbox where features are merged. Nightly test builds are produced from this branch.
*   **`feature/*` (Feature Development):** Created for specific modules or sub-tasks (e.g. `feature/authentication`, `feature/member-management`). Merged into `develop`.
*   **`hotfix/*` (Urgent Patches):** Used to resolve critical production bugs on `main`. Merged into both `main` and `develop`.

### Branch Naming Conventions
*   Features: `feature/short-description` (e.g. `feature/user-roles`)
*   Bugfixes: `bugfix/issue-description` (e.g. `bugfix/fix-qr-validation`)

---

## 🔄 Development & PR Workflow

1.  **Sync Local Repository:** Pull latest modifications before starting:
    ```bash
    git checkout develop
    git pull origin develop
    ```
2.  **Create Feature Branch:**
    ```bash
    git checkout -b feature/your-feature-name
    ```
3.  **Implement Changes:** Follow our [Code Style Guide](CODE_STYLE.md) and MVVM architecture.
4.  **Local Testing:** Build and run the project locally. Ensure all unit and integration tests pass.
5.  **Commit Changes:** Write descriptive, imperative commit messages:
    ```bash
    git add .
    git commit -m "Add member registration validation rules"
    ```
6.  **Push and Create Pull Request (PR):** Push your branch to GitHub and open a PR targeting the `develop` branch.
7.  **Review & Merge:** A minimum of one peer review is required before merging. Once approved, merge using squash or merge commits.

---

## 🏁 Definition of Done

Do not submit a PR for a module until:
*   Database tables (SQLite & MySQL) are initialized.
*   Clean MVVM bindings are established.
*   Form input validation is fully functional.
*   Tests are written and passing.
*   Documentation (including `docs/06_Changelog.md`) is updated.
