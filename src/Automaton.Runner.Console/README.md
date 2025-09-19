# Automaton Runner Console

A cross-platform command-line runner for the Automaton workflow automation system with **API Key authentication**.

## Features

- **Cross-platform**: Runs on Windows, Linux, and macOS
- **No UI dependencies**: Pure console application
- **API Key Authentication**: Secure, automation-friendly authentication
- **Auto-reconnection**: Handles network interruptions gracefully
- **File-based storage**: Runner config stored securely in user profile
- **Real-time execution**: Receives and executes workflows via SignalR

## Installation

### Prerequisites

- .NET 8.0 Runtime

### Build from Source

```bash
cd src/Automaton.Runner.Console
dotnet build -c Release
dotnet publish -c Release --self-contained false
```

The executable will be located in `bin/Release/net8.0/publish/automaton-runner.exe` (Windows) or `bin/Release/net8.0/publish/automaton-runner` (Linux/macOS).

## Authentication

### 🔐 API Key Authentication

API keys are the **primary and only** authentication method for console runners.

**Benefits:**
- ✅ No interactive login required
- ✅ Long-lived credentials
- ✅ Easy credential rotation
- ✅ Environment variable friendly
- ✅ Scoped permissions
- ✅ Perfect for automation and CI/CD
- ✅ **Server URL is automatically saved**

### Getting Started

#### Step 1: Create an API Key

1. Login to Automaton Studio Web Interface
2. Go to **Settings → API Keys**
3. Click **Create API Key**
4. Fill in the details:
   - **Name**: Descriptive name (e.g., "Production Runner")
   - **Scope**: Choose "Runner" for console runner access
   - **Description**: Optional description
   - **Expiration**: Optional expiration date

#### Step 2: Run the Interactive Setup

```bash
# Start the runner - it will guide you through everything ONCE
automaton-runner
```

**That's it!** The runner will interactively prompt you for:
- ✅ Your API key (when needed) - **saved securely**
- ✅ Server URL (if different from default) - **remembered for future runs**
- ✅ Runner name (with smart defaults) - **saved automatically**
- ✅ Automatic connection and startup

**💡 Smart Memory**: After the first setup, future runs of `automaton-runner` will remember:
- Your server URL
- Your API key (if not using environment variable)
- Your runner registration

#### Alternative: Environment Variable Method (Best for Automation)

For automated deployments, you can set the API key as an environment variable:

```bash
# Set environment variable
export AUTOMATON_API_KEY="ak_runner_1234567890abcdef"

# Optionally set server URL to avoid any prompts
export AppConfig__BaseUrl="https://your-server.com"

# Start directly - no prompts needed at all
automaton-runner
```

#### Advanced: Explicit Command Usage

For scripted workflows or advanced users, you can use explicit commands (see Commands section below):

```bash
# Set API key and server URL explicitly (both will be saved)
automaton-runner auth --api-key ak_runner_1234567890abcdef --server https://localhost:7091

# Register runner explicitly  
automaton-runner register --name "Production-Runner"

# Start runner - everything remembered from previous commands
automaton-runner
```

## Usage

### ?? Quick Start (Recommended)

**The easiest way to get started:**

1. **Get an API key from Automaton Studio** (see Step 1 above)
2. **Run the interactive setup:**
   ```bash
   automaton-runner
   ```
3. **Follow the prompts** - the runner will guide you through authentication, registration, and startup automatically!

### ?? Alternative Approaches

#### **For Automation/CI-CD:**
```bash
# Set your API key as environment variable
export AUTOMATON_API_KEY="ak_runner_xxxxxxxxxxxx"

# Run directly - fully automated
automaton-runner
```

#### **For Advanced Users:**
```bash
# Configure step-by-step with explicit commands
automaton-runner auth --api-key ak_runner_xxxxxxxxxxxx --server https://localhost:7091
automaton-runner register --name "My-Runner"
automaton-runner
```

### Commands

All available commands for the automaton-runner:

```bash
automaton-runner [command] [options]
```

**Available Commands:**
- [`(default)`](#default---start-the-runner) - Start the interactive runner
- [`auth`](#auth---authenticate-with-api-key) - Authenticate with API key  
- [`register`](#register---register-runner-with-server) - Register runner with server
- [`status`](#status---show-configuration-and-status) - Show runner status
- [`server`](#server---configure-server-url) - Configure server URL
- [`service`](#service---service-management-optional-convenience-commands) - Service management
- [`cleanup`](#cleanup---cleanup-stored-data)

**Quick Reference:**
```bash
# Most common usage patterns
automaton-runner                    # Interactive setup and start
automaton-runner status             # Show current status
automaton-runner service status     # Show service status
automaton-runner cleanup            # Clear stored configuration
```

#### Default - Start the Runner

```bash
automaton-runner
```

**Interactive setup**: Guides through authentication and registration if needed.
**Interactive Commands (while running):**
- `q`: Quit the runner
- `s`: Show status information
- `r`: Manual reconnection attempt

#### `auth` - Authenticate with API Key

Use this command to explicitly set your API key and server URL.

```bash
automaton-runner auth --api-key <api-key> [--server <url>]
```

**Options:**
- `--api-key`: API key for authentication (optional - will prompt if not provided)
- `--server`: Server URL (optional - will prompt if not provided)

**Examples:**
```bash
# With all parameters
automaton-runner auth --api-key ak_runner_1234567890abcdef --server https://localhost:7091

# Interactive mode - will prompt for missing values
automaton-runner auth

# Just set the API key, use default server
automaton-runner auth --api-key ak_runner_1234567890abcdef
```

#### `register` - Register Runner with Server

Use this command to explicitly register your runner with a specific name.

**?? Authentication Required**: This command requires an API key to be set via `auth` command or `AUTOMATON_API_KEY` environment variable.

```bash
automaton-runner register --name <runner-name> [--force]
```

**Options:**
- `--name`: Runner name (optional if already configured) - will appear in the Automaton Studio UI
- `--force`: Force re-registration even if already registered

**Examples:**
```bash
# Register with specific name
automaton-runner register --name "Production-Server-01"

# Interactive registration - will prompt for name
automaton-runner register

# Force re-registration with new name
automaton-runner register --name "New-Runner-Name" --force
```

**Prerequisites:**
Set API key first using one of these methods:
```bash
# Method 1: Use auth command
automaton-runner auth --api-key ak_runner_xxxxxxxxxxxx

# Method 2: Set environment variable
export AUTOMATON_API_KEY="ak_runner_xxxxxxxxxxxx"
```

#### `status` - Show Configuration and Status

```bash
automaton-runner status
```

Shows current runner configuration, authentication status, and registration status.

#### `server` - Configure Server URL

```bash
automaton-runner server [--url <url>]
```

**Options:**
- `--url`: Server URL to configure (optional - will prompt if not provided)

**Examples:**
```bash
# Set server URL explicitly (will be saved for future use)
automaton-runner server --url https://your-server.com

# Interactive server configuration (will be saved)
automaton-runner server
```

**💾 Persistent Storage**: The server URL is automatically saved to local storage and will be remembered for all future runs. You only need to configure it once per machine/user.

#### `cleanup` - Cleanup Stored Data

```bash
automaton-runner cleanup
```

Removes stored API key, runner registration data, and server URL configuration from local storage. Useful for:
- Switching between different API keys or servers
- Clearing cached runner registrations
- Preparing for fresh deployment
- Resetting all configuration to start over

**What gets cleared:**
- ✅ Stored API key authentication
- ✅ Runner registration (ID and name)
- ✅ Server URL configuration
- ❌ Environment variables (not affected)

**Note**: After cleanup, the next run of `automaton-runner` will prompt for configuration again, just like a fresh installation.

#### `service` - Service Management (Optional Convenience Commands)

Manage the runner as a system service. **Note**: For new installations, we recommend using the installation scripts which provide better service setup.

```bash
automaton-runner service <subcommand>
```

**Subcommands:**

##### `install` - Install as System Service
```bash
automaton-runner service install [--name <runner-name>] [--no-start]
```

**Options:**
- `--name`: Runner name for the service (optional)
- `--no-start`: Install service but don't start it immediately

**Examples:**
```bash
# Install and start service
automaton-runner service install

# Install with custom name
automaton-runner service install --name "Production-Runner"

# Install but don't start
automaton-runner service install --no-start
```

**Requirements by Platform:**
- **Windows**: Administrator privileges, NSSM recommended
- **Linux**: sudo privileges, systemd
- **macOS**: sudo privileges, launchd

##### `uninstall` - Remove System Service
```bash
automaton-runner service uninstall
```

Stops and removes the system service.

##### `status` - Show Service Status
```bash
automaton-runner service status
```

Shows the current status of the system service and available management commands.

**Platform-Specific Service Commands:**

| Platform | Start Service | Stop Service | Status |
|----------|---------------|--------------|---------|
| **Windows** | `net start AutomatonRunner` | `net stop AutomatonRunner` | `automaton-runner service status` |
| **Linux** | `sudo systemctl start AutomatonRunner` | `sudo systemctl stop AutomatonRunner` | `systemctl status AutomatonRunner` |
| **macOS** | `launchctl start com.automaton.runner` | `launchctl stop com.automaton.runner` | `automaton-runner service status` |

## Examples

### Basic Usage Examples

#### Interactive Setup (Recommended)
```bash
# Start the runner - it will guide you through everything
automaton-runner

# The runner will prompt for:
# 1. Server URL (if not configured)
# 2. API key (if not authenticated) 
# 3. Runner name (if not registered)
# 4. Automatically start the runner
```

#### Pre-configured Environment
```bash
# Set environment variables for fully automated operation
export AUTOMATON_API_KEY="ak_runner_1234567890abcdef"
export AppConfig__BaseUrl="https://automaton.company.com"

# Start directly with no prompts
automaton-runner
```

#### Step-by-Step Configuration
```bash
# Configure each component separately
automaton-runner auth --api-key ak_runner_xxx --server https://server.com
automaton-runner register --name "Production-Runner-01"
automaton-runner  # Start the runner
```

#### Service Management Examples
```bash
# Install runner as a system service
automaton-runner service install --name "Production-Runner"

# Check service status
automaton-runner service status

# Remove the service
automaton-runner service uninstall
```

#### Troubleshooting Examples
```bash
# Check current configuration and status
automaton-runner status

# Reset all stored configuration
automaton-runner cleanup

# Reconfigure server URL
automaton-runner server --url https://new-server.com

# Force re-registration with new name
automaton-runner register --name "New-Runner-Name" --force
```

### Advanced Usage Scenarios

#### CI/CD Pipeline Integration
```bash
#!/bin/bash
# Automated deployment script

# Set credentials
export AUTOMATON_API_KEY="$RUNNER_API_KEY"  # From CI secrets
export AppConfig__BaseUrl="https://automaton.company.com"

# Deploy and register
automaton-runner register --name "CI-Runner-$BUILD_ID"

# Install as service for permanent deployment
sudo automaton-runner service install --name "CI-Runner-$BUILD_ID"
```

#### Container Deployment
```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0
COPY automaton-runner /app/
WORKDIR /app

# Set environment variables
ENV AUTOMATON_API_KEY=""
ENV AppConfig__BaseUrl=""

# Run the interactive setup on container start
ENTRYPOINT ["./automaton-runner"]
```

#### Development Environment
```bash
# Quick setup for development
automaton-runner auth --api-key ak_dev_xxx --server https://localhost:7091
automaton-runner register --name "Dev-$(whoami)-$(hostname)"
automaton-runner  # Start and keep running