# GymTrackPro — API Documentation

**Base URL:** `https://<host>/api/v1`  
**Authentication:** Firebase ID token in `Authorization: Bearer <token>`  
**Standard response:** Most endpoints return an `ApiResponse<T>` envelope. Profile-picture endpoints return image bytes and report exports return CSV. The notifications controller currently returns its entity list directly and returns `204 No Content` when an item is marked read.

> This inventory is derived from the API controllers on the `solsolplan` branch. Each controller action is listed once. Route parameters shown as `{id}` or `{memberId}` are integers unless stated otherwise.

## Auth — `/auth`

| Method and route | Policy | Input / result |
|:--|:--|:--|
| POST `/auth/sync-user` | `FirebaseOnboarding`; Auth rate limit | No body. Syncs the verified Firebase identity and returns `ApiResponse<UserResponseDto>`. |
| POST `/auth/activate` | `FirebaseOnboarding`; Activation rate limit | `ActivateInviteDto`; activates an invite and returns `ApiResponse<UserResponseDto>`. |

## Members — `/members`

Unless noted, all member routes require `BackOffice` (Administrator or Receptionist).

| Method and route | Input / result |
|:--|:--|
| GET `/members` | All non-deleted members; `ApiResponse<IEnumerable<MemberResponseDto>>`. |
| GET `/members/{id}` | A member; `404` when absent. |
| POST `/members/qr/lookup` | `QrCodeLookupRequestDto`; a member or `404`. |
| POST `/members` | `CreateMemberDto`; creates a member and returns `201`. |
| PUT `/members/{id}` | `UpdateMemberDto`; updates a member. |
| GET `/members/search` | Query: `search`, `status`, `page` (default 1), `pageSize` (default 10, max 100); paged members. |
| DELETE `/members/{id}` | **`OwnerOnly`**; soft-deletes a member. |
| POST `/members/{id}/app-invite` | `CreateAppInviteDto`; creates a member app invite. |
| GET `/members/{id}/app-invite/status` | Current member app-invite status. |
| DELETE `/members/{id}/app-invite` | Revokes the member app invite. |

## Attendance — `/attendance`

Unless noted, attendance routes require `BackOffice`.

| Method and route | Input / result |
|:--|:--|
| GET `/attendance/{id}` | One attendance log or `404`. |
| GET `/attendance/member/{memberId}/history` | Query: `from`, `to`, `page` (default 1), `pageSize` (default 50); paged history. |
| POST `/attendance/check-in` | `CheckInRequestDto`; staff QR check-in and `201` attendance record. |
| POST `/attendance/{id}/check-out` | `CheckOutRequestDto`; checks out the open session. |
| POST `/attendance/{id}/correct-checkout` | **`OwnerOnly`**; `CorrectCheckoutRequestDto`; corrects a checkout. |
| POST `/attendance/{id}/void` | **`OwnerOnly`**; `VoidAttendanceRequestDto`; voids an attendance record. |
| GET `/attendance/member/{memberId}` | **Deprecated** legacy history route; use `/history`. Sends `Deprecation`, `Sunset`, and successor `Link` headers. |
| POST `/attendance/checkin` | **Deprecated** legacy QR check-in. Body is a QR-code string; use `/check-in`. Sends deprecation headers. |
| POST `/attendance/{id}/checkout` | **Deprecated** legacy checkout; use `/check-out`. Sends deprecation headers. |

The legacy attendance routes have a sunset date of `Tue, 12 Jan 2027 00:00:00 GMT`.

## Subscriptions — `/subscriptions`

All routes require `BackOffice`.

| Method and route | Input / result |
|:--|:--|
| GET `/subscriptions/{id}` | One subscription or `404`. |
| GET `/subscriptions/member/{memberId}` | All subscriptions for a member. |
| POST `/subscriptions` | `CreateSubscriptionDto`; creates a subscription and returns `201`. |
| POST `/subscriptions/{id}/pause` | `PauseSubscriptionDto`; pauses the subscription. |
| POST `/subscriptions/{id}/resume` | Resumes the subscription. |
| POST `/subscriptions/renew` | `RenewSubscriptionDto`; renews a subscription and records payment in the service transaction. |

## Payments — `/payments`

All routes require `BackOffice` unless noted.

| Method and route | Input / result |
|:--|:--|
| GET `/payments/{id}` | One payment or `404`. |
| GET `/payments/member/{memberId}` | Payments for a member. |
| POST `/payments` | `CreatePaymentDto`; processes payment and returns `201`. |
| POST `/payments/{id}/refund` | **`OwnerOnly`**; refunds a payment. |
| GET `/payments/search` | Optional query: `date`, `method`, `status`, `memberId`, `receiptNumber`. |

## Membership plans — `/plans`

Reads require `BackOffice`; writes require `OwnerOnly`.

| Method and route | Input / result |
|:--|:--|
| GET `/plans` | All membership plans. |
| GET `/plans/{id}` | One membership plan or `404`. |
| POST `/plans` | **`OwnerOnly`**; `CreateMembershipPlanDto`; returns `201`. |
| PUT `/plans/{id}` | **`OwnerOnly`**; `CreateMembershipPlanDto`; updates the plan. |
| DELETE `/plans/{id}` | **`OwnerOnly`**; marks the plan `Inactive` through the plan service. |

## Reports — `/reports`

Every report route requires `OwnerOnly`. Date-range report routes accept `startDate` and `endDate`; export routes return CSV.

| Method and route | Result |
|:--|:--|
| GET `/reports/daily-revenue` | Daily revenue data. |
| GET `/reports/daily-revenue/export` | Daily revenue CSV. |
| GET `/reports/monthly-revenue` | Monthly revenue data. |
| GET `/reports/monthly-revenue/export` | Monthly revenue CSV. |
| GET `/reports/attendance` | Attendance report data. |
| GET `/reports/attendance/export` | Attendance CSV. |
| GET `/reports/membership-sales` | Membership sales data. |
| GET `/reports/membership-sales/export` | Membership sales CSV. |
| GET `/reports/expiring-memberships` | Expiring memberships; optional `nextDays` (default 7). |
| GET `/reports/expiring-memberships/export` | Expiring-memberships CSV; optional `nextDays`. |
| GET `/reports/refunds` | Refund report data. |
| GET `/reports/refunds/export` | Refund CSV. |
| GET `/reports/cashier-activity` | Cashier-activity data. |
| GET `/reports/cashier-activity/export` | Cashier-activity CSV. |
| GET `/reports/attendance/summary` | Trend data; optional `from`, `to`, and `bucket`. |
| GET `/reports/attendance/summary/export` | Attendance-trend CSV; optional `from`, `to`, and `bucket`. |

## Settings and notifications

| Method and route | Policy | Input / result |
|:--|:--|:--|
| GET `/settings` | `BackOffice` | All system settings. |
| PUT `/settings/{key}` | `OwnerOnly` | `UpdateSettingDto`; updates one setting. |
| GET `/notifications` | `BackOffice` | Optional `memberId`; returns notifications directly. |
| PUT `/notifications/{id}/read` | `BackOffice` | Marks a notification read; returns `204` or `404`. |

## Back-office dashboard and profile pictures

| Method and route | Policy | Input / result |
|:--|:--|:--|
| GET `/dashboard/metrics` | `BackOffice` | `ApiResponse<DashboardMetricsDto>` with operations, membership, revenue, and plan metrics. |
| GET `/members/{memberId}/profile-picture` | `ActiveAppUser` | Back-office users only; returns the member image bytes or `404`. |
| GET `/me/profile-picture` | `ActiveAppUser` | GymGoer only; returns the caller's image bytes or `404`. |

## Gym Goer self-service — `/me`

| Method and route | Policy | Input / result |
|:--|:--|:--|
| GET `/me` | `ActiveAppUser` | The current synced user's `UserResponseDto`. |
| GET `/me/attendance` | `GymGoerSelf` | Query: `from`, `to`, `page` (default 1), `pageSize` (default 30); personal history. |
| GET `/me/attendance/current` | `GymGoerSelf` | Current session state. |
| POST `/me/attendance/check-in` | `GymGoerSelf` | `AttendanceOperationRequestDto`; idempotent self check-in. |
| POST `/me/attendance/checkout` | `GymGoerSelf` | `CheckOutRequestDto`; self checkout. |
| GET `/me/dashboard` | `GymGoerSelf` | `GoerDashboardDto` projection. |
| GET `/me/digital-card` | `GymGoerSelf` | `GoerDigitalCardDto`. |
| GET `/me/progress` | `GymGoerSelf` | Optional `month`; monthly progress data. |

## User app invites — `/users`

These are the only routes exposed by `UsersController`; the API does **not** provide user-list or user-detail routes.

| Method and route | Policy | Input / result |
|:--|:--|:--|
| POST `/users/{userId}/app-invite` | `OwnerOnly` | `CreateAppInviteDto`; creates an app invite for a user. |
| GET `/users/{userId}/app-invite/status` | `OwnerOnly` | App-invite status for a user. |
| DELETE `/users/{userId}/app-invite` | `OwnerOnly` | Revokes the user app invite. |
