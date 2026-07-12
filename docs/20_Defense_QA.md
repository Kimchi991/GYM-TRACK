# GymTrackPro — Capstone Defense Q&A

> Prepared answers for panel questions at the BSIT Capstone Final Defense.

---

## Technical Questions

**Q1: What architecture pattern did you use for the mobile app?**

We used the **MVVM (Model-View-ViewModel)** pattern. The `Views` (XAML pages) only handle UI layout and data bindings. The `ViewModels` contain all the presentation logic and call services for data. The `Services` layer handles all HTTP communication and local storage. This separation keeps our UI code clean and makes the business logic independently testable.

---

**Q2: What design patterns did you implement in the backend?**

We implemented several patterns:
- **Repository Pattern** — each entity has a repository (`IMemberRepository`, `IPaymentRepository`, etc.) to abstract database access from business logic
- **Service Layer** — all business rules live in services, not controllers; controllers are thin wrappers
- **Background Worker Pattern** — `SubscriptionExpirationWorker` runs as an `IHostedService` to auto-expire subscriptions
- **Observer/Event Pattern** — `InMemoryEventPublisher` and `DomainEventPublisher` for decoupled notification triggering
- **Optimistic Concurrency** — `RowVersion` byte arrays on `AttendanceLogs` and `AccountInvites` prevent race conditions
- **Projection Pattern** — `GymGoerProjectionService` computes a derived read model for each member's dashboard without storing redundant fields

---

**Q3: Why did you choose .NET MAUI for the mobile app?**

We chose .NET MAUI because our entire team already works in C# and the .NET ecosystem. MAUI allows us to target both Android and iOS from a single codebase, share our DTO and entity contracts with the backend via the `GymTrackPro.Shared` project, and integrate Firebase, SQLite, and QR scanning through stable NuGet packages. This significantly reduced duplication of effort.

---

**Q4: How does QR code generation work?**

When a new member is registered, the API generates a unique QR code string in the format `GTP-XXXXXX` where `XXXXXX` is a random alphanumeric string. The API checks the `Members` table to ensure the generated code does not already exist. This QR code is stored in the member's record and printed on their membership card. On mobile, ZXing.Net.Maui scans the QR code using the device camera, and the resulting string is sent to the check-in endpoint.

---

**Q5: How do background workers run?**

The `SubscriptionExpirationWorker` and `NotificationWorker` are registered as `IHostedService` implementations in `Program.cs`. They run in the background on the server independently of HTTP requests. The `SubscriptionExpirationWorker` periodically queries all active subscriptions whose `EndDate` has passed and marks them as `Expired`. The `NotificationWorker` dequeues tasks from an in-memory `INotificationQueue` and processes them.

---

## Architecture Questions

**Q6: Why did you put entities, DTOs, and interfaces in a shared library?**

The `GymTrackPro.Shared` project is a .NET class library referenced by both the API and the Mobile projects. By sharing DTOs and entity contracts, we eliminate the risk of the API and mobile app becoming out of sync on field names or data types. When we update a DTO in Shared, the compiler immediately flags any breaking changes in both projects.

---

**Q7: How does authorization work without storing passwords?**

Firebase Authentication handles all passwords and credential storage. When a user signs in, Firebase issues a cryptographically signed ID Token (a JWT). Our API validates this token using Firebase's public key endpoint — we never see or store the password. Once the Firebase UID is validated, we look up the user in our own `Users` table to get their role (Administrator, Receptionist, GymGoer) and apply our ASP.NET Core authorization policies accordingly.

---

**Q8: How do you prevent one staff member from processing another staff member's request?**

We use ASP.NET Core authorization policies. The `BackOffice` policy allows both Administrators and Receptionists to access general operations. The `OwnerOnly` policy restricts critical operations (delete, refund, reports, settings) to Administrators only. The `GymGoerSelf` policy restricts self-service endpoints to GymGoer role only, preventing staff from accessing a member's private dashboard or self-checking-in on their behalf.

---

**Q9: What happens when the mobile app loses internet connection?**

The mobile app uses a `SyncService` backed by a local SQLite database (`LocalDatabaseService`). When offline, operation requests that would normally go to the API are queued in the local SQLite sync table. When connectivity is restored (detected by `NetworkService`/`INetworkService`), `SyncService` replays the queued operations in order. The `ILocalDatabaseService` manages schema versioning with `LocalDatabaseCompatibilityException` handling for schema upgrade scenarios.

---

## Database Questions

**Q10: Why did you use soft deletes instead of hard deletes?**

Gym businesses frequently need to re-examine historical records for disputes, audits, and reporting. If we hard-deleted a member, all their attendance and payment history would cascade-delete (or become orphaned), breaking financial reports. Soft delete (`IsDeleted = true`) hides the record from normal queries but preserves the full history. Administrators can see that a deletion happened, when it happened, and trace the record in audit logs.

---

**Q11: What are `AttendanceAdjustments` and `AttendanceOperations`? Why do you need two extra tables?**

These solve two different problems:
- **`AttendanceOperations`** is an idempotency table. Each check-in or check-out has a unique `OperationId` (GUID). If the mobile app retries a network request after a timeout, the API detects the duplicate `OperationId` and returns the existing result instead of creating a duplicate record. This is critical for self-service check-in.
- **`AttendanceAdjustments`** is an audit trail for corrections. When an Administrator corrects a wrong check-out time, the original entry is superseded by a new one, and the adjustment records exactly what changed, who changed it, and when. This provides forensic accountability.

---

**Q12: Why does the `Users` table have a nullable `MemberID` foreign key?**

Not all users are gym members. Administrators and Receptionists are staff accounts without member profiles. Only `GymGoer` role users are linked to a `Member` record. The `MemberID` is nullable to represent these two different user categories in one table. When a Gym Goer activates their invite, the link is established.

---

**Q13: How does `MemberProjectionVersion` work?**

The Gym Goer dashboard is a computed projection — it aggregates attendance history, subscription status, streaks, and badges. `MemberProjectionVersions` stores a monotonic mutation version that changes after attendance updates. The API includes that version, a content ETag, and a freshness timestamp in each projection response. The mobile app stores the last successful dashboard locally and uses it only as an offline or temporarily unavailable-server fallback; the current API does not use conditional requests to return a server-cached projection.

---

## Security Questions

**Q14: What security measures did you implement?**

1. **Firebase Authentication** — no passwords stored in our DB
2. **JWT Bearer validation** — every API call verifies the Firebase token signature
3. **Authorization policies** — five granular policies enforce role-based access at the controller level
4. **Rate limiting** — built-in ASP.NET Core rate limiter applied to auth endpoints (`[EnableRateLimiting("Auth")]`)
5. **Optimistic concurrency** — prevents race conditions on concurrent invite redemptions (`RowVersion`)
6. **Invite token hashing** — `AccountInvite.TokenHash` stores a 32-byte SHA256 hash of the invite code; the plaintext is shown once and never stored
7. **CSV injection prevention** — `CsvCellEncoder` in `ReportsController` detects and escapes formula-injection characters (`=`, `+`, `-`, `@`) in all CSV exports
8. **HTTPS enforcement** — `ApiEndpointConfiguration` enforces HTTPS on production builds and rejects non-localhost HTTP in debug mode

---

**Q15: How does the invite system prevent replay attacks?**

`AccountInvite` has:
- `ExpiresAtUtc` — tokens expire after a set time window (derived from implementation)
- `UsedAtUtc` — once used, the token is marked and rejected on reuse
- `RevokedAtUtc` — staff can revoke an unused invite
- `RedemptionOperationId` (GUID) — used for idempotent redemption; if the same activation is retried, it returns the same result without creating a duplicate user

---

## Scalability Questions

**Q16: Can this system handle multiple gyms?**

Not in the current version. The system is designed for a single-gym deployment. The `develop` branch contains the start of a Phase 1 SaaS migration that introduces `Gym`, `GymSubscription`, and `GymSetting` entities with tenant isolation via global EF Core query filters — but this is not merged into `solsolplan`.

---

**Q17: How would you scale the attendance system if the gym grows to 1,000 check-ins per day?**

The current attendance system already has good foundations:
- Idempotent operations via GUID prevent duplicate inserts under load
- Optimistic concurrency (`RowVersion`) handles concurrent writes without locking
- For further scaling, we would add a Redis distributed cache for the `GymGoerProjection`, replace the in-memory notification queue with a message broker (RabbitMQ or Azure Service Bus), and add read replicas to SQL Server for report queries

---

## Design Decision Questions

**Q18: Why did you use EF Core Code-First instead of database-first?**

Code-First allows us to manage the database schema entirely from C# entity classes using EF Core migrations. This means schema changes are version-controlled alongside the code, can be reviewed in pull requests, and can be applied automatically during deployment. Database-first would require maintaining SQL scripts separately from the codebase, creating synchronization risk.

---

**Q19: Why did you implement a separate `GymGoerProjectionService` instead of just querying from the Goer dashboard endpoint directly?**

The Gym Goer dashboard requires computing current membership status, open session, monthly minutes, streaks, and badge eligibility. Keeping this aggregation in `GymGoerProjectionService` gives the app a focused, testable read-model boundary instead of scattering calculations through controllers. The response carries version and freshness metadata, while the mobile client keeps its last successful dashboard as an offline fallback. A shared server-side cache or conditional-response path can be added later if load requires it.

---

**Q20: Why does the attendance system have both `Source` and `ActorUserID` fields?**

`Source` categorizes the origin of the check-in (e.g., `StaffQr`, `SelfCheckIn`, `HistoricalImport`). `ActorUserID` records which specific staff member performed the action. These serve different purposes: `Source` is used for analytics (how many self-check-ins vs. staff check-ins), while `ActorUserID` provides accountability (which receptionist did this check-in).

---

## Testing Questions

**Q21: How did you test the system?**

We have two testing layers:
1. **Unit Tests** — `GymTrackPro.Tests` and `GymTrackPro.Mobile.Tests` projects for isolated service and ViewModel logic
2. **Integration Tests** — A suite of PowerShell scripts in the `scratch/` folder that start the actual API against a Docker SQL Server instance and test all endpoints end-to-end. The scripts cover authentication, member management, attendance, subscriptions, payments, plans, settings, notifications, and analytics — approximately 80+ test cases.

---

**Q22: How do you handle test data isolation between test runs?**

Each PowerShell integration test script creates its own unique test users with distinct usernames (e.g., `admin_user`, `recep_user`, `events_admin`). Before each test suite run, the API is reset to a clean state by restarting it against the Docker SQL Server. This ensures tests do not share state across suites.

---

## Deployment Questions

**Q23: How is the application deployed?**

- **API:** Hosted on MonsterASP.net (as evidenced from commit history) as an ASP.NET Core application targeting .NET 8
- **Database:** SQL Server hosted on the same server or a Docker container
- **Mobile:** Distributed as an Android APK sideloaded to test devices; iOS requires a paid Apple Developer account
- **Firebase:** Configured per environment using `FirebaseAuthSettings.cs` which loads the API key embedded in the assembly metadata at build time

---

**Q24: What environment-specific configuration does the app support?**

The `ApiEndpointConfiguration` class enforces these rules:
- **Debug:** HTTP is allowed only for `localhost` or the Android emulator host (`10.0.2.2`); other hosts must use HTTPS
- **Release/Production:** HTTPS on the default port (443) is mandatory; any localhost or development host is rejected at startup
- The API base URL and expected host are embedded as `AssemblyMetadata` attributes at build time, preventing runtime configuration tampering

---

## Future Improvements

**Q25: What would you improve if given more time?**

1. **Push notifications** — Firebase Cloud Messaging (FCM) is stubbed in `MauiProgram.cs` as "Phase 10" but not implemented
2. **Multi-gym (SaaS)** — The Phase 1 SaaS foundation is partially built in `develop` but not shipped
3. **Web portal** — An administrative web dashboard to complement the mobile app
4. **Biometric check-in** — Face recognition or fingerprint scan instead of QR codes
5. **PDF report export** — Currently only CSV; PDF would be more presentation-ready
6. **Automated backup** — Scheduled database backups to cloud storage
7. **Member communication** — Email/SMS integration for expiration reminders
8. **Google Sign-In** — Currently implemented in `FirebaseAuthService` (via `LoginWithGoogleAsync`) but requires additional native platform setup
