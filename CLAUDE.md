# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Automaton is a cross-platform workflow automation platform built on .NET 9.0 with Blazor. It provides a visual workflow designer, integrated web app builder, remote execution runners, and custom step authoring capabilities with Python/PowerShell/Batch support.

**Core Architecture:**
- **Automaton.Studio.Web**: Blazor web host for the UI
- **Automaton.Studio**: Shared Blazor UI library (Razor components, pages, services)
- **Automaton.Studio.Server**: ASP.NET Core Web API backend with SignalR hubs
- **Automaton.Runner.Console**: Cross-platform console runner (connects via SignalR, executes flows remotely)
- **Automaton.Core**: Workflow execution engine (inspired by Workflow Core)
- **Automaton.Steps**: Built-in automation steps library
- **Automaton.Core.Scripting**: Python integration via pythonnet

## Common Commands

### Build

```bash
# Build all projects
dotnet build

# Build specific project
dotnet build src/Automaton.Studio.Web
dotnet build src/Automaton.Runner.Console

# Build in Release mode
dotnet build -c Release
```

### Run

```bash
# Run the web application (from solution root)
dotnet run --project src/Automaton.Studio.Web

# Run the runner console
dotnet run --project src/Automaton.Runner.Console

# Or after building
cd src/Automaton.Studio.Web
dotnet run
```

### Testing

```bash
# Run all tests
dotnet test

# Run tests for specific project
dotnet test src/UnitTests/Automaton.Core.Tests
dotnet test src/UnitTests/Automaton.Studio.Server.Tests
dotnet test src/UnitTests/Automaton.Runner.Console.Tests

# Run with detailed output
dotnet test --verbosity normal

# Run a specific test class
dotnet test --filter FullyQualifiedName~Automaton.Core.Tests.FlowExecuteServiceTests

# Run tests in parallel
dotnet test --parallel
```

### Database Migrations

```bash
# Ensure dotnet-ef is installed
dotnet tool install --global dotnet-ef

# Navigate to the server project
cd src/Automaton.Studio.Server

# Add a new migration
dotnet ef migrations add MigrationName

# Update database to latest migration
dotnet ef database update

# View migration SQL without applying
dotnet ef migrations script

# Remove last migration (if not applied)
dotnet ef migrations remove

# List all migrations
dotnet ef migrations list
```

### Runner Deployment

```bash
# Publish runner (framework-dependent)
cd src/Automaton.Runner.Console
dotnet publish -c Release --self-contained false

# Publish runner (self-contained for specific OS)
dotnet publish -c Release --self-contained true -r win-x64
dotnet publish -c Release --self-contained true -r linux-x64
dotnet publish -c Release --self-contained true -r osx-x64
```

## Architecture Notes

### Multi-Layered Structure

**Presentation → API → Services → Data**

1. **UI Layer** (`Automaton.Studio`): Blazor components under `Pages/` organized by feature:
   - `/flows` - Flow management and visual designer
   - `/apps` - Web app builder with Monaco editor
   - `/customsteps` - Custom step designer (Python/PowerShell/Batch)
   - `/runners` - Runner registration and management
   - `/projects` - **NEW**: Project/organization multi-tenancy feature
   - `/apikeys`, `/ai-keys`, `/downloads`, `/settings`, `/account`

2. **API Layer** (`Automaton.Studio.Server/Controllers`): 23 REST controllers including:
   - `FlowsController`, `FlowExecutionsController`
   - `AppsController`, `PublicWebAppsController`
   - `ProjectsController`, `ProjectMembersController`, `ProjectResourcesController` (new)
   - `RunnersController`, `CustomStepsController`
   - `ApiKeysController`, `AccountController`, `TokenController`

3. **Business Logic** (`Automaton.Studio.Server/Services`, `Automaton.Core`):
   - `FlowExecuteService` - Workflow execution engine
   - `FlowConvertService` - Flow conversion and serialization
   - MediatR pattern for commands/queries
   - SignalR hubs for real-time runner communication

4. **Data Layer** (`Automaton.Studio.Server/Data`, `/Entities`):
   - Entity Framework Core 9.0 with MySQL/MariaDB
   - ASP.NET Core Identity for authentication
   - See `ApplicationDbContext` for entity configuration

### Key Execution Flow

**Flow Execution Path:**
1. User triggers flow in Studio UI
2. `FlowsController.Execute()` called
3. `FlowExecuteService` creates `FlowExecution` record
4. SignalR hub sends execution command to registered runner(s)
5. Runner executes steps via `Automaton.Core` workflow engine
6. Runner reports progress/logs back to server via SignalR
7. Studio UI shows real-time logs via SignalR subscription

### Authentication & Authorization

**Multi-Mode Authentication:**
- **JWT Bearer**: User authentication for Studio web UI
- **API Keys**: Scoped keys for runners, web apps, and integrations
  - Format: `ak_<scope>_<random>` (e.g., `ak_runner_xxx`, `ak_execute_xxx`)
  - Scopes: Runner, Execute, ReadOnly, WebApp
  - See `docs/apikeys/` for detailed scope documentation

**Project-Based Authorization** (NEW):
- 4 roles: `ProjectAdmin`, `Developer`, `AppUser`, `Viewer`
- Policy-based authorization in `Authorization/` folder
- Resource scopes: Personal, Project, Public
- See `docs/projects/` for multi-tenancy architecture

### SignalR Communication

**Real-time Hubs:**
- **RunnerHub** (`Hubs/RunnerHub.cs`): Runner registration, heartbeat, execution dispatch
- **FlowExecutionHub**: Real-time execution logs and status updates
- Runners authenticate via API keys, maintain persistent WebSocket connections
- Reconnection logic with exponential backoff in runner console

### Python Integration

**Automaton.Core.Scripting:**
- Uses `pythonnet` library for CPython interop
- Custom steps execute Python code in isolated contexts
- Variable injection and output capture
- Error handling and timeout support

## Important Implementation Patterns

### Service Registration

The server uses ASP.NET Core's dependency injection extensively. Services are registered in `Program.cs`:
- Scoped services for per-request operations (DbContext, user-specific services)
- Singleton services for shared state (SignalR connection tracking, background services)
- Hangfire for scheduled job execution

### Variable System

Flows use a variable mapping system (`VariableParser` in `Automaton.Core`):
- Variables defined as `${variableName}` in step configurations
- Input/output variables mapped between steps
- Global flow inputs/outputs
- Type conversion and validation

### Step Architecture

All steps inherit from `BaseStep` in `Automaton.Steps`:
```csharp
public class CustomStep : BaseStep
{
    public override Task<ExecutionResult> ExecuteAsync(IStepExecutionContext context)
    {
        // Step implementation
    }
}
```

Steps are registered in the flow designer and serialized to JSON for execution.

### Web App Integration

Published web apps (`/web/{publicUrl}`) use `window.automatonApi` client helper:
```javascript
// Executes a flow from web app frontend
await window.automatonApi.executeFlow(flowId, { inputs: {...} });
```

See `src/Automaton.Studio.Web/wwwroot/automatonApi.js` and `AGENTS.md` (Apps section) for details.

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

**Required Environment Setup:**

1. **MySQL/MariaDB Database:**
   - Connection string in `appsettings.json` or `appsettings.Development.json`
   - Run migrations before first use: `dotnet ef database update`

2. **JWT Settings:**
   - Set `JwtOptions:Issuer` and `AuthenticationSettings:HMacSettings:SecretKey`
   - Or configure RSA keys for `RsaSettings`

3. **AI Providers (Optional):**
   - Configure API keys in Studio UI (`/ai-keys` page)
   - Or set in `AiProviders` section of appsettings
   - Supports OpenAI, Anthropic, Google, Azure

4. **Email Configuration (Optional):**
   - Set `EmailConfig:EmailApiKey` for user registration emails

## Testing Strategy

**Unit Tests Structure:**
- `Automaton.Core.Tests` - Core workflow engine tests
- `Automaton.Studio.Server.Tests` - API controller and service tests
- `Automaton.Runner.Console.Tests` - Runner logic tests
- `Automaton.Studio.Tests` - UI component tests (Blazor)

**Key Test Patterns:**
- Use `InMemoryDatabase` for EF Core tests
- Mock SignalR hub contexts for real-time communication tests
- Integration tests use `WebApplicationFactory<Program>`

## Troubleshooting

**Common Issues:**

1. **Migration Errors:**
   - Ensure you're in `src/Automaton.Studio.Server` directory
   - Check connection string is valid
   - Verify MySQL server is running

2. **Runner Connection Issues:**
   - Verify API key is valid and has `Runner` scope
   - Check SignalR endpoint URL in runner configuration
   - Review runner logs for connection errors

3. **Python Step Failures:**
   - Ensure Python runtime is installed and accessible
   - Check `pythonnet` package compatibility
   - Verify custom step code syntax

4. **Build Errors:**
   - Clean solution: `dotnet clean && dotnet build`
   - Restore packages: `dotnet restore`
   - Check .NET SDK version: `dotnet --version` (requires .NET 9.0)

## Additional Documentation

For comprehensive documentation, see:
- `AGENTS.md` - Detailed project documentation and feature reference
- `README.md` - Project overview and quick start
- `docs/` - Feature-specific technical documentation:
  - `docs/projects/` - Multi-tenancy implementation
  - `docs/flows/` - Flow architecture
  - `docs/webapps/` - Web apps feature
  - `docs/runners/` - Runner deployment guides
  - `docs/apikeys/` - API key scopes and security
  - `docs/mcp/` - Model Context Protocol integration
