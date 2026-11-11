# Repository development

- **Website**: Vue 3 + TypeScript + Vite (`src/automaton-vue/`)
- **Studio frontend**: Vue 3 + TypeScript + Vite + PrimeVue (`src/automaton-studio-vue/`)
- **Studio backend**: ASP.NET Core 10 REST API + SignalR (`src/Automaton.Api/`)
- **Worker**: .NET background worker for CAP/RabbitMQ consumers and async side effects (`src/Automaton.Worker/`)
- **CLI**: .NET global tool for local app workflows (`src/Automaton.Cli/`)
- **Runner**: cross-platform .NET runner for backend execution (`src/Automaton.Runner/`)
- **Core libraries**: `Automaton.Core`

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

- .NET 10 SDK
- Node.js 22+ and npm/pnpm
- PostgreSQL 16 (the local Compose default)
- Docker, for local database/runner workflows

## Quickstart for repository development

### 1. Start local infrastructure

```bash
# From repo root
docker compose -f docker-compose.dev.yml up -d postgres
docker compose -f docker-compose.rabbitmq.yml up -d
```

Configure the backend connection string. Replace `<your-POSTGRES_PASSWORD>` with the `POSTGRES_PASSWORD` the PostgreSQL container starts with: set it in a local, untracked `.env` in the repo root (start from `.env.example`) before the first `docker compose up`. The compose files refuse to start without it (also set `RABBITMQ_PASSWORD` for RabbitMQ), so no password is committed to the repository; the Worker's `appsettings.json` only holds placeholders, so override them for `src/Automaton.Worker` with user secrets (`ConnectionStrings:PostgreSqlConnection`, `RabbitMq:Password`) or environment variables (`ConnectionStrings__PostgreSqlConnection`, `RabbitMq__Password`). `docker/test-setup.sh` and `scripts/setup-local-development.ps1` generate missing passwords into `.env` for you; the local runtime stack (`docker-compose.runtime-local.yml`) uses `LOCAL_RUNTIME_POSTGRES_PASSWORD` and `LOCAL_RUNTIME_RABBITMQ_PASSWORD`.

```bash
cd src/Automaton.Api
dotnet user-secrets set "ConnectionStrings:PostgreSqlConnection" "Host=localhost;Port=5432;Database=automaton;Username=automaton_user;Password=<your-POSTGRES_PASSWORD>"
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
