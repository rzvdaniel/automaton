# Repository development

- **Website**: Vue 3 + TypeScript + Vite (`src/automaton-vue/`)
- **Studio frontend**: Vue 3 + TypeScript + Vite + PrimeVue (`src/automaton-studio-vue/`)
- **Studio backend**: ASP.NET Core 9 REST API + SignalR (`src/Automaton.Api/`)
- **Worker**: .NET background worker for CAP/RabbitMQ consumers and async side effects (`src/Automaton.Worker/`)
- **CLI**: .NET global tool for local app workflows (`src/Automaton.Cli/`)
- **Runner**: cross-platform .NET runner for backend execution (`src/Automaton.Runner/`)
- **Core libraries**: `Automaton.Core`, `Automaton.Steps`, `Automaton.Core.Scripting`

AI provider implementation currently lives under `src/Automaton.Mcp/`; check current project naming and intent before extending it.

## App model

Automaton apps use a source/runtime split. The source layout is application-defined; the persisted `application` contract describes how to build and run it:

- `source/` — editable application source
- `live/` — disposable deployed artifact/runtime output
- `application.build.command` — optional structured build command
- `application.runtime.command` — structured runtime command for HTTP/public applications; optional only for finite-work or Actions-only apps
- `application.health` and `application.storage` — readiness and durable data policy

Studio editing, CLI pull, remix/fork, snapshots, and import/export should prefer `source/`.

Public app serving must never expose source files, secrets, logs, runner state, or unsafe file types.

## Requirements

- .NET 9 SDK
- Node.js 22+ and npm/pnpm
- MySQL 8.0 (the local Compose default)
- Docker, for local database/runner workflows

## Quickstart for repository development

### 1. Start local infrastructure

```bash
# From repo root
docker compose -f docker-compose.dev.yml up -d mysql
docker compose -f docker-compose.rabbitmq.yml up -d
```

Configure the backend connection string:

```bash
cd src/Automaton.Api
dotnet user-secrets set "ConnectionStrings:MySqlConnection" "Server=localhost;Port=3306;Database=automaton;User=automaton_user;Password=AutomatonPass123!;"
```

Apply migrations:

```bash
dotnet tool install --global dotnet-ef
dotnet ef database update
```

### 2. Run Studio backend

```bash
cd src/Automaton.Api
dotnet run
```

Swagger:

- `https://localhost:7091/swagger/index.html`
- `https://localhost:7091/swagger/v1/swagger.json`

### 3. Run Worker

```bash
cd src/Automaton.Worker
dotnet run
```

### 4. Run Studio frontend

```bash
cd src/automaton-studio-vue
npm install
npm run dev
```

Default Vite dev URL:

```txt
http://localhost:5173
```

### 5. Run website

```bash
cd src/automaton-vue
npm install
npm run dev
```

## Type generation

Generate TypeScript types from the backend OpenAPI spec:

```bash
cd src/automaton-studio-vue
npm run generate:types
```

Then verify:

```bash
npm run type-check
npm run build
```


## Checks

Run focused tests for the change. For backend changes, follow the build/analyzer instructions in [AGENTS.md](../AGENTS.md).

```bash
dotnet build Automaton.All.sln -c Debug --no-incremental
```

Release workflows live under [.github/workflows](../.github/workflows). Server bundles, Runner images and CLI/NuGet releases are published separately. See [CLI publishing](prod/cli-nuget-publishing.md).
