# Console Runner Setup Guide

<div class="doc-hero hero-runners">
  <div class="doc-hero-content">
    <div class="doc-hero-icon">🏃</div>
    <h1>Console Runner Setup</h1>
    <p class="hero-subtitle">
      Complete guide to installing, configuring, and running the Automaton Console Runner on any platform
    </p>
    <div class="doc-hero-stats">
      <div class="doc-hero-stat">
        <span class="doc-hero-stat-number">🖥️</span>
        <span class="doc-hero-stat-label">Cross-Platform</span>
      </div>
      <div class="doc-hero-stat">
        <span class="doc-hero-stat-number">⚡</span>
        <span class="doc-hero-stat-label">One-Command Setup</span>
      </div>
      <div class="doc-hero-stat">
        <span class="doc-hero-stat-number">🔐</span>
        <span class="doc-hero-stat-label">Secure API Keys</span>
      </div>
    </div>
  </div>
</div>

## What is the Console Runner?

The **Automaton Console Runner** is a cross-platform command-line application that executes automation workflows on your machines. It's perfect for:

- **Production Deployments**: Run workflows on servers without GUI
- **CI/CD Pipelines**: Integrate automation into your build and deployment processes
- **Distributed Execution**: Deploy multiple runners across different machines
- **Cloud/Container Environments**: Run in Docker, Kubernetes, or cloud VMs

### Key Features

- ✅ **Cross-platform**: Works on Windows, Linux, and macOS
- ✅ **Interactive Setup**: One-command wizard guides you through configuration
- ✅ **API Key Authentication**: Secure, automation-friendly authentication
- ✅ **Auto-reconnection**: Handles network interruptions gracefully
- ✅ **Service Mode**: Run as a system service/daemon
- ✅ **Zero UI Dependencies**: Pure console application

## Prerequisites

Before installing the Console Runner, ensure you have:

### System Requirements
- **Operating System**: Windows 10+, Linux (Ubuntu 20.04+, RHEL 8+, etc.), or macOS 11+
- **.NET Runtime**: .NET 8.0 Runtime (automatically installed with installation scripts)
- **Network Access**: Connection to your Automaton Studio server
- **API Key**: Created in Automaton Studio (see [Getting an API Key](#getting-an-api-key) below)

### Supported Platforms
| Platform | Architecture | Status |
|----------|-------------|--------|
| Windows | x64, ARM64 | ✅ Fully Supported |
| Linux | x64, ARM64 | ✅ Fully Supported |
| macOS | Intel, Apple Silicon | ✅ Fully Supported |

## Installation

There are two ways to install the Console Runner:

### Option 1: Quick Installation (Recommended)

The quickest way to get started is using our automated installation scripts.

#### Windows (PowerShell)

**Run as Administrator:**

```powershell
iwr -useb https://your-automaton-server.com/downloads/install.ps1 | iex
```

This script will:
- Download the appropriate runner version for your system
- Install it to `C:\Program Files\Automaton\Runner`
- Add the runner to your PATH
- Create the `automaton-runner` command globally

#### Linux / macOS (Terminal)

```bash
curl -sSL https://your-automaton-server.com/downloads/install.sh | bash
```

This script will:
- Download the appropriate runner version for your system
- Install it to `/usr/local/bin`
- Make it executable
- Create the `automaton-runner` command globally

> **Note**: Replace `https://your-automaton-server.com` with your actual Automaton Studio server URL.

### Option 2: Manual Installation

If you prefer manual installation or need more control:

#### 1. Download the Runner

Visit your Automaton Studio instance and go to **Downloads → Console Runner**, or download directly:

- **Windows x64**: `automaton-runner-win-x64.zip`
- **Windows ARM64**: `automaton-runner-win-arm64.zip`
- **Linux x64**: `automaton-runner-linux-x64.tar.gz`
- **Linux ARM64**: `automaton-runner-linux-arm64.tar.gz`
- **macOS Intel**: `automaton-runner-macos-x64.tar.gz`
- **macOS Apple Silicon**: `automaton-runner-macos-arm64.tar.gz`

#### 2. Extract the Package

**Windows:**
```powershell
Expand-Archive -Path automaton-runner-win-x64.zip -DestinationPath C:\Automaton\Runner
```

**Linux/macOS:**
```bash
sudo tar -xzf automaton-runner-linux-x64.tar.gz -C /usr/local/bin/
sudo chmod +x /usr/local/bin/automaton-runner
```

#### 3. Add to PATH (Windows only)

Add `C:\Automaton\Runner` to your system PATH environment variable.

#### 4. Verify Installation

```bash
automaton-runner --version
```

## Getting an API Key

Before you can run the Console Runner, you need an API key from Automaton Studio.

### Step-by-Step:

1. **Login to Automaton Studio** - Open your Automaton Studio web interface
2. **Navigate to API Keys** - Go to **Settings → API Keys** in the left sidebar
3. **Create New Key** - Click the **Create API Key** button
4. **Configure Key Settings**:
   - **Name**: Give it a descriptive name (e.g., "Production Runner 01")
   - **Scope**: Select **"Runner"** - this grants the key permission to register and run workflows
   - **Description**: (Optional) Add notes about where this key will be used
   - **Expiration**: (Optional) Set an expiration date for security
5. **Save and Copy** - Click **Create** and immediately copy the API key (starts with `ak_`)
   - ⚠️ **Important**: You won't be able to see the key again after closing the dialog!

### API Key Format

Runner API keys typically look like:
```
ak_runner_1234567890abcdef
```

Keys starting with `ak_` and at least 10 characters long are valid.

## First Run - Interactive Setup

The easiest way to configure your runner is through the interactive setup wizard.

### Start the Runner

Simply run:
```bash
automaton-runner
```

### What Happens Next

The runner will automatically guide you through setup:

#### 1. **Server URL Configuration** (if not set)
```
Enter server URL (default: https://localhost:7091):
```
- Press Enter to use the default
- Or type your server URL (e.g., `https://automaton.company.com`)
- This URL is **saved automatically** for future runs

#### 2. **API Key Authentication** (if not authenticated)
```
Enter API key:
```
- Paste your API key from Automaton Studio
- The key is **stored securely** in your user profile
- Future runs won't ask for it again

#### 3. **Runner Registration** (if not registered)
```
Enter runner name (default: COMPUTERNAME-user):
```
- Press Enter to use the smart default (computer name + username)
- Or type a custom name (e.g., "Production-Server-01")
- This name appears in Automaton Studio's Runners page

#### 4. **Automatic Startup**
```
✅ Connected to Automaton Studio
✅ Runner registered: Production-Server-01
🏃 Runner is now active and waiting for workflows...

Press 'q' to quit, 's' for status, 'r' to reconnect
```

The runner is now live and ready to execute workflows!

### Interactive Commands (While Running)

While the runner is active, you can use these keyboard commands:
- **`q`** - Quit the runner gracefully
- **`s`** - Show status information (connection, runner ID, jobs)
- **`r`** - Force a reconnection attempt

## Configuration Methods

The Console Runner supports multiple configuration methods for different use cases:

### Method 1: Interactive Setup (Best for Getting Started)

Just run `automaton-runner` and follow the prompts.

**Pros**: 
- Easiest to use
- Guided setup
- Configuration is saved automatically

**Best for**: 
- First-time setup
- Development environments
- Manual deployments

### Method 2: Environment Variables (Best for Automation)

Set configuration via environment variables to skip all prompts.

**Windows:**
```powershell
$env:AUTOMATON_API_KEY = "ak_runner_1234567890abcdef"
$env:AppConfig__BaseUrl = "https://automaton.company.com"
automaton-runner
```

**Linux/macOS:**
```bash
export AUTOMATON_API_KEY="ak_runner_1234567890abcdef"
export AppConfig__BaseUrl="https://automaton.company.com"
automaton-runner
```

**Pros**:
- Fully automated
- No interactive prompts
- Perfect for CI/CD

**Best for**:
- Docker containers
- CI/CD pipelines
- Automated deployments
- Systemd services

### Method 3: Explicit Commands (Best for Scripting)

Use explicit commands to configure step-by-step.

```bash
# Configure authentication
automaton-runner auth --api-key ak_runner_xxx --server https://server.com

# Register the runner
automaton-runner register --name "Production-Runner-01"

# Start the runner
automaton-runner
```

**Pros**:
- Explicit control
- Scriptable
- Configuration is persisted

**Best for**:
- Scripted deployments
- Advanced users
- Multi-step setup scripts

## Running as a System Service

For production deployments, you'll want the runner to start automatically with the system.

### Install as Service

```bash
automaton-runner service install --name "My-Production-Runner"
```

This command:
- Creates a system service (Windows Service, systemd, or launchd)
- Configures auto-start on system boot
- Sets up proper logging

### Service Management Commands

#### Check Service Status
```bash
automaton-runner service status
```

#### Platform-Specific Commands

**Windows:**
```powershell
# Start service
net start AutomatonRunner

# Stop service
net stop AutomatonRunner

# Restart service
net stop AutomatonRunner && net start AutomatonRunner
```

**Linux (systemd):**
```bash
# Start service
sudo systemctl start AutomatonRunner

# Stop service
sudo systemctl stop AutomatonRunner

# Restart service
sudo systemctl restart AutomatonRunner

# Enable auto-start
sudo systemctl enable AutomatonRunner

# Check status
systemctl status AutomatonRunner
```

**macOS (launchd):**
```bash
# Start service
launchctl start com.automaton.runner

# Stop service
launchctl stop com.automaton.runner

# Check status
launchctl list | grep automaton
```

### Uninstall Service

```bash
automaton-runner service uninstall
```

## Advanced Configuration

### Checking Configuration Status

View your current runner configuration:

```bash
automaton-runner status
```

Output includes:
- ✅ Server URL
- ✅ Authentication status
- ✅ Runner registration details
- ✅ Connection status

### Changing Server URL

To point your runner to a different server:

```bash
automaton-runner server --url https://new-server.com
```

Or interactively:
```bash
automaton-runner server
```

### Re-registering a Runner

To change the runner name or force a new registration:

```bash
automaton-runner register --name "New-Runner-Name" --force
```

### Cleaning Up Configuration

To remove all stored configuration and start fresh:

```bash
automaton-runner cleanup
```

This removes:
- Stored API key
- Runner registration
- Server URL configuration

Environment variables are not affected.

## Docker Deployment

Running the Console Runner in Docker containers.

### Dockerfile Example

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0

# Copy the runner binary
COPY automaton-runner /app/
WORKDIR /app

# Make executable (Linux containers)
RUN chmod +x automaton-runner

# Set environment variables
ENV AUTOMATON_API_KEY=""
ENV AppConfig__BaseUrl=""

# Run the runner
ENTRYPOINT ["./automaton-runner"]
```

### Docker Compose Example

```yaml
version: '3.8'

services:
  automaton-runner:
    image: automaton-runner:latest
    environment:
      - AUTOMATON_API_KEY=ak_runner_1234567890abcdef
      - AppConfig__BaseUrl=https://automaton.company.com
    restart: unless-stopped
    container_name: automaton-runner-01
```

### Running with Docker

```bash
docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  -e AUTOMATON_API_KEY="ak_runner_xxx" \
  -e AppConfig__BaseUrl="https://automaton.company.com" \
  automaton-runner:latest
```

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Deploy Automaton Workflow

on:
  push:
    branches: [ main ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    
    steps:
    - name: Install Automaton Runner
      run: |
        curl -sSL https://automaton.company.com/downloads/install.sh | bash
    
    - name: Configure Runner
      env:
        AUTOMATON_API_KEY: ${{ secrets.AUTOMATON_API_KEY }}
        AppConfig__BaseUrl: ${{ secrets.AUTOMATON_SERVER_URL }}
      run: |
        automaton-runner register --name "CI-Runner-${{ github.run_id }}"
```

### Azure DevOps Example

```yaml
trigger:
  branches:
    include:
    - main

pool:
  vmImage: 'ubuntu-latest'

steps:
- script: |
    curl -sSL $(AUTOMATON_SERVER_URL)/downloads/install.sh | bash
  displayName: 'Install Automaton Runner'

- script: |
    export AUTOMATON_API_KEY=$(AUTOMATON_API_KEY)
    export AppConfig__BaseUrl=$(AUTOMATON_SERVER_URL)
    automaton-runner register --name "Azure-Runner-$(Build.BuildId)"
  displayName: 'Register Runner'
```

### GitLab CI Example

```yaml
deploy:
  stage: deploy
  script:
    - curl -sSL $AUTOMATON_SERVER_URL/downloads/install.sh | bash
    - export AUTOMATON_API_KEY=$AUTOMATON_API_KEY
    - export AppConfig__BaseUrl=$AUTOMATON_SERVER_URL
    - automaton-runner register --name "GitLab-Runner-$CI_PIPELINE_ID"
  only:
    - main
```

## Troubleshooting

### Runner Won't Connect

**Symptoms**: Runner shows connection errors or can't reach server

**Solutions**:
1. Check server URL is correct:
   ```bash
   automaton-runner status
   ```
2. Verify network connectivity:
   ```bash
   curl https://your-server.com
   ```
3. Check firewall settings - ensure outbound HTTPS (443) is allowed
4. Verify API key is valid in Automaton Studio

### Authentication Failed

**Symptoms**: "Invalid API key" or "Authentication failed" errors

**Solutions**:
1. Verify API key in Automaton Studio (Settings → API Keys)
2. Check the key hasn't expired
3. Ensure the key has "Runner" scope
4. Re-authenticate:
   ```bash
   automaton-runner cleanup
   automaton-runner auth --api-key ak_your_new_key
   ```

### Runner Not Appearing in Studio

**Symptoms**: Runner is connected but doesn't show in Runners page

**Solutions**:
1. Check runner is actually registered:
   ```bash
   automaton-runner status
   ```
2. Force re-registration:
   ```bash
   automaton-runner register --force
   ```
3. Check server logs for registration errors
4. Verify network connectivity and SignalR connections

### Permission Denied (Linux/macOS)

**Symptoms**: "Permission denied" when running commands

**Solutions**:
1. Make the binary executable:
   ```bash
   chmod +x /usr/local/bin/automaton-runner
   ```
2. For service installation, use sudo:
   ```bash
   sudo automaton-runner service install
   ```

### Service Won't Start

**Symptoms**: Service installation succeeds but service won't start

**Solutions**:
1. Check service status:
   ```bash
   # Linux
   systemctl status AutomatonRunner
   
   # Windows
   Get-Service AutomatonRunner
   ```
2. Review service logs:
   - **Linux**: `journalctl -u AutomatonRunner`
   - **Windows**: Event Viewer → Windows Logs → Application
3. Ensure environment variables are set in service configuration
4. Verify .NET 8.0 runtime is installed

### Workflows Not Executing

**Symptoms**: Runner is connected but workflows don't start

**Solutions**:
1. Check runner status in Automaton Studio (Runners page)
2. Verify the runner is set to "Online" status
3. Check flow is assigned to this runner or any runner
4. Review runner logs for execution errors
5. Ensure runner has permissions to access required resources

## Command Reference

### Quick Command Summary

| Command | Description |
|---------|-------------|
| `automaton-runner` | Start the runner (interactive setup if needed) |
| `automaton-runner --version` | Show version information |
| `automaton-runner --help` | Show help and all commands |
| `automaton-runner auth` | Configure API key authentication |
| `automaton-runner register` | Register runner with server |
| `automaton-runner status` | Show configuration and status |
| `automaton-runner server` | Configure server URL |
| `automaton-runner cleanup` | Remove stored configuration |
| `automaton-runner service install` | Install as system service |
| `automaton-runner service uninstall` | Remove system service |
| `automaton-runner service status` | Show service status |

### Detailed Command Reference

For complete command documentation, see the [Console Runner README](https://github.com/rzvdaniel/automaton/blob/main/src/Automaton.Runner.Console/README.md).

## Best Practices

### Security

- 🔐 **Rotate API keys regularly** - Set expiration dates and rotate keys every 90 days
- 🔐 **Use environment variables in production** - Don't store keys in scripts or config files
- 🔐 **Limit key scope** - Use "Runner" scope specifically, not admin scopes
- 🔐 **Monitor key usage** - Check API Keys page for unusual activity
- 🔐 **Revoke compromised keys immediately** - Delete and create new keys if suspected

### Deployment

- 📦 **Use service mode in production** - Ensure runners restart automatically
- 📦 **Implement health monitoring** - Check runner status periodically
- 📦 **Deploy multiple runners** - Distribute load and ensure availability
- 📦 **Name runners meaningfully** - Use descriptive names like "Prod-Web-01"
- 📦 **Document runner locations** - Keep inventory of where runners are deployed

### Maintenance

- 🔧 **Keep runners updated** - Check for updates regularly
- 🔧 **Monitor logs** - Review runner logs for errors or warnings
- 🔧 **Test before production** - Deploy to development/staging first
- 🔧 **Backup configurations** - Document environment variables and settings
- 🔧 **Plan for failures** - Have procedures for runner replacement

## Next Steps

Now that your Console Runner is set up:

1. **Assign Workflows** - [Learn how to assign flows to runners](flows.md)
2. **Monitor Execution** - [View runner activity and logs](runners.md#monitoring-runners)
3. **Scale Your Deployment** - [Deploy additional runners](runners.md#adding-multiple-runners)
4. **Configure Custom Steps** - [Create custom workflow steps](custom-steps.md)

## Additional Resources

- [Managing Runners in Studio](runners.md) - Learn about the Runners page in Automaton Studio
- [Working with Flows](flows.md) - Create and manage automation workflows
- [API Keys Guide](../api/authentication.md) - Understand API key authentication
- [Console Runner Source](https://github.com/rzvdaniel/automaton/tree/main/src/Automaton.Runner.Console) - View source code and contribute
