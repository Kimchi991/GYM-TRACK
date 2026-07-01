# Coding Standards

This document defines the team's coding conventions, formatting standards, and structural guidelines for GymTrackPro. Following these standards keeps code consistent and maintainable for our three-person team.

---

## 🔠 Naming Conventions

We adhere to the Microsoft C# formatting guidelines:

### C# Casing Rules
*   **PascalCase:** Used for Class names, Interface names (prefixed with `I`), Method names, Properties, and Public Events.
    ```csharp
    public interface IMemberRepository { ... }
    public class MemberService {
        public string FullName { get; set; }
        public async Task RegisterMemberAsync() { ... }
    }
    ```
*   **camelCase:** Used for Method parameters and Local variables.
    ```csharp
    public void UpdateStatus(int memberId, string newStatus) {
        var statusChanged = true;
    }
    ```
*   **camelCase with leading underscore (`_camelCase`):** Used for Private fields.
    ```csharp
    private readonly IMemberRepository _memberRepository;
    ```
*   **UPPER_CASE:** Used for Constants.
    ```csharp
    public const int MAX_PAUSE_DAYS = 90;
    ```

### Suffixes
*   **Async Methods:** Any method that returns a `Task` or `Task<T>` must end with the `Async` suffix.
    ```csharp
    // Good
    public async Task<UserDto> AuthenticateAsync(LoginRequestDto request)
    // Bad
    public async Task<UserDto> Authenticate(LoginRequestDto request)
    ```

---

## 🛠️ Code Structure & Best Practices

1.  **One Class Per File:** Every class, interface, and enum must reside in its own dedicated source file.
2.  **Nullable Reference Types:** Enabled globally (`<Nullable>enable</Nullable>`) across all three projects in the solution to prevent runtime `NullReferenceExceptions`.
3.  **Avoid Magic Strings:** Never hardcode strings for roles, statuses, configuration keys, or error messages directly in logic. Define them in static classes inside `GymTrackPro.Shared` under `Constants/` or as Enums:
    ```csharp
    // Good
    if (user.Role == UserRoles.Administrator)
    // Bad
    if (user.Role == "Administrator")
    ```
4.  **Constructor Dependency Injection:** Inject all required repositories, contexts, or services through class constructors. Avoid using service locators (like passing around container instances).
    ```csharp
    public class MemberService {
        private readonly IMemberRepository _memberRepository;

        public MemberService(IMemberRepository memberRepository) {
            _memberRepository = memberRepository;
        }
    }
    ```
5.  **Single Responsibility Principle (SRP):** Keep methods short, concise, and focused on exactly one task. If a method exceeds 30–40 lines, evaluate splitting it.
6.  **XML Comments:** Reserve XML documentation comments (`///`) only for public APIs, complex interfaces, and shared utilities in `GymTrackPro.Shared`. Code-behind files and basic implementations do not need XML documentation unless they contain complex algorithms.
7.  **Namespace Alignment:** Ensure namespaces match the folder directories. If a class is located in `GymTrackPro.API/Services/`, its namespace must be `GymTrackPro.API.Services`.
