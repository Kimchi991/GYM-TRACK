# Definition of Done

This document establishes the mandatory quality and completion checklist that every module in GymTrackPro must satisfy before it can be merged into the `develop` integration branch.

---

## 🏁 Module Completion Checklist

A development task or module is declared **Complete** only when all of the following requirements are met:

### 1. 🎨 User Interface (UI)
- [ ] XAML pages and elements match the wireframes/designs.
- [ ] Responsive layouts verified on all target viewports (mobile/desktop).
- [ ] Status indicators (Syncing, Synced, Failed) are integrated into relevant list and form views.

### 2. 🛡️ Input Validation
- [ ] Client-side validation prevents invalid forms from being submitted (checks empty, lengths, formats).
- [ ] Server-side API model validation mirrors client constraints and returns structured validation messages (HTTP 400 Bad Request).
- [ ] Sensitive inputs (e.g. phone numbers, email, QR codes) are validated for database uniqueness before writes.

### 3. 💼 Business Rules
- [ ] All business rules defined in Chapter 12 of the Project Specification are enforced (e.g., active membership rules, daily check-in limits).
- [ ] Financial calculations (prices, discounts, balance subtractions) verify bounds (> 0) and use precise dec/fixed arithmetic.

### 4. 🔌 API & Backend
- [ ] REST API endpoints are fully implemented with correct HTTP verbs (GET/POST/PUT/PATCH/DELETE).
- [ ] Route authorization attributes (`[Authorize(Roles = "...")]`) restrict endpoints to intended roles.
- [ ] Endpoints return standard HTTP status codes (200 OK, 201 Created, 400 Bad Request, 401 Unauthorized, 403 Forbidden, 404 Not Found).

### 5. 💾 Database Persistence
- [ ] Server database migrations are generated using Entity Framework Core, tested locally, and applied to SQL Server.
- [ ] SQLite local database repository actions are implemented.
- [ ] Soft deletion flags (e.g., `Status = 'Inactive'`) are utilized instead of raw delete queries for core data.

### 6. 🔄 Offline Synchronization
- [ ] Sync queue table entry is populated when writing offline.
- [ ] Sync handler processes queue and uploads local state successfully to SQL Server.
- [ ] Conflict resolution checks are verified via timestamps.

### 7. 🚨 Error Handling
- [ ] Operations are wrapped in try-catch handlers to prevent application crashes.
- [ ] Standard, user-friendly error messages (refer to Chapter 18 of the specification) are displayed on the UI.
- [ ] Critical exceptions are logged in the database/console.

### 8. 🧪 Verification & Manual Testing
- [ ] Code builds and compiles with zero errors or major warnings.
- [ ] Manual test walkthrough is performed using both administrator and receptionist roles.
- [ ] Tests confirm that offline actions queue and sync correctly once connection returns.

### 9. 📝 Documentation & Git Workflow
- [ ] [docs/06_Changelog.md](file:///d:/DEV/DEV/IT123P.GymTracker.APP/docs/06_Changelog.md) is updated with changes.
- [ ] Feature branch code is pushed to GitHub, and a pull request (PR) targeting `develop` is created.
- [ ] Pull request passes all GitHub Actions CI build checks.
- [ ] Code is reviewed and approved by at least one other team member.
