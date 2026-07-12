# Future Production Hardening Plan

This document is retained as a future commercial-hardening reference. Its
production infrastructure, operational, and scale requirements are not blockers
for the controlled academic capstone demo. Current capstone scope, setup,
acceptance evidence, and known limitations are defined in
[`24_CapstoneImplementationAndDemoGuide.md`](24_CapstoneImplementationAndDemoGuide.md).

The roadmap below describes how GymTrackPro could progress beyond the capstone
into a production-ready application suitable for real users and small commercial
deployments.

---

## 📋 Roadmaps By Priority

To maximize the impact of the remaining development time, the suggested phases are categorized into three priority tiers:

### Tier 1: Must-Have for Capstone Excellence
*These features directly impact the stability, security, and presentation of the system during defense.*

*   **Phase 10 — Architecture Documentation**: Completing ADRs (Architecture Decision Records), ERDs, Sequence Diagrams, and API Specs. This serves as the primary artifact for capstone grading.
*   **Phase 13 — Security Hardening**: Enforcing Rate Limiting, Input validation, and Security headers. Defending against OWASP Top 10 vulnerabilities (SQLi, XSS, CSRF) is a core requirement for a passing mark.
*   **Phase 5 — Application Settings**: Moving values like daily check-in limits, QR prefixes, and currency formats out of the code and into a database-backed `SystemSettings` table.
*   **Phase 1 — Notification Module**: Implementing a unified in-app notification system that triggers logs for key lifecycle changes (membership expiration, failed check-ins, refunds).

### Tier 2: Should-Have for Architectural Polish
*These layers elevate the engineering quality of the codebase to professional, production-level standards.*

*   **Phase 2 — File Storage Abstraction (`IFileStorage`)**: Abstracting profile photo saving out of direct physical paths into a generic storage interface to support cloud storage (AWS S3/Azure Blob) or local storage.
*   **Phase 3 — Background Workers (Scheduled Jobs)**: Transitioning recurring tasks (such as deleting expired JWT blacklist tokens, archiving orphan files, or auto-expiring memberships) into standard ASP.NET background workers.
*   **Phase 7 — Health Monitoring**: Exposing `/health`, `/ready`, and `/live` endpoints to monitor dependencies (SQL Server, file system, memory usage).
*   **Phase 9 — OpenAPI/Swagger Documentation**: Rich schemas, error response mapping, XML comments, and security documentation.

### Tier 3: Nice-to-Have (Commercial SaaS Grade)
*Advanced infrastructure features that are optional for capstone validation but critical for commercial scaling.*

*   **Phase 4 — Cache Abstractions**: Adding `IMemoryCache` or Redis layers for hot aggregates (like Dashboard widgets and Plan details).
*   **Phase 6 — Activity Timelines**: Providing feed-based activity logs on the receptionist panel dashboard.
*   **Phase 11 — CI/CD Automation**: Building GitHub actions for automated compilation and E2E test runs.
*   **Phase 12 — Observability Stack**: Integrating Serilog, OpenTelemetry, or Seq for structured log aggregation.

---

## 🛠️ Next Steps

To begin execution, we will focus on **Phase 10 (Architecture Documentation)** and **Phase 13 (Security Hardening & Input validation)** as they represent the most immediate needs for stabilizing the core API.
