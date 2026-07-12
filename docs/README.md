# GymTrackPro — Documentation Index

> All documentation files for the GymTrackPro BSIT Capstone defense.

| # | Document | File | Purpose |
|:--|:--|:--|:--|
| 01 | Project Overview | [01_Project_Overview.md](./01_Project_Overview.md) | Problem statement, objectives, tech stack, scope |
| 02 | Scope & Limitations | [02_Scope_and_Limitations.md](./02_Scope_and_Limitations.md) | Detailed in/out-of-scope and known limitations |
| 04 | Software Architecture | [04_Software_Architecture.md](./04_Software_Architecture.md) | Architecture patterns, structure, auth flow, background workers |
| 08 | Database Analysis | [08_Database_Analysis.md](./08_Database_Analysis.md) | All 15 tables, columns, types, constraints, relationships |
| 09 | ERD | [09_ERD.md](./09_ERD.md) | Mermaid entity-relationship diagram |
| 10 | Use Cases | [10_Use_Cases.md](./10_Use_Cases.md) | Use case diagram + 9 detailed use case descriptions with sequence diagrams |
| 11 | Module Documentation | [11_Module_Documentation.md](./11_Module_Documentation.md) | All 10 modules: purpose, actors, business rules, data flow |
| 13 | API Documentation | [13_API_Documentation.md](./13_API_Documentation.md) | All 50+ API endpoints with policies, request/response |
| 13b | API ELI5 House Analogy | [13b_API_ELI5_House_Analogy.md](./13b_API_ELI5_House_Analogy.md) | Every endpoint described as a household object for presentations |
| 17 | Progress Report | [17_Progress_Report.md](./17_Progress_Report.md) | Development timeline, completion status, known issues |
| 19 | Capstone Presentation | [19_Capstone_Presentation.md](./19_Capstone_Presentation.md) | 15-slide presentation script with speaker notes |
| 20 | Defense Q&A | [20_Defense_QA.md](./20_Defense_QA.md) | 25 panel Q&A with detailed technical answers |

---

## Quick Reference

### User Roles
| Role | Int Value | Access |
|:--|:--|:--|
| Administrator | 0 | Full access |
| Receptionist | 1 | Day-to-day operations |
| GymGoer | 2 | Self-service only |

### Authorization Policies
| Policy | Allowed Roles |
|:--|:--|
| `FirebaseOnboarding` | Any verified Firebase user |
| `BackOffice` | Administrator, Receptionist |
| `OwnerOnly` | Administrator only |
| `GymGoerSelf` | GymGoer only |
| `ActiveAppUser` | Any synced user |

### Key API Endpoints
| Endpoint | Purpose |
|:--|:--|
| `POST /api/v1/auth/sync-user` | First login sync |
| `POST /api/v1/auth/activate` | Invite activation |
| `POST /api/v1/attendance/check-in` | Staff QR check-in |
| `POST /api/v1/me/attendance/check-in` | Self check-in |
| `GET /api/v1/dashboard/metrics` | Back-office KPI metrics |
| `GET /api/v1/me/dashboard` | Gym Goer dashboard projection |
| `GET /api/v1/me/digital-card` | Gym Goer's digital membership card |
| `POST /api/v1/subscriptions/renew` | Transactional renewal + payment |
| `GET /api/v1/reports/daily-revenue/export` | CSV download |

### Database Tables
Members, Users, AttendanceLogs, AttendanceAdjustments, AttendanceOperations, Subscriptions, MembershipPlans, MembershipPauses, Payments, Notifications, AuditLogs, SystemSettings, WalkInVisitors, AccountInvites, MemberProjectionVersions
