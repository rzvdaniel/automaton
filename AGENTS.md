# AGENTS.md

This document is a practical guide for developers and agents working on the Automaton codebase. It follows industry-standard conventions and reflects the current architecture: Vue.js + TypeScript frontend, ASP.NET Core backend, and cross-platform runners.

## Project Overview

Automaton is an AI-first workflow automation platform. Users describe automations in natural language, design web apps, author custom steps, and execute flows securely via distributed runners.

**Core Architecture:**
- **Frontend**: Vue 3 + TypeScript + Vite + PrimeVue (located in `src/automaton-studio-vue/`)
- **Backend**: ASP.NET Core 9.0 REST API + SignalR (located in `src/Automaton.Studio.Server/`)
- **Runners**: Cross-platform console app for distributed execution (located in `src/Automaton.Runner/`)
- **Core Libraries**: `Automaton.Core`, `Automaton.Steps`, `Automaton.Core.Scripting`, `Automaton.Mcp`

**Primary Capabilities:**
- **AI Flow Generation**: Natural language → generated flow with reusable/custom steps
- **Web Apps Platform**: Author, preview, publish frontend apps that call flows via helper APIs
- **Custom Steps**: Python/PowerShell/Batch steps with inline code editing and testing
- **Projects**: Group flows, apps, steps under shared settings and access control
- **Distributed Execution**: Secure runner nodes with real-time telemetry via SignalR

## Application Routes

The Vue.js frontend implements feature-based routes (see `src/automaton-studio-vue/src/features/`):

- `/` – Dashboard / Home
- `/flows` – Flows list and management
  - `/flows/generate` – AI-powered generation (natural language)
  - `/flows/:id/view` – Flow timeline and details
  - `/flows/:id/logs` – Execution logs
  - `/flows/:id/history` – Execution history
- `/apps` – Apps gallery
  - `/apps/:id/editor` – Visual IDE (Monaco) + live preview
  - `/apps/:id/settings` – PWA and deployment settings
- `/steps` – Custom steps library
  - `/steps/:id/editor` – Step designer (code + IO + tests)
- `/runners` – Runner management
- `/projects` – Project organization
  - `/projects/:id/settings` – Membership, shared variables, permissions
- `/organizations` – Organization management
  - `/organizations/:id` – Organization dashboard
  - `/organizations/:id/members` – Member management and invitations
  - `/organizations/:id/apps` – App assignments to users
  - `/organizations/:id/projects` – Projects in organization
  - `/organizations/:id/settings` – Organization settings
- `/my-apps` – Apps assigned to current user (AppUser portal)
- `/apikeys` – API keys management
- `/ai-keys` – AI provider keys
- `/settings` – Application settings
- `/account` – User profile
- `/auth/login`, `/auth/register` – Authentication

## Project Structure

**Frontend (`src/automaton-studio-vue/`):**
- `src/features/` – Feature modules (auth, dashboard, flows, apps, steps, runners, projects, settings)
- `src/shared/` – Shared components, services (Axios, SignalR), composables, utils, types
- `src/router/` – Vue Router aggregation across features
- `src/assets/` – Styles, images, themes
- `App.vue` and `main.ts` – App entry
- `package.json`, `vite.config.ts`, `tsconfig.json` – Tooling and configuration

**Backend (`src/Automaton.Studio.Server/`):**
- `Controllers/` – 25+ REST endpoints (Flows, Apps, Runners, Steps, Projects, etc.)
- `Services/` – Business logic (FlowExecution, FlowGeneration, Apps, Steps)
- `Data/`, `Entities/`, `Migrations/` – EF Core data layer
- `Hubs/` – SignalR hubs for runners and flow execution
- `Program.cs` – Startup and DI configuration

**Execution & Core:**
- `src/Automaton.Runner/` – Console runner (SignalR client, resilient execution)
- `src/Automaton.Core/` – Workflow engine (MediatR, variable mapping, orchestration)
- `src/Automaton.Steps/` – Built-in steps
- `src/Automaton.Core.Scripting/` – Python interop (pythonnet)
- `src/Automaton.Mcp/` – AI integration and MCP tools
- `src/Automaton.Web/` – Static marketing site

**Tests (`src/UnitTests/`):**
- `Automaton.Core.Tests/`, `Automaton.Studio.Server.Tests/`, `Automaton.Runner.Tests/`

## Development Environment Setup

**Frontend:**
- Node.js 18+ and npm/pnpm
- VS Code with Volar (Vue) recommended

**Backend:**
- .NET 9.0 SDK
- MySQL 5.7+ or MariaDB 10.2+
- IDE: VS Code, Visual Studio, or JetBrains Rider

**Entity Framework CLI (migrations):**
```bash
dotnet tool install --global dotnet-ef
```

## Building and Running the Project

### Frontend (Vue.js)

```bash
cd src/automaton-studio-vue
npm install
npm run dev        # http://localhost:5173 with HMR

npm run build      # Production build to dist/
npm run preview    # Preview production build locally

npm run type-check # TypeScript checks
npm run lint       # ESLint
npm run format     # Prettier
```

### Backend (ASP.NET Core)

```bash
cd src/Automaton.Studio.Server
dotnet run                    # https://localhost:7091

dotnet publish -c Release     # Production build

dotnet ef database update     # Apply EF Core migrations
```

### Runner (Docker Compose – Preferred)

Use Docker Compose for streamlined runner deployment and management.

```bash
# From repo root
cp .env.example .env

# Set required vars in .env
# AUTOMATON_SERVER_URL=https://your-server-url
# AUTOMATON_API_KEY=ak_runner_xxx

# Start the runner
docker compose -f docker-compose.runner.yml up -d

# View logs
docker compose -f docker-compose.runner.yml logs -f runner

# Stop/remove
docker compose -f docker-compose.runner.yml down
```

Manual runner (fallback for local debugging):

```bash
cd src/Automaton.Runner
dotnet build -c Release
dotnet publish -c Release --self-contained false
AUTOMATON_API_KEY=ak_runner_xxx automaton-runner
```

See `docs/runners/docker-runner.md` for advanced options.

## Backend API

**Swagger / OpenAPI:**
- Swagger UI: `https://localhost:7091/swagger/index.html`
- OpenAPI JSON: `https://localhost:7091/swagger/v1/swagger.json`

The frontend generates TypeScript types from the OpenAPI spec via `openapi-typescript`.

## Important Developer Notes

### Frontend (Vue.js)

**Feature-Based Architecture:**
```
src/features/flows/
├─ components/       # Vue components
├─ composables/      # Composition API hooks (e.g., useFlows)
├─ services/         # API calls (flowsService.ts)
├─ stores/           # Pinia state (flowsStore.ts)
├─ types/            # TypeScript interfaces
├─ views/            # Page-level components
└─ routes.ts         # Feature routes
```

**State Management:** Pinia stores accessed via `useXxxStore()`. Keep stores lean; colocate logic per feature.

**HTTP Client:** Axios instance (`src/shared/services/api/axios-instance.ts`) injects JWT automatically via interceptors.

**Type Generation:**
```bash
npm run generate:types  # Generates src/shared/types/api/ from OpenAPI
```

**Component Testing:** Vitest + Vue Test Utils with colocated `*.spec.ts` files.

**Component Design:**
- **Split heavy conditionals**: When a component has large `v-if/v-else` blocks (>50 lines each) with diverging logic, extract each path into separate components. Use a thin orchestrator view for conditional rendering.
- **Example**: `DashboardView.vue` renders `StandardDashboard.vue` or `AppUserDashboard.vue` based on user role, keeping each dashboard focused and testable.
- **See**: [.github/skills/vue/component-splitting-pattern/SKILL.md](.github/skills/vue/component-splitting-pattern/SKILL.md) for detailed refactoring guidance.

### Implementing a New Feature

When adding a new feature, use the feature-based structure with components, services, types, and views. Match C# DTOs exactly in TypeScript, use `const` objects instead of enums, and implement loading states with toast notifications.

**For complete step-by-step guide with code examples, see:** [docs/skills/implementing-features/SKILL.md](docs/skills/implementing-features/SKILL.md)

**Reference Implementation:** API Keys feature in `src/automaton-studio-vue/src/features/settings/`

### Backend (ASP.NET Core)

**No magic strings for well-known values.** Whenever code uses string literals to identify a mode, state, role, or category that has a fixed set of known values — replace them with a `static class` of `const string` properties (not a C# `enum`, because enums need `.ToString()` when serialized over the wire or compared to JSON strings).

```csharp
// ✅ DO — static class with const string values
public static class ConversationModes
{
    public const string Plan  = "plan";
    public const string Agent = "agent";
}

// ❌ DON'T — scattered magic strings
if (request.Mode == "plan") { ... }
public string Mode { get; set; } = "agent";
```

Apply this pattern to: mode names, provider names, role names, status values, event type identifiers, action names, and any other string that appears in more than one place.

### API Key Scopes

API keys authenticate runners, web apps, and integrations. Format: `ak_<scope>_<random>`.

| Example Format | Description | Use Case |
|----------------|-------------|----------|
| ak_runner_xxx  | Runner keys | Console runners, automation agents |
| ak_execute_xxx | Execution keys | CI/CD pipelines, external integrations |
| ak_readonly_xxx| Read-only keys | Monitoring, reporting tools |
| ak_xxxxxxxxx   | Flexible format | Any scope (configured on server) |

## Apps (Web App Platform)

Create, preview, and publish frontend apps (HTML/CSS/JS) that call Flows as their backend via REST.

**Key Concepts:**
- Visual app editor (Monaco) with live preview and sandboxed iframe
- Runner selection for executions triggered from the app
- PWA settings (manifest, theme colors, icons)
- AI-assisted frontend editing

**Publishing & Access:**
- Draft vs. Published states
- Published apps get a `PublicUrl` (`/web/{publicUrl}`)
- Public (no login) or Protected (requires Automaton login)
- Publish/Unpublish flows with clear status badges

**Calling Flows from Published Apps:**

Published apps use the `window.automatonApi` helper provided by the platform. Basic usage:

```javascript
const flowId = 'your-flow-id';
const payload = {
  inputs: { name: 'Alice', email: 'alice@example.com' },
  runnerNames: ['runner-1']
};

window.automatonApi.executeFlow(flowId, payload)
  .then(result => console.log('Execution result', result))
  .catch(err => console.error('Execution error', err));
```

**For complete API reference, request/response formats, and examples, see:** [docs/skills/web-app-integration/SKILL.md](docs/skills/web-app-integration/SKILL.md)

## Projects

Projects organize related Flows, Apps, and Custom Steps with shared settings and access control.

**Key Features:**
- Containers for assets (one project per asset)
- Shared variables available to member flows
- Project-level permissions and roles
- Assets remain (orphaned) if a project is deleted

**UI Implementation (Vue):**
- `src/features/projects/views/ProjectsListView.vue` – list, create/delete, quick actions
- `src/features/projects/views/ProjectSettingsView.vue` – manage assets, shared variables, permissions

**Scopes (UI semantics):**
- **Personal**: Shows resources you own (via `WebAppUsers`/`FlowUsers`/etc.), even if they’re also linked to a project. It means “mine,” not “unassigned.”
- **Project**: Shows resources in the selected project that you can access via membership (includes items owned by others).

**Personal list badges:**
- Personal views display a small badge `Project: {name}` on items linked to a project. These items cannot be deleted from Personal; manage them under the project.

## Feature Modules

The frontend is composed of self-contained feature modules:

- **auth/** – Login/Register, token management
- **dashboard/** – Overview, quick actions, charts
- **flows/** – Flow list, AI generation, timeline, logs/history
- **apps/** – App gallery, editor (Monaco), preview, PWA, publish
- **steps/** – Step library, editor (code, IO, tests), AI assist
- **runners/** – Runner list, status, logs, actions
- **projects/** – Organization, shared settings, permissions
- **organizations/** – Multi-tenant structure, member management, app assignments, role-based access
- **settings/** – User/app settings, API keys, AI keys

Shared modules (`src/shared/`): components, services (Axios, SignalR), composables, utils, types.

## Documentation & Resources

- **Swagger UI**: `https://localhost:7091/swagger/index.html` (source of truth for API)
- **Vue.js Redesign**: [src/automaton-studio-vue/REDESIGN_PLAN.md](src/automaton-studio-vue/REDESIGN_PLAN.md)
- **Feature Docs**: `docs/` folder (flows, apps, runners, projects, etc.)
- **Agent Skills**: `docs/skills/` for concrete patterns and code examples
- **Teaching & Learning**: [FOR-DEVELOPERS.md](FOR-DEVELOPERS.md) explains decisions and lessons learned

## AI Skills

**Agent skills** codify reusable workflows and best practices for consistent, high-quality development. Store project-specific skills in `.github/skills/`.

**Proactive self-improvement (IMPORTANT):** After fixing a bug, applying a pattern, or performing a repeated action, the AI **must** ask itself:
> *"Would future me — or another agent — make the same mistake or repeat the same manual steps?"*

If yes, immediately update the relevant document **without waiting to be asked**:

| What happened | Where to document it |
|---|---|
| Fixed a recurring bug caused by a wrong pattern (e.g. duplicate `<Toast />`) | Add a ❌/✅ rule to the relevant `.github/instructions/*.md` file |
| Implemented a multi-step feature pattern used across the codebase | Create or update a `.github/skills/<name>/SKILL.md` |
| Discovered an architectural decision, pitfall, or lesson learned | Add to `FOR-DEVELOPERS.md` under the appropriate section |
| Added a significant feature that others will extend | Update `AGENTS.md` to describe the feature, its location, and key concepts |

**When to Create or Update a Skill:**
- After implementing a significant feature (e.g., multi-tenancy, runner scoping)
- When you identify a repeatable development pattern (e.g., post-change validation)
- When you document best practices for a specific task (e.g., debugging, testing)
- After completing a task that involved multiple steps and could benefit future agents

**Example Skills:**
- `post-change-validation` – Run frontend type-check and backend build after edits
- `debugging-runner-issues` – Troubleshooting runner registration and connection problems
- `adding-a-feature` – Step-by-step guide for implementing a new feature end-to-end

**Skill File Format:**
- Directory: `.github/skills/skill-name/` (lowercase, hyphens for spaces)
- File: `SKILL.md` (exact filename required)
- Content: YAML frontmatter + Markdown instructions
- See `.github/skills/post-change-validation/SKILL.md` for reference

## Teaching & Learning: FOR-DEVELOPERS.md

**Philosophy:** Every project is a lesson. Automaton is a vehicle for learning architecture, design patterns, real-world tradeoffs, and engineering wisdom.

**Purpose:** [FOR-DEVELOPERS.md](FOR-DEVELOPERS.md) explains the *entire* project in engaging, plain language—not as dry technical docs, but as a narrative of decisions, lessons, pitfalls, and how good engineers think.

**What It Covers:**
- **Architecture Overview** – Why this design? (Use analogies to make it memorable)
- **Key Technical Decisions** – Vue vs. Blazor, distributed runners, project-based multi-tenancy; the *why*, not just the *how*
- **Common Pitfalls & Solutions** – Real bugs we hit, how we fixed them, how to avoid them
- **Lessons Learned** – Testing strategies, performance gotchas, migration challenges, surprises
- **How We Think** – Philosophy behind best practices (theme-aware styling, feature-based architecture, post-change validation)

**Style:** Engage, don't bore. Use anecdotes, analogies, and context. Make it memorable enough that future developers internalize *why* decisions were made, not just *what* was built.

**When to Update:**
- After resolving a tricky bug or discovering a pitfall
- After completing a major architectural feature
- When adding a new technology or pattern
- When you realize "I wish I'd known this earlier"—that's a lesson worth documenting

## Plan Mode

- **Design for AI agents, not human timelines**: Plans should describe *what* to build and implementation *order*, not human work estimates ("7 weeks")
- **Parallelize work**: Identify independent tasks that can be executed concurrently (database migrations + service layer + UI components can happen in parallel)
- **Eliminate unnecessary ceremony**: No feature flags, no phased rollouts, no production best practices unless the project is deployed to external users
- **Focus on completeness**: Agents should build the entire feature end-to-end in one implementation pass
- **Make plans concise**: Sacrifice grammar for brevity. Avoid fluffy timelines.
- **End with open questions**: List unresolved decisions that need your input before implementation starts
