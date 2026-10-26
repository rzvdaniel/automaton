# Automaton

Automaton is an AI-first platform for building, deploying, publishing, and running web apps.

Build apps locally with your preferred AI coding agent, describe their build/runtime contract explicitly, deploy them with the Automaton CLI, and publish them through Automaton Studio. Applications may use any technology available in the selected Runner workspace.

> AI can code. Automaton helps it ship.

## What Automaton provides

- **CLI-first app workflow** — create, pull, deploy, and publish Automaton apps from a local folder.
- **Studio** — authenticated UI for apps, publishing, runners, API keys, AI keys, admin features, projects, and account management.
- **Published app hosting** — expose a healthy application runtime through public Automaton app URLs.
- **Generic application contract** — declare structured build/runtime commands, port, health, environment, and persistent storage without selecting a framework.
- **Runner model** — run the declared application process on your own infrastructure or managed runner infrastructure.
- **AI-agent harness** — repository instructions, skills, and verification guidance for tools like Claude Code, Codex, Cursor, and Copilot.

## Install on your server

The versioned installer targets x86_64 Ubuntu 24.04 or newer. Use an Ubuntu release supported by Docker; LTS releases are recommended for servers. Before you begin, choose a published `server-vX.Y.Z` release, prepare `api`, `studio`, and `apps` DNS records (including `*.apps`), and allow inbound ports 80, 443 and 5671. The server needs at least 10 GiB free disk space.

1. With access to the private source repository, download and run the installer, replacing the version with a release that exists on the [Releases page](https://github.com/rzvdaniel/automaton/releases):

   ```bash
   VERSION=server-vX.Y.Z
   gh release download "$VERSION" --repo rzvdaniel/automaton \
     --pattern automaton-server.tar.gz --pattern automaton-server.tar.gz.sha256
   sha256sum -c automaton-server.tar.gz.sha256
   tar -xzf automaton-server.tar.gz
   sudo bash automaton-server/install.sh --install-docker
   ```

   It asks for your domain and certificate email, creates protected server credentials, then starts API, Studio, Worker, MySQL, RabbitMQ and Traefik. A missing Docker Compose plugin is installed automatically. `--install-docker` installs Docker Engine only when it is missing.

2. Open `https://studio.example.com/install`. On the server, run `sudo cat /etc/automaton/secrets/setup-token`, paste the token into Studio, check the installation, and create your first administrator.
3. Sign in and install a Machine Management Runner from **Runners** before deploying apps. You can install it on this server or another machine.

See the [server installation guide](docs/prod/install-automaton.md) for Cloudflare certificates, custom paths, configuration, recovery and backup details.

For installed servers, `sudo bash /opt/automaton-server/update.sh --version server-vX.Y.Z` updates compatible API, Worker and Studio images from the public GHCR distribution package after making a recovery backup. The initial installer download still requires repository access; see the guide for the offline bundle path and update limits.

## Core architecture

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
- Node.js 18+ and npm/pnpm
- MySQL 5.7+ or MariaDB 10.2+
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

## Runner

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

Alternative manual runner for local debugging:

```bash
cd src/Automaton.Runner
dotnet build -c Release
dotnet publish -c Release --self-contained false
AUTOMATON_API_KEY=ak_runner_xxx automaton-runner
```

See detailed guidance in `docs/runners/docker-runner.md`.

## Build and verification

### Backend and tests

```bash
dotnet test
```

### Studio frontend

```bash
cd src/automaton-studio-vue
npm run type-check
npm run build
```

### Website

```bash
cd src/automaton-vue
npm run build
```

### Production scripts

```bash
bash -n scripts/prod-redeploy-all.sh
bash -n scripts/prod-redeploy-api.sh
bash -n scripts/prod-redeploy-worker.sh
bash -n scripts/prod-redeploy-studio.sh
bash -n scripts/prod-redeploy-website.sh
bash -n scripts/prod-verify.sh
bash -n scripts/prod-reset-blue-green.sh
```

Run only the commands relevant to the files you changed.

Agents should start with `AGENTS.md`, then read the matching skill from `.agents/skills/`.

Repository-owned skills cover generic runtime behavior, production deployment, and administrator documentation. Developer-local skills may also exist there but are not part of the repository contract.

## Documentation

- [Self-hosted roadmap](docs/roadmap/README.md) — proposed direction and milestones for simplifying the existing Runner architecture
- `AGENTS.md` — repository-level agent/developer contract
- `.agents/skills/` — task-specific AI agent skills
- `docs/dev/messaging-rabbitmq.md` — internal CAP + RabbitMQ event publishing/consumption architecture
- production `appsettings.Production.json` is mounted into both `Automaton.Api` and `Automaton.Worker`; RabbitMQ/MySQL credentials live there, not in the public reverse proxy
- `docs/runners/docker-runner.md` — runner deployment details
- `src/automaton-studio-vue/REDESIGN_PLAN.md` — frontend redesign notes, if still relevant

## License

Licensed under the Apache License 2.0.
