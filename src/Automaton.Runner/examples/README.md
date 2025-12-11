# Automaton Runner - Deployment Examples

This directory contains example configurations and deployment guides for various scenarios.

## Available Examples

### 1. Hetzner Cloud Deployment
**File**: `hetzner-deployment.md`

Complete guide for deploying Automaton Runner on Hetzner Cloud servers, including:
- Server provisioning
- Docker installation
- Single and multi-user setups
- Cost optimization tips

**Best for**: Production deployments in Europe, cost-effective hosting

---

### 2. Multi-User Docker Compose
**Files**: 
- `docker-compose.multi-user.yml`
- `.env.multi-user.example`

Run multiple Automaton Runners on a single host, each with:
- Separate containers
- Isolated data volumes
- Individual API keys
- Resource limits per user

**Best for**: SaaS providers, multi-tenant environments, development teams

**Quick Start**:
```bash
cp .env.multi-user.example .env
nano .env  # Add API keys
docker-compose -f docker-compose.multi-user.yml up -d
```

---

## Other Deployment Scenarios

### AWS EC2
Similar to Hetzner deployment, but use Amazon Linux 2 or Ubuntu AMI:
```bash
# On EC2 instance
sudo yum update -y  # Amazon Linux
curl -fsSL https://get.docker.com | sh
sudo systemctl start docker
sudo usermod -aG docker ec2-user
# Then follow standard deployment steps
```

### Azure Container Instances
Deploy as a container instance:
```bash
az container create \
  --resource-group myResourceGroup \
  --name automaton-runner \
  --image automaton-runner:latest \
  --environment-variables \
    AUTOMATON_API_KEY=ak_xxx \
    AUTOMATON_SERVER_URL=https://studio.com \
  --restart-policy Always
```

### Google Cloud Run
For serverless deployment (requires modifications for Cloud Run's constraints)

### Kubernetes
For orchestrated multi-runner deployments:
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: automaton-runner
spec:
  replicas: 3
  selector:
    matchLabels:
      app: automaton-runner
  template:
    metadata:
      labels:
        app: automaton-runner
    spec:
      containers:
      - name: runner
        image: automaton-runner:latest
        env:
        - name: AUTOMATON_API_KEY
          valueFrom:
            secretKeyRef:
              name: automaton-secrets
              key: api-key
        - name: AUTOMATON_SERVER_URL
          value: "https://studio.company.com"
```

### Docker Swarm
For simple clustering:
```bash
docker swarm init
docker stack deploy -c docker-compose.production.yml automaton
```

## Contributing

Have a deployment example you'd like to share? Please:
1. Create a markdown guide or docker-compose file
2. Test it thoroughly
3. Submit a pull request

## Support

- **Main Documentation**: [../DOCKER.md](../DOCKER.md)
- **Quick Start**: [../QUICKSTART-DOCKER.md](../QUICKSTART-DOCKER.md)
- **Runner README**: [../README.md](../README.md)
