# GymTrackPro 🏋️‍♂️

GymTrackPro is a modern, cross-platform Gym Membership Management System designed to digitize gym operations, replacing traditional manual and paper-based tracking methods. Built using an **Offline-First** architecture, it guarantees uninterrupted front-desk operations even during internet outages, synchronizing data automatically once connection is restored.

---

## 🚀 Key Features

*   **Authentication & Role-Based Access Control (RBAC):** Secure login for Administrators (full access) and Receptionists (operational access).
*   **Member Management:** Registration, full profiles, search, filtering, and status badges (Active, Paused, Expired).
*   **Membership Subscriptions:** Plan assignment, renewal, and pause/resume logic.
*   **Payments:** Recording, tracking, receipt generation, and payment methods (Cash, GCash, Card, Bank Transfer).
*   **Attendance & QR Check-In:** Live attendance tracking, checkout validation, and fast QR code scanning.
*   **Digital Membership Card:** QR-equipped virtual membership IDs for gym members.
*   **Walk-In Visitors:** Tracking day passes and walk-in revenues.
*   **Notifications & Alerts:** Automatic alerts for expiring plans, failed syncs, and payment reminders.
*   **Reports:** Detailed summaries of daily/monthly revenue, attendance, and membership counts.
*   **Audit Logging:** Complete tamper-proof log of user activities for security and transparency.

---

## 🏗️ Architecture Overview

GymTrackPro utilizes a modern layered Client-Server architecture:
*   **Mobile App:** Built with **.NET MAUI** utilizing the **MVVM (Model-View-ViewModel)** pattern and **Repository Pattern**.
*   **Web API:** Built with **ASP.NET Core Web API** and **Entity Framework Core (EF Core)**.
*   **Databases:**
    *   **Local (Offline):** SQLite database embedded in the mobile app.
    *   **Global (Online):** MySQL database hosted on a central server.
*   **Offline Synchronization:** A custom queue system that processes writes locally, listens to connectivity changes, and syncs upstream automatically using a "newest update wins" conflict-resolution rule.

---

## 📂 Folder Structure

```directory
GymTrackPro/
├── docs/               # Developer documentation & specifications
├── api/                # ASP.NET Core Web API Backend
├── mobile/             # .NET MAUI Cross-platform Mobile App
├── src/                # Shared libraries / core domain logic
├── .gitignore          # Repository git-ignore configuration
└── README.md           # Project readme (this file)
```

---

## 🛠️ Technology Stack

*   **Mobile Framework:** .NET MAUI (.NET 8/9)
*   **Backend Framework:** ASP.NET Core Web API
*   **ORM:** Entity Framework Core
*   **Local DB:** SQLite
*   **Server DB:** MySQL
*   **Communication:** RESTful APIs over HTTPS / JSON
*   **Authentication:** JSON Web Tokens (JWT)

---

## 🧑‍💻 Team Roles

1.  **Lead Developer:** Project Architecture, Backend API, Database, Authentication, and Integration.
2.  **Frontend Developer:** Mobile UI Views, Dashboard, Member Management, Reports, and Settings.
3.  **Backend & QA Developer:** Payments, Attendance, QR Scanning, Testing, Bug Fixes, and Documentation.
