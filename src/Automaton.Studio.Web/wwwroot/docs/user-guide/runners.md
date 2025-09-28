# Managing Runners

Runners are the execution agents that run your automation flows. They can be deployed anywhere - on your local machine, remote servers, or in containers.

## What are Runners?

A **Runner** is a lightweight agent that connects to your Automaton Studio and executes flows on demand. Think of runners as the "workers" that carry out your automation tasks.

### Types of Runners

#### Console Runner (Cross-platform)
- **Platform**: Windows, macOS, Linux
- **Interface**: Command-line interface
- **Use Case**: Servers, containers, automated deployments
- **Resource Usage**: Minimal memory footprint

#### GUI Runner (Windows)
- **Platform**: Windows only  
- **Interface**: Graphical user interface
- **Use Case**: Desktop automation, interactive scenarios
- **Resource Usage**: Standard desktop application

## Setting Up Runners

### Prerequisites
- .NET 8.0 Runtime (automatically included in self-contained builds)
- Network connectivity to your Automaton Studio
- API Key from the Studio

### Console Runner Setup

#### Option 1: Download Pre-built Runner
1. Go to **Downloads Hub** in Automaton Studio
2. Download the appropriate version for your platform
3. Extract the files to your desired location

#### Option 2: Build from Source
```bash
cd src/Automaton.Runner.Console
dotnet build -c Release
dotnet publish -c Release --self-contained true
```

### Getting an API Key

1. **Navigate to API Keys**: Go to **Settings > API Keys** in Automaton Studio
2. **Create New Key**: Click **Create New Key**
3. **Set Permissions**: Choose appropriate access level:
   - `ak_runner_xxx`: Full runner access
   - `ak_execute_xxx`: Execute flows only
   - `ak_readonly_xxx`: Read-only access
4. **Copy Key**: Save the generated key (you won't see it again)

### Starting a Runner

#### Interactive Setup (Recommended for First Time)
```bash
# Navigate to runner directory
cd /path/to/automaton-runner

# Start with interactive setup
./automaton-runner
```

The runner will guide you through:
- API key configuration
- Runner name and description
- Connection testing
- Registration with the Studio

#### Automated Setup
```bash
# Set API key as environment variable
export AUTOMATON_API_KEY="ak_runner_your_key_here"

# Start runner with automatic configuration
./automaton-runner
```

#### Configuration File
Create `appsettings.json` in the runner directory:
```json
{
  "ApiKey": "ak_runner_your_key_here",
  "StudioUrl": "https://your-studio-url:7091",
  "RunnerName": "Production Server 1",
  "RunnerDescription": "Main production server runner",
  "MaxConcurrentJobs": 5,
  "HeartbeatInterval": 30
}
```

## Runner Management

### Viewing Runners
1. **Navigate to Runners**: Click **Runners** in the left sidebar
2. **Runner List**: See all connected runners with status
3. **Runner Details**: Click on a runner to see:
   - Connection status (Online/Offline)
   - Current jobs and queue
   - System information
   - Performance metrics

### Runner Status Indicators
- **🟢 Online**: Connected and ready to execute flows
- **🟡 Busy**: Currently executing flows
- **🔴 Offline**: Disconnected or unreachable
- **⚠️ Error**: Connection issues or errors

### Managing Runner Capacity
- **Max Concurrent Jobs**: Limit how many flows run simultaneously
- **Resource Monitoring**: Track CPU, memory, and disk usage
- **Queue Management**: See pending flows waiting for execution

## Deploying Runners

### Local Development
Perfect for testing and development:
```bash
# Simple local runner
./automaton-runner
```

### Production Server
For reliable production deployments:

#### As a Service (Linux)
Create `/etc/systemd/system/automaton-runner.service`:
```ini
[Unit]
Description=Automaton Runner Service
After=network.target

[Service]
Type=simple
User=automaton
WorkingDirectory=/opt/automaton-runner
ExecStart=/opt/automaton-runner/automaton-runner
Restart=always
RestartSec=10
Environment=AUTOMATON_API_KEY=ak_runner_your_key_here

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl enable automaton-runner
sudo systemctl start automaton-runner
```

#### As a Windows Service
Use tools like NSSM (Non-Sucking Service Manager):
```cmd
nssm install "Automaton Runner" "C:\automaton-runner\automaton-runner.exe"
nssm set "Automaton Runner" AppDirectory "C:\automaton-runner"
nssm set "Automaton Runner" AppEnvironmentExtra "AUTOMATON_API_KEY=ak_runner_your_key_here"
nssm start "Automaton Runner"
```

### Container Deployment
Perfect for cloud and orchestrated environments:

#### Docker
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
COPY automaton-runner/ /app/
WORKDIR /app
ENV AUTOMATON_API_KEY=ak_runner_your_key_here
CMD ["./automaton-runner"]
```

Build and run:
```bash
docker build -t automaton-runner .
docker run -d --name runner1 automaton-runner
```

#### Docker Compose
```yaml
version: '3.8'
services:
  automaton-runner:
    build: .
    environment:
      - AUTOMATON_API_KEY=ak_runner_your_key_here
      - RUNNER_NAME=Docker Runner 1
    restart: unless-stopped
    deploy:
      replicas: 3
```

#### Kubernetes
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: automaton-runners
spec:
  replicas: 5
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
```

## Advanced Configuration

### Performance Tuning
```json
{
  "Performance": {
    "MaxConcurrentJobs": 10,
    "JobTimeoutMinutes": 60,
    "HeartbeatIntervalSeconds": 30,
    "MaxMemoryUsageMB": 2048
  }
}
```

### Logging Configuration
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Automaton": "Debug"
    },
    "File": {
      "Path": "logs/runner.log",
      "RollingInterval": "Day",
      "RetainedFileCountLimit": 7
    }
  }
}
```

### Network Settings
```json
{
  "Network": {
    "ConnectionTimeoutSeconds": 30,
    "RetryAttempts": 3,
    "RetryDelaySeconds": 5,
    "ProxyUrl": "http://proxy.company.com:8080"
  }
}
```

## Monitoring and Troubleshooting

### Health Checks
- **Connection Status**: Verify runner can reach Studio
- **Resource Usage**: Monitor CPU, memory, disk
- **Job Queue**: Check for stuck or failed jobs
- **Log Analysis**: Review runner logs for errors

### Common Issues

#### Runner Won't Connect
- **Check API Key**: Ensure it's valid and has runner permissions
- **Verify URL**: Studio URL must be accessible from runner
- **Firewall**: Ensure ports 7091 (HTTPS) and 7090 (HTTP) are open
- **Certificate**: Self-signed certificates may need additional configuration

#### Poor Performance
- **Resource Limits**: Increase MaxConcurrentJobs if resources allow
- **Network Latency**: Consider runner placement closer to Studio
- **Disk I/O**: Ensure adequate disk space and performance

#### Frequent Disconnections
- **Network Stability**: Check for intermittent connectivity issues
- **Heartbeat Settings**: Adjust HeartbeatInterval for network conditions
- **Resource Exhaustion**: Monitor memory and CPU usage

### Log Analysis
```bash
# View recent logs
tail -f logs/runner.log

# Search for errors
grep "ERROR" logs/runner.log

# Monitor connection issues
grep "connection" logs/runner.log -i
```

## Security Considerations

### API Key Management
- **Secure Storage**: Never commit API keys to source control
- **Environment Variables**: Use secure environment variable management
- **Key Rotation**: Regularly rotate API keys
- **Minimal Permissions**: Use least-privilege access levels

### Network Security
- **HTTPS Only**: Always use HTTPS for Studio communication
- **Firewall Rules**: Restrict network access to necessary ports only
- **VPN/Private Networks**: Use secure networks for sensitive environments

### Runner Isolation
- **Separate Users**: Run runners under dedicated service accounts
- **Resource Limits**: Use container or system limits to prevent resource exhaustion
- **Sandboxing**: Consider container deployment for additional isolation

## Best Practices

### Deployment Strategy
- **Environment Separation**: Use different runners for dev/test/prod
- **Geographic Distribution**: Place runners close to resources they'll access
- **Redundancy**: Deploy multiple runners for high availability
- **Capacity Planning**: Monitor usage and scale accordingly

### Maintenance
- **Regular Updates**: Keep runners updated with latest versions
- **Health Monitoring**: Implement automated health checks
- **Log Rotation**: Prevent log files from consuming excessive disk space
- **Backup Configuration**: Store runner configuration in version control

### Performance Optimization
- **Right-sizing**: Match runner specifications to workload requirements
- **Load Balancing**: Distribute flows across multiple runners
- **Caching**: Use local caches where appropriate
- **Batch Processing**: Group similar operations for efficiency

## Next Steps

- Learn about [Flow Scheduling](flows.md#scheduling-flows) to automate execution
- Explore [Custom Steps](custom-steps.md) that runners can execute
- Check [Deployment Guide](../deployment/runners.md) for production setups
- Review [API Reference](../api/runners.md) for programmatic management