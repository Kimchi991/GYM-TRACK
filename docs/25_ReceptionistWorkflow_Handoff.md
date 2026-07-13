# GymTrackPro — Receptionist Workflow & Occupancy Handoff Document

> **Status:** Implementation Phase (In Progress)  
> **Target Branch:** `solsolplan`  
> **Workspace:** `IT123P.GymTracker.APP-fresh`  

---

## 📌 Feature Overview
This update automates walk-in member registration, separates regular member onboarding from walk-in guest payments, and introduces a real-time occupancy and plan-distribution view on the administrative dashboard.

---

## 📂 Proposed File Changes & Checklist

### 1. Shared Layer (`GymTrackPro.Shared`)
*   **[NEW] `Entities/MemberApplication.cs`**:
    *   Captures online member registrations (FullName, Email, Phone, selected Plan, Payment Reference Number, status, and verification metadata).
*   **[NEW] `DTOs/MemberApplicationDtos.cs`**:
    *   Contains DTOs: `SubmitApplicationDto`, `ApplicationListItemDto`, and `VerifyApplicationDto`.
*   **[MODIFY] `Enums/UserRole.cs`**:
    *   Confirm role values are: `Administrator = 0`, `Receptionist = 1`, `GymGoer = 2`.
*   **[MODIFY] `Entities/WalkInVisitor.cs`**:
    *   Add fields for reference numbers and verifier IDs to log walk-in check-ins securely.

### 2. API Backend (`GymTrackPro.API`)
*   **[MODIFY] `Data/GymDbContext.cs`**:
    *   Register `DbSet<MemberApplication> MemberApplications` and map relationships.
*   **[NEW] `Controllers/ApplicationController.cs`**:
    *   `POST /api/v1/applications` (Public): Guest submissions.
    *   `GET /api/v1/applications/pending` (Staff-only): Pending queue.
    *   `POST /api/v1/applications/{id}/verify` (Staff-only): Approves/rejects application.
*   **[MODIFY] `Services/AuthenticationService.cs`**:
    *   Add transactional approval logic: creating `Member`, `Subscription`, `Payment`, and `AccountInvite` upon application verification.
*   **[MODIFY] `Controllers/DashboardController.cs`**:
    *   Expand KPI object to return a list of currently checked-in members (Live Gym Occupancy) and a count of users per membership plan.

### 3. Mobile Client (`GymTrackPro.Mobile`)
*   **[NEW] `Views/ApplicationsPage.xaml` & `xaml.cs`**:
    *   Display queue of incoming applications for the receptionist to approve or reject.
*   **[NEW] `ViewModels/ApplicationsViewModel.cs`**:
    *   Handles loading applications and submitting verification API requests.
*   **[MODIFY] `Views/DashboardPage.xaml`**:
    *   Redesign dashboard layout to display the real-time list of checked-in members and membership plan distribution cards.
*   **[MODIFY] `ViewModels/DashboardViewModel.cs`**:
    *   Fetch and populate live occupancy lists and plan counts.

---

## 🔄 How the Flows Connect Logically

### A. Member Registration & Verification
1.  **Submit**: A guest scans a QR code at the gym desk which opens a public form, submits their registration details, GCash/Maya reference number, and plan selection to `POST /api/v1/applications`.
2.  **Review**: The Receptionist opens the **Applications** page on their mobile app, checks their GCash/Maya wallet to confirm the payment reference number matches, and clicks **Approve**.
3.  **Provision**: The API automatically:
    *   Creates a `Member` record with a unique QR code suffix (`GTP-XXXXXX`).
    *   Saves the `Payment` and activates their `Subscription`.
    *   Generates a one-time activation `AccountInvite` token.
    *   Sends a verification email link containing the activation code.
4.  **Access**: The member registers on the mobile app, inputs the invite code, and can now check-in self-service.

### B. One-Day Walk-In Pass Flow
1.  **Submit**: Guest scans the walk-in QR code, fills in their name/phone, and completes the payment.
2.  **Approve**: Receptionist verifies the payment and approves the application.
3.  **Access**: Since it is a one-day pass, the system logs the visit inside `WalkInVisitors` and grants check-in access for the current calendar date only, without creating a full user profile.

---

## 🧪 Verification Plan
*   **Integration Tests**: Script `scratch/applications_integration_test.ps1` will simulate public submission, verification, rejection, and successful creation of profiles, payments, and invites.
*   **Build Check**: Verify that both the API and Mobile projects compile with `0 warnings` and `0 errors`.
