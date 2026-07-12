# GymTrackPro — Project Overview

## Title
**GymTrackPro** — A Mobile-First Gym Management and Member Self-Service Platform

---

## Project Description

GymTrackPro is a cross-platform gym management system built for the undergraduate BSIT capstone defense. It is composed of a **.NET 8 REST API backend** and a **.NET MAUI cross-platform mobile application** that runs on Android and iOS.

The system is designed to digitize and streamline the day-to-day operations of a physical fitness gym. It covers member registration, QR-code-based attendance tracking, membership subscription management, payment recording, financial reporting, staff access control, and a dedicated member-facing (Gym Goer) self-service experience.

---

## Problem Statement

Small-to-medium fitness gyms in the Philippines typically rely on manual paper logbooks and spreadsheets to manage member records, attendance, and payments. This approach leads to:

- **Inaccurate attendance records** due to manual entry errors
- **Lost payment receipts** and poor financial tracking
- **No real-time visibility** for gym owners over business metrics
- **Difficulty identifying expired memberships** before they become collection problems
- **No self-service tools** for gym members to track their own fitness progress

---

## Proposed Solution

GymTrackPro replaces these manual workflows with a centralized digital platform:

- A **mobile app used by gym staff** (Administrator, Receptionist) for back-office operations
- A **mobile app used by gym members** (GymGoer role) for self-service dashboard, digital membership card, and personal attendance history
- A **RESTful API** that serves both user groups with role-enforced access control
- **Firebase Authentication** for secure identity management without storing passwords in the application database
- **SQLite offline-first storage** on mobile for local sync queuing

---

## General Objective

To design and develop a mobile-first gym management system that automates member management, attendance tracking, subscription lifecycle, payment processing, and reporting operations for a single-location fitness gym.

---

## Specific Objectives

1. Implement a secure Firebase-authenticated mobile login supporting email/password and Google OAuth sign-in.
2. Implement a member registry with QR code assignment, profile picture upload, and soft-delete support.
3. Implement a QR code-based attendance check-in and check-out system for both staff-side and member-side.
4. Implement a full membership subscription lifecycle (create, pause, resume, renew, expire).
5. Implement payment recording with refund support and auto-generated sequential receipt numbers.
6. Implement a financial and operational reporting engine with CSV export support for six report types.
7. Implement a role-based authorization system with three distinct roles: Administrator, Receptionist, and GymGoer.
8. Implement a self-service mobile dashboard for Gym Goer members showing membership status, streak, badge achievements, and digital membership card.
9. Implement a background worker that auto-expires subscriptions past their end date.
10. Implement a gym system settings module for configurable operational parameters.

---

## Target Users

| User | Description |
|:--|:--|
| **Administrator (Gym Owner)** | Full access to all system functions including reports, refunds, member deletion, and system settings |
| **Receptionist** | Day-to-day operations: check-in/out, member creation, subscription and payment recording |
| **Gym Goer (Member)** | Self-service view of personal dashboard, digital membership card, attendance history, and fitness progress |

---

## Scope

- Single-gym deployment (one gym instance per deployment)
- Android and iOS mobile client via .NET MAUI
- REST API hosted on a cloud or local server (MonsterASP.net or localhost)
- Firebase Authentication for all user identity operations
- SQL Server database via Entity Framework Core (Code-First)
- Offline-first SQLite sync queue on mobile
- Six financial and operational report types with CSV export
- QR code scanning for check-in (staff-side via camera) and self-service (member-side via idempotent operation)
- Profile picture upload, storage, and retrieval from server-side file system
- Background subscription expiration worker

---

## Limitations

- No multi-gym (multi-tenant) support in this version
- No integrated payment gateway (payments are manually recorded by staff)
- No push notification delivery (Firebase Cloud Messaging infrastructure noted in `MauiProgram.cs` as Phase 10 stub)
- Google Sign-In requires additional native platform configuration per device
- No web portal; management is exclusively through the mobile application
- CSV export does not include charts or PDF generation
- No automated data backup mechanism
- The system does not support walk-in visitor advanced reporting beyond basic fee recording

---

## Technology Stack

| Layer | Technology |
|:--|:--|
| **Mobile Frontend** | .NET MAUI (C#, XAML) |
| **Backend API** | ASP.NET Core 8 (C#) |
| **Shared Library** | .NET 8 Class Library |
| **Database** | SQL Server (via Docker or hosted) |
| **ORM** | Entity Framework Core 8 (Code-First migrations) |
| **Authentication** | Firebase Authentication (Email/Password, Google OAuth) |
| **Local Storage** | SQLite (via sqlite-net-pcl) |
| **QR Scanning** | ZXing.Net.Maui |
| **API Versioning** | URL path versioning (`/api/v1/`) |
| **Rate Limiting** | ASP.NET Core built-in rate limiter |
| **Dependency Injection** | Built-in .NET DI container |
| **IDE** | Visual Studio 2022 |
| **Source Control** | Git / GitHub |

---

## Project Team

*(Derived from implementation — group name and member names should be filled in by the team)*

- **Project:** IT123P Capstone Project
- **Institution:** *(insert school name)*
- **Semester:** *(insert semester/year)*
