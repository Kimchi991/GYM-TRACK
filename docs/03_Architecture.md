# System Architecture

GymTrackPro uses a Client-Server architecture utilizing a modern layered design on both the Client and Server sides. It is engineered with an **Offline-First** mindset to support constant operability at gym check-in desks.

---

## 🏛️ Overall Architecture Diagram

```mermaid
graph TD
    subgraph Client App (.NET MAUI Mobile)
        View[Presentation Layer - Views / XAML]
        VM[ViewModel Layer]
        ServClient[Service Layer - Sync & Validation]
        RepoSQLite[Repository Layer - SQLite]
        SQLite[(SQLite DB)]
        
        View <--> VM
        VM <--> ServClient
        ServClient <--> RepoSQLite
        RepoSQLite <--> SQLite
    end

    subgraph Backend Services (ASP.NET Core)
        API[REST Web API Controllers]
        ServAPI[Service Layer - Business Logic]
        RepoMySQL[Repository Layer - EF Core]
        MySQL[(MySQL DB)]
        
        API <--> ServAPI
        ServAPI <--> RepoMySQL
        RepoMySQL <--> MySQL
    end

    ServClient -- HTTPS / REST --> API
```

---

## 📂 Architecture Layers

### 1. Presentation Layer (Mobile App)
Handles the user interface. It consumes the ViewModels and exposes visual elements to the end user.
*   **Technologies:** XAML, .NET MAUI Pages, UserControls.
*   **Rule:** Views must only bind to properties and commands on their respective ViewModels. They must never contain business logic or query databases directly.

### 2. ViewModel Layer (Mobile App)
Handles user interactions, UI state, and notifications. It translates commands from the View to call the appropriate service methods.
*   **Technologies:** CommunityToolkit.Mvvm (ObservableObject, ObservableProperty, RelayCommand).
*   **Rule:** ViewModels must not know about database details (e.g. SQLite queries or MySQL connection strings). They communicate exclusively with the Service Layer.

### 3. Service Layer (Client & Server)
Where the business rules live.
*   **Client Services:** Coordinate API calls, perform basic input validation, check network connectivity, and handle the offline synchronization queue.
*   **Server Services:** Execute core validations, authenticate sessions, process records, and control transactional operations.

### 4. Repository Layer (Client & Server)
Abstracts data access.
*   **Client Repositories:** Run raw SQL or SQLite-net ORM queries against the local SQLite database.
*   **Server Repositories:** Use Entity Framework Core to perform operations against the online MySQL database.
*   **Rule:** All database-related changes should happen here, shielding the business logic from schema details.

---

## 🔄 Offline-First & Synchronization Mechanism

To ensure the gym receptionist can check in members even during an internet outage, GymTrackPro writes data locally first and syncs upstream asynchronously.

### 🗳️ The Synchronization Queue
1.  **Write Operations:** When a user creates or edits a record (e.g. checks in a member, records a payment), the client app writes the change directly to SQLite.
2.  **Queue Entry:** A sync queue entry is written to SQLite containing the target table name, target record ID, the action type (Create, Update, Delete), and a `LastModified` timestamp.
3.  **Connection Monitor:** The app listens to the network status. When a connection is detected, a background worker processes the sync queue:
    *   It retrieves the pending queue items.
    *   It sends HTTP requests with payload to the Web API.
    *   Upon receiving an HTTP 200 OK from the API, it marks the local SQLite record as "Synced" and purges the sync queue entry.

### ⚔️ Conflict Resolution ("Newest Update Wins")
When the API receives an update for a record that has also been modified elsewhere:
*   It compares the `LastModified` timestamp from the incoming client payload with the database record's `LastModified` timestamp.
*   The record with the **newest** timestamp is saved.
*   An audit log entry is written to record the resolution.

### 🗑️ Soft Deletion Rules
Records are never deleted immediately.
1.  On delete, a `Deleted` flag (or `Status = 'Inactive'`) is set on the local SQLite record.
2.  An update is queued in the sync queue.
3.  The API processes the soft delete.
4.  Only after confirmation is the local record updated accordingly.
