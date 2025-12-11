# Hetzner Cloud Deployment Guide

This guide provides step-by-step instructions for deploying Automaton Runner on Hetzner Cloud with Docker.

## Overview

Deploy Automaton Runner in a production environment on Hetzner Cloud with:
- Docker containerization
- Automatic restarts
- Persistent storage
- Secure configuration
- Resource optimization

## Prerequisites

- Hetzner Cloud account
- Automaton Studio API key (Runner scope)
- Basic SSH and Docker knowledge

## Cost Estimate

| Server Type | vCPUs | RAM | Storage | Price/month | Use Case |
|-------------|-------|-----|---------|-------------|----------|
| CX11 | 1 | 2 GB | 20 GB | €4.15 | Testing |
| CX21 | 2 | 4 GB | 40 GB | €5.83 | Light workloads |
| CX31 | 2 | 8 GB | 80 GB | €10.90 | Production (1-2 users) |
| CX41 | 4 | 16 GB | 160 GB | €19.90 | Production (3-5 users) |

## Quick Deployment

```bash
# 1. Create Hetzner server (Ubuntu 22.04, CX21 or higher)
# 2. SSH into server
ssh root@YOUR_SERVER_IP

# 3. Install Docker
curl -fsSL https://get.docker.com | sh
apt-get install -y docker-compose-plugin

# 4. Deploy runner
mkdir -p /opt/automaton-runner && cd /opt/automaton-runner
curl -O https://raw.githubusercontent.com/yourusername/automaton/main/src/Automaton.Runner/docker-compose.production.yml
curl -O https://raw.githubusercontent.com/yourusername/automaton/main/src/Automaton.Runner/.env.example
cp .env.example .env
nano .env  # Add your AUTOMATON_API_KEY and AUTOMATON_SERVER_URL

# 5. Start
mkdir -p data logs config
docker compose -f docker-compose.production.yml up -d
```

## Detailed Setup Guide

See [DOCKER.md](../DOCKER.md) for comprehensive deployment instructions including:
- Server setup and security
- Multi-user deployment
- Monitoring and maintenance
- Backup strategies
- Troubleshooting

## Multi-User Setup Example

For deploying one runner per user on a single Hetzner server:

```bash
# Create directory structure
cd /opt
mkdir -p automaton-runners/{user1,user2,user3}

# For each user
cd automaton-runners/user1
# Download files and create .env with user-specific API key
# Start: docker compose -f docker-compose.user1.yml up -d
```

Each user gets:
- Separate container
- Separate volumes (data/logs/config)
- Own API key
- Own runner name

## Support

- **Full Docker Guide**: [DOCKER.md](../DOCKER.md)
- **Quick Start**: [QUICKSTART-DOCKER.md](../QUICKSTART-DOCKER.md)
