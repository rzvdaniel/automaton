# Quick Start: Automaton Runner with Docker

Get your Automaton Runner up and running in Docker in 5 minutes!

## For Testers: Automated Local Setup 🧪

If you want to test the runner **locally** with Automaton Studio, use our automated setup script:

```bash
cd src/Automaton.Runner
./setup-local-test.sh
```

See [LOCAL-TESTING.md](LOCAL-TESTING.md) for complete local testing guide with troubleshooting.

---

## Production Deployment

## Prerequisites

- Docker installed ([Get Docker](https://docs.docker.com/get-docker/))
- Automaton Studio API key

## Step-by-Step Guide

### 1. Get Your API Key

1. Open Automaton Studio in your browser
2. Navigate to **Settings → API Keys**
3. Click **Create API Key**
4. Set:
   - Name: `My Docker Runner`
   - Scope: `Runner`
5. **Copy the API key** (starts with `ak_runner_...`)

### 2. Clone or Download Files

```bash
# Option A: Clone the repository
git clone https://github.com/rzvdaniel/automaton.git
cd automaton/src/Automaton.Runner

# Option B: Download just the Docker files
# Replace with your repository URL if you forked the project
GITHUB_URL="https://raw.githubusercontent.com/rzvdaniel/automaton/main"
curl -O ${GITHUB_URL}/src/Automaton.Runner/Dockerfile.production
curl -O ${GITHUB_URL}/src/Automaton.Runner/docker-compose.production.yml
curl -O ${GITHUB_URL}/src/Automaton.Runner/docker-entrypoint.sh
curl -O ${GITHUB_URL}/src/Automaton.Runner/.env.example
```

### 3. Configure Environment

```bash
# Copy example configuration
cp .env.example .env

# Edit with your favorite editor
nano .env
# or
vim .env
```

Replace these values in `.env`:
```bash
AUTOMATON_API_KEY=ak_runner_YOUR_KEY_HERE
AUTOMATON_SERVER_URL=https://your-studio-server.com
AUTOMATON_RUNNER_NAME=my-docker-runner
```

### 4. Create Data Directories

```bash
# Create directories for persistent storage
mkdir -p data logs config
```

### 5. Start the Runner

```bash
# Build and start in the background
docker-compose -f docker-compose.production.yml up -d
```

### 6. Verify It's Running

```bash
# Check container status
docker-compose -f docker-compose.production.yml ps

# View logs
docker-compose -f docker-compose.production.yml logs -f
```

You should see output like:
```
✓ Python and pip are installed
✓ Python 'requests' library is installed
📋 Configuration:
  • Server URL: https://your-studio-server.com
  • Runner Name: my-docker-runner
🚀 Starting Automaton Runner in service mode...
```

### 7. Check in Studio

Open Automaton Studio and go to **Runners** page. You should see your new runner listed and connected!

## Common Commands

```bash
# Start runner
docker-compose -f docker-compose.production.yml up -d

# Stop runner
docker-compose -f docker-compose.production.yml down

# View logs (follow mode)
docker-compose -f docker-compose.production.yml logs -f

# View logs (last 100 lines)
docker-compose -f docker-compose.production.yml logs --tail=100

# Restart runner
docker-compose -f docker-compose.production.yml restart

# Check status
docker-compose -f docker-compose.production.yml ps

# Execute command in running container
docker-compose -f docker-compose.production.yml exec automaton-runner bash
```

## Troubleshooting

### Runner Won't Start

**Check logs:**
```bash
docker-compose -f docker-compose.production.yml logs
```

**Common issues:**
1. **Missing API key** - Make sure `AUTOMATON_API_KEY` is set in `.env`
2. **Wrong server URL** - Verify `AUTOMATON_SERVER_URL` is correct
3. **Network issues** - Test: `docker run --rm curlimages/curl curl https://your-studio-server.com`

### Python Not Working

**Test Python inside container:**
```bash
docker-compose -f docker-compose.production.yml exec automaton-runner python3 --version
docker-compose -f docker-compose.production.yml exec automaton-runner python3 -c "import requests; print(requests.__version__)"
```

### Reset Configuration

```bash
# Stop container
docker-compose -f docker-compose.production.yml down

# Clear config (keeps data and logs)
rm -rf config/*

# Start again
docker-compose -f docker-compose.production.yml up -d
```

## Next Steps

- **Read the full guide**: [DOCKER.md](DOCKER.md)
- **Deploy to production**: See Hetzner deployment section
- **Scale up**: Deploy multiple runners for multiple users
- **Monitor**: Set up log aggregation and monitoring

## Need Help?

- Check [DOCKER.md](DOCKER.md) for detailed documentation
- See [README.md](README.md) for runner commands and configuration
- Open an issue on GitHub for bugs or questions
