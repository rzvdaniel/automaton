# CLAUDE.md

Claude-specific configuration and guidance for working with the Automaton codebase.

## Project Context

**See [AGENTS.md](AGENTS.md) for comprehensive project documentation.**

This file contains Claude-specific extensions and mental models.

## Claude's Mental Model

When working on Automaton, adopt this mental framework:

### 1. **Think in Layers**
The codebase is strictly layered: Frontend → REST API → Services → Core Engine → Persistence

- **Frontend changes** shouldn't require backend changes (via API contracts)
- **Backend changes** must maintain API compatibility or version endpoints
- **Core engine changes** impact runners, always test end-to-end
- **Database changes** require migrations with rollback safety

**Implication:** When adding a feature, implement each layer independently if possible. Don't create tight coupling between presentation and persistence.

### 2. **Real-Time is Critical**
SignalR isn't optional—it's how runners receive commands and the frontend gets execution updates.

- Runner connection failures cascade into flow failures
- Execution logs must stream in real-time (no polling)
- Frontend must handle reconnection gracefully
- Always test with runners actually disconnected to verify resilience

**Implication:** When implementing execution features, assume the network will fail. Design for recovery, not prevention.

### 3. **Type Safety is Non-Negotiable**
The system bridges three type systems: C# (backend), TypeScript (frontend), Python (custom steps)

- **Critical rule:** TypeScript interfaces must match C# DTOs exactly (property names, types, optionality)
- **Enums:** Use `const` objects in TypeScript instead of TypeScript enums (enables type narrowing)
- **Custom steps:** Python code runs in isolated context—validate inputs rigorously

**Implication:** When modifying a DTO, immediately update the TypeScript interface. Generate types from the OpenAPI spec when needed.

### 4. **Projects = Multi-Tenancy Boundary**
The entire permission and resource model revolves around project membership.

- Resources belong to projects (project = owner)
- Users access resources via project membership roles
- "Personal" is a UI concept (user-owned resources), not a database concept
- Personal items linked to a project can't be deleted from Personal view

**Implication:** When adding a new resource type (flows, apps, steps), it must support project linking. Check existing migrations for the pattern.

### 5. **Theme Inheritance Matters**
The UI supports light/dark themes. Background colors are inherited, not set.

- Set `background-color` only at the root or major container level
- Nested components inherit `--p-surface-ground`
- Borders and text adapt via CSS variables
- Dark mode is a class toggle (`app-dark`)

**Implication:** Never hardcode backgrounds in nested components. If a component looks wrong, check parent backgrounds first.

### 6. **Testing is Your Safeguard**
This codebase has good test coverage. Use it.

- Frontend: Vitest + Vue Test Utils (unit/integration)
- Backend: xUnit + Moq + InMemoryDatabase
- Run tests after **any** change (not just after completing a feature)
- Test both success **and** failure paths

**Implication:** Before making a PR, run the full test suite. If tests fail, the change is incomplete.

## When to Use Agent Skills

This project includes reusable Agent Skills in `docs/skills/`:

- **[theme-aware-styling](docs/skills/theme-aware-styling/SKILL.md)** - How to write theme-aware CSS
- **[implementing-features](docs/skills/implementing-features/SKILL.md)** - Full-stack feature workflow
- **[web-app-integration](docs/skills/web-app-integration/SKILL.md)** - How published apps call flows
- **[signalr-communication](docs/skills/signalr-communication/SKILL.md)** - Real-time runner/frontend patterns
- **[testing-patterns](docs/skills/testing-patterns/SKILL.md)** - Test examples and best practices

Invoke these skills when you need concrete code examples for a specific pattern.

## Critical Constraints

### ❌ Don't

1. **Hardcode CSS backgrounds** - Breaks dark mode
2. **Use TypeScript enums** - Use `const` objects instead
3. **Skip migrations** - Database changes require EF Core migrations
4. **Add dependencies without testing** - The codebase is tightly integrated
5. **Assume synchronous SignalR** - Always use async/await
6. **Modify DTOs without updating types** - Causes frontend bugs
7. **Delete tests when they fail** - Fix the code instead

### ✅ Do

1. **Match TypeScript to C# exactly** - Property names, types, optionality
2. **Test both success and failure** - Error paths are important
3. **Use const objects instead of enums** - Better type narrowing
4. **Implement theme-aware CSS** - Inherit from parents, use `--p-surface-ground`
5. **Add loading states** - Every async operation needs one
6. **Show toast notifications** - All user feedback via toast
7. **Handle errors gracefully** - Try/catch with user-friendly messages

## Common Pitfalls

### "Components look weird in dark mode"
→ Check parent background colors. Nested components should not set backgrounds.

### "Frontend types don't match backend responses"
→ Generate types from OpenAPI spec or manually sync DTOs. Use `npm run generate:types`.

### "Tests pass locally but fail in CI"
→ Database migrations or test data setup issue. Check EF Core migrations in `src/Automaton.Studio.Server/Migrations/`.

### "SignalR messages aren't received"
→ Verify runners are actually connected (check admin logs). Use groups for targeted messaging.

### "API endpoint returns wrong structure"
→ Check Swagger UI (`https://localhost:7091/swagger`) for source of truth. Update backend first, then types.

## Architectural Decisions Worth Understanding

**Why SignalR instead of polling?**
Flows can run for hours. Polling would waste resources. SignalR keeps persistent connections with automatic reconnection.

**Why projects instead of users?**
Single-user flows are boring. The system is designed for teams sharing automation logic. Projects enable role-based access without hardcoding permissions per resource.

**Why const objects instead of TypeScript enums?**
Enums create a separate type namespace. Const objects are just values, enabling better type narrowing and compatibility with C#.

**Why feature-based folders instead of by-type?**
Keeps related code together (types, services, components, stores). Easier to delete/refactor features. Matches how brains organize information.

## Development Velocity Tips

1. **Run type-check frequently** - `npm run type-check` catches issues early
2. **Use Swagger for the source of truth** - When in doubt, check the API spec
3. **Start with tests** - Write test cases before implementing features
4. **Keep services simple** - Just API calls, no business logic
5. **Use composables for shared logic** - Promotes reuse and testability
6. **Test in both themes** - Toggle theme switcher to verify styling

## When to Check AGENTS.md

- Building/running the project
- Finding where code lives
- Understanding the project structure
- Learning about specific features
- Deployment and DevOps

## When to Check a Skill

- Implementing theme-aware CSS
- Adding a full-stack feature
- Calling flows from published apps
- Using SignalR for real-time updates
- Writing tests

## When to Trust Your Judgment

- Architecture decisions (ask via code review)
- Refactoring opportunities
- Performance optimizations
- Error handling strategies
- Testing edge cases

## Quick Reference: Post-Change Validation

After any change, run:

```bash
# Frontend
npm run type-check  # Catch type mismatches
npm run lint        # Style consistency
npm run test        # Verify behavior

# Backend
dotnet build        # Compilation check
dotnet test         # Verify behavior
```

If all pass, you're good. If any fail, the change is incomplete.
