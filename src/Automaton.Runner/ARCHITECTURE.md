# Automaton Runner Docker Architecture

This document describes the architecture of the Dockerized Automaton Runner.

## Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Docker Container                          │
│  ┌────────────────────────────────────────────────────────┐ │
│  │           Automaton Runner (.NET 9.0)                  │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │  Service Mode                                     │  │ │
│  │  │  - SignalR Connection to Studio                  │  │ │
│  │  │  - Workflow Execution Engine                     │  │ │
│  │  │  - Step Handlers                                 │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  │                                                          │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │  Python 3.11 Runtime                             │  │ │
│  │  │  - requests library                              │  │ │
│  │  │  - Custom script execution                       │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                               │
│  Persistent Volumes (mounted from host):                     │
│  ┌──────────┐  ┌──────────┐  ┌───────────┐                 │
│  │  /data   │  │  /logs   │  │  /config  │                 │
│  │ Files,   │  │  App     │  │  API Key, │                 │
│  │ DBs,     │  │  Logs    │  │  Runner   │                 │
│  │ Images   │  │          │  │  Settings │                 │
│  └────┬─────┘  └────┬─────┘  └─────┬─────┘                 │
└───────┼─────────────┼──────────────┼────────────────────────┘
        │             │              │
        ▼             ▼              ▼
   Host Storage    Host Storage   Host Storage
```

## Components

### 1. Docker Image Layers

**Base Image**: `mcr.microsoft.com/dotnet/runtime:9.0`
- Debian-based Linux
- .NET 9.0 runtime
- Security updates

**Python Layer**:
- Python 3.11 from Debian repositories
- pip3 package manager
- requests library (2.x)
- Symbolic links for python/python3

**Application Layer**:
- Compiled Automaton Runner
- Configuration files
- Entrypoint script

### 2. Runner Service

**Components**:
- `RunnerHostService` - Main service orchestrator
- `HubService` - SignalR connection management
- `FlowExecutionService` - Workflow execution
- `CustomStepExecuteService` - Python/PowerShell script execution
- `ApiKeyAuthenticationService` - Authentication handling

**Lifecycle**:
1. Container starts → entrypoint.sh
2. Verify Python installation
3. Configure authentication from env vars
4. Register runner with Studio
5. Start service mode (persistent connection)
6. Wait for workflow execution requests
7. Execute workflows using Python/PowerShell as needed

### 3. Persistent Storage

**Three volumes for data persistence**:

```
/app/data/
├── databases/           # SQLite or other local DBs
├── files/              # User-uploaded files
├── images/             # Generated or processed images
└── temp/               # Temporary workflow data

/app/logs/
├── automaton-runner-YYYY-MM-DD.log
└── older logs...

/app/.config/AutomatonRunner/
├── server.json         # Server URL configuration
├── runner.json         # Runner registration data
└── apikey.json         # Encrypted API key (if stored)
```

### 4. Network Communication

```
Docker Container
       │
       │ HTTPS (443)
       │ SignalR/WebSocket
       │
       ▼
┌─────────────────┐
│ Automaton Studio│
│     Server      │
└─────────────────┘
```

**Protocols**:
- HTTPS for API calls
- SignalR over WebSocket for real-time communication
- Automatic reconnection on network issues

## Environment Variables

### Required
- `AUTOMATON_API_KEY` - Authentication key
- `AUTOMATON_SERVER_URL` - Studio server URL

### Optional
- `AUTOMATON_RUNNER_NAME` - Custom runner name
- `DOTNET_ENVIRONMENT` - Environment (Production/Development)

## Security Model

### Container Security

1. **Non-root User**: Runs as `automaton` user (UID 1000)
2. **No Privileged Access**: Standard container isolation
3. **Read-only Filesystem**: Except mounted volumes
4. **Resource Limits**: CPU and memory caps

### Network Security

1. **Outbound Only**: No inbound ports exposed
2. **HTTPS Only**: All communication encrypted
3. **API Key Auth**: Secure token-based authentication

### Data Security

1. **Volume Isolation**: Per-user data separation
2. **Encrypted Transit**: All API calls over HTTPS
3. **Secure Storage**: API keys encrypted at rest

## Multi-User Architecture

For multi-user deployments:

```
┌───────────────────────────────────────────────────────┐
│                   Docker Host                          │
│                                                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐│
│  │  Container 1 │  │  Container 2 │  │  Container 3 ││
│  │  (User 1)    │  │  (User 2)    │  │  (User 3)    ││
│  │              │  │              │  │              ││
│  │  Runner +    │  │  Runner +    │  │  Runner +    ││
│  │  Python 3.11 │  │  Python 3.11 │  │  Python 3.11 ││
│  │              │  │              │  │              ││
│  │  Volumes:    │  │  Volumes:    │  │  Volumes:    ││
│  │  - data1/    │  │  - data2/    │  │  - data3/    ││
│  │  - logs1/    │  │  - logs2/    │  │  - logs3/    ││
│  │  - config1/  │  │  - config2/  │  │  - config3/  ││
│  └──────────────┘  └──────────────┘  └──────────────┘│
│         │                 │                 │          │
└─────────┼─────────────────┼─────────────────┼──────────┘
          │                 │                 │
          └─────────────────┴─────────────────┘
                           │
                    Shared Network
                           │
                           ▼
                   Automaton Studio
```

**Benefits**:
- Resource isolation per user
- Independent scaling
- Fault isolation
- Per-user monitoring

## Resource Requirements

### Minimum (per container)
- **CPU**: 0.5 cores
- **Memory**: 512 MB
- **Storage**: 5 GB

### Recommended (per container)
- **CPU**: 1-2 cores
- **Memory**: 1-2 GB
- **Storage**: 20-50 GB

### High Load (per container)
- **CPU**: 2-4 cores
- **Memory**: 2-4 GB
- **Storage**: 50-100 GB

## Python Integration

### Python Environment

```
/usr/bin/python3.11          # Python interpreter
/usr/bin/python3             # Symlink to python3.11
/usr/bin/python              # Symlink to python3.11
/usr/bin/pip3                # Python package manager
/usr/lib/python3.11/         # Standard library
/usr/local/lib/python3.11/   # Site packages
```

### Pre-installed Packages
- **requests** (2.x) - HTTP library for API calls

### Custom Package Installation

Users can install additional packages in workflows:
```python
import subprocess
subprocess.run(["pip3", "install", "pandas", "numpy"])
```

Or via custom step scripts.

## Monitoring and Health Checks

### Health Check
- **Type**: Process check
- **Interval**: 30 seconds
- **Timeout**: 10 seconds
- **Start Period**: 40 seconds
- **Retries**: 3

**Check**: `pgrep -f "automaton-runner"`

### Logs
- **Location**: `/app/logs/`
- **Format**: Structured JSON
- **Rotation**: Daily
- **Retention**: 7 days (configurable)

### Metrics
Monitor via `docker stats`:
- CPU usage
- Memory usage
- Network I/O
- Block I/O

## Scaling Strategies

### Vertical Scaling
- Increase CPU/memory limits
- Add more storage
- Upgrade host machine

### Horizontal Scaling
- Add more containers
- Use Docker Swarm/Kubernetes
- Load balance across runners

### Geographic Scaling
- Deploy runners closer to users
- Multiple regions on Hetzner/AWS/Azure
- Reduce latency

## Upgrade Path

1. **Pull New Image**:
   ```bash
   docker pull automaton-runner:latest
   ```

2. **Stop Old Container**:
   ```bash
   docker-compose down
   ```

3. **Backup Data**:
   ```bash
   tar -czf backup.tar.gz data/ logs/ config/
   ```

4. **Start New Container**:
   ```bash
   docker-compose up -d
   ```

5. **Verify**:
   ```bash
   docker logs -f automaton-runner
   ```

## Troubleshooting

### Common Issues

1. **Container Won't Start**
   - Check logs: `docker logs automaton-runner`
   - Verify environment variables
   - Check network connectivity

2. **Python Scripts Fail**
   - Verify Python: `docker exec automaton-runner python3 --version`
   - Check packages: `docker exec automaton-runner pip3 list`
   - Review script logs

3. **Connection Issues**
   - Test connectivity: `docker exec automaton-runner curl -v $SERVER_URL`
   - Check firewall rules
   - Verify API key

4. **Performance Issues**
   - Monitor: `docker stats automaton-runner`
   - Increase resources
   - Check host load

## Best Practices

1. **Always use persistent volumes** - Don't lose data on restart
2. **Monitor resource usage** - Adjust limits as needed
3. **Regular backups** - Automate volume backups
4. **Update regularly** - Pull latest images for security
5. **Use secrets management** - Don't hardcode API keys
6. **Enable logging** - Centralize logs for analysis
7. **Test before production** - Validate in test environment
8. **Document custom changes** - Track configuration changes
9. **Monitor health checks** - Set up alerts
10. **Plan for disaster recovery** - Have rollback strategy

## Support

- **Documentation**: See README.md, DOCKER.md, TESTING.md
- **Issues**: GitHub Issues
- **Examples**: See `examples/` directory
