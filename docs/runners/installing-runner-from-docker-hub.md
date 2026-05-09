# Installing Automaton Runner from Docker Hub

This guide explains how to install and run the Automaton Runner container locally using the pre-built image from Docker Hub.

## Prerequisites

1. **Docker Desktop** installed and running
2. **Automaton API Key** — Create one in your Automaton Studio:
   - Go to **API Keys**
   - Click **Create API Key**
   - Select scope: **Runner**
   - Copy the key (starts with `ak_`)

## Quick Start

Pull and run the runner with a single command:

```bash
docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  -e AUTOMATON_SERVER_URL=https://api.automaton.monster \
  -e AUTOMATON_API_KEY=ak_your_key_here \
  -e AUTOMATON_RUNNER_NAME=My-Runner \
  -v automaton-runner-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-logs:/app/logs \
  -v automaton-runner-data:/app/data \
  rzvdaniel/automaton-runner:latest
```

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `AUTOMATON_SERVER_URL` | Yes | — | URL of your Automaton Studio Server |
| `AUTOMATON_API_KEY` | Yes | — | API key for authentication |
| `AUTOMATON_RUNNER_NAME` | No | `Service-{hostname}` | Display name for the runner |

## Installing via Docker Desktop GUI

If you prefer using the Docker Desktop graphical interface instead of the command line:

### 1. Search and Pull the Image

1. Open **Docker Desktop**
2. Click on **Images** in the left sidebar
3. Click the **Search images to run** button (or use the search bar at the top)
4. Search for `rzvdaniel/automaton-runner`
5. Click **Pull** to download the latest image

### 2. Run the Container

1. After pulling, find the image in **Images** list
2. Click the **Run** button (play icon) next to the image
3. Expand **Optional settings** before clicking Run
4. Configure the following:
   - **Container name**: `automaton-runner`
   - **Environment variables** (click **+ Add** for each):
     - `AUTOMATON_SERVER_URL` = `https://api.automaton.monster`
     - `AUTOMATON_API_KEY` = `ak_your_key_here`
     - `AUTOMATON_RUNNER_NAME` = `My-Runner`
5. Click **Run**

### 3. Verify Connection

1. Go to **Containers** in the left sidebar
2. Click on your `automaton-runner` container
3. Select the **Logs** tab to see output
4. You should see: `Runner registered successfully` and `Connected to Automaton Server`

### Managing via Docker Desktop

- **Stop**: Click the stop button (square icon) next to the container
- **Start**: Click the start button (play icon)
- **Restart**: Click the restart button (circular arrows)
- **Delete**: Click the delete button (trash icon)
- **View Logs**: Click the container name, then select **Logs** tab

---

## Step-by-Step Installation (Command Line)

### 1. Pull the Image from Docker Hub

```bash
# Pull the latest version
docker pull rzvdaniel/automaton-runner:latest

# Or pull a specific version
docker pull rzvdaniel/automaton-runner:v1.0.0
```

### 2. Create an Environment File (Recommended)

For easier configuration management, create a `.env` file:

```bash
# Create .env file
cat > .env << EOF
AUTOMATON_SERVER_URL=https://api.automaton.monster
AUTOMATON_API_KEY=ak_your_key_here
AUTOMATON_RUNNER_NAME=My-Runner
EOF
```

### 3. Run the Container

**Option A: Using environment file:**

```bash
docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  --env-file .env \
  -v automaton-runner-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-logs:/app/logs \
  -v automaton-runner-data:/app/data \
  rzvdaniel/automaton-runner:latest
```

**Option B: Inline environment variables:**

```bash
docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  -e AUTOMATON_SERVER_URL=https://api.automaton.monster \
  -e AUTOMATON_API_KEY=ak_your_key_here \
  -e AUTOMATON_RUNNER_NAME=My-Runner \
  -v automaton-runner-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-logs:/app/logs \
  -v automaton-runner-data:/app/data \
  rzvdaniel/automaton-runner:latest
```

### 4. Verify the Runner is Connected

Check the container logs:

```bash
docker logs -f automaton-runner
```

You should see:
```
[INFO] Runner registered successfully
[INFO] Connected to Automaton Server
[INFO] Waiting for workflow executions...
```

Also verify in Automaton Studio:
1. Go to **Runners** page
2. Your runner should appear in the list with status **Online**

## Connecting to Local Development Server

If you're running Automaton Studio Server **locally on your machine**, use `host.docker.internal`:

```bash
docker run -d \
  --name automaton-runner-dev \
  --restart unless-stopped \
  -e AUTOMATON_SERVER_URL=http://host.docker.internal:5091 \
  -e AUTOMATON_API_KEY=ak_your_dev_key \
  -e AUTOMATON_RUNNER_NAME=Local-Dev-Runner \
  -v automaton-runner-dev-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-dev-logs:/app/logs \
  -v automaton-runner-dev-data:/app/data \
  rzvdaniel/automaton-runner:latest
```

**Note:** Use port `5091` for HTTP or `7091` for HTTPS (requires certificate trust).

### Linux Users

On Linux, `host.docker.internal` doesn't work by default. Add it manually:

```bash
docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  --add-host=host.docker.internal:host-gateway \
  -e AUTOMATON_SERVER_URL=http://host.docker.internal:5091 \
  -e AUTOMATON_API_KEY=ak_your_key \
  -v automaton-runner-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-logs:/app/logs \
  -v automaton-runner-data:/app/data \
  rzvdaniel/automaton-runner:latest
```

## Managing the Container

### View Logs

```bash
# Follow live logs
docker logs -f automaton-runner

# View last 100 lines
docker logs --tail 100 automaton-runner

# View logs since 1 hour ago
docker logs --since 1h automaton-runner
```

### Stop the Runner

```bash
docker stop automaton-runner
```

### Start the Runner

```bash
docker start automaton-runner
```

### Restart the Runner

```bash
docker restart automaton-runner
```

### Remove the Container

```bash
# Stop and remove
docker stop automaton-runner
docker rm automaton-runner
```

### Update to Latest Version

```bash
# Pull the latest image
docker pull rzvdaniel/automaton-runner:latest

# Stop and remove old container
docker stop automaton-runner
docker rm automaton-runner

# Start new container with same configuration
docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  --env-file .env \
  -v automaton-runner-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-logs:/app/logs \
  -v automaton-runner-data:/app/data \
  rzvdaniel/automaton-runner:latest
```

## Using Docker Compose (Recommended for Production)

For easier management, create a `docker-compose.yml`:

```yaml
services:
  runner:
    image: rzvdaniel/automaton-runner:latest
    container_name: automaton-runner
    restart: unless-stopped
    environment:
      - AUTOMATON_SERVER_URL=https://api.automaton.monster
      - AUTOMATON_API_KEY=ak_your_key_here
      - AUTOMATON_RUNNER_NAME=My-Runner
    volumes:
      - runner-config:/home/automaton/.config/AutomatonRunner
      - runner-logs:/app/logs
      - runner-data:/app/data
    # Uncomment for local dev server
    # extra_hosts:
    #   - "host.docker.internal:host-gateway"

volumes:
  runner-config:
  runner-logs:
  runner-data:
```

Then manage with simple commands:

```bash
# Start
docker compose up -d

# View logs
docker compose logs -f

# Stop
docker compose down

# Update to latest
docker compose pull
docker compose up -d
```

## Advanced Configuration

### Running Multiple Runners

You can run multiple runner containers on the same machine:

```bash
# Runner 1
docker run -d \
  --name automaton-runner-1 \
  --restart unless-stopped \
  -e AUTOMATON_SERVER_URL=https://api.automaton.monster \
  -e AUTOMATON_API_KEY=ak_key_1 \
  -e AUTOMATON_RUNNER_NAME=Runner-1 \
  -v automaton-runner-1-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-1-logs:/app/logs \
  -v automaton-runner-1-data:/app/data \
  rzvdaniel/automaton-runner:latest

# Runner 2
docker run -d \
  --name automaton-runner-2 \
  --restart unless-stopped \
  -e AUTOMATON_SERVER_URL=https://api.automaton.monster \
  -e AUTOMATON_API_KEY=ak_key_2 \
  -e AUTOMATON_RUNNER_NAME=Runner-2 \
  -v automaton-runner-2-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-2-logs:/app/logs \
  -v automaton-runner-2-data:/app/data \
  rzvdaniel/automaton-runner:latest
```

### Volume Mounting (For Persistent Data)

The runner requires three named volumes for data persistence. If your workflows also need to read files from the host, mount an additional path alongside the required volumes:

```bash
docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  -e AUTOMATON_SERVER_URL=https://api.automaton.monster \
  -e AUTOMATON_API_KEY=ak_your_key \
  -v automaton-runner-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-logs:/app/logs \
  -v automaton-runner-data:/app/data \
  -v /host/shared:/app/shared \
  rzvdaniel/automaton-runner:latest
```

### Resource Limits

Limit CPU and memory usage:

```bash
docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  --memory="2g" \
  --cpus="2.0" \
  -e AUTOMATON_SERVER_URL=https://api.automaton.monster \
  -e AUTOMATON_API_KEY=ak_your_key \
  -v automaton-runner-config:/home/automaton/.config/AutomatonRunner \
  -v automaton-runner-logs:/app/logs \
  -v automaton-runner-data:/app/data \
  rzvdaniel/automaton-runner:latest
```

## What's Included in the Image

The runner image includes:

- **.NET 9 Runtime** — Runs the Automaton Runner console application
- **Python 3.13** — For executing custom Python steps
- **Pre-installed Python packages**:
  - HTTP & API: `requests`, `httpx`, `aiohttp`
  - Data Processing: `pandas`, `numpy`
  - Serialization: `pyyaml`, `python-dateutil`
  - Databases: `pymysql`, `psycopg2-binary`
  - Cloud: `boto3` (AWS SDK)
  - Utilities: `beautifulsoup4`, `lxml`, `Pillow`, `python-dotenv`, `jinja2`
- **Pre-installed system packages**: `ffmpeg`, `ghostscript`, `imagemagick`, `poppler-utils`, `pandoc`

Image size: ~500-600MB

## Troubleshooting

### Container Exits Immediately

Check logs for errors:

```bash
docker logs automaton-runner
```

Common issues:
- Missing or invalid `AUTOMATON_API_KEY`
- Cannot reach `AUTOMATON_SERVER_URL` (network/firewall)
- Server rejecting API key (check scope)

### Connection Refused

If you see `Connection refused` in logs:

**Connecting to local server:**
- Use `http://host.docker.internal:5091` (Windows/Mac)
- Use `http://host.docker.internal:5091` with `--add-host` (Linux)
- Verify the server is running on your host machine

**Connecting to remote server:**
- Ensure the URL is correct (including `https://` or `http://`)
- Check firewall/network settings
- Verify the server is accessible from Docker container

### API Key Rejected

```
[ERR] Registration failed: Unauthorized
```

**Solutions:**
1. Verify the API key is correct (starts with `ak_`)
2. Check the key hasn't expired (in Studio > API Keys)
3. Ensure the key has **Runner** scope
4. Try creating a new API key

### SSL/TLS Certificate Errors

If connecting to an HTTPS server with self-signed certificates:

```
[ERR] The SSL connection could not be established
```

**Solutions:**
1. Use HTTP instead of HTTPS (development only)
2. Configure certificate validation (advanced)
3. Use a properly signed certificate on the server

### Image Not Found

```
Error: Unable to find image 'rzvdaniel/automaton-runner:latest' locally
```

**Solution:** Pull the image first:
```bash
docker pull rzvdaniel/automaton-runner:latest
```

Or check you're using the correct image name (it's case-sensitive).

## Security Best Practices

### Protect Your API Key

**Never commit API keys to version control!**

- Use environment files (`.env`) and add them to `.gitignore`
- Use Docker secrets for production deployments
- Rotate API keys periodically

### Network Security

- Use HTTPS for production connections
- Use firewall rules to restrict outbound connections
- Run containers on isolated networks

### Keep Images Updated

Regularly update to the latest image for security patches:

```bash
docker pull rzvdaniel/automaton-runner:latest
docker compose up -d  # Recreates container with new image
```

## Production Deployment

For production environments, consider:

1. **Container Orchestration**: Use Kubernetes, Docker Swarm, or Azure Container Instances
2. **Monitoring**: Integrate with logging/monitoring systems (Datadog, New Relic, etc.)
3. **High Availability**: Run multiple runner instances across different hosts
4. **Auto-scaling**: Scale runners based on workflow queue depth

## Related Documentation

- [Publishing Runner to Docker Hub](./publishing-runner-to-docker-hub.md) — How to build and publish new versions
- [Docker Runner Development Guide](./docker-runner.md) — Full development guide
- [Azure Pipelines Configuration](../../azure-pipelines-runner-docker.yml) — Automated CI/CD
