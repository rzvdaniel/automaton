<div class="doc-hero hero-runners">
  <div class="doc-hero-content">
    <div class="doc-hero-icon">🏃</div>
    <h1>Managing Runners</h1>
    <p class="hero-subtitle">
      Deploy and manage execution agents that bring your workflows to life. Run automations anywhere with flexible, scalable runners.
    </p>
    <div class="doc-hero-stats">
      <div class="doc-hero-stat">
        <span class="doc-hero-stat-number">🖥️</span>
        <span class="doc-hero-stat-label">Cross-Platform</span>
      </div>
      <div class="doc-hero-stat">
        <span class="doc-hero-stat-number">☁️</span>
        <span class="doc-hero-stat-label">Remote Execution</span>
      </div>
      <div class="doc-hero-stat">
        <span class="doc-hero-stat-number">📊</span>
        <span class="doc-hero-stat-label">Real-Time Status</span>
      </div>
    </div>
  </div>
</div>

# Managing Runners

Runners are the execution agents that run your automation flows. They connect to the Automaton platform and execute your workflows on different machines.

## What are Runners?

A **Runner** is a lightweight agent that connects to your Automaton platform and executes flows on demand. Think of runners as the "workers" that carry out your automation tasks.

### Types of Runners

#### Console Runner (Cross-platform)
- **Platform**: Windows, macOS, Linux
- **Interface**: Command-line interface
- **Use Case**: Servers, automated environments
- **Resource Usage**: Minimal memory footprint

#### GUI Runner (Windows)
- **Platform**: Windows only  
- **Interface**: Graphical user interface
- **Use Case**: Desktop automation, interactive scenarios
- **Resource Usage**: Standard desktop application

## Setting Up Your First Runner

> 💡 **Quick Start**: For complete setup instructions including installation, configuration, and troubleshooting, see the [Runner Setup Guide](runner-setup.md).

### Deployment Options

| Method | Recommended For | Time to Deploy |
|--------|-----------------|----------------|
| **🐳 Docker** (Recommended) | Production, Cloud, CI/CD | ~1 minute |
| **📦 Native Executable** | Local development, Bare metal | ~5 minutes |

### Docker Deployment (Recommended)

The fastest way to get a runner up and running:

```bash
docker run -d \
  --name automaton-runner \
  -e AUTOMATON_API_KEY=ak_your_api_key_here \
  rzvdaniel/automaton-runner:latest
```

The Docker image includes:
- .NET 9.0 Runtime
- Python 3.13 with pre-installed packages (pandas, numpy, requests, boto3, etc.)

### Quick Setup Overview (Native Executable)

1. **Download the Runner**: 
   - Go to **Downloads Hub** in Automaton Studio
   - Choose your platform (Windows, macOS, Linux)
   - Use the quick installation script (recommended) or manual download

2. **Get an API Key**: 
   - Navigate to **Settings → API Keys** in Automaton Studio
   - Click **Create API Key**
   - Choose **"Runner"** scope
   - Copy the generated key (starts with `ak_`)

3. **Run Interactive Setup**:
   ```bash
   automaton-runner
   ```
   The runner will guide you through authentication and registration automatically.

4. **Verify Connection**:
   - Check the **Runners** page in Automaton Studio
   - Your runner should appear with a 🟢 Online status

### Detailed Setup Instructions

For comprehensive documentation including:
- Docker deployment (Docker CLI, Docker Desktop, Docker Compose)
- Detailed installation for all platforms
- CI/CD integration examples
- System service configuration
- Troubleshooting common issues
- Security best practices

**👉 See the complete [Runner Setup Guide](runner-setup.md)**

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

## Using Runners with Flows

### Assigning Runners to Flows
When creating or editing a flow:
1. **Flow Settings**: Click **Settings** in the flow designer
2. **Select Runner**: Choose from available runners
3. **Save Configuration**: Save your flow with the runner assignment

### Runner Selection
- **Automatic**: Let the platform choose the best available runner
- **Specific Runner**: Assign to a particular runner by name
- **Runner Group**: Assign to any runner in a specific group

### Monitoring Flow Execution
- **Real-time Status**: See which flows are running on each runner
- **Execution Queue**: View pending flows waiting for runner availability
- **Performance Metrics**: Track how different runners perform

## Common Tasks

### Adding Multiple Runners
To increase capacity or add redundancy:
1. Download additional runner instances
2. Install on different machines or locations
3. Connect each with the same or different API keys
4. Assign flows to specific runners or let the platform balance the load

### Troubleshooting Runners
If a runner appears offline or has issues:
1. **Check Connection**: Verify network connectivity to the platform
2. **Verify API Key**: Ensure the API key is valid and has correct permissions
3. **Review Logs**: Check runner logs for error messages
4. **Restart Runner**: Try stopping and starting the runner application

### Runner Performance
To optimize runner performance:
- **Monitor Resource Usage**: Keep an eye on CPU and memory consumption
- **Adjust Concurrent Jobs**: Increase or decrease based on system capacity
- **Strategic Placement**: Place runners close to resources they need to access

## Best Practices

### Runner Deployment
- **Environment Separation**: Use different runners for development and production
- **Geographic Distribution**: Place runners close to the resources they'll access
- **Redundancy**: Deploy multiple runners for high availability
- **Capacity Planning**: Monitor usage and add runners as needed

### Security
- **API Key Management**: Regularly rotate API keys and use appropriate permissions
- **Network Security**: Ensure secure connections between runners and the platform
- **Access Control**: Use minimal permissions for each runner's intended purpose

### Maintenance
- **Regular Updates**: Keep runners updated with the latest versions
- **Health Monitoring**: Monitor runner status and performance regularly
- **Log Management**: Review logs periodically to identify issues early

## Next Steps

- Learn about [Working with Flows](flows.md) to create automations for your runners
- Check the [Platform Pages](platform-pages.md) guide for managing runners through the interface
