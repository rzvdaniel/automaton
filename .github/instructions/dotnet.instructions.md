---
applyTo: "**/*.cs,**/*.csproj,**/Program.cs,**/Startup.cs"
---

# .NET / C# Coding Standards — Automaton

## Core Principles

Apply SOLID principles. Write clean, maintainable, testable code. Always use async/await for I/O. Prefer composition over inheritance.

## No Magic Strings

Replace string literals that represent a fixed set of known values with a `static class` of `const string` properties. Never use C# `enum` for values serialized over the wire or compared to JSON — enums require `.ToString()` and break silently.

```csharp
// ✅ DO
public static class ConversationModes
{
    public const string Plan  = "plan";
    public const string Agent = "agent";
}

// ❌ DON'T
if (request.Mode == "plan") { ... }
```

Apply this to: mode names, provider names, role names, status values, event type identifiers, action names, and any string used in more than one place.

## DTOs, Not Entities

Never return EF Core entity classes from API endpoints. Always map to a DTO. This prevents serialization loops, data leakage, and tight coupling between persistence and API contracts.

## Async/Await

- Every I/O operation must be `async`/`await` — no `.Result` or `.Wait()`
- Use `CancellationToken` for all controller actions and long-running operations
- Return `Task<IActionResult>` or `Task<ActionResult<T>>` from controllers

## Dependency Injection

- Register services in `Program.cs` — prefer `AddScoped` for request-scoped services
- Inject via constructor, never use `ServiceLocator`
- Program to interfaces (`IUserService`), not concrete classes

## Design Patterns in Use

- **Repository + Service** — Controllers call Services, Services call Repositories or DbContext
- **MediatR** — used in `Automaton.Core` for workflow orchestration
- **Options pattern** — strongly-typed config via `IOptions<T>`
- **Factory** — use factory methods or static `From()` when object construction is complex

## EF Core / Database

- All schema changes require a migration: `dotnet ef migrations add <Name>`
- Never write raw SQL unless EF cannot express it
- Use `AsNoTracking()` for read-only queries

## Error Handling

- Use `try/catch` at controller or service boundaries, not deep in repositories
- Log exceptions with structured logging (`ILogger<T>`)
- Return meaningful HTTP status codes — don't always return `200 OK`

## Testing

- xUnit for unit tests, with Moq for mocking
- Use `InMemoryDatabase` for EF Core integration tests
- Test both success and failure paths — error handling is not optional
- Arrange/Act/Assert structure; one logical assertion per test

## Naming

- Classes: `PascalCase`
- Methods/Properties: `PascalCase`  
- Private fields: `_camelCase`
- Interfaces: `IPrefix`
- Constants class: `static class XxxNames / XxxTypes / XxxModes`
