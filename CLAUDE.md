# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Automaton is a cross-platform workflow automation platform built on .NET 9.0 with Blazor. It provides a visual workflow designer, integrated web app builder, remote execution runners, and custom step authoring capabilities with Python/PowerShell/Batch support.

**Core Architecture:**
- **Automaton.Web**: Static marketing website (HTML/CSS/JS)
- **Automaton.Studio**: Blazor WebAssembly UI application
  - Uses AntDesign component library for UI
  - BlazorMonaco for code editing in custom steps and web apps
  - Blazored.LocalStorage for client-side storage
  - References: Automaton.Core, Automaton.Steps, Automaton.Core.Scripting, Automaton.Mcp, Automaton.Studio.Auth
- **Automaton.Studio.Server**: ASP.NET Core Web API backend
  - 25 REST API controllers for all features
  - Entity Framework Core 9.0 with Pomelo MySQL provider
  - SignalR hubs for real-time communication
  - Hangfire for background job scheduling
  - ASP.NET Core Identity with JWT Bearer authentication
  - Serilog logging with MariaDB sink
  - Swashbuckle/Swagger for API documentation
- **Automaton.Studio.Auth**: Shared authentication library
  - JWT Bearer token handling
  - Blazored.LocalStorage integration for token persistence
  - ASP.NET Core Components Authorization
  - Used by both Studio and external integrations
- **Automaton.Runner**: Cross-platform console runner
  - Standalone executable (can be published as single-file)
  - SignalR client for server communication
  - Polly for resilience and retry policies
  - Serilog logging to console and file
  - System.CommandLine for CLI interface
  - Executes flows remotely on behalf of server
- **Automaton.Core**: Workflow execution engine
  - MediatR for CQRS pattern implementation
  - System.Linq.Dynamic.Core for dynamic expressions
  - Serilog HTTP sink for remote logging
  - Inspired by Workflow Core architecture
- **Automaton.Steps**: Built-in automation steps library
  - HTTP client steps for API calls
  - References Core and Core.Scripting
  - System.Linq.Dynamic.Core for dynamic filtering
- **Automaton.Core.Scripting**: Python integration layer
  - pythonnet 3.0.5 for CPython interop
  - Custom step execution with variable injection
  - MediatR integration for step lifecycle
- **Automaton.Mcp**: Model Context Protocol integration
  - OpenAI SDK integration for AI-powered code generation
  - Custom AI providers abstraction layer
  - SYSTEM_PROMPT_DOCUMENTATION.md for prompt engineering
  - Used for AI-assisted custom step and web app development
- **Automaton.ContainerAgent**: Docker/container orchestration agent (in development)
  - Located at src/Automaton.ContainerAgent/ but not yet fully implemented

## Common Commands

### Build

```bash
# Build all projects
dotnet build

# Build specific project
dotnet build src/Automaton.Studio
dotnet build src/Automaton.Runner

# Build in Release mode
dotnet build -c Release
```

### Run

```bash
# Run the web application (from solution root)
dotnet run --project src/Automaton.Studio

# Run the runner console
dotnet run --project src/Automaton.Runner

# Or after building
cd src/Automaton.Studio
dotnet run
```

### Testing

```bash
# Run all tests
dotnet test

# Run tests for specific project
dotnet test src/UnitTests/Automaton.Core.Tests
dotnet test src/UnitTests/Automaton.Studio.Server.Tests
dotnet test src/UnitTests/Automaton.Runner.Tests

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
cd src/Automaton.Runner
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
   - `Account/` - User account management and profile settings
   - `AiApiKeys/` - AI provider API key configuration (OpenAI, Anthropic, Google, Azure)
   - `ApiKeys/` - API key management for runners, webhooks, and integrations
   - `Apps/` - Web app builder with Monaco editor integration
   - `Authentication/` - Login, registration, email confirmation pages
   - `Dashboard/` - Main dashboard with recent activities and statistics
   - `Downloads/` - Runner downloads page for different platforms
   - `Flows/` - Flow management and visual workflow designer
   - `Install/` - Initial installation and setup wizard
   - `Projects/` - **NEW**: Project/organization multi-tenancy feature
   - `Runners/` - Runner registration and management
   - `Settings/` - Application settings and configuration
   - `StepDesigner/` - Custom step designer (Python/PowerShell/Batch)
   - `Steps/` - Individual step component definitions

2. **API Layer** (`Automaton.Studio.Server/Controllers`): 25 REST controllers:
   - **Core Features:**
     - `FlowsController` - Flow CRUD operations and execution triggers
     - `FlowExecutionsController` - Flow execution history and logs
     - `AppsController` - Web app management and publishing
     - `PublicWebAppsController` - Public endpoints for published web apps
     - `AppsFlowExecutionController` - Execute flows from web apps
     - `AppsStepExecutionController` - Execute individual steps from web apps
     - `RunnersController` - Runner registration and management
     - `CustomStepsController` - Custom step authoring and management
   - **Project Management (NEW):**
     - `ProjectsController` - Project/organization CRUD
     - `ProjectMembersController` - Team member management
     - `ProjectResourcesController` - Link flows/apps/runners to projects
     - `InvitationsController` - Team invitation workflow
   - **Security & Configuration:**
     - `ApiKeysController` - API key generation and management
     - `AiApiKeysController` - AI provider API key configuration
     - `AccountController` - User account management
     - `TokenController` - JWT token generation and refresh
     - `UserController` - User profile and settings
   - **System:**
     - `DashboardController` - Dashboard statistics and metrics
     - `RecentActivitiesController` - Activity feed
     - `SchedulesController` - Flow scheduling (Hangfire integration)
     - `SensitiveVariablesController` - Encrypted variable storage
     - `SettingsController` - Application configuration
     - `InstallController` - Initial setup wizard
     - `ClientLogsController` - Client-side error logging

3. **Business Logic** (`Automaton.Studio.Server/Services`, `Automaton.Core`):
   - **Core Services:**
     - `FlowsService` - Flow management and CRUD operations
     - `FlowExecutionHistoryService` - Execution tracking and history
     - `FlowExecutionLogService` - Real-time log streaming
     - `AppsService` - Web app publishing and management
     - `AppsStepExecutionService` - Execute steps from web apps
     - `AppStepsService` - Manage app-embedded workflow steps
     - `CustomStepsService` - Custom step creation and validation
   - **Security & Configuration:**
     - `ApiKeyService` - API key generation and validation
     - `AiApiKeyService` - AI provider key management with encryption
     - `ConfigurationService` - Application settings
     - `SensitiveVariablesService` - Encrypted variable storage (not shown but likely exists)
   - **System Services:**
     - `DashboardStatsService` - Dashboard metrics calculation
     - `EmailService` / `NoOpEmailService` - Email sending abstraction
     - `InstallService` - Initial setup wizard logic
     - `InvitationTokenService` - Team invitation token management
   - **AI Integration:**
     - `AiProviders/` directory - OpenAI, Anthropic, Google, Azure integrations
   - **Architecture Patterns:**
     - MediatR for CQRS commands/queries
     - SignalR hubs for real-time runner communication
     - Service interfaces in `Interfaces/` directory

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
- Uses `pythonnet 3.0.5` library for CPython interop
- Custom steps execute Python code in isolated contexts
- Variable injection and output capture
- Error handling and timeout support
- MediatR integration for step execution lifecycle

### Model Context Protocol (MCP) Integration

**Automaton.Mcp:**
- OpenAI SDK integration for AI-powered code generation
- Custom AI provider abstraction supporting multiple LLM providers
- Used for AI-assisted custom step and web app development
- System prompt engineering documented in `SYSTEM_PROMPT_DOCUMENTATION.md`
- Key features:
  - Automatic code generation with context awareness
  - Multi-turn conversations for iterative development
  - Tool execution for testing generated code
  - Support for OpenAI, Anthropic, Google, and Azure AI providers

**MCP Tool Providers (January 2026 Updates):**
- **StepMcpToolProvider**: Custom step designer with code execution testing
- **UnifiedAppMcpToolProvider**: Full-stack app generation (frontend + backend)
  - Auto-linking: Backend steps automatically linked to apps on creation
  - Testing capability: `execute_backend_step_code` tool validates steps before deployment
  - Shared execution service ensures consistency with Runner execution
- **Recent Improvements**:
  - Steps created via `create_backend_step` are automatically linked (atomic operation)
  - AI must test backend steps with `execute_backend_step_code` before finalizing apps
  - Detailed error reporting with line numbers, stack traces, and recommendations
  - System prompts enforce test-fix-retest workflow for quality assurance
- See `docs/mcp/BACKEND_STEP_TESTING.md` for testing workflow documentation

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

**Unit Tests Structure** (Located in `src/UnitTests/`):
- `Automaton.Core.Tests/` - Core workflow engine and execution tests
- `Automaton.Studio.Server.Tests/` - API controller and service tests
- `Automaton.Runner.Tests/` - Runner SignalR client and execution tests
- `Automaton.Studio.Tests/` - Blazor UI component tests

**Key Test Patterns:**
- Use `InMemoryDatabase` for EF Core tests
- Mock SignalR hub contexts for real-time communication tests
- Integration tests use `WebApplicationFactory<Program>`

**Test Commands:**
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test src/UnitTests/Automaton.Core.Tests
dotnet test src/UnitTests/Automaton.Studio.Server.Tests
dotnet test src/UnitTests/Automaton.Runner.Tests
dotnet test src/UnitTests/Automaton.Studio.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

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

## Key Technologies & Libraries

**Backend (.NET 9.0):**
- Entity Framework Core 9.0 with Pomelo.EntityFrameworkCore.MySql 9.0
- ASP.NET Core Identity for authentication
- SignalR for real-time communication
- Hangfire 1.8.16 for background jobs (with MySQL storage)
- MediatR 12.4.1 for CQRS pattern
- Serilog 4.1.0 for structured logging
- FluentValidation for request validation
- Swashbuckle (Swagger) for API documentation
- MailKit/MimeKit for email sending
- OpenAI SDK 2.5.0 for AI integration
- System.Linq.Dynamic.Core 1.6.7 for dynamic queries
- pythonnet 3.0.5 for Python interop

**Frontend (Blazor WebAssembly):**
- AntDesign 1.4.3 - Component library
- BlazorMonaco 3.4.0 - Code editor (Monaco Editor)
- Blazored.LocalStorage 4.5.0 - Browser storage
- Blazored.FluentValidation 2.2.0 - Client-side validation
- Markdig 0.38.0 - Markdown rendering
- Cronos 0.8.4 - Cron expression parsing
- CronExpressionDescriptor 2.36.0 - Human-readable cron descriptions

**Runner Console:**
- Microsoft.AspNetCore.SignalR.Client 9.0 for server connection
- Polly 8.5.0 for resilience and retry policies
- System.CommandLine 2.0.0-beta4 for CLI interface
- Serilog with Console and File sinks

**Testing:**
- xUnit (implied from test project structure)
- WebApplicationFactory for integration tests
- In-memory database for EF Core tests

## Project Structure Summary

```
src/
├── Automaton.Web/                    # Static marketing website
├── Automaton.Studio/                 # Blazor WebAssembly UI
│   ├── Pages/                        # Blazor pages organized by feature
│   ├── Services/                     # Client-side services
│   └── wwwroot/                      # Static assets
├── Automaton.Studio.Server/          # ASP.NET Core API
│   ├── Controllers/                  # 25 REST API controllers
│   ├── Services/                     # Business logic services
│   ├── Data/                         # EF Core DbContext
│   ├── Entities/                     # Database entities
│   ├── Hubs/                         # SignalR hubs
│   └── Migrations/                   # EF Core migrations
├── Automaton.Studio.Auth/            # Shared auth library
├── Automaton.Runner/                 # Console runner application
├── Automaton.Core/                   # Workflow execution engine
├── Automaton.Steps/                  # Built-in automation steps
├── Automaton.Core.Scripting/         # Python integration
├── Automaton.Mcp/                    # Model Context Protocol client
├── Automaton.ContainerAgent/         # Container orchestration (WIP)
└── UnitTests/
    ├── Automaton.Core.Tests/
    ├── Automaton.Studio.Server.Tests/
    ├── Automaton.Runner.Tests/
    └── Automaton.Studio.Tests/
```

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
