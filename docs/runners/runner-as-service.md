# Running Automaton Runner as a System Service

This guide shows you how to configure Automaton Runner to run as a system service on Windows, Linux, and macOS. Running as a service provides automatic startup, resilience, and better resource management for production deployments.

## The `service` Command

When running as a system service, use the **`service`** command:

```bash
automaton-runner service
```

This runs the runner in **non-interactive mode**, which:
- ✅ Reads configuration from environment variables only (no prompts)
- ✅ Auto-registers with the server if not already registered
- ✅ Uses default name `Service-{MachineName}` for auto-registration
- ✅ Runs indefinitely without console interaction
- ✅ Logs errors and exits with proper exit codes for service managers

**Required Environment Variables:**
| Variable | Description |
|----------|-------------|
| `AUTOMATON_SERVER_URL` | Server URL (e.g., `https://studio.automaton.monster`) |
| `AUTOMATON_API_KEY` | Your API key from Automaton Studio |

**Alternative for Server URL:** You can also use `AppConfig__BaseUrl` (standard .NET configuration binding).

## Understanding Service Managers vs. the `service` Command

The `service` command does **one thing**: run the runner in headless mode (no prompts, read config from environment variables, run forever). The **service manager** handles everything else:

| Deployment | Service Manager | What handles lifecycle? |
|------------|-----------------|------------------------|
| **Docker container** | Docker (`restart: unless-stopped`) | Docker manages start, stop, restart, health |
| **Windows native** | NSSM (Non-Sucking Service Manager) | NSSM wraps the process as a Windows Service |
| **Linux native** | systemd | systemd manages the service unit |
| **macOS native** | launchd | launchd manages the launch daemon/agent |

**What the service manager provides:**
- ✅ Start on system boot
- ✅ Restart on crash/failure
- ✅ Graceful stop on shutdown
- ✅ Health monitoring and status reporting
- ✅ Log management

**What the `service` command provides:**
- ✅ Non-interactive mode (no console prompts)
- ✅ Read configuration from environment variables
- ✅ Auto-registration with default naming
- ✅ Proper exit codes for service managers

### Docker vs. Native Installation

**Docker containers:** You do NOT install the runner as a service inside the container. Docker itself is the service manager. The container runs `automaton-runner service` as its main process (PID 1), and Docker handles restarts via the `restart` policy in docker-compose.

**Native installations (Windows/Linux/macOS):** Users install the runner as a system service using platform-specific tools (NSSM, systemd, launchd). These tools wrap `automaton-runner service` and manage its lifecycle.

> **See also:** [Running Runner in Docker](docker-runner.md) for container-based deployments.

## Quick Setup with Scripts

For simplified installation, we provide automated setup scripts:

**🚀 Recommended: Two-Step Workflow**

**Step 1: Configure the runner interactively**
```powershell
# Windows - Run the runner first to configure everything
automaton-runner
# ↑ Prompts for API key, server URL, runner name
# ↑ Automatically saves API key as environment variable
```

```bash
# Linux/macOS - Run the runner first to configure everything
automaton-runner
# ↑ Prompts for API key, server URL, runner name
# ↑ Saves configuration automatically
```

**Step 2: Convert to service (no parameters needed!)**
```powershell
# Windows (PowerShell as Administrator)
.\scripts\setup-service.ps1
# ↑ Detects all configuration automatically - just confirm!
```

```bash
# Linux/macOS (with sudo)
sudo ./scripts/setup-service.sh
# ↑ Detects all configuration automatically - just confirm!
```

The scripts will:
- ✅ Download required service management tools (NSSM for Windows)
- ✅ Create the service with proper configuration
- ✅ Set up logging and automatic restart
- ✅ Configure environment variables
- ✅ Enable automatic startup on boot

**For manual setup or advanced configuration**, continue reading the detailed instructions below.

## Why Run as a Service?

**Benefits:**
- ✅ **Automatic startup** on system boot
- ✅ **Auto-restart** on crashes or failures
- ✅ **Persistent connection** to the Automaton server
- ✅ **Background execution** without active user session
- ✅ **Better resource management** through OS thread pools
- ✅ **Standard monitoring** with OS tools

**When to Use:**
- Production server deployments
- Always-on automation requirements
- High-volume workflow execution
- Unattended operation

**When NOT to Use:**
- Development and testing
- Desktop/laptop personal use
- CI/CD ephemeral runners
- Learning and experimentation

## Prerequisites

Before you begin:
1. Install Automaton Runner (see [Getting Started](../README.md))
2. Test the runner works in console mode: `automaton-runner`
3. Have your API key ready from the Automaton Studio
4. Know your server URL (e.g., `https://studio.automaton.monster`)

## Windows Service Setup

### Option 1: Using NSSM (Recommended)

NSSM (Non-Sucking Service Manager) is the easiest way to run any console application as a Windows service.

#### Step 1: Download NSSM

Download from [nssm.cc](https://nssm.cc/download) or use Chocolatey:
```powershell
choco install nssm
```

#### Step 2: Locate Runner Executable

Find where the runner is installed:
```powershell
# Usually in one of these locations:
C:\Program Files\Automaton\automaton-runner.exe
C:\Users\<YourName>\.automaton\bin\automaton-runner.exe
```

#### Step 3: Create the Service

Open PowerShell **as Administrator** and run:

```powershell
# Set your configuration
$runnerPath = "C:\Path\To\automaton-runner.exe"
$serviceName = "AutomatonRunner"
$apiKey = "ak_your_api_key_here"
$serverUrl = "https://studio.automaton.monster"

# Install service with 'service' command argument
nssm install $serviceName $runnerPath service

# Configure environment variables
nssm set $serviceName AppEnvironmentExtra "AUTOMATON_API_KEY=$apiKey" "AUTOMATON_SERVER_URL=$serverUrl"

# Set description
nssm set $serviceName Description "Automaton workflow runner for automation"

# Configure automatic startup
nssm set $serviceName Start SERVICE_AUTO_START

# Set up logging
nssm set $serviceName AppStdout "C:\Automaton\Logs\service-output.log"
nssm set $serviceName AppStderr "C:\Automaton\Logs\service-error.log"

# Enable log rotation (10MB files)
nssm set $serviceName AppRotateFiles 1
nssm set $serviceName AppRotateOnline 1
nssm set $serviceName AppRotateBytes 10485760

# Start the service
nssm start $serviceName
```

#### Step 4: Verify the Service

```powershell
# Check service status
nssm status AutomatonRunner

# Or use Windows service commands
Get-Service AutomatonRunner
sc query AutomatonRunner

# View logs
Get-Content C:\Automaton\Logs\service-output.log -Tail 20 -Wait
```

#### Managing the Service

```powershell
# Stop service
nssm stop AutomatonRunner

# Start service
nssm start AutomatonRunner

# Restart service
nssm restart AutomatonRunner

# Remove service
nssm remove AutomatonRunner confirm
```

### Option 2: Using sc.exe (Native Windows)

If you prefer not to use NSSM, you can use Windows' built-in `sc.exe` command. However, this requires your application to implement Windows Service APIs (which the runner doesn't), so **NSSM is strongly recommended**.

## Linux Service Setup (systemd)

Most modern Linux distributions use systemd for service management.

### Step 1: Create Service Unit File

Create a file at `/etc/systemd/system/automaton-runner.service`:

```bash
sudo nano /etc/systemd/system/automaton-runner.service
```

Add the following content (adjust paths and values):

```ini
[Unit]
Description=Automaton workflow runner for cross-platform automation
After=network.target
Wants=network.target

[Service]
Type=simple
User=<your-username>
Group=<your-username>
WorkingDirectory=/home/<your-username>
Environment="AUTOMATON_SERVER_URL=https://studio.automaton.monster"
Environment="AUTOMATON_API_KEY=ak_your_api_key_here"
ExecStart=/usr/local/bin/automaton-runner service
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal

# Optional: Resource limits
# MemoryMax=1G
# CPUQuota=80%

[Install]
WantedBy=multi-user.target
```

**Configuration Notes:**
- Replace `<your-username>` with your actual username
- Update `AUTOMATON_API_KEY` with your API key
- Update `AUTOMATON_SERVER_URL` to your server URL
- Note the `service` argument in `ExecStart` — this is required for non-interactive mode
- Adjust `WorkingDirectory` if runner stores files

### Step 2: Set Permissions

```bash
# Set correct ownership and permissions
sudo chown root:root /etc/systemd/system/automaton-runner.service
sudo chmod 644 /etc/systemd/system/automaton-runner.service
```

### Step 3: Enable and Start the Service

```bash
# Reload systemd to recognize new service
sudo systemctl daemon-reload

# Enable service to start on boot
sudo systemctl enable automaton-runner

# Start the service now
sudo systemctl start automaton-runner

# Check status
sudo systemctl status automaton-runner
```

### Step 4: Verify Operation

```bash
# View real-time logs
sudo journalctl -u automaton-runner -f

# View last 50 log entries
sudo journalctl -u automaton-runner -n 50

# Check if service is running
sudo systemctl is-active automaton-runner
```

### Managing the Service

```bash
# Stop service
sudo systemctl stop automaton-runner

# Start service
sudo systemctl start automaton-runner

# Restart service
sudo systemctl restart automaton-runner

# Disable auto-start on boot
sudo systemctl disable automaton-runner

# Remove service
sudo systemctl stop automaton-runner
sudo systemctl disable automaton-runner
sudo rm /etc/systemd/system/automaton-runner.service
sudo systemctl daemon-reload
```

### Advanced systemd Configuration

For enhanced security, add these options to the `[Service]` section:

```ini
# Restrict access to home directories
ProtectHome=yes

# Make system directories read-only
ProtectSystem=strict

# Private temporary directories
PrivateTmp=yes

# Prevent privilege escalation
NoNewPrivileges=yes

# Limit network access (if needed)
RestrictAddressFamilies=AF_INET AF_INET6

# Memory and CPU limits
MemoryMax=1G
MemoryHigh=800M
CPUQuota=80%
```

## macOS Service Setup (launchd)

macOS uses `launchd` for service management. You can create either a system daemon (runs for all users) or a user agent (runs for specific user).

### Option 1: System-Wide Daemon (Recommended for Servers)

#### Step 1: Create Launch Daemon Plist

Create a file at `/Library/LaunchDaemons/com.automaton.runner.plist`:

```bash
sudo nano /Library/LaunchDaemons/com.automaton.runner.plist
```

Add the following content:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.automaton.runner</string>
    
    <key>ProgramArguments</key>
    <array>
        <string>/usr/local/bin/automaton-runner</string>
        <string>service</string>
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
        <key>AUTOMATON_SERVER_URL</key>
        <string>https://studio.automaton.monster</string>
        <key>AUTOMATON_API_KEY</key>
        <string>ak_your_api_key_here</string>
    </dict>
    
    <key>WorkingDirectory</key>
    <string>/usr/local/var/automaton</string>
</dict>
</plist>
```

> **Note:** The `service` argument is included in `ProgramArguments` to run in non-interactive mode.

#### Step 2: Set Permissions

```bash
sudo chown root:wheel /Library/LaunchDaemons/com.automaton.runner.plist
sudo chmod 644 /Library/LaunchDaemons/com.automaton.runner.plist
```

#### Step 3: Load and Start

```bash
# Load the daemon
sudo launchctl load /Library/LaunchDaemons/com.automaton.runner.plist

# Verify it's running
sudo launchctl list | grep automaton
```

### Option 2: User Agent (For Desktop Use)

If you want the runner to run only when you're logged in:

#### Step 1: Create User Launch Agent

Create a file at `~/Library/LaunchAgents/com.automaton.runner.plist`:

```bash
mkdir -p ~/Library/LaunchAgents
nano ~/Library/LaunchAgents/com.automaton.runner.plist
```

Use the same XML content as above, but adjust paths to be in your home directory.

#### Step 2: Load the Agent

```bash
# No sudo needed for user agents
launchctl load ~/Library/LaunchAgents/com.automaton.runner.plist

# Verify
launchctl list | grep automaton
```

### Managing the Service

```bash
# Stop (unload) service
sudo launchctl unload /Library/LaunchDaemons/com.automaton.runner.plist

# Start (load) service
sudo launchctl load /Library/LaunchDaemons/com.automaton.runner.plist

# View logs
tail -f /var/log/automaton-runner.log

# Remove service
sudo launchctl unload /Library/LaunchDaemons/com.automaton.runner.plist
sudo rm /Library/LaunchDaemons/com.automaton.runner.plist
```

## Configuration Management

### Environment Variables

All platforms support environment variables for configuration:

| Variable | Required | Description |
|----------|----------|-------------|
| `AUTOMATON_API_KEY` | Yes | Your API key from Automaton Studio |
| `AUTOMATON_SERVER_URL` | Yes | Server URL (e.g., `https://studio.automaton.monster`) |
| `AppConfig__BaseUrl` | Alt | Alternative to `AUTOMATON_SERVER_URL` (.NET config binding) |
| `DOTNET_ENVIRONMENT` | No | Set to `Production` for production deployments |
| `DOTNET_ThreadPool_MinThreads` | No | Minimum thread pool size (default: CPU count) |
| `DOTNET_ThreadPool_MaxThreads` | No | Maximum thread pool size (default: CPU count × 250) |

### Storing API Keys Securely

**DO NOT** hardcode API keys in configuration files. Use environment variables instead:

**Windows (NSSM):**
```powershell
nssm set AutomatonRunner AppEnvironmentExtra "AUTOMATON_API_KEY=ak_..." "AUTOMATON_SERVER_URL=https://..."
```

**Linux (systemd):**
```ini
[Service]
Environment="AUTOMATON_API_KEY=ak_..."
Environment="AUTOMATON_SERVER_URL=https://..."
```

**macOS (launchd):**
```xml
<key>EnvironmentVariables</key>
<dict>
    <key>AUTOMATON_API_KEY</key>
    <string>ak_...</string>
    <key>AUTOMATON_SERVER_URL</key>
    <string>ak_...</string>
</dict>
```

For even better security, use OS-specific secret management:
- **Windows**: Windows Credential Manager
- **Linux**: systemd credentials or secrets files
- **macOS**: Keychain

## Monitoring and Logs

### Windows (NSSM)

```powershell
# View output logs
Get-Content C:\Automaton\Logs\service-output.log -Tail 50 -Wait

# View error logs
Get-Content C:\Automaton\Logs\service-error.log -Tail 50 -Wait

# Check service status
Get-Service AutomatonRunner | Select-Object Status, StartType
```

### Linux (systemd)

```bash
# Real-time logs
sudo journalctl -u automaton-runner -f

# Last 100 lines
sudo journalctl -u automaton-runner -n 100

# Logs since yesterday
sudo journalctl -u automaton-runner --since yesterday

# Check service status
sudo systemctl status automaton-runner
```

### macOS (launchd)

```bash
# View logs
tail -f /var/log/automaton-runner.log

# View error logs
tail -f /var/log/automaton-runner-error.log

# Check if loaded
launchctl list | grep automaton
```

## Troubleshooting

### Service Won't Start

**Check Prerequisites:**
1. Verify runner works in console mode first
2. Confirm .NET 8.0 runtime is installed
3. Test API key with console mode
4. Check network connectivity to server

**Platform-Specific:**

**Windows:**
```powershell
# Check NSSM status
nssm status AutomatonRunner

# View error logs
Get-Content C:\Automaton\Logs\service-error.log -Tail 20
```

**Linux:**
```bash
# Check service status
sudo systemctl status automaton-runner

# View detailed logs
sudo journalctl -u automaton-runner -xe
```

**macOS:**
```bash
# Check error logs
tail -20 /var/log/automaton-runner-error.log

# Verify plist syntax
plutil -lint /Library/LaunchDaemons/com.automaton.runner.plist
```

### High Memory Usage

Monitor and limit memory:

**Linux (systemd):**
```ini
[Service]
MemoryMax=1G
MemoryHigh=800M
```

**Check current usage:**
```bash
# Linux
ps aux | grep automaton-runner

# Windows
Get-Process automaton-runner | Select-Object WS, PM, CPU

# macOS
ps aux | grep automaton-runner
```

### Connection Issues

**Test connectivity:**
```bash
# Test server connection
curl -I https://studio.automaton.monster

# Check DNS resolution
nslookup studio.automaton.monster
```

**Check API key:**
```bash
# Run in console mode first to verify
automaton-runner
```

### Service Keeps Restarting

**Check logs** for errors. Common causes:
- Invalid API key
- Server unreachable
- Missing dependencies
- Permission issues

**Temporarily disable restart** to debug:

**Linux:**
```bash
# Edit service
sudo systemctl edit automaton-runner

# Add:
[Service]
Restart=no
```

**Windows:**
```powershell
nssm set AutomatonRunner Start SERVICE_DEMAND_START
```

## Performance Tuning

### Thread Pool Configuration

For high-volume workflow execution:

```bash
# Increase thread pool size
# Add to environment variables

# For CPU-bound workflows
DOTNET_ThreadPool_MinThreads=16
DOTNET_ThreadPool_MaxThreads=500

# For I/O-bound workflows
DOTNET_ThreadPool_MinThreads=4
DOTNET_ThreadPool_MaxThreads=1000
```

### Memory Management

```bash
# Enable server GC for better performance
DOTNET_gcServer=1

# Set memory limit (in bytes, 1GB example)
DOTNET_GCHeapHardLimit=0x40000000
```

## Updating the Service

When updating the runner:

1. **Stop the service:**
   ```bash
   # Windows
   nssm stop AutomatonRunner
   
   # Linux
   sudo systemctl stop automaton-runner
   
   # macOS
   sudo launchctl unload /Library/LaunchDaemons/com.automaton.runner.plist
   ```

2. **Update the binary:**
   Download and replace the runner executable

3. **Start the service:**
   ```bash
   # Windows
   nssm start AutomatonRunner
   
   # Linux
   sudo systemctl start automaton-runner
   
   # macOS
   sudo launchctl load /Library/LaunchDaemons/com.automaton.runner.plist
   ```

## Multiple Runner Instances

You can run multiple runners as separate services:

### Linux Example

```bash
# Create separate service files
sudo cp /etc/systemd/system/automaton-runner.service \
        /etc/systemd/system/automaton-runner-priority.service

# Edit each with different environment variables
sudo nano /etc/systemd/system/automaton-runner-priority.service

# Start both services
sudo systemctl start automaton-runner
sudo systemctl start automaton-runner-priority
```

### Windows Example

```powershell
# Install second instance
nssm install AutomatonRunner-Priority "C:\Path\To\automaton-runner.exe"
nssm set AutomatonRunner-Priority AppEnvironmentExtra "AUTOMATON_API_KEY=ak_..." "RUNNER_NAME=priority-runner"
nssm start AutomatonRunner-Priority
```

## Security Best Practices

1. **Run with minimal privileges** - Use dedicated service accounts
2. **Protect API keys** - Use environment variables, not config files
3. **Restrict file permissions** - Only service user should access config
4. **Enable TLS** - Always use HTTPS for server communication
5. **Rotate API keys** - Regularly update credentials
6. **Monitor logs** - Watch for suspicious activity
7. **Update regularly** - Keep runner updated with security patches

## Summary

Running Automaton Runner as a service provides resilience and better resource management for production deployments. Choose the approach that fits your platform:

- **Windows**: NSSM (easiest)
- **Linux**: systemd (standard)
- **macOS**: launchd (native)

For development and testing, console mode is simpler. For production servers, service mode is recommended.

## Additional Resources

- [Automaton Documentation](../README.md)
- [Runner Downloads](https://studio.automaton.monster/downloads)
- [NSSM Documentation](https://nssm.cc/)
- [systemd Service Documentation](https://www.freedesktop.org/software/systemd/man/systemd.service.html)
- [launchd Documentation](https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html)
