# Deploying the Automaton Runner

This guide covers deploying an Automaton Runner on any Linux server (Hetzner, DigitalOcean, AWS EC2, etc.) using the pre-built Docker image published to Docker Hub.

## How the Release Pipeline Works

The runner is built and published automatically by `azure-pipelines-runner-docker.yml` when a git tag matching `v*` or `runner-v*` is pushed:

```bash
git tag runner-v1.2.0
git push origin runner-v1.2.0
```

This triggers a build from `src/Automaton.Runner/Dockerfile` (build context `src/`) and pushes two tags to Docker Hub:

| Tag | Description |
|----|-------------|
| `rzvdaniel/automaton-runner:latest` | Always points to the most recent release |
| `rzvdaniel/automaton-runner:v1.2.0` | Pinned version tag |

---

## Quick Start (Docker Compose)

This is the recommended production deployment method.

### 1. Prerequisites

- Linux server with Docker Engine 24+ and Docker Compose v2 (`docker compose`)
- An API key from your Automaton server: Settings → API Keys → Create (scope: `runner`)
- The URL of your Automaton server (e.g., `https://api.automaton.monster`)

### 2. Create the working directory

```bash
mkdir -p ~/automaton-runner && cd ~/automaton-runner
```

### 3. Create `.env`

```bash
cat > .env << 'EOF'
AUTOMATON_SERVER_URL=https://api.automaton.monster
AUTOMATON_API_KEY=ak_runner_xxxxxxxxxxxxxxxxxxxx
AUTOMATON_RUNNER_NAME=my-server-runner
EOF
```

> **Note:** Never commit `.env` to source control. Keep your API key secret.

### 4. Create `docker-compose.yml`

```yaml
services:
  runner:
    image: rzvdaniel/automaton-runner:latest
    container_name: automaton-runner
    restart: unless-stopped
    env_file: .env
    environment:
      - RUNNER_SCRIPTS_DIR=/app/data/apps
    volumes:
      - runner-config:/home/automaton/.config/AutomatonRunner
      - runner-logs:/app/logs
      - runner-data:/app/data

volumes:
  runner-config:
  runner-logs:
  runner-data:
```

### 5. Start the runner

```bash
docker compose up -d
docker compose logs -f runner
```

You should see output like:

```
Starting FastAPI backend...
FastAPI backend ready
[+] Running AutomatonRunner...
[*] Connecting to https://api.automaton.monster ...
[*] Runner registered: my-server-runner
```

> FastAPI is pre-installed in the image and starts automatically. No extra setup needed — app controller execution works out of the box.

---

## Named Volume Reference

| Volume | Mount path | Contents |
|--------|-----------|----------|
| `runner-config` | `/home/automaton/.config/AutomatonRunner` | Runner registration token, server binding |
| `runner-logs` | `/app/logs` | Serilog structured log files |
| `runner-data` | `/app/data` | Deployed app controller scripts (`/app/data/apps`) |

### `RUNNER_SCRIPTS_DIR`

This environment variable tells the runner where to store Python/PowerShell scripts deployed from the Automaton server when users publish app controllers. The Docker Compose example sets it to `/app/data/apps`, which lives inside the `runner-data` volume so scripts survive container restarts and upgrades.

---

## Upgrading the Runner

Pull the new image and recreate the container. Named volumes are preserved automatically.

```bash
docker compose pull
docker compose up -d
```

To upgrade to a specific version instead of `latest`:

```bash
# In docker-compose.yml, change image to:
#   image: rzvdaniel/automaton-runner:v1.2.0
docker compose up -d
```

---

## Environment Variable Reference

| Variable | Required | Description |
|----------|----------|-------------|
| `AUTOMATON_SERVER_URL` | Yes | Base URL of the Automaton server |
| `AUTOMATON_API_KEY` | Yes | Runner API key (format: `ak_runner_xxx`) |
| `AUTOMATON_RUNNER_NAME` | No | Display name. Defaults to hostname |
| `RUNNER_SCRIPTS_DIR` | Yes | Where to store deployed app scripts. Set to `/app/data/apps` |
| `DOTNET_ENVIRONMENT` | No | Set to `Development` **only** when the server uses a self-signed certificate (local dev). Do **not** set this in production |

---

## SSL / Certificate Notes

The runner validates the server's TLS certificate in `Production` mode (the default). If the Automaton server has a valid certificate from a public CA (Let's Encrypt, etc.), no additional configuration is needed.

For local development only — where the server runs on `localhost` with a self-signed ASP.NET developer certificate — add this to `.env`:

```
DOTNET_ENVIRONMENT=Development
```

**Do not set this in production.** It disables certificate validation.

---

## Viewing Logs

```bash
# Stream live logs
docker compose logs -f runner

# Last 100 lines
docker compose logs --tail=100 runner

# Persistent log files inside the container
docker exec automaton-runner ls /app/logs/
```

---

## Troubleshooting

### Runner doesn't appear in the Automaton dashboard

1. Check that `AUTOMATON_SERVER_URL` is reachable from the container.
2. Verify the API key is correct and has the `runner` scope.
3. Check logs for connection errors: `docker compose logs runner`.

### App controller scripts fail to execute

Ensure `RUNNER_SCRIPTS_DIR=/app/data/apps` is set and the `runner-data` volume is mounted so deployed scripts persist across restarts. FastAPI is pre-installed in the image and starts automatically via `docker-entrypoint.sh` — if it fails to start, the container will exit with an error (check `docker compose logs runner`).

### Certificate validation errors

If you see `System.Net.Http.HttpRequestException: The SSL connection could not be established`, the server certificate is not trusted. Add `DOTNET_ENVIRONMENT=Development` to `.env` **only if** the server is local dev. For production, ensure the server has a valid TLS certificate.

### Container exits immediately

```bash
docker compose logs runner
```

Common causes:
- Missing or invalid `AUTOMATON_API_KEY`
- `AUTOMATON_SERVER_URL` unreachable
- Port conflict (check if another runner is already running)

---

## Deploying on Hetzner Cloud

Hetzner CX22 (2 vCPU, 4 GB RAM) is sufficient for most runner workloads.

1. Create a server (Ubuntu 24.04 recommended)
2. SSH in and install Docker:
   ```bash
   curl -fsSL https://get.docker.com | sh
   ```
3. Follow the [Quick Start](#quick-start-docker-compose) above
4. (Optional) Set up automatic restarts with systemd or use `restart: unless-stopped` (already in the example compose file)

For runners executing resource-intensive flows (heavy scripting, large file processing), consider CX32 or CX42.
