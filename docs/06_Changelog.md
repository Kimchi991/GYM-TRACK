# Project Changelog

All notable changes to the GymTrackPro project will be documented in this file. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [v2.0.0-rc1.rev1] - 2026-07-02

### Added
*   **GitHub Templates & Workflows:** Created `.github/workflows/build.yml` for C# build verification, pull request templates, and issue templates (bug reports & features).
*   **License File:** Added `LICENSE` (MIT) in the repository root.

### Changed
*   **Agent Guidelines (`docs/01_Agent_Guidelines.md`):** Updated to assign the "Technical Lead" role to the agent, detailing standing review responsibilities and the strict "Never Assume, Always Verify" policy for technology stacks.
*   **Development Roadmap (`docs/02_Development_Roadmap.md`):** Injected **Phase 0: Technology Decisions & Architecture Validation** to lock down databases, ORM, and auth selections before scaffolding code.
*   **System Architecture (`docs/03_Architecture.md`):** Refactored to align with Clean Architecture layers (`Domain`, `Application`, `Infrastructure`, `Shared`, `Client`, `Server`). Removed hard assumptions about ORM and remote database technologies.
*   **Database Specifications (`docs/04_Database.md`):** Adjusted descriptions to keep physical database dialects TBD under Phase 0 evaluation.
*   **Decisions Log (`docs/05_Decisions.md`):** Shifted Database selection, ORM integration, and Authentication providers to **Pending Decisions** under Phase 0. Approved Clean Architecture structural layout and Soft Delete policies.
*   **Project Blueprint (`docs/07_Project_Blueprint.md`):** Updated directory structure definitions to match Clean Architecture layers and classified persistence layers as TBD.
