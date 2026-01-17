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

**1. Create Feature Directory Structure:**
```
src/features/your-feature/
├── components/          # Feature-specific components (e.g., YourFeatureSection.vue)
├── services/            # API service calls (e.g., yourFeature.service.ts)
├── types/               # TypeScript interfaces (e.g., yourFeature.types.ts)
├── views/               # Page-level components (if needed)
└── routes.ts            # Feature routes (if needed)
```

**2. Define TypeScript Types:**
- Match backend C# DTOs exactly
- Use const objects instead of enums for compatibility: `export const MyEnum = { Value1: 1, Value2: 2 } as const`
- Add helper functions for UI display (e.g., `getSeverity()`, `getIcon()`, `formatDate()`)
- Example pattern:
```typescript
// types/yourFeature.types.ts
export interface YourModel {
  id: string
  name: string
  createdAt: string  // ISO date string
}

export interface CreateRequest {
  name: string
  description?: string
}

// Helper functions
export function getSeverity(status: string): 'success' | 'warn' | 'danger' {
  // ... implementation
}
```

**3. Create API Service:**
- Use `apiClient` from `@/shared/services/api/axios-instance` 
- All requests are automatically authenticated with JWT tokens
- Handle errors with try/catch and toast notifications
- Example pattern:
```typescript
// services/yourFeature.service.ts
import apiClient from '@/shared/services/api/axios-instance'
import type { YourModel, CreateRequest } from '../types/yourFeature.types'

class YourFeatureService {
  async list(projectId?: string): Promise<YourModel[]> {
    const params = projectId ? { projectId } : {}
    const { data } = await apiClient.get<YourModel[]>('/api/yourfeature', { params })
    return data
  }

  async create(request: CreateRequest): Promise<YourModel> {
    const { data } = await apiClient.post<YourModel>('/api/yourfeature', request)
    return data
  }

  async delete(id: string): Promise<void> {
    await apiClient.delete(`/api/yourfeature/${id}`)
  }
}

export const yourFeatureService = new YourFeatureService()
```

**4. Create Vue Components:**
- Use PrimeVue components (Card, DataTable, Dialog, Button, etc.)
- Implement loading states, error handling, and user feedback with toasts
- Keep components focused and single-purpose
- Example pattern:
```vue
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useToast } from 'primevue/usetoast'
import { yourFeatureService } from '../services/yourFeature.service'
import type { YourModel } from '../types/yourFeature.types'

const toast = useToast()
const loading = ref(true)
const items = ref<YourModel[]>([])

async function loadItems() {
  loading.value = true
  try {
    items.value = await yourFeatureService.list()
  } catch (err) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load items',
      life: 5000,
    })
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadItems()
})
</script>

<template>
  <Card>
    <template #content>
      <DataTable :value="items" :loading="loading">
        <!-- columns -->
      </DataTable>
    </template>
  </Card>
</template>
```

**5. Connect to Backend Controllers:**
- Backend controllers are in `src/Automaton.Studio.Server/Controllers/`
- Controller endpoints follow pattern: `/api/controllername`
- DTOs are defined in controller files or separate models
- Ensure TypeScript types match C# DTOs exactly
- Common endpoints:
  - `GET /api/resource` - List all
  - `GET /api/resource/{id}` - Get single
  - `POST /api/resource` - Create
  - `PUT /api/resource/{id}` - Update
  - `DELETE /api/resource/{id}` - Delete

**6. Best Practices:**
- ✅ Always show loading states during async operations
- ✅ Use toast notifications for user feedback (success, error, warning)
- ✅ Add empty states with helpful messages when no data exists
- ✅ Implement confirmation dialogs for destructive actions (delete, revoke)
- ✅ Use computed properties to optimize template expressions
- ✅ Handle errors gracefully with try/catch blocks
- ✅ Format dates consistently using helper functions
- ✅ Use PrimeVue severity types for consistent styling
- ✅ Keep services stateless and focused on API calls
- ✅ Colocate feature code (types, services, components together)

**Example: API Keys Feature Implementation**

See `src/automaton-studio-vue/src/features/settings/` for a complete example:
- `types/apikeys.types.ts` - TypeScript interfaces matching C# DTOs
- `services/apikeys.service.ts` - Service calling `/api/apikeys` endpoints
- `components/ApiKeysSection.vue` - Complete UI with CRUD operations

This feature demonstrates:
- Type matching with backend (ApiKeyScope enum → const object)
- Service integration with ApiKeysController.cs
- DataTable with sorting, pagination, empty states
- Create dialog with validation
- One-time display of sensitive data (API key)
- Copy-to-clipboard functionality
- Revoke with confirmation
- Toast notifications for all operations

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

## Plan Mode

- Make the plan extremely concise. Sacrifice grammar for the sake of concision.
- At the end of each plan, give me a list of unresolved questions to answer, if any.
