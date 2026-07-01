# Code Style Guide

To maintain a consistent codebase that is easy to read, refactor, and review, please follow these C# and XAML standards for GymTrackPro.

---

## 📐 C# Naming Conventions

We adhere to Microsoft's official C# naming conventions:

| Element | Casing | Example |
| :--- | :--- | :--- |
| **Classes / Structs** | PascalCase | `MemberRepository` |
| **Interfaces** | PascalCase (prefixed with `I`) | `IMemberRepository` |
| **Methods** | PascalCase | `RegisterMemberAsync` |
| **Properties** | PascalCase | `DateRegistered` |
| **Parameters** | camelCase | `memberId` |
| **Local Variables** | camelCase | `activeSubscription` |
| **Private Fields** | camelCase (prefixed with `_`) | `_memberRepository` |
| **Constants** | UPPER_CASE | `MAX_PAUSE_DAYS` |

### Code Structure Rules
*   **Braces:** Place open and close braces `{}` on their own lines (Allman style).
*   **Spacing:** Use 4 spaces for indentation (no tabs).
*   **Async/Await:** Methods returning Tasks must use the `Async` suffix (e.g. `RecordPaymentAsync`) and always propagate cancellation tokens where appropriate.

---

## 🎨 XAML Coding Standards

*   **View Naming:** Views/Pages must end with the suffix `Page` (e.g. `MembersPage.xaml`, `RenewMembershipPage.xaml`).
*   **Control Naming:** Controls that require code-behind reference or specific data binding must be named clearly using PascalCase:
    *   Buttons: `SubmitButton`, `CancelButton`
    *   Entries: `UsernameEntry`, `AmountEntry`
    *   Labels: `ErrorMessageLabel`, `TotalCountLabel`
    *   ListViews / CollectionViews: `MembersListView`
*   **Layouts:** Prefer Grid over StackLayout for complex screens to optimize performance and prevent nesting overhead.

---

## 🏗️ MVVM & Dependency Injection

*   **Property Binding:** Use `CommunityToolkit.Mvvm` attributes (`[ObservableProperty]`) on fields to automatically generate properties:
    ```csharp
    // Good
    [ObservableProperty]
    private string _firstName;
    ```
*   **Command Binding:** Use `[RelayCommand]` on methods returning `Task` or `void` to generate commands:
    ```csharp
    // Good
    [RelayCommand]
    private async Task SaveMemberAsync() { ... }
    ```
*   **No Direct DB Access:** Under no circumstances should a Page or ViewModel make SQL connections, call DB contexts, or access SQLite/MySQL APIs directly. All data access must go through injected repositories.
*   **DI Injection:** Inject dependencies via constructor injection. Avoid using service locators.
