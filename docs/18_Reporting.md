# Reporting & Analytics Architecture

This document describes the design, business rules, export options, and integration details for the GymTrackPro Reporting module.

---

## 1. Business Rules

*   **Date Range Queries**: All operational reports (revenue, attendance, sales, refunds, audits) validate and run against client-provided `startDate` and `endDate` query parameters.
*   **Dual-Format Responses**: Every reporting endpoint supports:
    *   *Standard JSON envelopes*: For client-side UI rendering.
    *   *CSV Export downloads*: Accessible via the `/export` URL suffix, returning file chunks directly for native viewing (e.g. in Microsoft Excel).
*   **Auditing and Cashier Activity**: Translates security database audit logs into activity reports, allowing administrators to audit cashier modifications, logins, and registrations.

---

## 2. API Contract

### 2.1 Endpoints List
*   `GET /api/v1/Reports/daily-revenue` & `/export` (Authorized)
*   `GET /api/v1/Reports/monthly-revenue` & `/export` (Authorized)
*   `GET /api/v1/Reports/attendance` & `/export` (Authorized)
*   `GET /api/v1/Reports/membership-sales` & `/export` (Authorized)
*   `GET /api/v1/Reports/expiring-memberships` & `/export` (Authorized)
*   `GET /api/v1/Reports/refunds` & `/export` (Authorized)
*   `GET /api/v1/Reports/cashier-activity` & `/export` (Authorized)

### 2.2 Request/Response Data Shapes

#### Get Daily Revenue JSON Response
```json
{
  "success": true,
  "message": "Success",
  "data": [
    {
      "date": "2026-07-02T00:00:00",
      "transactionCount": 5,
      "grossAmount": 10000.00,
      "totalDiscount": 500.00,
      "netAmount": 9500.00
    }
  ],
  "errors": []
}
```

#### CSV Export Response (e.g. `/api/v1/reports/daily-revenue/export`)
*   **Content-Type**: `text/csv`
*   **Content-Disposition**: `attachment; filename=daily_revenue_20260701_20260703.csv`
*   **Payload**:
    ```csv
    Date,TransactionCount,GrossAmount,TotalDiscount,NetAmount
    2026-07-02,5,10000.00,500.00,9500.00
    ```

---

## 3. Data Models

*   **DailyRevenueReportDto**: Date, count, gross, discount, net.
*   **MonthlyRevenueReportDto**: Year-month string, count, gross, discount, net.
*   **AttendanceReportDto**: Attendance ID, member name, plan name, check-in, check-out.
*   **MembershipSalesReportDto**: Member, plan, amount, discount, net, date paid, method.
*   **ExpiringMembershipsReportDto**: Member, plan, start date, end date, status.
*   **RefundReportDto**: Payment ID, member name, receipt number, refund amount, date.
*   **CashierActivityReportDto**: Username, action description, details, timestamp, IP.

---

## 4. Security

*   **Role-Based Access Control (RBAC)**:
    *   Both `Administrator` and `Receptionist` roles are allowed to access attendance, sales, and expiring membership reports.
    *   `Cashier Activity Audits` and `Revenue/Refund` reports are restricted to the `Administrator` role to maintain security division of labor.

---

## 5. Integration Points

*   **Payments & Refunds**: Aggregates all financial data.
*   **Audit logs**: Parses cashier activity logs.
*   **Attendance logs**: Summarizes check-in foot traffic.
*   **Memberships**: Resolves expiring subscriptions.

---

## 6. Testing Coverage

The reporting E2E integration tests verify:
1.  **JSON responses**: Ensures reports return valid array structures.
2.  **CSV Headers**: Confirms `Content-Type` matches `text/csv`.
3.  **Active Plan Resolution**: Verifies that attendance reports look up and map active plan names at check-in time.

---

## 7. Known Limitations

*   **Memory Overhead**: Large report ranges returning millions of rows can cause high memory utilization. Paginated views or streaming response chunks are recommended if performance degrades.

---

## 8. Architecture Decisions

*   **Why Web API Controller CSV Formatting?**
    *   *Decision*: Emitting CSV directly from controllers keeps the client code extremely thin. Mobile apps or browser interfaces can initiate downloads natively without having to map raw JSON payloads.
