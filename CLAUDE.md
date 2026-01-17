# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Automaton is a cross-platform workflow automation platform with an AI-first approach to workflow design. It combines a Vue.js/TypeScript web frontend with a .NET 9.0 backend, providing natural language flow generation, integrated web app building, remote execution runners, and custom step authoring with Python/PowerShell/Batch support.

**Core Components:**

- **Frontend (`src/automaton-studio-vue/`)**: Vue 3 + TypeScript + Vite
  - Modern, feature-based architecture with composables, stores, and services
  - PrimeVue for enterprise-grade UI components
  - Monaco Editor for code editing (flows, apps, custom steps)
  - Pinia for state management
  - Natural language flow generation interface (AI-first)

- **Backend (`src/Automaton.Studio.Server/`)**: ASP.NET Core 9.0 Web API
  - 25+ REST API controllers for flows, apps, runners, steps, projects
  - Entity Framework Core 9.0 with MySQL/MariaDB persistence
  - SignalR hubs for real-time runner communication and execution updates
  - Hangfire for background job scheduling
  - ASP.NET Core Identity with JWT Bearer authentication
  - Serilog structured logging
  - Swagger/OpenAPI documentation

- **Runner (`src/Automaton.Runner/`)**: Cross-platform console application
  - Standalone executable (single-file or framework-dependent)
  - SignalR client for server communication and flow execution dispatch
  - Polly for resilience and retry policies
  - Executes flows on remote machines via runner nodes

- **Core Engine (`src/Automaton.Core/`)**: Workflow execution engine
  - MediatR for CQRS pattern
  - Variable mapping and step orchestration
  - Dynamic expression evaluation
  - Pluggable step architecture

- **Built-in Steps (`src/Automaton.Steps/`)**: Step library
  - HTTP, email, database, file operations
  - References Core engine and Scripting layer

- **Python Integration (`src/Automaton.Core.Scripting/`)**: 
  - pythonnet 3.0.5 for CPython interop
  - Custom step execution with variable injection

- **AI Integration (`src/Automaton.Mcp/`)**: 
  - Model Context Protocol (MCP) for AI-powered flow/step generation
  - Multi-provider support (OpenAI, Anthropic, Google, Azure)
  - Step testing and refinement workflow

- **Marketing Site (`src/Automaton.Web/`)**: Static HTML/CSS/JS website

## Common Commands

### Frontend (Vue.js)

```bash
# Install dependencies
cd src/automaton-studio-vue
npm install

# Development server with HMR
npm run dev

# Build for production
npm run build

# Run tests
npm run test

# Type checking
npm run type-check

# Lint and format
npm run lint
npm run format
```

### Backend (.NET)

```bash
# Build all projects
dotnet build

# Build specific project
dotnet build src/Automaton.Studio.Server
dotnet build src/Automaton.Runner

# Build in Release mode
dotnet build -c Release

# Run the API server
dotnet run --project src/Automaton.Studio.Server

# Runner is managed via Docker Compose (see Runner Deployment)

# Run all tests
dotnet test

# Run specific test project
dotnet test src/UnitTests/Automaton.Core.Tests
```

### Database Migrations

```bash
# Ensure dotnet-ef is installed
dotnet tool install --global dotnet-ef

# Navigate to server project
cd src/Automaton.Studio.Server

# Add a new migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update

# View migration SQL
dotnet ef migrations script

# Remove last migration
dotnet ef migrations remove

# List all migrations
dotnet ef migrations list
```

### Runner Deployment (Docker Compose – Preferred)

Use the provided Docker Compose file for deploying and managing the runner.

```bash
# From repo root
cp .env.example .env

# Configure environment in .env
# AUTOMATON_SERVER_URL=https://your-server-url
# AUTOMATON_API_KEY=ak_runner_xxx

# Start the runner
docker compose -f docker-compose.runner.yml up -d

# View logs
docker compose -f docker-compose.runner.yml logs -f runner

# Stop/remove
docker compose -f docker-compose.runner.yml down
```

Manual execution of the runner is deprecated; use Docker Compose for production and staging environments. For local debugging only, see README for a minimal fallback.

## Architecture Notes

### Multi-Layered Design

**Frontend → REST API → Services → Data Persistence**

#### Frontend Architecture (Vue.js + TypeScript)

**Feature-Based Structure** (`src/automaton-studio-vue/src/features/`):

Each feature (auth, flows, apps, steps, etc.) is self-contained with:
- **components/** - Vue components specific to the feature
- **composables/** - Reusable Composition API functions
- **services/** - API calls and business logic
- **stores/** - Pinia state management
- **types/** - TypeScript interfaces and types
- **views/** - Page-level components
- **routes.ts** - Feature routes (aggregated in main router)

Key features:
- **auth/** - Authentication, login, registration
- **dashboard/** - Home page, statistics, recent activity
- **flows/** - Flow management and AI-first generation interface
- **apps/** - Web app builder with Monaco editor
- **steps/** - Custom step library and designer
- **runners/** - Runner registration and management
- **projects/** - Project/organization management
- **settings/** - User and application settings

Benefits of this structure:
- Colocation of related code
- Easy feature isolation and testing
- Lazy loading of entire features
- Clear separation of concerns

#### Backend Architecture (ASP.NET Core)

**Layered Structure:**

1. **API Layer** (`Automaton.Studio.Server/Controllers/`): 25+ REST endpoints
   - FlowsController, AppsController, RunnersController, etc.
   - Handles HTTP requests/responses and basic validation

2. **Service Layer** (`Automaton.Studio.Server/Services/`): Business logic
   - FlowsService, AppsService, FlowGenerationService, etc.
   - Handles workflows, validation, and orchestration

3. **Data Layer** (`Automaton.Studio.Server/Data/Entities/`): EF Core
   - ApplicationDbContext, Entities, Migrations

4. **Core Engine** (`Automaton.Core/`): Execution logic
   - Flow execution, step orchestration, variable mapping

### Key Execution Flow

**Flow Execution Path:**
1. User submits flow execution request via REST API
2. `FlowsController` → `FlowsService` creates execution record
3. SignalR hub sends command to registered runner(s)
4. Runner executes steps via `Automaton.Core` workflow engine
5. Runner reports progress/logs back to server via SignalR
6. Frontend receives real-time updates via SignalR subscription

**Flow Generation Path (AI-First):**
1. User enters natural language description in frontend
2. Frontend calls `POST /api/flows/generate` via REST
3. `FlowGenerationService` searches for reusable existing steps
4. Calls AI provider (OpenAI/Anthropic/etc) with context
5. AI generates flow structure with steps and connections
6. Service creates custom steps (if needed) and flow definition
7. Flow saved to database in draft state
8. Frontend displays generated flow for review/refinement

### Frontend-Backend Communication

**REST API Integration:**
- All data operations use REST endpoints via Axios
- JWT tokens automatically injected by interceptors
- Controllers in `src/Automaton.Studio.Server/Controllers/`
- OpenAPI spec at `https://localhost:7091/swagger/v1/swagger.json`

**Request Flow:**
1. Vue component calls feature service method
2. Service makes HTTP request via `apiClient` (Axios instance)
3. Request interceptor adds JWT token to Authorization header
4. Backend controller receives request, validates, processes
5. Controller returns response (200 OK, 400 Bad Request, etc.)
6. Service returns typed data to component
7. Component updates UI and shows feedback (toast, loading state)

**Example End-to-End Flow:**

```typescript
// 1. Frontend: User clicks "Create API Key"
// Component: ApiKeysSection.vue
async function createApiKey() {
  createLoading.value = true
  try {
    const response = await apiKeysService.create(createForm.value)
    // Success handling
  } catch (err) {
    // Error handling with toast
  } finally {
    createLoading.value = false
  }
}

// 2. Service: Makes HTTP call
// Service: apikeys.service.ts
async create(request: CreateApiKeyRequest): Promise<CreateApiKeyResponse> {
  const { data } = await apiClient.post<CreateApiKeyResponse>('/api/apikeys', request)
  return data
}

// 3. Backend: Controller receives request
// Controller: ApiKeysController.cs
[HttpPost]
public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request)
{
    var userId = userContextService.GetUserId();
    var (apiKey, entity) = await apiKeyService.CreateApiKeyAsync(
        userId, request.Name, request.Description, request.ExpiresAt, request.Scope);
    
    return Ok(new CreateApiKeyResponse { ... });
}
```

**Type Safety Across Stack:**

TypeScript types must match C# DTOs exactly:

```csharp
// Backend: ApiKeysController.cs
public class CreateApiKeyRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public ApiKeyScope Scope { get; set; } = ApiKeyScope.FullAccess;
}

public enum ApiKeyScope
{
    ReadOnly = 1,
    Execute = 2,
    Runner = 3,
    FullAccess = 4,
    WebApp = 5
}
```

```typescript
// Frontend: apikeys.types.ts
export interface CreateApiKeyRequest {
  name: string
  description?: string
  expiresAt?: string  // ISO date string
  scope: ApiKeyScope
}

// Use const object instead of enum for TypeScript compatibility
export const ApiKeyScope = {
  ReadOnly: 1,
  Execute: 2,
  Runner: 3,
  FullAccess: 4,
  WebApp: 5,
} as const

export type ApiKeyScope = (typeof ApiKeyScope)[keyof typeof ApiKeyScope]
```

**Common Patterns:**

1. **List Resources:**
```typescript
// Frontend
const items = await service.list(projectId)

// Backend
[HttpGet]
public async Task<IActionResult> GetItems([FromQuery] Guid? projectId = null)
```

2. **Create Resource:**
```typescript
// Frontend
const created = await service.create(request)

// Backend
[HttpPost]
public async Task<IActionResult> Create([FromBody] CreateRequest request)
```

3. **Delete Resource:**
```typescript
// Frontend
await service.delete(id)

// Backend
[HttpDelete("{id}")]
public async Task<IActionResult> Delete(Guid id)
```

**Error Handling:**

Backend returns standard HTTP status codes:
- 200 OK - Success
- 201 Created - Resource created
- 204 No Content - Success with no response body
- 400 Bad Request - Validation error
- 401 Unauthorized - Auth required
- 403 Forbidden - Insufficient permissions
- 404 Not Found - Resource not found
- 500 Internal Server Error - Server error

Frontend catches errors and shows appropriate feedback:
```typescript
try {
  await service.operation()
  toast.add({ severity: 'success', summary: 'Success', ... })
} catch (err: unknown) {
  console.error('Operation failed:', err)
  toast.add({ severity: 'error', summary: 'Error', detail: 'Operation failed', ... })
}
```

### Implementing a New Feature

**Step-by-Step Guide:**

**1. Backend First (ASP.NET Core):**

Create controller in `src/Automaton.Studio.Server/Controllers/YourFeatureController.cs`:
```csharp
[Route("api/[controller]")]
[ApiController]
[Authorize]  // Require authentication
public class YourFeatureController : ControllerBase
{
    private readonly YourFeatureService _service;
    
    public YourFeatureController(YourFeatureService service)
    {
        _service = service;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<YourModel>>> GetAll()
    {
        var items = await _service.GetAllAsync();
        return Ok(items);
    }
    
    [HttpPost]
    public async Task<ActionResult<YourModel>> Create([FromBody] CreateRequest request)
    {
        var created = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }
}
```

Define DTOs in the same file or separate model file:
```csharp
public class YourModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
}
```

**2. Frontend Feature Structure:**

Create feature directory in `src/automaton-studio-vue/src/features/your-feature/`:

```
your-feature/
├── components/
│   └── YourFeatureSection.vue
├── services/
│   └── yourFeature.service.ts
├── types/
│   └── yourFeature.types.ts
└── views/ (optional)
    └── YourFeatureView.vue
```

**3. TypeScript Types:**

Create `types/yourFeature.types.ts` matching backend DTOs:
```typescript
export interface YourModel {
  id: string
  name: string
  createdAt: string  // ISO date string
}

export interface CreateRequest {
  name: string
  description?: string
}
```

**4. API Service:**

Create `services/yourFeature.service.ts`:
```typescript
import apiClient from '@/shared/services/api/axios-instance'
import type { YourModel, CreateRequest } from '../types/yourFeature.types'

class YourFeatureService {
  async list(): Promise<YourModel[]> {
    const { data } = await apiClient.get<YourModel[]>('/api/yourfeature')
    return data
  }

  async create(request: CreateRequest): Promise<YourModel> {
    const { data } = await apiClient.post<YourModel>('/api/yourfeature', request)
    return data
  }
}

export const yourFeatureService = new YourFeatureService()
```

**5. Vue Component:**

Create `components/YourFeatureSection.vue`:
```vue
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import Card from 'primevue/card'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
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
    console.error('Failed to load items:', err)
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
    <template #title>Your Feature</template>
    <template #content>
      <DataTable :value="items" :loading="loading">
        <Column field="name" header="Name"></Column>
        <Column field="createdAt" header="Created"></Column>
      </DataTable>
    </template>
  </Card>
</template>
```

**6. Integration:**

Import and use component in parent view:
```vue
<script setup lang="ts">
import YourFeatureSection from '@/features/your-feature/components/YourFeatureSection.vue'
</script>

<template>
  <YourFeatureSection />
</template>
```

**Best Practices Checklist:**

- ✅ Match TypeScript types to C# DTOs exactly
- ✅ Use const objects instead of enums for TypeScript compatibility
- ✅ Add loading states for all async operations
- ✅ Show toast notifications for success/error feedback
- ✅ Implement empty states with helpful messages
- ✅ Add confirmation dialogs for destructive actions
- ✅ Handle errors with try/catch and user-friendly messages
- ✅ Use PrimeVue components for consistency
- ✅ Keep services focused on API calls only
- ✅ Colocate related code in feature folders
- ✅ Add helper functions for common UI operations (formatting, colors, etc.)

**Real-World Example:**

See the API Keys implementation in `src/automaton-studio-vue/src/features/settings/`:
- Backend: `src/Automaton.Studio.Server/Controllers/ApiKeysController.cs`
- Frontend Types: `src/features/settings/types/apikeys.types.ts`
- Frontend Service: `src/features/settings/services/apikeys.service.ts`
- Frontend Component: `src/features/settings/components/ApiKeysSection.vue`

This demonstrates complete CRUD operations, type safety, error handling, and UX best practices.

### Authentication & Authorization

**Multi-Mode:**
- **JWT Bearer**: Web UI authentication (OAuth, standard bearer token)
- **API Keys**: Runner authentication and integrations
  - Format: `ak_<scope>_<random>` (e.g., `ak_runner_xxx`)
  - Scopes: Runner, Execute, ReadOnly, WebApp
  
**Project-Based Authorization** (Role-based access):
- Roles: `ProjectAdmin`, `Developer`, `AppUser`, `Viewer`
- Resource scopes: Personal, Project, Public

### SignalR Real-Time Hubs

**RunnerHub**: Runner registration and heartbeat
- Runners authenticate via API keys
- Maintain persistent WebSocket connections
- Receive execution commands from server

**FlowExecutionHub**: Execution progress and logs
- Stream real-time execution logs to frontend
- Update execution status as steps complete

### Variable System

Flows use placeholder-based variable mapping:
- Variables defined as `${variableName}` in configurations
- Step inputs/outputs automatically mapped between steps
- Type validation and conversion at execution time

### Step Architecture

All steps inherit from `BaseStep` in `Automaton.Core`:
```csharp
public abstract class BaseStep
{
    public abstract Task<ExecutionResult> ExecuteAsync(IStepExecutionContext context);
}
```

Custom steps (Python/PowerShell/Batch) are defined with:
- Name, display name, description
- Input variable schemas (name, type, required)
- Output variable schemas
- Execution code

### Web App Integration

Published web apps call backend flows via `window.automatonApi` helper:
```javascript
// Frontend code in published web app
await window.automatonApi.executeFlow(flowId, { 
  inputs: {...} 
});
```

See AGENTS.md (Apps section) and `src/Automaton.Studio.Web/wwwroot/automatonApi.js` for details.

## Important Implementation Patterns

### Frontend: Feature-Based Organization

Each feature folder (`src/automaton-studio-vue/src/features/`) follows this pattern:

```
feature-name/
├── components/          # Feature-specific Vue components
├── composables/         # Composition API functions
├── services/            # API service calls
├── stores/              # Pinia store(s)
├── types/               # TypeScript interfaces
├── views/               # Top-level page components
└── routes.ts            # Feature routes
```

Example: `flows/` feature
- Components: FlowCard, FlowTimeline, FlowGenerationPanel
- Composables: useFlows(), useFlowGeneration()
- Services: flowsService.ts (REST calls to /api/flows)
- Store: flowsStore.ts (Pinia for flow state)
- Views: FlowsListView, FlowGeneratorView

### Backend: Service Registration & Dependency Injection

Services registered in `Program.cs`:
- **Scoped**: Per-request services (DbContext, user-specific services)
- **Singleton**: Shared state (SignalR connection tracking, cache)
- **Transient**: Stateless utilities

Example:
```csharp
builder.Services.AddScoped<FlowsService>();
builder.Services.AddScoped<FlowGenerationService>();
builder.Services.AddSingleton<ConnectionManager>();
```

### API Endpoints

New flow generation endpoints (in `FlowsController` or new `FlowGenerationController`):

```csharp
[HttpPost("generate")]
public async Task<ActionResult<GenerateFlowResponse>> GenerateFlow(
    GenerateFlowRequest request)
{
    var response = await _flowGenerationService.GenerateFlowAsync(
        request, 
        User.FindFirst("sub")?.Value);
    return Ok(response);
}

[HttpPost("generate/refine/{flowId}")]
public async Task<ActionResult<GeneratedFlow>> RefineFlow(
    Guid flowId,
    RefineFlowRequest request)
{
    // Implementation
}

[HttpGet("generate/search-steps")]
public async Task<ActionResult<StepSearchResponse>> SearchSteps(
    [FromQuery] string query)
{
    // Implementation
}
```

### Frontend: HTTP Client with Axios

```typescript
// src/shared/services/api/axios-instance.ts
import axios from 'axios';
import { useAuthStore } from '@/features/auth/stores/auth.store';

const instance = axios.create({
  baseURL: import.meta.env.VITE_API_URL,
  timeout: 10000,
});

// Inject JWT token
instance.interceptors.request.use((config) => {
  const auth = useAuthStore();
  if (auth.token) {
    config.headers.Authorization = `Bearer ${auth.token}`;
  }
  return config;
});

export default instance;
```

### Frontend: Pinia State Management

```typescript
// src/features/flows/stores/flows.store.ts
import { defineStore } from 'pinia';
import { ref, computed } from 'vue';

export const useFlowsStore = defineStore('flows', () => {
  const flows = ref<Flow[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);

  const fetchFlows = async () => {
    loading.value = true;
    try {
      const response = await flowsService.getFlows();
      flows.value = response.data;
    } catch (e) {
      error.value = (e as Error).message;
    } finally {
      loading.value = false;
    }
  };

  const flowCount = computed(() => flows.value.length);

  return {
    flows,
    loading,
    error,
    flowCount,
    fetchFlows,
  };
});
```

### Backend: Custom Step Execution

Custom steps inherit from `BaseStep` and implement:
```csharp
public class ValidateEmailStep : BaseStep
{
    public override async Task<ExecutionResult> ExecuteAsync(
        IStepExecutionContext context)
    {
        var email = context.GetInputVariable<string>("email");
        var isValid = /* validation logic */;
        
        context.SetOutputVariable("isValid", isValid);
        return ExecutionResult.Success();
    }
}
```

### Web App API Calls

Web apps call backend flows using `window.automatonApi` helper:

```javascript
// Published web app frontend code
const result = await window.automatonApi.executeFlow(flowId, {
  inputs: {
    orderId: '12345',
    customerId: 'cust_999'
  }
});

console.log(result.outputVariables);
```

### SignalR Real-Time Communication

**Runner registration and command dispatch:**
```csharp
// Automaton.Studio.Server/Hubs/RunnerHub.cs
public class RunnerHub : Hub
{
    public async Task RegisterRunner(string runnerId, string version)
    {
        // Runner connects and identifies itself
        await Clients.All.SendAsync("RunnerOnline", runnerId);
    }

    public async Task ExecuteFlow(Guid flowId, object inputs)
    {
        // Server dispatches flow execution to runner
        await Clients.Group(runnerId).SendAsync("ExecuteFlow", flowId, inputs);
    }
}
```

**Frontend receives execution updates:**
```typescript
// src/shared/services/signalr/signalr-client.ts
const connection = new HubConnectionBuilder()
  .withUrl('/hubs/flow-execution')
  .withAutomaticReconnect()
  .build();

connection.on('ExecutionProgress', (stepName, status, logs) => {
  // Update execution timeline in real-time
  executionStore.addLog(stepName, status, logs);
});
```

## Current Development Context

**Active Feature: Project/Organization Multi-Tenancy**

The codebase is currently implementing project-based multi-tenancy. Recent commits show:
- Database migrations for `Projects`, `ProjectMembers`, `ProjectApps`, `ProjectFlows`, `ProjectRunners`, `ProjectCustomSteps`
- API controllers for project management
- Blazor UI for project settings and member management
- Role-based access control with 4 roles

**Implementation Status:**
- ✅ Phase 1: Database schema and core APIs (completed)
- ✅ Phase 2: Project management UI pages (in progress)
- 🔄 Phase 3: Resource linking and sharing (current)
- ⏳ Phase 4: Role-based dashboards and personalization

See `docs/projects/` for comprehensive documentation including:
- `project-organization-tad.md` - Technical approach document
- `implementation-guide.md` - Step-by-step implementation guide
- `discussion-and-faq.md` - Design decisions and FAQ

## Configuration Requirements

### Frontend (Vue.js)

**Environment Variables** (`.env.development`, `.env.production`):

```env
VITE_API_URL=http://localhost:7091
VITE_SIGNALR_URL=http://localhost:7091/hubs
VITE_APP_TITLE=Automaton Studio
```

### Backend (ASP.NET Core)

**Required Settings** (`appsettings.json` or `appsettings.Development.json`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=automaton;User Id=root;Password=..."
  },
  "JwtOptions": {
    "Issuer": "Automaton.Studio",
    "Audience": "Automaton.User",
    "HMacSettings": {
      "SecretKey": "..."
    }
  },
  "AiProviders": {
    "OpenAI": {
      "ApiKey": "sk_..."
    }
  }
}
```

### Database

**MySQL/MariaDB Setup:**
- Create database: `CREATE DATABASE automaton;`
- Run migrations: `dotnet ef database update`
- Supports: MySQL 5.7+, MariaDB 10.2+

### AI Provider Configuration

**Optional AI Keys** (configured in Studio UI or appsettings):
- OpenAI API key
- Anthropic API key
- Google Vertex AI credentials
- Azure OpenAI endpoint

## Testing Strategy

### Frontend (Vue.js)

```bash
# Run all tests
npm run test

# Run tests in watch mode
npm run test:watch

# Generate coverage report
npm run test:coverage
```

**Test Structure** (Vitest + Vue Test Utils):
- **Unit Tests**: Components, composables, services, stores
- **Integration Tests**: Feature workflows with mocked API
- **E2E Tests** (Playwright): Critical user flows

**Test File Naming**: `ComponentName.spec.ts` or `functionName.spec.ts`

Example test:
```typescript
// src/features/flows/components/__tests__/FlowCard.spec.ts
import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import FlowCard from '../FlowCard.vue';

describe('FlowCard', () => {
  it('renders flow name and status', () => {
    const flow = { id: '1', name: 'Test Flow', status: 'active' };
    const wrapper = mount(FlowCard, { props: { flow } });
    expect(wrapper.text()).toContain('Test Flow');
  });
});
```

### Backend (.NET)

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test src/UnitTests/Automaton.Core.Tests

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific test class
dotnet test --filter FullyQualifiedName~Automaton.Core.Tests.FlowExecuteServiceTests
```

**Test Projects:**
- `Automaton.Core.Tests` - Workflow engine and step execution
- `Automaton.Studio.Server.Tests` - API controllers and services
- `Automaton.Runner.Tests` - Runner SignalR client and flow execution

**Test Patterns:**
- Use `InMemoryDatabase` for EF Core tests
- Mock SignalR hub contexts for real-time tests
- `WebApplicationFactory<Program>` for integration tests
- Arrange-Act-Assert pattern

**Target Coverage:** 80%+ for business logic, 60%+ overall

## Troubleshooting

### Frontend (Vue.js)

**Common Issues:**

1. **HMR (Hot Module Reload) Not Working:**
   - Ensure Vite dev server is running on correct port (default: 5173)
   - Check firewall isn't blocking localhost connections
   - Clear browser cache: Cmd+Shift+Delete (Chrome/Edge)

2. **API Calls Failing:**
   - Verify `VITE_API_URL` points to correct backend
   - Check backend server is running (`npm run dev:backend` or `dotnet run`)
   - Review browser DevTools Network tab for 401 (auth) or 404 (endpoint) errors

3. **TypeScript Errors:**
   - Run `npm run type-check` to validate types
   - Regenerate types: `npm run generate:types`
   - Clear node_modules: `rm -r node_modules && npm install`

### Backend (.NET)

**Common Issues:**

1. **Migration Errors:**
   - Ensure database connection is valid
   - Navigate to `src/Automaton.Studio.Server`
   - Run: `dotnet ef database update`

2. **Runner Connection Issues:**
   - Verify API key has `Runner` scope
   - Check SignalR URL matches server configuration
   - Review runner logs for connection errors

3. **Python Step Failures:**
   - Ensure Python is installed and in PATH
   - Verify `pythonnet` compatibility: `pip install pythonnet==3.0.5`
   - Check custom step code syntax

4. **Build Errors:**
   - Clean: `dotnet clean && dotnet build`
   - Restore: `dotnet restore`
   - Verify .NET SDK: `dotnet --version` (requires 9.0)

## Key Technologies & Libraries

### Frontend (Vue.js 3 + TypeScript)

| Technology | Purpose |
|-----------|---------|
| **Vue 3** | Progressive reactive framework |
| **TypeScript 5.0+** | Type-safe JavaScript |
| **Vite 5.0+** | Fast build tool with HMR |
| **PrimeVue 4.0+** | Enterprise-grade UI component library (90+ components) |
| **Pinia 2.1+** | State management (Vue 3 official) |
| **Vue Router 4.0+** | Client-side routing |
| **Axios 1.6+** | HTTP client with request interceptors |
| **Monaco Editor** | Code editor (direct integration, no wrappers) |
| **openapi-typescript** | Auto-generate TypeScript types from C# API |

### Backend (.NET 9.0)

| Technology | Purpose |
|-----------|---------|
| **ASP.NET Core 9.0** | Web API framework |
| **Entity Framework Core 9.0** | ORM for MySQL/MariaDB |
| **Pomelo.EntityFrameworkCore.MySql 9.0** | MySQL provider |
| **SignalR** | Real-time communication with runners |
| **Hangfire 1.8.16** | Background job scheduling |
| **MediatR 12.4.1** | CQRS pattern implementation |
| **Serilog 4.1.0** | Structured logging |
| **FluentValidation** | Request validation |
| **Swashbuckle** | Swagger/OpenAPI documentation |
| **ASP.NET Core Identity** | Authentication and authorization |
| **System.Linq.Dynamic.Core** | Dynamic LINQ expressions |

### Runner Console

| Technology | Purpose |
|-----------|---------|
| **SignalR Client** | Connect to server and receive execution commands |
| **Polly 8.5.0** | Resilience policies and retry logic |
| **System.CommandLine 2.0.0-beta4** | CLI argument parsing |
| **Serilog** | Structured logging |

### Scripting & AI

| Technology | Purpose |
|-----------|---------|
| **pythonnet 3.0.5** | CPython interop for custom steps |
| **OpenAI SDK 2.5.0** | AI-powered flow and step generation |
| **Anthropic SDK** | Alternative AI provider |
| **Google Vertex AI SDK** | Alternative AI provider |

## Project Structure Summary

```
src/
├── Automaton.Web/                          # Static marketing website
│
├── automaton-studio-vue/                   # NEW: Vue.js frontend (replacing Blazor)
│   ├── src/
│   │   ├── features/                       # Feature-based modules
│   │   │   ├── auth/                       # Authentication (login, register)
│   │   │   ├── dashboard/                  # Dashboard and home page
│   │   │   ├── flows/                      # Flow management & AI generation
│   │   │   ├── apps/                       # Web app builder
│   │   │   ├── steps/                      # Custom step library
│   │   │   ├── runners/                    # Runner management
│   │   │   ├── projects/                   # Project organization
│   │   │   └── settings/                   # User/app settings
│   │   ├── shared/                         # Shared components, utils, services
│   │   │   ├── components/                 # Reusable UI components
│   │   │   ├── services/                   # HTTP client, SignalR
│   │   │   ├── composables/                # Shared composition API functions
│   │   │   ├── types/                      # Common TypeScript types
│   │   │   └── utils/                      # Helper functions
│   │   ├── router/                         # Vue Router configuration
│   │   ├── App.vue                         # Root component
│   │   └── main.ts                         # Entry point
│   ├── package.json
│   ├── tsconfig.json
│   ├── vite.config.ts
│   └── README.md
│
├── Automaton.Studio.Server/                # ASP.NET Core API
│   ├── Controllers/                        # 25+ REST API controllers
│   ├── Services/                           # Business logic services
│   ├── Data/                               # EF Core DbContext
│   ├── Entities/                           # Database entities
│   ├── Hubs/                               # SignalR hubs
│   ├── Migrations/                         # EF Core migrations
│   └── Program.cs                          # Startup configuration
│
├── Automaton.Core/                         # Workflow execution engine
│   ├── Workflow/                           # Core workflow logic
│   ├── Steps/                              # Step interfaces
│   └── Models/                             # Data models
│
├── Automaton.Steps/                        # Built-in steps library
│
├── Automaton.Core.Scripting/               # Python integration
│
├── Automaton.Mcp/                          # AI integration & flow generation
│
├── Automaton.Runner/                       # Console runner application
│
└── UnitTests/                              # Test projects
    ├── Automaton.Core.Tests/
    ├── Automaton.Studio.Server.Tests/
    └── Automaton.Runner.Tests/
```

## Additional Documentation

For comprehensive documentation, see:
- `AGENTS.md` - Detailed project documentation and feature reference
- `README.md` - Project overview and quick start
- `src/automaton-studio-vue/REDESIGN_PLAN.md` - Complete Vue.js redesign specification
- `docs/` - Feature-specific technical documentation:
  - `docs/projects/` - Multi-tenancy implementation
  - `docs/flows/` - Flow architecture
  - `docs/apps/` - Web apps platform
  - `docs/runners/` - Runner deployment guides
  - `docs/apikeys/` - API key scopes and security
  - `docs/mcp/` - Model Context Protocol integration

## Current Development Context

Automaton is transitioning from Blazor WebAssembly to a modern Vue.js + TypeScript frontend (January 2026).

**Transition Status:**
- ✅ Architecture and design complete
- ✅ Technology stack finalized (Vue 3 + TypeScript + PrimeVue)
- 🔄 Frontend development underway (`src/automaton-studio-vue/`)
- ✅ Backend APIs stable and unchanged

**Key Features in Development:**
- AI-first natural language flow generation
- Feature-based Vue.js module architecture
- Real-time execution updates via SignalR
- Enhanced app builder with direct Monaco editor integration

**Why This Change:**
- Blazor WASM had stability issues (tabs, trees)
- Modern frontend development experience with Vue.js
- Better browser DevTools integration
- Access to mature Vue ecosystem

**What Stays the Same:**
- All .NET backend code unchanged
- API contracts preserved
- Database schema stable
- Runner architecture untouched

## API Documentation

### OpenAPI/Swagger

Access auto-generated API documentation:
- **Swagger UI**: `https://localhost:7091/swagger/index.html`
- **OpenAPI JSON**: `https://localhost:7091/swagger/v1/swagger.json`

The backend exports OpenAPI 3.0 specs. The frontend uses `openapi-typescript` to auto-generate TypeScript types from these specs.

## Plan Mode

- Make the plan extremely concise. Sacrifice grammar for the sake of concision.
- At the end of each plan, give me a list of unresolved questions to answer, if any.
