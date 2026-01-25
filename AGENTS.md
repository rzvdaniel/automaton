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

### Implementing a New Feature in Vue.js

When adding a new feature to the Vue.js frontend, follow this systematic approach:

**1. Feature Directory Structure:**
```
src/features/your-feature/
├── components/          # Feature-specific Vue components
├── services/            # API service calls
├── types/               # TypeScript interfaces
├── views/               # Page-level components (optional)
└── routes.ts            # Feature routes (optional)
```

**2. TypeScript Types:**
- Match backend C# DTOs exactly (property names, types, optionality)
- **Important:** Use const objects instead of enums: `export const MyEnum = { Value1: 1 } as const`
- Add helper functions for UI display (getSeverity, getIcon, formatDate)

**3. API Service:**
- Import and use `apiClient` from `@/shared/services/api/axios-instance`
- JWT tokens are automatically injected by interceptors
- Handle errors with try/catch and toast notifications
- Service class pattern: `export const yourFeatureService = new YourFeatureService()`

**4. Vue Components:**
- Use PrimeVue components (Card, DataTable, Dialog, Button, etc.)
- Implement loading states with `ref(true/false)`
- Use `useToast()` for user feedback (success, error, warning)
- Keep components focused and single-purpose

**5. Theme-Aware Styling (CRITICAL):**

The app supports light/dark themes. **Never hardcode background colors** - they break theme switching.

```css
/* ❌ WRONG - breaks dark mode */
.my-panel {
  background-color: var(--p-surface-0);   /* Always white */
  background-color: var(--p-surface-100); /* Always light */
  background-color: white;
}

/* ✅ CORRECT - inherit from parent (preferred) */
.my-panel {
  /* No background - inherits from themed parent */
  border: 1px solid var(--p-surface-200);
}

/* ✅ CORRECT - use adaptive variables */
.my-panel {
  background-color: var(--p-surface-ground); /* Adapts */
  color: var(--p-text-color);                /* Adapts */
}

/* Dark mode - only adjust borders, not backgrounds */
:global(.app-dark) .my-panel {
  border-color: var(--p-surface-700);
}
```

**Safe CSS variables:** `--p-surface-ground`, `--p-text-color`, `--p-text-muted-color`, `--p-surface-hover`

**Unsafe (don't use for backgrounds):** `--p-surface-0` through `--p-surface-400`, `--p-surface-800`, `--p-surface-900`

**6. Backend Integration:**
- Controllers in `src/Automaton.Studio.Server/Controllers/`
- Endpoints: `/api/controllername`
- Common patterns: GET (list/get), POST (create), PUT (update), DELETE (delete)
- Ensure TypeScript interfaces match C# DTOs exactly

**7. Best Practices:**
- ✅ Show loading states during async operations
- ✅ Use toast notifications for all user feedback
- ✅ Add empty states with helpful messages
- ✅ Implement confirmation dialogs for destructive actions
- ✅ Use computed properties to optimize template expressions
- ✅ Handle errors gracefully with try/catch
- ✅ Format dates consistently using helper functions
- ✅ Keep services stateless and focused on API calls
- ✅ Colocate feature code (types, services, components together)
- ✅ **Never hardcode backgrounds** - omit background-color or use `--p-surface-ground`

**Reference Implementation:**

See API Keys feature in `src/automaton-studio-vue/src/features/settings/` for complete example demonstrating all patterns above.

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

**Security:**
- Use `WebApp`-scoped API keys or short-lived tokens
- Configure CORS for published app origins
- Avoid embedding privileged keys in client-side code

**Calling Flows from Published Apps:**

Published apps use the `window.automatonApi` helper provided by the platform:

```javascript
// Basic usage
const flowId = 'your-flow-id';
const payload = {
  inputs: { name: 'Alice', email: 'alice@example.com' },
  runnerNames: ['runner-1']
};

window.automatonApi.executeFlow(flowId, payload)
  .then(result => console.log('Execution result', result))
  .catch(err => console.error('Execution error', err));
```

Example request body:
```json
{
  "inputs": {
    "name": "Alice",
    "email": "alice@example.com"
  },
  "runnerNames": ["runner-1", "runner-2"]
}
```

Example response:
```json
{
  "success": true,
  "flowId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Completed",
  "runnerCount": 2,
  "executedAt": "2025-11-09T12:34:56Z",
  "message": "Execution finished successfully",
  "outputVariables": {
    "messageId": "msg_123",
    "summary": "Processed 10 items"
  },
  "logs": [
    "Started execution on runner-1",
    "Step: FetchData - OK",
    "Step: ProcessItems - OK",
    "Execution completed"
  ]
}
```

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
- **settings/** – User/app settings, API keys, AI keys

Shared modules (`src/shared/`): components, services (Axios, SignalR), composables, utils, types.

## Documentation

**Developer Guides:**
- See [CLAUDE.md](CLAUDE.md) for comprehensive development guidance
- See [src/automaton-studio-vue/REDESIGN_PLAN.md](src/automaton-studio-vue/REDESIGN_PLAN.md) for the full Vue.js redesign spec

**API Reference:**
- Swagger UI: `https://localhost:7091/swagger/index.html`
- OpenAPI JSON: `https://localhost:7091/swagger/v1/swagger.json`

**Additional Docs:**
- `docs/` folder contains feature-specific documents and guides

## AI Skills

**Agent skills** codify reusable workflows and best practices for consistent, high-quality development. Store project-specific skills in `.github/skills/`.

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

- Make the plan extremely concise. Sacrifice grammar for the sake of concision.
- At the end of each plan, give me a list of unresolved questions to answer, if any.
