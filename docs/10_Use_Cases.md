# GymTrackPro — System Flow & Use Cases

## Use Case Diagram (Mermaid)

```mermaid
graph TD
    Admin([Administrator])
    Recep([Receptionist])
    Goer([Gym Goer / Member])

    Admin --> UC1[Manage Members]
    Admin --> UC2[Manage Membership Plans]
    Admin --> UC3[Manage Subscriptions]
    Admin --> UC4[Process Payments]
    Admin --> UC5[Refund Payment]
    Admin --> UC6[View Reports & Export CSV]
    Admin --> UC7[Manage System Settings]
    Admin --> UC8[Check In / Out Members]
    Admin --> UC9[Void Attendance]
    Admin --> UC10[Correct Checkout]
    Admin --> UC11[Generate App Invite for Member]
    Admin --> UC12[Manage Notifications]
    Admin --> UC13[View Dashboard KPIs]

    Recep --> UC1
    Recep --> UC3
    Recep --> UC4
    Recep --> UC8
    Recep --> UC11
    Recep --> UC12
    Recep --> UC13

    Goer --> UC14[Login via Firebase]
    Goer --> UC15[View Personal Dashboard]
    Goer --> UC16[Self Check-In]
    Goer --> UC17[Self Check-Out]
    Goer --> UC18[View Attendance History]
    Goer --> UC19[View Digital Membership Card]
    Goer --> UC20[View Progress & Badges]
```

---

## System Flow Diagrams

### Authentication Flow

```mermaid
sequenceDiagram
    participant User as Mobile App
    participant FB as Firebase Auth
    participant API as GymTrackPro API
    participant DB as SQL Server

    User->>FB: Sign in (email + password)
    FB-->>User: Firebase ID Token
    User->>API: POST /auth/sync-user (Bearer: Firebase ID Token)
    API->>API: Validate Firebase JWT (public key)
    API->>DB: SELECT User WHERE FirebaseUid = uid
    alt User not found
        API->>DB: INSERT User (new account)
    else User found
        API->>DB: UPDATE LastLoginAt
    end
    API-->>User: UserResponseDto (role, status)
    User->>User: Store token in SecureStorage
    User->>User: Navigate to role-based Shell
```

---

### Staff-Side Check-In Flow

```mermaid
sequenceDiagram
    participant Staff as Receptionist App
    participant API as GymTrackPro API
    participant DB as SQL Server

    Staff->>Staff: Scan QR code (ZXing camera)
    Staff->>API: POST /attendance/check-in { qrCode: "GTP-XXXXXX" }
    API->>DB: SELECT Member WHERE QRCode = 'GTP-XXXXXX' AND NOT IsDeleted
    alt Member not found
        API-->>Staff: 404 Not Found
    end
    API->>DB: SELECT active Subscription for member
    alt No active subscription
        API-->>Staff: 400 Bad Request (no active plan)
    end
    API->>DB: Check for open session (no CheckOutTime)
    alt Already checked in
        API-->>Staff: 409 Conflict
    end
    API->>DB: INSERT AttendanceLogs
    API->>DB: INSERT AttendanceOperations (CheckIn, Applied)
    API-->>Staff: 201 AttendanceDto
```

---

### Gym Goer Self Check-In Flow

```mermaid
sequenceDiagram
    participant Goer as Gym Goer App
    participant API as GymTrackPro API
    participant DB as SQL Server

    Goer->>Goer: Tap "Check In" button
    Goer->>Goer: Generate Guid operationId
    Goer->>API: POST /me/attendance/check-in { operationId: Guid }
    API->>API: Resolve MemberID from Firebase UID (via Users.MemberID)
    API->>DB: Check idempotency (AttendanceOperations WHERE OperationId = guid)
    alt Already processed
        API-->>Goer: Return existing attendance record
    end
    API->>DB: Validate active subscription
    API->>DB: INSERT AttendanceLogs (Source = "SelfCheckIn")
    API->>DB: INSERT AttendanceOperations
    API->>DB: UPDATE MemberProjectionVersions (increment version)
    API-->>Goer: 200 AttendanceDto
    Goer->>Goer: Refresh dashboard projection
```

---

### Subscription & Payment Flow

```mermaid
sequenceDiagram
    participant Staff as Back-Office App
    participant API as GymTrackPro API
    participant DB as SQL Server

    Staff->>API: POST /subscriptions { MemberID, PlanID }
    API->>DB: SELECT MembershipPlan WHERE PlanID
    API->>DB: INSERT Subscription (Status='Active', EndDate = StartDate + DurationDays)
    API-->>Staff: 201 SubscriptionResponseDto

    Staff->>API: POST /payments { MemberID, SubscriptionID, Amount, PaymentMethod }
    API->>DB: SELECT next ReceiptNumber (sequential)
    API->>DB: INSERT Payment (FinalAmount = Amount - Discount)
    API->>DB: UPDATE Subscription Status = 'Active' (if Pending)
    API-->>Staff: 201 PaymentResponseDto

    Note over API,DB: Background Worker (hourly)
    API->>DB: SELECT Subscriptions WHERE EndDate < UtcNow AND Status = 'Active'
    API->>DB: UPDATE Status = 'Expired' (batch)
```

---

### Report Generation Flow

```mermaid
sequenceDiagram
    participant Admin as Administrator App
    participant API as GymTrackPro API
    participant DB as SQL Server

    Admin->>API: GET /reports/daily-revenue?startDate=&endDate=
    API->>DB: SELECT SUM(FinalAmount), COUNT(*) FROM Payments GROUP BY DAY
    API-->>Admin: ApiResponse<IEnumerable<DailyRevenueReportDto>>

    Admin->>API: GET /reports/daily-revenue/export?startDate=&endDate=
    API->>DB: Same query
    API->>API: Build CSV string (CsvCellEncoder with injection prevention)
    API-->>Admin: File(text/csv, daily_revenue_YYYYMMDD_YYYYMMDD.csv)
```

---

## Use Case Descriptions

### UC-01: Create Member
- **Actor:** Administrator, Receptionist
- **Precondition:** User is authenticated with `BackOffice` policy
- **Steps:**
  1. Staff fills out member registration form
  2. App calls `POST /api/v1/members`
  3. API validates uniqueness of PhoneNumber and Email
  4. API generates unique QR code (`GTP-` prefix + 6 alphanumeric characters)
  5. If profile picture provided (base64), saves to server filesystem
  6. Member record created in DB
- **Postcondition:** New member appears in member list with `Status = Active`
- **Exceptions:** Duplicate phone or email → 400 Bad Request

---

### UC-02: Check In Member (Staff)
- **Actor:** Administrator, Receptionist
- **Precondition:** Member exists, member has active subscription
- **Steps:**
  1. Staff opens Attendance screen and scans QR code via camera (ZXing)
  2. App sends QR code to `POST /api/v1/attendance/check-in`
  3. API validates QR → Member → Active subscription
  4. API checks for existing open session
  5. Creates attendance log
- **Exceptions:** No active subscription → error; already checked in → error

---

### UC-03: Self Check-In (Gym Goer)
- **Actor:** Gym Goer (member with linked account)
- **Precondition:** GymGoer is logged in, has active subscription
- **Steps:**
  1. Member taps "Check In" on GoerDashboardPage
  2. App generates idempotent operation GUID
  3. App calls `POST /api/v1/me/attendance/check-in`
  4. API resolves member from Firebase UID
  5. Validates active subscription, no open session
  6. Creates attendance record
  7. Increments MemberProjectionVersion
- **Postcondition:** Dashboard shows "Checked In" state

---

### UC-04: Process Payment
- **Actor:** Administrator, Receptionist
- **Precondition:** Member has subscription, subscription is active or pending
- **Steps:**
  1. Staff records payment on PaymentsPage
  2. App calls `POST /api/v1/payments`
  3. API generates sequential receipt number
  4. Calculates FinalAmount = Amount − Discount
  5. Saves payment record
  6. Logs audit event
- **Postcondition:** Payment appears in member's payment history

---

### UC-05: Refund Payment
- **Actor:** Administrator only
- **Precondition:** Payment has `Status = Paid`
- **Steps:**
  1. Admin taps Refund on payment record
  2. App calls `POST /api/v1/payments/{id}/refund`
  3. API sets `PaymentStatus = Refunded`
  4. API sets linked subscription `Status = Cancelled`
  5. Audit log written
- **Postcondition:** Payment shows Refunded; subscription shows Cancelled

---

### UC-06: Generate Gym Goer App Invite
- **Actor:** Administrator, Receptionist
- **Precondition:** Member exists and has no active invite
- **Steps:**
  1. Staff opens Member Details and taps "Generate App Invite"
  2. Staff enters the member's email address
  3. App calls `POST /api/v1/members/{id}/app-invite`
  4. API creates `AccountInvite` with hashed token, sets 24-hour expiry
  5. Invite code displayed on screen (plaintext, one-time view)
  6. Staff shares code with member
  7. Member downloads app, registers with Firebase, enters code
  8. App calls `POST /api/v1/auth/activate`
  9. Firebase UID linked to member's `User` record with `GymGoer` role
- **Postcondition:** Member can log in to the GymGoer shell

---

### UC-07: View Dashboard (Admin/Receptionist)
- **Actor:** Administrator, Receptionist
- **Steps:**
  1. App loads DashboardPage on login
  2. Calls `GET /api/v1/dashboard/metrics`
  3. API returns: total active members, today's check-ins, monthly revenue, memberships expiring within 7 days
- **Output:** KPI metrics cards on dashboard

---

### UC-08: View Gym Goer Dashboard
- **Actor:** Gym Goer
- **Steps:**
  1. GoerDashboardPage loads on login
  2. App calls `GET /api/v1/me/dashboard`
  3. API runs `GymGoerProjectionService` — calculates:
     - Current membership status
     - Current open session
     - Monthly workout minutes and duration in seconds
     - Visit count
     - Current streak and longest streak
     - Badge eligibility (derived from implementation)
  4. Returns `GoerDashboardDto` with `ProjectionMetadataDto` (version, ETag, freshness)
- **Output:** Dashboard showing progress, session status, and achievements

---

### UC-09: Generate Reports
- **Actor:** Administrator only
- **Steps:**
  1. Admin selects report type and date range on ReportsPage
  2. App calls appropriate report endpoint
  3. Response displayed as list
  4. Optionally, Admin taps Export → App calls `/export` variant
  5. CSV file saved to device
