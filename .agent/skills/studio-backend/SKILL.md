---
name: studio-backend
description: Use when changing Automaton Studio Server, REST APIs, SignalR hubs, EF Core entities/migrations, auth, API keys, app publishing, runners, hosted runners, or admin backend behavior.
---

# Studio Backend

Use this skill when touching:

- `src/Automaton.Studio.Server/**`
- controllers
- services
- EF Core entities or migrations
- authentication and authorization
- API keys
- app publishing/forking/remix
- runners and hosted runners
- admin APIs
- SignalR hubs

## API rules

- Keep controllers thin; put business logic in services.
- Use async APIs consistently.
- Preserve cancellation tokens where surrounding code uses them.
- Return actionable errors.
- Avoid leaking secrets or internal details in API responses.
- Keep request/response DTOs compatible unless a breaking change is intentional.
- Update frontend generated types when OpenAPI DTOs change and the project requires it.

## Well-known strings

Do not scatter magic strings for fixed values such as modes, statuses, roles, providers, event types, scopes, or action names.

Use static classes with `const string` values when values are serialized as strings.

```csharp
public static class ConversationModes
{
    public const string Plan = "plan";
    public const string Agent = "agent";
}
```

Avoid C# enums when JSON/client comparisons depend on stable string values.

## Database rules

- Add migrations intentionally.
- Create EF Core migrations with `dotnet ef migrations add <Name> --project src/Automaton.Studio.Server`; do not create migration files by hand.
- Avoid destructive migrations unless explicitly requested.
- Do not add subscription columns for every entitlement unless query/indexing needs justify it.
- Prefer extending `PlanCapabilities` for plan capabilities.
- Check migration contents before committing.

## Secrets and API keys

- Never log raw API keys, access tokens, refresh tokens, AI provider keys, connection strings, or `.env` values.
- Show raw generated API keys only in intentional one-time reveal flows.
- Store hashes or redacted values where expected.
- Hosted runner command previews must stay redacted.

## Audit and logging

- For audit event design, metadata sanitization, event naming, and diagnostic logging decisions, load `.agent/skills/audit-logging/SKILL.md`.

## Verification

Run the most specific backend test project if known. If unsure:

```bash
dotnet test
```

For EF changes:

```bash
dotnet ef migrations list --project src/Automaton.Studio.Server
```

For API DTO changes that affect Studio frontend, also run the relevant frontend type generation/build steps from `.agent/harness/verification.md`.
