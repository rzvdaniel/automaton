# Deploying the Automaton Runner

This guide covers deploying an Automaton Runner on any Linux server (Hetzner, DigitalOcean, AWS EC2, etc.) using the pre-built Docker image published to GHCR.

## How the Release Pipeline Works

The production Runner artifact authority is GitHub Actions
[Runner image](../../.github/workflows/runner-image.yml). It builds
`src/Automaton.Runner/Dockerfile` with the repository root as its context, checks
the non-root user, Docker CLI and bundled trusted Actions, then pushes that same
image to `ghcr.io/rzvdaniel/automaton-runner`.

Relevant changes merged to `main` publish `sha-<full-commit-sha>` and `latest`.
`v*` and `runner-v*` tags publish the immutable SHA tag and a version alias.
Manual **Run workflow** builds use the selected repository branch or tag and
publish its exact checked-out SHA; non-main branch builds do not move `latest`.
There is no pull-request image publication or production-host image build.

Use the workflow summary and its `runner-image` artifact to obtain the published
`repository@sha256:<digest>` reference. Verify its OCI source-revision label
before updating a machine. Do not substitute `latest` for acceptance evidence.
Existing pinned Docker Hub images remain usable as rollback artifacts; their
registry is no longer the active build/publish authority.

GHCR publishing uses the workflow's `GITHUB_TOKEN`; no Docker Hub secret is
required. A newly created GHCR package is private by default. Make the Runner
package Public in GitHub package settings before expecting anonymous installation,
or supply authorized registry authentication. Existing installations keep their
configured image reference until an operator updates it.


---

## Quick Start (Docker Compose)

This is the recommended production deployment method.

### 1. Prerequisites

- Linux server with Docker Engine 24+ and Docker Compose v2 (`docker compose`)
- An API key from your Automaton server: API Keys → Generate New Key (scope: `runner`)
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
    image: ghcr.io/rzvdaniel/automaton-runner:latest
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
| `runner-config` | `/home/automaton/.config/AutomatonRunner` | Runner local configuration and server binding |
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
#   image: ghcr.io/rzvdaniel/automaton-runner:v1.2.0
docker compose up -d
```

---

## Environment Variable Reference

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `AUTOMATON_SERVER_URL` | Yes | `https://api.automaton.monster` | Base URL of the Automaton server |
| `AUTOMATON_API_KEY` | Yes | _(none)_ | Runner API key (format: `ak_runner_xxx`) |
| `AUTOMATON_RUNNER_NAME` | No | hostname | Display name shown in the Automaton dashboard |
| `RUNNER_SCRIPTS_DIR` | No | `/app/data/apps` (Docker) or `<exe-dir>/data/apps` (standalone) | Directory where deployed app controller scripts are stored. Override if you want scripts on a different volume or path. |
| `DOTNET_ENVIRONMENT` | No | `Production` | Set to `Development` **only** when the server uses a self-signed certificate (local dev). Do **not** set this in production |
| `PYTHONNET_PYDLL` | No | `/usr/local/lib/libpython3.13.so` | Path to the Python shared library used by PythonNet for custom Python steps. Override if using a different Python version or install path. |

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

## Running as a Standalone Service (On-Premise, No Docker)

Use this when you want to run the runner directly on a Windows or Linux machine without Docker.

### Prerequisites

- .NET 9.0 Runtime
- Python 3.x with `pip` (for app controller execution via FastAPI)
- The runner binary (`automaton-runner` / `automaton-runner.exe`)

### 1. Install Python dependencies

```bash
pip install fastapi uvicorn[standard]
```

### 2. Set environment variables

**Linux / macOS:**

```bash
export AUTOMATON_SERVER_URL=https://api.automaton.monster
export AUTOMATON_API_KEY=ak_runner_xxxxxxxxxxxxxxxxxxxx
export AUTOMATON_RUNNER_NAME=my-on-premise-runner

# Optional: override script storage location (default: <exe-dir>/data/apps)
# export RUNNER_SCRIPTS_DIR=/var/lib/automaton/apps
```

**Windows (PowerShell):**

```powershell
$env:AUTOMATON_SERVER_URL = "https://api.automaton.monster"
$env:AUTOMATON_API_KEY    = "ak_runner_xxxxxxxxxxxxxxxxxxxx"
$env:AUTOMATON_RUNNER_NAME = "my-on-premise-runner"

# Optional: override script storage location (default: <exe-dir>\data\apps)
# $env:RUNNER_SCRIPTS_DIR = "C:\AutomatonData\apps"
```

For a permanent installation, set these as system environment variables or use a `.env` file in the runner directory.

### 3. Run the runner

```bash
./automaton-runner          # Linux / macOS
automaton-runner.exe        # Windows
```

The runner will automatically launch the FastAPI backend for app controller execution. It discovers Python from `PATH`, so ensure Python is available.

### 4. Run as a system service (Linux systemd)

```ini
# /etc/systemd/system/automaton-runner.service
[Unit]
Description=Automaton Runner
After=network.target

[Service]
User=automaton
WorkingDirectory=/opt/automaton-runner
ExecStart=/opt/automaton-runner/automaton-runner
Restart=on-failure
Environment=AUTOMATON_SERVER_URL=https://api.automaton.monster
Environment=AUTOMATON_API_KEY=ak_runner_xxxxxxxxxxxxxxxxxxxx
Environment=AUTOMATON_RUNNER_NAME=my-server-runner

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now automaton-runner
sudo journalctl -u automaton-runner -f
```

### 5. Run as a Windows Service

Use the `scripts/setup-service.ps1` helper:

```powershell
.\scripts\setup-service.ps1
```

Or register manually with `sc.exe` after setting the environment variables in the system settings.

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
