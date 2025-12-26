# Running Automaton Runner in Docker (Development)

This guide explains how to run the Automaton Runner as a Docker container for local development.

## Prerequisites

- Docker Desktop installed and running
- Automaton Studio Server running locally (default: `https://localhost:7091`)
- An API key created in Automaton Studio

## Quick Start

### 1. Create an API Key

1. Start Automaton Studio Server locally
2. Open the Studio UI in your browser
3. Go to **Settings > API Keys**
4. Click **Create API Key**
5. Select scope: **Runner** (or **All** for development)
6. Copy the generated key (starts with `ak_`)

### 2. Configure Environment

Copy the example environment file and add your API key:

```bash
# From the repository root
cp .env.example .env
```

Edit `.env` and set your API key in the Runner Configuration section:

```env
AUTOMATON_SERVER_URL=https://host.docker.internal:7091
AUTOMATON_API_KEY=ak_your_copied_key_here
AUTOMATON_RUNNER_NAME=Docker-Dev-Runner
```

### 3. Build and Start the Runner

The Docker container uses the `service` command which runs in non-interactive mode.

**Build and start with one command:**
```bash
# Build the image and start the runner (recommended for first run)
docker compose -f docker-compose.dev.yml up runner-dev --build -d
```

**Other options:**
```bash
# Start only the runner (if already built)
docker compose -f docker-compose.dev.yml up runner-dev -d

# Start MySQL + runner together
docker compose -f docker-compose.dev.yml up -d

# Start in foreground (see logs directly)
docker compose -f docker-compose.dev.yml up runner-dev

# Rebuild after code changes
docker compose -f docker-compose.dev.yml build runner-dev
docker compose -f docker-compose.dev.yml up runner-dev -d
```

The runner will:
1. Read `AUTOMATON_SERVER_URL` and `AUTOMATON_API_KEY` from the env file
2. Auto-register with the server using name `Service-{container-hostname}`
3. Connect to the SignalR hub and wait for workflow executions

### 4. Verify Connection

Once started, you should see the runner appear in **Runners** page in Automaton Studio.

Check logs if there are issues:

```bash
docker compose -f docker-compose.dev.yml logs runner-dev
```

## Troubleshooting

### Connection refused / Cannot reach server

**Windows/Mac:** The compose file uses `host.docker.internal` which should work automatically.

**Linux:** The compose file includes `extra_hosts` configuration, but if it doesn't work:
- Use your host's actual IP address in `AUTOMATON_SERVER_URL`
- Or run the Studio Server in Docker on the same network

### SSL/TLS certificate errors

For local development with self-signed certificates, you may need to:
1. Add the certificate to the container's trust store, or
2. Configure the runner to skip certificate validation (development only)

### API key rejected

- Ensure the API key starts with `ak_`
- Check the key hasn't expired
- Verify the key has **Runner** scope

## Rebuilding the Image

If you make changes to the runner code:

```bash
docker compose -f docker-compose.dev.yml build runner-dev
docker compose -f docker-compose.dev.yml up runner-dev
```

## Stopping the Runner

```bash
docker compose -f docker-compose.dev.yml down runner-dev

# Remove volumes too (clears registration/logs)
docker compose -f docker-compose.dev.yml down runner-dev -v
```

## What's Included

The development runner image includes:
- **.NET 9 Runtime** — runs the Automaton Runner console application
- **Python 3.13** — for executing custom Python steps
- **Non-root user** — runs as `automaton` user for security

### Pre-installed Python Packages

The runner image comes with commonly-used Python packages pre-installed:

| Category | Packages |
|----------|----------|
| **HTTP & API** | `requests`, `httpx`, `aiohttp` |
| **Data Processing** | `pandas`, `numpy` |
| **Serialization** | `pyyaml`, `python-dateutil` |
| **Databases** | `pymysql`, `psycopg2-binary` |
| **Cloud** | `boto3` (AWS SDK) |
| **Utilities** | `beautifulsoup4`, `lxml`, `Pillow`, `python-dotenv`, `jinja2` |

### Installing Additional Packages

If your custom step needs a package that's not pre-installed, you have several options:

**Option 1: Install at the start of your step (temporary)**
```python
import subprocess
import sys

# Install package if not available
try:
    import some_package
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "some_package"])
    import some_package

# Your step code here...
```

**Option 2: Extend the Docker image (permanent)**

Create a custom Dockerfile:
```dockerfile
FROM automaton-runner:latest

USER root
RUN pip install --no-cache-dir \
    your-package-1 \
    your-package-2
USER automaton
```

Build and use your custom image:
```bash
docker build -t my-automaton-runner .
```

**Option 3: Request package addition**

If a package is commonly needed, consider requesting it be added to the base image by opening an issue or PR.

## Why Docker for Python Steps?

**Docker is the recommended way to run Python custom steps** because it provides a controlled, reproducible environment.

### Docker vs Local Installation

| Concern | Docker Container | Local Installation |
|---------|------------------|-------------------|
| **Python version** | Pinned to 3.13 (tested) | User's version (may be incompatible) |
| **Python location** | Known path (`/usr/local/bin/python3`) | Varies (system, pyenv, conda, Windows Store...) |
| **PythonNet compatibility** | Guaranteed | May break with certain Python builds |
| **Shared library** | Pre-configured | Must auto-detect (fragile) |
| **Dependencies** | Pre-installed in image | User must install manually |

### Common Local Installation Issues

Running Python steps on a locally-installed runner can hit these problems:

- **Windows Store Python** — Missing DLLs or different paths
- **Anaconda/Miniconda** — Non-standard directory structure
- **pyenv / asdf** — Python in unusual locations
- **Multiple Python versions** — Unclear which version is used
- **32-bit vs 64-bit mismatch** — PythonNet crashes
- **Missing shared library** — Python compiled without `--enable-shared`

### Recommendation

| Use Case | Recommended Runner |
|----------|-------------------|
| **Development** | Docker (consistent, easy setup) |
| **Production** | Docker or Kubernetes (scalable, reproducible) |
| **Simple flows (no Python)** | Local runner is fine |
| **Python custom steps** | Docker strongly recommended |

If you must use a local runner with Python steps, ensure you have a supported Python version (3.10+) installed with the shared library available, and configure the paths in the Runner Settings page.

## See Also

- [Runner Registration Flow](runner-registration-flow.md)
- [Runner Resilience Guide](runner-resilience-guide.md)
- [Running Runner as a Service](runner-as-service.md)
