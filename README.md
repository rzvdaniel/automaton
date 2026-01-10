# Automaton

Automaton is an AI-first, cross-platform workflow automation platform. Describe what you want to automate in natural language, generate flows with reusable/custom steps, build web apps that call those flows, and execute them securely via distributed runners.

## Overview

**Architecture**
- **Frontend**: Vue 3 + TypeScript + Vite + PrimeVue (src/automaton-studio-vue)
- **Backend**: ASP.NET Core 9.0 REST API + SignalR (src/Automaton.Studio.Server)
- **Runners**: Cross-platform console app for distributed execution (src/Automaton.Runner)
- **Core Libraries**: Automaton.Core, Automaton.Steps, Automaton.Core.Scripting, Automaton.Mcp

**Key Capabilities**
- AI Flow Generation (natural language → flow + steps)
- Web Apps Platform (Monaco editor, live preview, PWA, publish)
- Custom Steps (Python/PowerShell/Batch) with inline testing
- Projects (organize flows/apps/steps with shared settings and roles)
- Secure, distributed execution via runner nodes with real-time telemetry
- API Keys and role-based access

See the detailed guide in [AGENTS.md](AGENTS.md) and the frontend redesign spec in [src/automaton-studio-vue/REDESIGN_PLAN.md](src/automaton-studio-vue/REDESIGN_PLAN.md).

## Requirements

- Node.js 18+ and npm/pnpm (Frontend)
- .NET 9.0 SDK (Backend & Runner)
- MySQL 5.7+ or MariaDB 10.2+ (Database)

## Quickstart

### Frontend (Vue.js)

```bash
cd src/automaton-studio-vue
npm install
npm run dev        # http://localhost:5173 with HMR

# Optional
npm run type-check # TypeScript checks
npm run lint       # ESLint
npm run format     # Prettier
```

### Backend (ASP.NET Core)

```bash
cd src/Automaton.Studio.Server
dotnet run                    # https://localhost:7091
```

### Database (Docker Compose)

Use the provided compose for local MySQL:

```bash
# From repo root
docker compose -f docker-compose.dev.yml up -d mysql

# Configure connection string (example)
cd src/Automaton.Studio.Server
dotnet user-secrets set "ConnectionStrings:MySqlConnection" "Server=localhost;Port=3306;Database=automaton;User=automaton_user;Password=AutomatonPass123!;"

# Apply EF Core migrations
dotnet tool install --global dotnet-ef
dotnet ef database update
```

### Type Generation (Frontend)

Generate TypeScript types from the backend OpenAPI spec:

```bash
cd src/automaton-studio-vue
npm run generate:types
```

### Runner (Docker Compose – preferred)

Use the provided Compose file for production-like runner deployment:

```bash
# From repo root
cp .env.example .env

# Configure environment in .env
# AUTOMATON_SERVER_URL=https://your-server-url
# AUTOMATON_API_KEY=ak_runner_xxx

# Start runner
docker compose -f docker-compose.runner.yml up -d

# Tail logs
docker compose -f docker-compose.runner.yml logs -f runner

# Stop/remove
docker compose -f docker-compose.runner.yml down
```

Alternative (manual, for local debugging only):

```bash
cd src/Automaton.Runner
dotnet build -c Release
dotnet publish -c Release --self-contained false
AUTOMATON_API_KEY=ak_runner_xxx automaton-runner
```

See detailed guidance in docs/runners/docker-runner.md.

## Build & Production

### Frontend

```bash
cd src/automaton-studio-vue
npm run build      # Outputs to dist/
npm run preview    # Preview production build locally
```

### Backend

```bash
cd src/Automaton.Studio.Server
dotnet publish -c Release
```

## API Documentation

- Swagger UI: https://localhost:7091/swagger/index.html
- OpenAPI JSON: https://localhost:7091/swagger/v1/swagger.json

## Screenshots

![Studio Flow](screenshots/Screenshot-2024-01-22-231011.png?raw=true "Studio Flow")

## License

Licensed under the Apache License 2.0.
