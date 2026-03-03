# Automaton — Qwen Development Guide

## Project Overview

**Automaton** is an AI-first, cross-platform workflow automation platform. Users describe automations in natural language, generate flows with reusable/custom steps, build web apps that call those flows, and execute them securely via distributed runners.

### Core Architecture

| Layer | Technology | Location |
|-------|------------|----------|
| **Frontend** | Vue 3 + TypeScript + Vite + PrimeVue | `src/automaton-studio-vue/` |
| **Backend** | ASP.NET Core 9.0 REST API + SignalR | `src/Automaton.Studio.Server/` |
| **Runners** | .NET 9.0 Console App (SignalR client) | `src/Automaton.Runner/` |
| **Core** | MediatR-based workflow engine | `src/Automaton.Core/` |
| **Steps** | Built-in + custom step library | `src/Automaton.Steps/` |
| **MCP** | AI integration & model context protocol | `src/Automaton.Mcp/` |

### Key Capabilities

- **AI Flow Generation**: Natural language → generated flow with reusable/custom steps
- **Web Apps Platform**: Monaco editor, live preview, PWA support, publish to `/web/{slug}`
- **Custom Steps**: Python/PowerShell/Batch with inline editing and testing
- **Projects**: Group flows, apps, steps under shared settings and role-based access
- **Distributed Execution**: Secure runner nodes with real-time SignalR telemetry

---

## Quickstart

### Prerequisites

- **Node.js 18+** and npm/pnpm (Frontend)
- **.NET 9.0 SDK** (Backend & Runner)
- **MySQL 8.0+** or MariaDB 10.2+ (Database)
- **Docker Compose** (optional, for MySQL/Runner)

### 1. Database Setup (Docker)

```bash
cd c:\Work\automaton.worktrees\copilot-worktree-2026-02-23T22-05-33
cp .env.example .env
docker compose -f docker-compose.dev.yml up -d mysql
```

### 2. Backend Setup

```bash
cd src/Automaton.Studio.Server

# Configure connection string (user secrets)
dotnet user-secrets set "ConnectionStrings:MySqlConnection" "Server=localhost;Port=3306;Database=automaton;User=automaton_user;Password=AutomatonPass123!;"

# Apply EF Core migrations
dotnet ef database update

# Run the server
dotnet run
# → https://localhost:7091
```

### 3. Frontend Setup

```bash
cd src/automaton-studio-vue
npm install
npm run generate:types  # Generate TypeScript types from OpenAPI
npm run dev
# → http://localhost:5173
```

### 4. Runner (Docker Compose – Recommended)

```bash
# From repo root
cp .env.example .env

# Edit .env: Set AUTOMATON_SERVER_URL and AUTOMATON_API_KEY
# Generate API key in Studio: Settings > API Keys > Create (scope: Runner)

docker compose -f docker-compose.runner.yml up -d
docker compose -f docker-compose.runner.yml logs -f runner
```

---

## Development Commands

### Frontend (Vue.js)

```bash
cd src/automaton-studio-vue

npm run dev          # Dev server with HMR (http://localhost:5173)
npm run build        # Production build to dist/
npm run preview      # Preview production build
npm run type-check   # TypeScript validation
npm run lint         # ESLint
npm run format       # Prettier
npm run generate:types  # Generate types from OpenAPI spec
```

### Backend (ASP.NET Core)

```bash
cd src/Automaton.Studio.Server

dotnet run                      # Run with hot reload
dotnet build                    # Build check
dotnet test                     # Run unit tests
dotnet ef database update       # Apply migrations
dotnet ef migrations add Name   # Create new migration
```

### Runner

```bash
cd src/Automaton.Runner

dotnet run                      # Interactive mode
dotnet run -- auth              # Authenticate with server
dotnet run -- register          # Register runner
dotnet run -- status            # Show status
dotnet run -- service           # Service mode (for systemd/Windows Service)
```

---

## Project Structure

```
c:\Work\automaton.worktrees\copilot-worktree-2026-02-23T22-05-33\
├── src/
│   ├── automaton-studio-vue/      # Vue 3 + TypeScript frontend
│   │   ├── src/features/          # Feature modules (flows, apps, steps, runners, projects)
│   │   ├── src/shared/            # Shared components, services, composables, types
│   │   ├── src/router/            # Vue Router aggregation
│   │   └── src/assets/            # Styles, themes
│   │
│   ├── Automaton.Studio.Server/   # ASP.NET Core backend
│   │   ├── Controllers/           # REST API endpoints (25+)
│   │   ├── Services/              # Business logic
│   │   ├── Hubs/                  # SignalR hubs (runners, execution)
│   │   ├── Entities/              # EF Core entities
│   │   ├── Migrations/            # Database migrations
│   │   └── Program.cs             # Startup & DI
│   │
│   ├── Automaton.Runner/          # Distributed runner
│   │   ├── Services/              # HubService, FlowExecutionService
│   │   ├── Connection/            # SignalR connection handling
│   │   └── Program.cs             # CLI entry point
│   │
│   ├── Automaton.Core/            # Workflow engine
│   │   ├── Services/              # FlowExecuteService, orchestration
│   │   ├── Models/                # Shared models
│   │   └── Interfaces/            # Core abstractions
│   │
│   ├── Automaton.Steps/           # Built-in steps library
│   ├── Automaton.Core.Scripting/  # Python interop (pythonnet)
│   └── Automaton.Mcp/             # AI integration & MCP tools
│
├── docs/                          # Feature documentation & plans
├── docker/                        # Docker configs & entrypoint scripts
├── .github/
│   ├── skills/                    # Reusable agent skills
│   └── workflows/                 # CI/CD pipelines
└── docker-compose*.yml            # Docker Compose configs
```

---

## Key Documentation

| Document | Purpose |
|----------|---------|
| [AGENTS.md](AGENTS.md) | Comprehensive project overview & technical reference |
| [FOR-DEVELOPERS.md](FOR-DEVELOPERS.md) | Narrative of decisions, lessons, pitfalls |
| [CLAUDE.md](CLAUDE.md) | Claude-specific guidance & mental models |
| [src/automaton-studio-vue/REDESIGN_PLAN.md](src/automaton-studio-vue/REDESIGN_PLAN.md) | Vue frontend redesign specification |
| [docs/plans/](docs/plans/) | Feature implementation plans (including HTTP tunnel) |
| [docs/skills/](docs/skills/) | Concrete code patterns & examples |

---

## Development Patterns

### 1. Feature-Based Frontend Architecture

Each feature is self-contained with its own components, services, types, and store:

```
src/features/flows/
├── components/        # Vue components
├── composables/       # Composition API hooks (useFlows)
├── services/          # API calls (flowsService.ts)
├── stores/            # Pinia state (flowsStore.ts)
├── types/             # TypeScript interfaces
├── views/             # Page-level components
└── routes.ts          # Feature routes
```

### 2. Type Safety

**TypeScript interfaces must match C# DTOs exactly.** Use `const` objects instead of enums:

```typescript
// ✅ Good: Const object (better type narrowing)
export const FlowStatus = {
  Pending: 'Pending',
  Running: 'Running',
  Completed: 'Completed',
  Failed: 'Failed'
} as const;

export type FlowStatus = typeof FlowStatus[keyof typeof FlowStatus];

// ❌ Avoid: TypeScript enums
enum FlowStatus { Pending, Running, ... }
```

Generate types from OpenAPI spec:
```bash
npm run generate:types
```

### 3. Theme-Aware Styling

**Never hardcode background colors** in nested components. Inherit from parent:

```css
/* ✅ Good: Inherit or use adaptive variables */
.my-component {
  background-color: var(--p-surface-ground);
  color: var(--p-text-color);
}

/* ❌ Bad: Hardcoded (breaks dark mode) */
.my-component {
  background-color: white;
}
```

### 4. Post-Change Validation

After any code change, run:

```bash
# Frontend
npm run type-check
npm run lint

# Backend
dotnet build
dotnet test
```

---

## API Documentation

- **Swagger UI**: https://localhost:7091/swagger/index.html
- **OpenAPI JSON**: https://localhost:7091/swagger/v1/swagger.json

---

## Application Routes

| Route | Description |
|-------|-------------|
| `/` | Dashboard / Home |
| `/flows` | Flows list, AI generation, timeline, logs, history |
| `/apps` | Apps gallery, Monaco editor, preview, PWA settings |
| `/steps` | Custom steps library, editor (code + IO + tests) |
| `/runners` | Runner management, status, logs |
| `/projects` | Project organization, shared settings, permissions |
| `/organizations` | Multi-tenant structure, member management |
| `/settings` | User/app settings, API keys, AI keys |
| `/my-apps` | Apps assigned to current user |

---

## Agent Skills

Reusable skills in `.github/skills/`:

| Skill | Purpose |
|-------|---------|
| `post-change-validation` | Run type-check + build after edits |
| `vue/component-splitting-pattern` | Split heavy conditionals into separate components |
| `backend-dto-pattern` | C# DTO → TypeScript interface patterns |

---

## Common Pitfalls

| Problem | Solution |
|---------|----------|
| Components look weird in dark mode | Check parent backgrounds; don't hardcode in nested components |
| Frontend types don't match backend | Run `npm run generate:types` or sync manually |
| Tests pass locally but fail in CI | Check EF Core migrations and test data setup |
| SignalR messages not received | Verify runner connection; use groups for targeted messaging |
| API returns wrong structure | Check Swagger UI for source of truth |

---

## Critical Constraints

### ❌ Don't

1. Hardcode CSS backgrounds (breaks dark mode)
2. Use TypeScript enums (use `const` objects)
3. Skip EF Core migrations for database changes
4. Add dependencies without testing
5. Assume synchronous SignalR (always async/await)
6. Modify DTOs without updating TypeScript types
7. Delete tests when they fail (fix the code)

### ✅ Do

1. Match TypeScript to C# exactly (names, types, optionality)
2. Test both success and failure paths
3. Use `const` objects instead of enums
4. Implement theme-aware CSS (inherit from parents)
5. Add loading states for async operations
6. Show toast notifications for user feedback
7. Handle errors gracefully with user-friendly messages

---

## Active Development Plans

### Runner HTTP Tunnel

See [docs/plans/runner-http-tunnel-plan.md](docs/plans/runner-http-tunnel-plan.md) for the implementation plan to replace SignalR RPC with HTTP tunneling for app controller execution. This enables:

- True concurrent Python execution (hundreds of requests in-flight)
- Full async Python (SQLAlchemy, httpx, etc.)
- Process isolation (Python crash ≠ Runner crash)
- No GIL serialization for I/O-bound workloads

**Status**: Plan approved, ready for implementation.

---

## Environment Variables

### Backend (appsettings.Development.json or user-secrets)

```json
{
  "ConnectionStrings": {
    "MySqlConnection": "Server=localhost;Port=3306;Database=automaton;User=automaton_user;Password=AutomatonPass123!;"
  },
  "AiProviders": {
    "OpenAiApiKey": "sk-...",
    "AnthropicApiKey": "sk-ant-..."
  }
}
```

### Frontend (.env.development)

```
VITE_API_BASE_URL=https://localhost:7091
VITE_SIGNALR_HUB=/hubs/automaton
```

### Runner (.env)

```
AUTOMATON_SERVER_URL=https://host.docker.internal:7091
AUTOMATON_API_KEY=ak_runner_xxx
AUTOMATON_RUNNER_NAME=Docker-Dev-Runner
```

---

## Testing

### Frontend

```bash
cd src/automaton-studio-vue
npm run test  # Vitest + Vue Test Utils
```

Colocated `*.spec.ts` files next to components.

### Backend

```bash
cd src/Automaton.Studio.Server
dotnet test  # xUnit + Moq + InMemoryDatabase
```

Tests in `src/UnitTests/Automaton.Studio.Server.Tests/`

---

## Deployment

### Frontend Production Build

```bash
cd src/automaton-studio-vue
npm run build
npm run preview  # Test production build
```

Output: `dist/`

### Backend Production Build

```bash
cd src/Automaton.Studio.Server
dotnet publish -c Release
```

### Runner Docker Image

```bash
docker compose -f docker-compose.runner.yml build
docker compose -f docker-compose.runner.yml up -d
```

---

## Additional Resources

- **Screenshots**: [screenshots/](screenshots/)
- **Examples**: [examples/](examples/)
- **Scripts**: [scripts/](scripts/)
- **Azure Pipelines**: `azure-pipelines-*.yml`
- **License**: Apache License 2.0 (see [LICENSE](LICENSE))
