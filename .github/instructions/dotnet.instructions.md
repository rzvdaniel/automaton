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

- All schema changes require a migration — **always use the CLI, never write migration files by hand**:
  ```bash
  dotnet ef migrations add <Name> --project src/Automaton.Studio.Server
  ```
  Hand-crafted migrations silently skip the required `.Designer.cs` companion file; EF then ignores the migration and `Database.Migrate()` fails at runtime with confusing column errors.
- **Never run `dotnet ef database update`** — migrations are applied automatically at startup via `ApplyMigrations()` in `Program.cs`. Running it manually is redundant and may cause issues in CI/CD.
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

## Use Framework Utilities — Never Roll Your Own

Do not write custom helper methods that duplicate functionality the framework already provides. Always prefer built-in APIs.

**MIME / Content Types:**
```csharp
// ✅ DO — covers 400+ types, maintained by the framework
var provider = new FileExtensionContentTypeProvider();
if (!provider.TryGetContentType(fileName, out var contentType))
    contentType = "application/octet-stream";

// ❌ DON'T — hand-rolled switch that covers only a handful of types
string GetContentType(string fileName) => Path.GetExtension(fileName) switch
{
    ".mp3" => "audio/mpeg",
    ".pdf" => "application/pdf",
    _ => "application/octet-stream"
};
```

Other examples of framework APIs to prefer: `JsonSerializer` over manual JSON building, `Uri` over string concatenation, `Path.Combine` over `+` for paths, `Encoding` classes over manual byte conversion.

## No Hardcoded System Paths

Never hardcode OS-specific paths. Use framework APIs that resolve the correct path on every platform.

**Temporary files:**
```csharp
// ✅ DO — works on Windows (%TEMP%) and Linux (/tmp/)
var dir = Path.Combine(Path.GetTempPath(), "uploads", sessionId);

// ❌ DON'T — Linux-only, breaks on Windows
var dir = $"/tmp/uploads/{sessionId}";
```

**General path rules:**
- `Path.GetTempPath()` for temp directories
- `Environment.GetFolderPath(Environment.SpecialFolder.*)` for system folders
- `AppContext.BaseDirectory` for app-relative paths
- `Path.Combine(...)` always — never string-concatenate path segments
- `Directory.CreateDirectory(path)` is idempotent — always safe to call before writing

## Naming

- Classes: `PascalCase`
- Methods/Properties: `PascalCase`  
- Private fields: `_camelCase`
- Interfaces: `IPrefix`
- Constants class: `static class XxxNames / XxxTypes / XxxModes`

### File Paths and System Folders

Never hardcode OS-specific paths like `/tmp`, `C:\Temp`, or any absolute directory. Use framework APIs that resolve correctly on every platform:

```csharp
// ✅ DO — works on Linux (/tmp/) and Windows (%TEMP%)
var uploadsDir = Path.Combine(Path.GetTempPath(), "uploads", sessionId);

// ❌ DON'T — Linux only
var uploadsDir = $"/tmp/uploads/{sessionId}";

// ❌ DON'T — Windows only
var uploadsDir = $@"C:\Temp\uploads\{sessionId}";
```

Same applies in Python (used in runner controllers):
```python
# ✅ DO
import tempfile, os
uploads_dir = os.path.join(tempfile.gettempdir(), 'uploads', session_id)

# ❌ DON'T
uploads_dir = f'/tmp/uploads/{session_id}'
```

Other path APIs to use instead of hardcoded values:
- `Path.GetTempPath()` — OS temp directory
- `Environment.GetFolderPath(Environment.SpecialFolder.X)` — special folders
- `AppContext.BaseDirectory` — app install/output directory
- `Directory.GetCurrentDirectory()` — working directory

### MIME Types / Content Types

Never write a custom `GetContentType()` switch or dictionary. Use `FileExtensionContentTypeProvider` from `Microsoft.AspNetCore.StaticFiles` — it covers 400+ types and is always up to date:

```csharp
// ✅ DO
using Microsoft.AspNetCore.StaticFiles;

private static readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

private static string GetContentType(string fileName)
{
    if (_contentTypeProvider.TryGetContentType(fileName, out var contentType))
        return contentType;
    return "application/octet-stream";
}

// ❌ DON'T — incomplete, unmaintainable, always missing types
private static string GetContentType(string fileName) => Path.GetExtension(fileName) switch
{
    ".mp3" => "audio/mpeg",
    ".pdf" => "application/pdf",
    _ => "application/octet-stream"
};
```

### General Rule

Before writing any utility method that maps, converts, or resolves a well-known system concept (paths, MIME types, encodings, cultures, time zones, HTTP status codes), check whether the framework already provides it. It almost always does.


