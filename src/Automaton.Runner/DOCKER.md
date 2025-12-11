# Automaton Runner - Docker Deployment Guide

This guide covers deploying the Automaton Runner using Docker, including production deployment on Hetzner or other cloud providers.

## Features

- ✅ **Python 3.11** pre-installed with `requests` library
- ✅ **Service mode** - runs automatically as a background service
- ✅ **Environment-based configuration** - no manual setup required
- ✅ **Persistent volumes** - for databases, files, images, and configuration
- ✅ **Production-ready** - designed for multi-user deployments
- ✅ **Health checks** - automatic container health monitoring
- ✅ **Resource limits** - configurable CPU and memory limits
- ✅ **Non-root user** - runs with restricted permissions for security

## Quick Start

### Prerequisites

- Docker 20.10 or later
- Docker Compose 1.29 or later
- Automaton Studio API key (create in Studio UI: Settings → API Keys)

### Step 1: Get an API Key

1. Log in to Automaton Studio web interface
2. Go to **Settings → API Keys**
3. Click **Create API Key**
4. Fill in:
   - **Name**: Production Runner (or any descriptive name)
   - **Scope**: Choose "Runner"
   - **Expiration**: Optional
5. **Copy the API key** (shown only once)

### Step 2: Configure Environment

```bash
# Copy example configuration
cp .env.example .env

# Edit with your values
nano .env
```

Required values in `.env`:
```bash
AUTOMATON_API_KEY=ak_runner_your_actual_key_here
AUTOMATON_SERVER_URL=https://your-studio-server.com
AUTOMATON_RUNNER_NAME=docker-runner-01
```

### Step 3: Start the Runner

```bash
# Build and start in detached mode
docker-compose -f docker-compose.production.yml up -d

# View logs
docker-compose -f docker-compose.production.yml logs -f

# Check status
docker-compose -f docker-compose.production.yml ps
```

### Step 4: Verify

Check Automaton Studio UI → Runners page to see your runner connected!

## Production Deployment

### Hetzner Cloud Deployment

#### 1. Create Hetzner Server

```bash
# Create a server (via Hetzner Cloud Console or CLI)
# Recommended: CX21 or CX31 (2-4 vCPUs, 4-8GB RAM)
# OS: Ubuntu 22.04 LTS
```

#### 2. Install Docker

```bash
# SSH into your server
ssh root@your-server-ip

# Install Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sh get-docker.sh

# Install Docker Compose
apt-get update
apt-get install -y docker-compose-plugin

# Verify installation
docker --version
docker compose version
```

#### 3. Deploy Runner

```bash
# Create deployment directory
mkdir -p /opt/automaton-runner
cd /opt/automaton-runner

# Upload files (from your local machine)
scp docker-compose.production.yml root@your-server-ip:/opt/automaton-runner/
scp Dockerfile.production root@your-server-ip:/opt/automaton-runner/Dockerfile
scp docker-entrypoint.sh root@your-server-ip:/opt/automaton-runner/

# Or clone from repository
git clone https://github.com/yourusername/automaton.git
cd automaton/src/Automaton.Runner

# Create .env file
nano .env
```

Add your configuration:
```bash
AUTOMATON_API_KEY=ak_runner_your_key
AUTOMATON_SERVER_URL=https://your-studio.com
AUTOMATON_RUNNER_NAME=hetzner-runner-01
DATA_DIR=/opt/automaton-runner/data
LOGS_DIR=/opt/automaton-runner/logs
CONFIG_DIR=/opt/automaton-runner/config
```

```bash
# Create data directories
mkdir -p data logs config

# Start the runner
docker compose -f docker-compose.production.yml up -d

# Enable auto-restart on server reboot
docker update --restart=unless-stopped automaton-runner
```

#### 4. Configure Firewall (Optional)

```bash
# If you need to access the runner directly (usually not needed)
ufw allow 22/tcp   # SSH
ufw enable
```

#### 5. Set Up Monitoring

```bash
# View logs
docker logs -f automaton-runner

# Check resource usage
docker stats automaton-runner

# Set up log rotation (logs already configured in docker-compose)
```

### Multi-User Deployment

For deploying multiple runners (one per user):

#### Option 1: Multiple Containers

```bash
# Create separate docker-compose files for each user
cp docker-compose.production.yml docker-compose.user1.yml

# Edit for user 1
nano docker-compose.user1.yml
```

Modify:
```yaml
services:
  automaton-runner:
    container_name: automaton-runner-user1
    environment:
      - AUTOMATON_RUNNER_NAME=user1-runner
      - AUTOMATON_API_KEY=${USER1_API_KEY}
    volumes:
      - ./data/user1:/app/data
      - ./logs/user1:/app/logs
      - ./config/user1:/app/.config
```

Start multiple instances:
```bash
docker-compose -f docker-compose.user1.yml up -d
docker-compose -f docker-compose.user2.yml up -d
```

#### Option 2: Docker Swarm/Kubernetes

For larger deployments, consider orchestration platforms:
- **Docker Swarm**: Simple, native Docker clustering
- **Kubernetes**: Advanced orchestration with scaling and load balancing

## Volume Management

### Data Persistence

The runner uses three volumes:

1. **`/app/data`** - Runtime data (databases, files, images)
2. **`/app/logs`** - Application logs
3. **`/app/.config`** - Runner configuration and credentials

### Backup Strategy

```bash
# Backup all data
docker compose -f docker-compose.production.yml down
tar -czf backup-$(date +%Y%m%d).tar.gz data/ logs/ config/

# Restore from backup
tar -xzf backup-20240101.tar.gz
docker compose -f docker-compose.production.yml up -d
```

### Migration

```bash
# Export volumes
docker run --rm -v automaton-data:/data -v $(pwd):/backup alpine \
    tar czf /backup/data-backup.tar.gz -C /data .

# Import on new server
docker run --rm -v automaton-data:/data -v $(pwd):/backup alpine \
    tar xzf /backup/data-backup.tar.gz -C /data
```

## Configuration Options

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `AUTOMATON_API_KEY` | Yes | - | API key for authentication |
| `AUTOMATON_SERVER_URL` | Yes | - | Studio server URL |
| `AUTOMATON_RUNNER_NAME` | No | `docker-runner-<hostname>` | Runner display name |
| `DATA_DIR` | No | `./data` | Host path for data volume |
| `LOGS_DIR` | No | `./logs` | Host path for logs volume |
| `CONFIG_DIR` | No | `./config` | Host path for config volume |

### Resource Limits

Edit `docker-compose.production.yml`:

```yaml
deploy:
  resources:
    limits:
      cpus: '4.0'      # Max CPUs
      memory: 4G       # Max memory
    reservations:
      cpus: '1.0'      # Reserved CPUs
      memory: 1G       # Reserved memory
```

## Troubleshooting

### Container Won't Start

```bash
# Check logs
docker logs automaton-runner

# Common issues:
# 1. Missing environment variables
# 2. Invalid API key
# 3. Server URL unreachable
```

### Runner Not Connecting

```bash
# Verify environment
docker exec automaton-runner env | grep AUTOMATON

# Test server connectivity
docker exec automaton-runner curl -v https://your-studio.com

# Check Python
docker exec automaton-runner python3 --version
docker exec automaton-runner python3 -c "import requests; print(requests.__version__)"
```

### Check Runner Status

```bash
# From inside container
docker exec -it automaton-runner dotnet automaton-runner.dll status
```

### Reset Configuration

```bash
# Stop container
docker-compose -f docker-compose.production.yml down

# Clear config (keeps data and logs)
rm -rf config/*

# Restart
docker-compose -f docker-compose.production.yml up -d
```

## Advanced Topics

### Using Docker Secrets (Production)

For enhanced security, use Docker secrets instead of environment variables:

```bash
# Create secrets
echo "ak_runner_your_key" | docker secret create automaton_api_key -
echo "https://studio.com" | docker secret create automaton_server_url -

# Update docker-compose.yml
services:
  automaton-runner:
    secrets:
      - automaton_api_key
      - automaton_server_url
    environment:
      - AUTOMATON_API_KEY_FILE=/run/secrets/automaton_api_key
      - AUTOMATON_SERVER_URL_FILE=/run/secrets/automaton_server_url

secrets:
  automaton_api_key:
    external: true
  automaton_server_url:
    external: true
```

### Custom Build

```bash
# Build with specific .NET version or Python packages
docker build -f Dockerfile.production -t automaton-runner:custom .

# Add additional Python packages
RUN pip3 install requests numpy pandas boto3
```

### Networking

```bash
# Connect to existing network
docker network create automaton-net
docker compose -f docker-compose.production.yml --network automaton-net up -d

# Custom DNS
services:
  automaton-runner:
    dns:
      - 8.8.8.8
      - 8.8.4.4
```

## Security Best Practices

1. **Never commit `.env` files** - use `.env.example` template
2. **Use Docker secrets** for API keys in production
3. **Run as non-root user** (already configured)
4. **Limit container resources** to prevent resource exhaustion
5. **Keep images updated** - rebuild regularly for security patches
6. **Use private registry** for custom images
7. **Enable Docker content trust** for image verification
8. **Configure firewall rules** appropriately
9. **Monitor container logs** for suspicious activity
10. **Regular backups** of volumes

## Support

- **Documentation**: See main README.md
- **Issues**: GitHub Issues
- **Community**: Discord/Forums (if available)

## License

See main LICENSE file
