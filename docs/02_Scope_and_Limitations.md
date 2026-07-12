# GymTrackPro — Scope and Limitations

## Scope

GymTrackPro is scoped as a **single-gym mobile management system** covering the following:

### In Scope

| Area | What Is Covered |
|:--|:--|
| **Platforms** | Android (primary), iOS (secondary — requires Apple Developer account for distribution) |
| **User Roles** | Administrator, Receptionist, Gym Goer (Member) |
| **Authentication** | Firebase Email/Password sign-in; Google Sign-In (requires native platform config) |
| **Member Management** | Registration, update, search with pagination, soft delete, QR code assignment, profile picture upload |
| **Attendance** | Staff QR scan check-in/out, self-service (Gym Goer) check-in/out, void, checkout correction |
| **Subscriptions** | Enroll in plan, pause (with reason), resume, renew (transactional with payment), auto-expiration via background worker |
| **Payments** | Record payment (multiple methods), refund (Admin only), search/filter, sequential receipt number generation |
| **Membership Plans** | Create, update, and deactivate plans with duration and price |
| **Reports** | Daily Revenue, Monthly Revenue, Attendance Detail, Membership Sales, Expiring Memberships, Refunds, Cashier Activity, Attendance Trend Summary |
| **CSV Export** | All 8 report types support CSV export with injection-safe encoding |
| **Settings** | Configurable gym parameters (key-value system) — read and update |
| **Notifications** | In-app notification creation and mark-as-read |
| **Gym Goer Portal** | Personal dashboard (membership status, session, streaks, badges), attendance history, digital membership card, monthly progress |
| **Walk-In Visitors** | Basic fee and purpose logging |
| **Audit Logging** | All critical operations logged with user, action, details, timestamp, and IP |
| **Offline Queue** | Local SQLite sync queue for offline operation deferral |
| **Security** | Firebase JWT validation, RBAC policies, rate limiting, invite token hashing, CSV injection prevention, HTTPS enforcement |
| **Background Services** | Automatic subscription expiration, notification queue processing |
| **Testing** | ~80+ PowerShell E2E integration tests and .NET unit tests |

---

## Limitations

### Functional Limitations

| Limitation | Description |
|:--|:--|
| **Single gym only** | The system is designed for one gym. There is no multi-tenancy, tenant switching, or multi-location support in this version. |
| **No integrated payment gateway** | Payments are recorded manually by staff. There is no GCash API, PayMongo, or card processing integration. |
| **No push notifications** | Firebase Cloud Messaging (FCM) is stubbed in `MauiProgram.cs` as "Phase 10" but is not yet implemented. |
| **No PDF export** | All reports export as CSV only. PDF generation is not implemented. |
| **No email or SMS sending** | The system does not send expiration reminders, payment receipts, or promotional emails to members. |
| **Google Sign-In requires platform setup** | The `LoginWithGoogleAsync` method exists in `FirebaseAuthService` but requires Google Cloud OAuth client ID configuration per platform (Android: `google-services.json`, iOS: `GoogleService-Info.plist`). |
| **No web portal** | All management is done through the mobile app only. There is no browser-based admin panel. |
| **Walk-in analytics are basic** | The `WalkInVisitors` table records name, fee, and purpose but does not have dedicated reporting, charts, or monthly trend analysis. |
| **No booking or class scheduling** | There is no class schedule, time slot booking, or trainer assignment module. |
| **No loyalty or rewards module** | Badges are computed from attendance data, but there is no formal points-based loyalty or redemption system. |

### Technical Limitations

| Limitation | Description |
|:--|:--|
| **In-memory notification queue** | The `MemoryNotificationQueue` does not survive API restarts. Queued notifications are lost if the server process stops. |
| **Local SQLite sync is one-way** | The sync queue pushes local operations to the server but does not pull server-side changes to the local database in real time. |
| **Background worker accuracy** | `SubscriptionExpirationWorker` runs on a polling interval. There is a window between the exact expiration time and when the worker marks the status — subscriptions can appear "Active" for a short period after their `EndDate` passes. |
| **Firebase token refresh** | Firebase ID Tokens expire every 1 hour. The `AuthenticatedHttpClientHandler` handles token refresh automatically, but a very slow network could cause a brief authentication failure during the refresh window. |
| **No distributed caching** | The Gym Goer projection is computed for online requests. The mobile app keeps a local SQLite fallback, but there is no Redis or other shared server cache; scaling to many concurrent Gym Goer users would require adding one. |
| **API is stateless (no SignalR)** | Real-time attendance updates are not pushed to connected clients. The Gym Goer dashboard must be refreshed manually or on a timer. |
| **Mobile SQLite schema versioning** | If the local database schema changes between app versions, `LocalDatabaseCompatibilityException` forces a logout and re-sync. This is handled gracefully but results in a forced re-login for users who upgrade the app with a schema-breaking change. |

---

## Out of Scope

The following are explicitly not covered by this system:

- Multi-gym (SaaS) tenant management
- Trainer-client session scheduling
- Workout program assignment and tracking
- Nutritional tracking or diet logging
- Leaderboards or competitive challenge modules
- Inventory or equipment management
- Payroll or staff scheduling
- Integration with third-party health apps (Apple Health, Google Fit)
- Point-of-sale (POS) hardware integration
- Biometric authentication (fingerprint, face recognition) for check-in
