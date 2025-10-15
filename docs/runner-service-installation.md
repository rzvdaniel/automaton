# Runner Service Installation Architecture

## Overview

This document outlines the architectural and technical considerations for installing the Automaton Runner Console as a system service across Windows, Linux, and macOS platforms. The service installation option enhances the runner's resilience, optimizes resource utilization through system-managed thread pools, and enables automatic startup and recovery.

## Current Implementation Status

The service installation functionality is **already implemented** in the Automaton.Runner.Console project. This document serves as a comprehensive guide to the architecture, technical details, and operational considerations.

### Available Commands

```bash
# Install as system service
automaton-runner service install [--name <runner-name>] [--no-start]

# Check service status
automaton-runner service status

# Uninstall service
automaton-runner service uninstall
```

## Architectural Overview

### High-Level Design

```
┌─────────────────────────────────────────────────────────────────┐
│                    Automaton Runner Service                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌────────────┐    ┌──────────────┐    ┌─────────────────┐    │
│  │  Service   │───▶│  SignalR Hub │───▶│  Flow Execution │    │
│  │  Manager   │    │  Connection  │    │     Engine      │    │
│  └────────────┘    └──────────────┘    └─────────────────┘    │
│         │                  │                      │             │
│         ▼                  ▼                      ▼             │
│  ┌────────────┐    ┌──────────────┐    ┌─────────────────┐    │
│  │  Platform  │    │  Auto-Recon  │    │  Thread Pool    │    │
│  │  Specific  │    │  & Heartbeat │    │   Management    │    │
│  │  Service   │    └──────────────┘    └─────────────────┘    │
│  └────────────┘                                                 │
│         │                                                        │
└─────────┼────────────────────────────────────────────────────────┘
          │
          ▼
┌─────────────────────────┐
│  OS Service Management  │
├─────────────────────────┤
│  • Windows: NSSM        │
│  • Linux: systemd       │
│  • macOS: launchd       │
└─────────────────────────┘
```

### Key Components

1. **ServiceManagementService**: Core service that handles platform-specific installation, configuration, and lifecycle management
2. **RunnerHostService**: Main runner orchestration service that manages flow execution
3. **HubService**: SignalR connection manager with auto-reconnection
4. **FlowExecutionService**: Workflow execution engine
5. **Platform Service Wrappers**: OS-specific service management tools

## Platform-Specific Implementation

### Windows Implementation

#### Service Manager: NSSM (Non-Sucking Service Manager)

**Why NSSM?**
- Wraps any console application as a Windows service
- Handles service lifecycle automatically
- Provides log rotation and process monitoring
- Widely used and battle-tested
- No code changes required in the application

**Technical Details:**

```csharp
// Location: ServiceManagementService.InstallWindowsServiceAsync()

Key Features:
- Service Name: "AutomatonRunner"
- Startup Type: Automatic
- Log Rotation: 10MB per file with automatic rotation
- Environment Variables: Injected via NSSM configuration
- Working Directory: Installation directory
```

**Configuration:**
```powershell
# Service is installed with:
nssm install AutomatonRunner "C:\Path\To\automaton-runner.exe"
nssm set AutomatonRunner Description "Automaton workflow runner"
nssm set AutomatonRunner Start SERVICE_AUTO_START
nssm set AutomatonRunner AppStdout "logs\service-output.log"
nssm set AutomatonRunner AppStderr "logs\service-error.log"
nssm set AutomatonRunner AppEnvironmentExtra "AUTOMATON_API_KEY=..." "AppConfig__BaseUrl=..."
```

**Management Commands:**
```powershell
net start AutomatonRunner
net stop AutomatonRunner
sc query AutomatonRunner
```

**Requirements:**
- Administrator privileges
- NSSM executable (auto-downloaded by install script)
- .NET 8.0 Runtime

### Linux Implementation

#### Service Manager: systemd

**Why systemd?**
- Standard init system on most modern Linux distributions
- Robust dependency management
- Automatic restart on failure
- Journal logging integration
- Resource management and security features

**Technical Details:**

```csharp
// Location: ServiceManagementService.InstallLinuxServiceAsync()

Key Features:
- Service Unit: /etc/systemd/system/AutomatonRunner.service
- Type: simple (foreground process)
- Restart Policy: always with 10-second delay
- Logging: systemd journal
- Environment Variables: Defined in unit file
```

**Service Unit File:**
```ini
[Unit]
Description=Automaton workflow runner for cross-platform automation
After=network.target
Wants=network.target

[Service]
Type=simple
User=<current-user>
WorkingDirectory=/home/<user>
Environment=AppConfig__BaseUrl=<server-url>
Environment=AUTOMATON_API_KEY=<api-key>
ExecStart=/usr/local/bin/automaton-runner
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
```

**Management Commands:**
```bash
sudo systemctl start AutomatonRunner
sudo systemctl stop AutomatonRunner
sudo systemctl status AutomatonRunner
sudo systemctl enable AutomatonRunner  # Auto-start on boot
journalctl -u AutomatonRunner -f       # View logs
```

**Requirements:**
- sudo privileges
- systemd (most modern Linux distributions)
- .NET 8.0 Runtime

### macOS Implementation

#### Service Manager: launchd

**Why launchd?**
- Native macOS service management system
- Supports both daemons and user agents
- Automatic restart on crash
- Built-in logging
- Property list (plist) configuration

**Technical Details:**

```csharp
// Location: ServiceManagementService.InstallMacOSServiceAsync()

Key Features:
- Plist Location: /Library/LaunchDaemons/com.automaton.runner.plist
- Label: com.automaton.runner
- RunAtLoad: true (start on boot)
- KeepAlive: true (restart on exit)
- Logging: /var/log/automaton-runner.log
```

**Plist Configuration:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "...">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.automaton.runner</string>
    <key>ProgramArguments</key>
    <array>
        <string>/usr/local/bin/automaton-runner</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>/var/log/automaton-runner.log</string>
    <key>StandardErrorPath</key>
    <string>/var/log/automaton-runner-error.log</string>
    <key>EnvironmentVariables</key>
    <dict>
        <key>AppConfig__BaseUrl</key>
        <string>https://server-url</string>
        <key>AUTOMATON_API_KEY</key>
        <string>ak_...</string>
    </dict>
</dict>
</plist>
```

**Management Commands:**
```bash
launchctl start com.automaton.runner
launchctl stop com.automaton.runner
launchctl list | grep automaton
```

**Requirements:**
- sudo privileges (for system-wide daemon)
- .NET 8.0 Runtime
- macOS 10.14 or later

## Threading and Queue Optimization

### System Thread Pool Benefits

When running as a service, the runner leverages the operating system's thread pool management, providing several optimization benefits:

#### 1. Thread Pool Reuse

**Without Service (Console Mode):**
```
Process Start → Create Threads → Execute Flow → Dispose Threads → Process End
```

**With Service:**
```
Service Start → Thread Pool Initialization (once)
    ↓
Flow 1 → Acquire Thread → Execute → Release Thread
Flow 2 → Acquire Thread → Execute → Release Thread
Flow 3 → Acquire Thread → Execute → Release Thread
    ↓
Service continues running (threads remain available)
```

**Benefits:**
- Reduced thread creation/destruction overhead
- Lower memory allocation churn
- Faster flow execution startup time
- Better resource utilization

#### 2. Concurrent Flow Execution

The runner uses .NET's `Task.Run()` which integrates with the ThreadPool:

```csharp
// From RunnerHostService.cs
public async Task ExecuteFlowAsync(FlowExecution execution)
{
    await Task.Run(async () =>
    {
        // Flow execution logic
        // Automatically uses ThreadPool threads
    });
}
```

**Optimization Details:**
- ThreadPool automatically scales based on workload
- Optimal thread count determined by: `Environment.ProcessorCount`
- Queue management handled by CLR runtime
- Work stealing algorithm for load balancing

#### 3. SignalR Connection Management

**Connection Pool Benefits:**
```csharp
// From HubService.cs
private async Task MaintainConnectionAsync()
{
    // Persistent connection maintained by service
    // No reconnection overhead between flows
    // Background heartbeat in ThreadPool
}
```

**Impact:**
- Single persistent SignalR connection
- Background reconnection logic runs in ThreadPool
- Lower latency for receiving new flow executions
- Reduced network overhead

### Performance Characteristics

| Metric | Console Mode (Per-Execution) | Service Mode |
|--------|------------------------------|--------------|
| Process Startup | ~500-1000ms per flow | One-time at service start |
| Thread Creation | New threads per flow | Reused from ThreadPool |
| Connection Setup | Reconnect each time | Persistent connection |
| Memory Overhead | Full process per execution | Shared across executions |
| Concurrent Flows | Limited by process spawn | Limited by ThreadPool (efficient) |

### Thread Pool Configuration

The .NET ThreadPool is automatically configured, but can be tuned via environment variables:

```bash
# Linux/macOS
export DOTNET_ThreadPool_MinThreads=4
export DOTNET_ThreadPool_MaxThreads=100

# Windows
set DOTNET_ThreadPool_MinThreads=4
set DOTNET_ThreadPool_MaxThreads=100
```

**Recommendations:**
- **Development**: Use defaults (ProcessorCount for min threads)
- **Production (Light Load)**: MinThreads = 2-4, MaxThreads = 50-100
- **Production (Heavy Load)**: MinThreads = 8-16, MaxThreads = 200-500

## Resilience Features

### 1. Automatic Restart on Failure

All platform implementations include automatic restart:

**Windows (NSSM):**
- Built-in process monitoring
- Automatic restart on crash
- Configurable restart delay

**Linux (systemd):**
```ini
Restart=always
RestartSec=10
```

**macOS (launchd):**
```xml
<key>KeepAlive</key>
<true/>
```

### 2. Auto-Reconnection

```csharp
// From HubService.cs
private async Task MaintainConnectionAsync()
{
    while (!_cancellationToken.IsCancellationRequested)
    {
        try
        {
            await EnsureConnectedAsync();
            // Connection maintained
        }
        catch (Exception ex)
        {
            logger.Warning("Connection lost, retrying in 5 seconds...");
            await Task.Delay(5000);
            // Automatic retry
        }
    }
}
```

**Benefits:**
- Survives network interruptions
- Handles server restarts gracefully
- No manual intervention required

### 3. Graceful Shutdown

```csharp
// Service receives shutdown signal
OnStop() 
{
    // Cancel current operations
    _cancellationTokenSource.Cancel();
    
    // Wait for in-progress flows to complete (with timeout)
    await Task.WhenAny(
        Task.WhenAll(_activeFlows),
        Task.Delay(TimeSpan.FromSeconds(30))
    );
    
    // Clean disconnect
    await hubConnection.StopAsync();
}
```

### 4. Resource Cleanup

Services properly implement IDisposable pattern:
```csharp
public void Dispose()
{
    _cancellationTokenSource?.Cancel();
    _hubConnection?.DisposeAsync().GetAwaiter().GetResult();
    _httpClient?.Dispose();
}
```

## Security Considerations

### Privilege Requirements

| Platform | Installation | Runtime | Reason |
|----------|-------------|---------|--------|
| Windows | Administrator | SYSTEM or User | Service registration requires admin |
| Linux | sudo | Specified user | systemd service creation requires root |
| macOS | sudo | Specified user | LaunchDaemon requires root access |

### Best Practices

1. **Use Least Privilege:**
   ```ini
   # Linux: Run as specific user, not root
   [Service]
   User=automaton-runner
   Group=automaton-runner
   ```

2. **Secure API Keys:**
   - Store API keys in environment variables
   - Use file permissions to protect configuration
   - Rotate keys regularly

3. **File System Permissions:**
   ```bash
   # Linux example
   sudo chmod 600 /etc/systemd/system/AutomatonRunner.service
   sudo chown root:root /etc/systemd/system/AutomatonRunner.service
   ```

4. **Log Security:**
   - Ensure logs don't contain sensitive data
   - Implement log rotation (already configured)
   - Restrict log file access

### Security Hardening (Linux systemd)

Additional security options available:

```ini
[Service]
# Prevent access to /home, /root, and /run/user
ProtectHome=yes

# Make /usr, /boot, and /etc read-only
ProtectSystem=strict

# Restrict network access if not needed
RestrictAddressFamilies=AF_INET AF_INET6

# Private temp directories
PrivateTmp=yes

# Prevent privilege escalation
NoNewPrivileges=yes
```

## Pros and Cons Analysis

### Pros

#### ✅ Resilience
- **Automatic Restart**: Service recovers automatically from crashes
- **Boot Persistence**: Starts automatically on system boot
- **Connection Recovery**: Maintains connection through network interruptions
- **Process Monitoring**: OS monitors service health

#### ✅ Performance Optimization
- **Thread Pool Reuse**: Eliminates thread creation overhead
- **Persistent Connections**: No reconnection latency between flows
- **Memory Efficiency**: Single process for multiple executions
- **Resource Management**: OS-level resource optimization

#### ✅ Operational Benefits
- **Centralized Management**: Standard OS tools for control
- **Logging Integration**: Native OS logging (journalctl, Event Viewer)
- **Monitoring**: Service status visible in system monitoring
- **Automation Ready**: Perfect for production deployments

#### ✅ Scalability
- **Concurrent Execution**: Efficient handling of multiple flows
- **Load Distribution**: ThreadPool work stealing
- **Resource Limits**: OS-level resource constraints available

### Cons

#### ❌ Complexity
- **Setup Overhead**: Additional installation steps required
- **Platform Differences**: Different management tools per OS
- **Troubleshooting**: Service-specific debugging required
- **Dependencies**: Requires external tools (NSSM on Windows)

#### ❌ Resource Usage
- **Always Running**: Consumes memory even when idle (~50-100MB)
- **Background Process**: CPU cycles for connection maintenance
- **Long-lived Process**: Potential memory leaks more impactful

#### ❌ Development Impact
- **Testing Difficulty**: Harder to debug than console app
- **Deployment Complexity**: More steps in CI/CD pipeline
- **Update Process**: Requires service stop/start for updates

#### ❌ Privilege Requirements
- **Admin Access**: Installation requires elevated privileges
- **Security Risk**: Services run with system-level permissions
- **Configuration**: More complex permission management

### Comparison Matrix

| Aspect | Console Mode | Service Mode |
|--------|--------------|--------------|
| **Setup Complexity** | Simple ⭐⭐⭐⭐⭐ | Moderate ⭐⭐⭐ |
| **Resilience** | Low ⭐⭐ | High ⭐⭐⭐⭐⭐ |
| **Performance** | Moderate ⭐⭐⭐ | High ⭐⭐⭐⭐⭐ |
| **Resource Usage (Idle)** | None ⭐⭐⭐⭐⭐ | Low ⭐⭐⭐⭐ |
| **Resource Usage (Active)** | High ⭐⭐ | Efficient ⭐⭐⭐⭐ |
| **Development/Testing** | Easy ⭐⭐⭐⭐⭐ | Harder ⭐⭐ |
| **Production Ready** | Moderate ⭐⭐⭐ | Excellent ⭐⭐⭐⭐⭐ |
| **Monitoring** | Manual ⭐⭐ | Integrated ⭐⭐⭐⭐⭐ |
| **Security** | User-level ⭐⭐⭐⭐ | Configurable ⭐⭐⭐ |

## Deployment Strategies

### Recommended Approach: Optional Service Mode

**Philosophy**: Service mode is **optional** and **not default** to maintain simplicity.

```
┌─────────────────────────────────────────────────────┐
│              Deployment Decision Tree                │
└─────────────────────────────────────────────────────┘

    Use Case?
        │
        ├── Development/Testing
        │   └─> Console Mode (simple, easy to debug)
        │
        ├── Personal/Single User
        │   └─> Console Mode with auto-start (user-level)
        │
        ├── Production/Server
        │   └─> Service Mode (resilient, optimized)
        │
        └── CI/CD Pipeline
            └─> Console Mode (ephemeral, per-job)
```

### Deployment Scenarios

#### Scenario 1: Development Environment
```bash
# Simple console execution
automaton-runner

# Configuration stored in user profile
# Easy to stop/start/debug
# No admin privileges required
```

**Recommended**: Console mode

#### Scenario 2: Production Server
```bash
# Install as service with installation script
curl -sSL https://studio.automaton.monster/downloads/install.sh | \
  bash -s -- --create-service --api-key "ak_..." --runner-name "prod-01"

# Or manual service installation
automaton-runner service install --name "production-runner"
```

**Recommended**: Service mode

#### Scenario 3: Multi-Runner Setup
```bash
# Runner 1: High priority workflows
automaton-runner service install --name "priority-runner"

# Runner 2: Background jobs
automaton-runner service install --name "background-runner"

# Each runs as separate service instance
```

**Recommended**: Service mode with multiple instances

#### Scenario 4: Desktop User
```bash
# User-level autostart (no admin required)
# Platform-specific user-level startup:

# Windows: Add to Startup folder
# Linux: User systemd unit (~/.config/systemd/user/)
# macOS: User LaunchAgent (~/Library/LaunchAgents/)
```

**Recommended**: User-level console mode

### Migration Path

For existing deployments, migration is non-disruptive:

```bash
# Step 1: Stop existing console runner (if running)
# Ctrl+C or process kill

# Step 2: Install as service
automaton-runner service install

# Step 3: Configuration is preserved (same API key, server URL)
# Step 4: Service starts automatically

# Rollback if needed:
automaton-runner service uninstall
# Then start console mode again
```

## Installation Scripts Analysis

### PowerShell Script (install.ps1)

**Features:**
- Automatic NSSM download and installation
- Architecture detection (x64/arm64)
- Service creation with proper configuration
- Environment variable injection
- Administrator privilege check

**Key Functions:**
```powershell
function Create-WindowsService {
    # Downloads latest runner
    # Installs NSSM
    # Creates service with NSSM
    # Configures logging
    # Sets environment variables
    # Starts service
}
```

### Bash Script (install.sh)

**Features:**
- Platform detection (Linux/macOS)
- Automatic systemd/launchd configuration
- Download and extraction
- Service creation
- Binary installation to /usr/local/bin

**Key Functions:**
```bash
create_systemd_service() {
    # Generates systemd unit file
    # Copies to /etc/systemd/system/
    # Enables and starts service
}

create_launchd_service() {
    # Generates plist file
    # Copies to /Library/LaunchDaemons/
    # Loads and starts service
}
```

### Script Usage

**Windows:**
```powershell
# Basic installation with service
iwr -useb https://studio.automaton.monster/downloads/install.ps1 | iex

# Or with parameters
.\install.ps1 -ApiKey "ak_..." -CreateService -RunnerName "prod-runner"
```

**Linux/macOS:**
```bash
# Basic installation with service
curl -sSL https://studio.automaton.monster/downloads/install.sh | \
  bash -s -- --create-service --api-key "ak_..." --runner-name "prod-runner"
```

## Best Practices and Recommendations

### 1. When to Use Service Mode

**✅ Use Service Mode When:**
- Deploying to production servers
- Running 24/7 operations
- Handling high-volume workflow executions
- Requiring automatic restart on failure
- Multiple concurrent flow executions expected
- Centralized monitoring is important

**❌ Avoid Service Mode When:**
- Development and testing
- Learning and experimentation
- Short-lived CI/CD jobs
- Desktop/laptop environments (unless always-on)
- Frequent code updates and redeployments

### 2. Configuration Management

**Environment Variables Strategy:**
```bash
# Store in service configuration
# Windows: NSSM AppEnvironmentExtra
# Linux: systemd Environment=
# macOS: launchd EnvironmentVariables

# Required:
AUTOMATON_API_KEY=ak_runner_...
AppConfig__BaseUrl=https://server.com

# Optional:
DOTNET_ThreadPool_MinThreads=8
DOTNET_ThreadPool_MaxThreads=200
DOTNET_ENVIRONMENT=Production
```

### 3. Logging Strategy

**Service Mode Logging:**
```bash
# Windows: Use NSSM log rotation
logs/service-output.log
logs/service-error.log

# Linux: Use systemd journal
journalctl -u AutomatonRunner -f

# macOS: Use launchd logging
tail -f /var/log/automaton-runner.log
```

**Log Rotation Configuration:**
- Maximum size: 10MB per file
- Keep 5 rotated files
- Compress old logs

### 4. Monitoring and Health Checks

**Service Status Monitoring:**
```bash
# Check service health
automaton-runner service status

# Platform-specific
# Windows: sc query AutomatonRunner
# Linux: systemctl status AutomatonRunner
# macOS: launchctl list com.automaton.runner
```

**Integration with Monitoring Tools:**
- Nagios/Icinga: Check service status via system commands
- Prometheus: Export metrics from runner API
- Grafana: Visualize runner activity and performance

### 5. Update Strategy

**Service Updates:**
```bash
# 1. Stop service
automaton-runner service uninstall

# 2. Update binary
curl -O https://studio.automaton.monster/downloads/runner/latest/...

# 3. Reinstall service
automaton-runner service install

# Or use installation script for automated updates
```

**Zero-Downtime Updates:**
- Run multiple runner instances
- Update one at a time
- Use load balancing at server level

### 6. Security Hardening

**Checklist:**
- ✅ Run with minimal required privileges
- ✅ Store API keys in environment variables (not config files)
- ✅ Implement log scrubbing for sensitive data
- ✅ Use TLS for all server communications
- ✅ Regular API key rotation
- ✅ File system permissions on service config
- ✅ Network firewall rules (if applicable)
- ✅ Regular security updates

## Performance Tuning

### Thread Pool Tuning

**Default Behavior:**
```csharp
MinThreads = Environment.ProcessorCount
MaxThreads = Environment.ProcessorCount * 250
```

**Production Tuning:**
```bash
# For CPU-bound workflows
export DOTNET_ThreadPool_MinThreads=16
export DOTNET_ThreadPool_MaxThreads=500

# For I/O-bound workflows
export DOTNET_ThreadPool_MinThreads=4
export DOTNET_ThreadPool_MaxThreads=1000
```

### Memory Management

**Monitor Memory Usage:**
```bash
# Linux
ps aux | grep automaton-runner

# Windows
Get-Process automaton-runner | Select-Object WS, PM, CPU
```

**GC Configuration:**
```bash
# Server GC (better for multi-core)
export DOTNET_gcServer=1

# Concurrent GC
export DOTNET_gcConcurrent=1

# Memory limit (1GB example)
export DOTNET_GCHeapHardLimit=0x40000000
```

### Connection Optimization

**SignalR Settings:**
```csharp
// Already configured in HubService.cs
HubConnectionBuilder()
    .WithUrl(hubUrl)
    .WithAutomaticReconnect() // Exponential backoff
    .Build();
```

## Troubleshooting Guide

### Common Issues

#### Issue 1: Service Won't Start

**Windows:**
```powershell
# Check service status
sc query AutomatonRunner

# Check NSSM logs
type logs\service-error.log

# Common causes:
# - Missing .NET Runtime
# - Invalid API key
# - Network connectivity
```

**Linux:**
```bash
# Check service status
systemctl status AutomatonRunner

# View logs
journalctl -u AutomatonRunner -n 50

# Common causes:
# - Incorrect user permissions
# - Missing dependencies
# - Invalid systemd unit file
```

#### Issue 2: High Memory Usage

**Diagnosis:**
```bash
# Check memory usage
# Linux: ps aux | grep automaton
# Windows: Get-Process automaton-runner

# If memory grows continuously:
# - Check for memory leaks in custom steps
# - Review log file sizes
# - Consider GC tuning
```

**Solution:**
```bash
# Configure memory limits
# Linux systemd:
[Service]
MemoryMax=1G
MemoryHigh=800M

# Or use DOTNET_GCHeapHardLimit
```

#### Issue 3: Service Keeps Restarting

**Check Logs:**
```bash
# Identify crash cause in logs
# Look for unhandled exceptions
# Check API key validity
# Verify server connectivity
```

**Temporary Disable Auto-Restart:**
```bash
# Linux
sudo systemctl edit AutomatonRunner
# Add: Restart=no

# Windows NSSM
nssm set AutomatonRunner Start SERVICE_DEMAND_START
```

## Future Enhancements

### Potential Improvements

1. **Docker Support**
   - Container-based deployment
   - Kubernetes orchestration
   - Helm charts

2. **Multi-Instance Management**
   - Built-in load balancing
   - Runner pool management
   - Centralized configuration

3. **Advanced Monitoring**
   - Prometheus metrics endpoint
   - Health check API
   - Performance profiling

4. **Enhanced Security**
   - Certificate-based authentication
   - mTLS support
   - Encrypted configuration storage

5. **Deployment Automation**
   - Ansible playbooks
   - Terraform modules
   - Chef/Puppet recipes

## Conclusion

The service installation option for Automaton.Runner.Console provides significant benefits for production deployments:

- **Resilience**: Automatic restart and recovery
- **Performance**: Thread pool optimization and persistent connections
- **Operational Excellence**: Standard OS service management
- **Flexibility**: Optional, not mandatory

**Recommendation**: Use console mode for development and testing, service mode for production deployments.

The architecture is sound, the implementation is cross-platform, and the operational benefits justify the additional complexity for production use cases.

## References

### Code Locations
- `src/Automaton.Runner.Console/Services/ServiceManagementService.cs` - Service management implementation
- `src/Automaton.Runner.Console/Program.cs` - Command-line interface
- `src/Automaton.Studio.Web/wwwroot/downloads/install.ps1` - Windows installation script
- `src/Automaton.Studio.Web/wwwroot/downloads/install.sh` - Linux/macOS installation script

### External Documentation
- [NSSM Documentation](https://nssm.cc/)
- [systemd Service Documentation](https://www.freedesktop.org/software/systemd/man/systemd.service.html)
- [launchd Documentation](https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html)
- [.NET Thread Pool](https://learn.microsoft.com/en-us/dotnet/standard/threading/the-managed-thread-pool)
