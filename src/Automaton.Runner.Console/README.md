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

**Note**: The compiled executable is named `automaton-runner` regardless of the project folder name.

## Authentication

### ?? API Key Authentication

API keys are the **primary and only** authentication method for console runners.

**Benefits:**
- ? No interactive login required
- ? Long-lived credentials
- ? Easy credential rotation
- ? Environment variable friendly
- ? Scoped permissions
- ? Perfect for automation and CI/CD

#### Step 1: Create an API Key

1. Login to Automaton Studio Web Interface
2. Go to **Settings ? API Keys**
3. Click **Create API Key**
4. Fill in the details:
   - **Name**: Descriptive name (e.g., "Production Runner")
   - **Scope**: Choose "Runner" for console runner access
   - **Description**: Optional description
   - **Expiration**: Optional expiration date

#### Step 2: Use the API Key

```bash
# Set API key for authentication
automaton-runner auth --api-key ak_runner_1234567890abcdef --server https://localhost:7091

# Register and run the runner
automaton-runner register --name "Production-Runner"
automaton-runner run --name "Production-Runner"
```

#### Environment Variable Method (Best for Automation)

```bash
# Set environment variable
export AUTOMATON_API_KEY="ak_runner_1234567890abcdef"

# Run directly without auth command
automaton-runner run --name "Production-Runner" --server https://localhost:7091
```

## Usage

### Quick Start

1. **Get an API key from Automaton Studio**
2. **Authenticate:**
   ```bash
   automaton-runner auth --api-key ak_runner_xxxxxxxxxxxx --server https://localhost:7091
   ```

3. **Register the runner:**
   ```bash
   automaton-runner register --name "My-Runner" --server https://localhost:7091
   ```

4. **Start the runner:**
   ```bash
   automaton-runner run --name "My-Runner"
   ```

### Help System

The console runner includes a built-in help system to guide you through available commands and options.

#### Getting Help

```bash
# Show general help and all available commands
automaton-runner --help

# Show help for a specific command
automaton-runner <command> --help
```

**Examples:**
```bash
# General help
automaton-runner --help
automaton-runner -h

# Command-specific help
automaton-runner run --help
automaton-runner auth --help
automaton-runner register --help
automaton-runner status --help
automaton-runner cleanup --help
```

#### Version Information

```bash
# Show version information
automaton-runner --version
```

#### Global Options

The following options are available for all commands:

- `--version`: Show version information
- `--help`, `-h`, `-?`: Show help and usage information

### Commands

#### `auth` - Authenticate with API Key

```bash
automaton-runner auth --api-key <api-key> [--server <url>]
```

**Options:**
- `--api-key`: API key for authentication (required)
- `--server`: Server URL (optional, uses config default if not specified)

**Example:**
```bash
automaton-runner auth --api-key ak_runner_1234567890abcdef --server https://localhost:7091
```

#### `register` - Register Runner with Server

**?? Authentication Required**: This command requires an API key to be set via `auth` command or `AUTOMATON_API_KEY` environment variable.

```bash
automaton-runner register --name <runner-name> [--server <url>]
```

**Options:**
- `--name`: Runner name (required) - will appear in the Automaton Studio UI
- `--server`: Server URL (optional)

**Prerequisites:**
1. **Set API key first** (one of these methods):
   ```bash
   # Method 1: Use auth command (stores key locally)
   automaton-runner auth --api-key ak_runner_xxxxxxxxxxxx --server https://localhost:7091
   
   # Method 2: Set environment variable (good for automation)
   export AUTOMATON_API_KEY="ak_runner_xxxxxxxxxxxx"
   ```

2. **Then register the runner**:
   ```bash
   automaton-runner register --name "Production-Server-01" --server https://localhost:7091
   ```

**Example:**
```bash
# Complete registration flow
automaton-runner auth --api-key ak_runner_1234567890abcdef --server https://localhost:7091
automaton-runner register --name "Production-Server-01"
```

#### `run` - Start the Runner

```bash
automaton-runner run --name <runner-name> [--server <url>]
```

**Options:**
- `--name`: Runner name (required)
- `--server`: Server URL (optional)

**Example:**
```bash
automaton-runner run --name "Production-Server-01"
```

**Interactive Commands (while running):**

While the runner is active, you can use these keyboard shortcuts:

- **`q`**: Quit the runner gracefully
  - Stops execution of any current workflows
  - Disconnects from the server
  - Exits the application cleanly

- **`s`**: Show status information
  - Displays current connection status
  - Shows runner registration details
  - Provides authentication status
  - Lists any active workflows

**Usage:**
```bash
automaton-runner run --name "My-Runner"
# Runner starts and shows connection status
# Press 's' to view detailed status
# Press 'q' to quit when done
```

#### `status` - Show Configuration and Status

```bash
automaton-runner status
```

Shows current runner configuration, authentication status, and registration status.

#### `cleanup` - Cleanup Stored Data

```bash
automaton-runner cleanup
```

Removes stored API key and runner registration data from local storage. Useful for:
- Switching between different API keys
- Clearing cached runner registrations
- Preparing for fresh deployment

**Note**: This command does not affect environment variables.

## Configuration

### Configuration File

The runner uses `appsettings.json` for configuration:

```json
{
  "AppConfig": {
    "ApplicationType": "ConsoleRunner",
    "BaseUrl": "https://localhost:7091",
    "WorkflowHubUrl": "api/workflow/hub",
    "FlowsUrl": "api/flows",
    "RunnersUrl": "api/runners",
    "LogsUrl": "api/logs",
    "FlowExecutionUrl": "api/flowexecutions"
  }
}
```

**Note**: The default server URL is `https://localhost:7091`. If your Automaton Studio Server runs on a different port, you can:
- Override it via command line: `--server https://localhost:YOUR_PORT`  
- Set environment variable: `AppConfig__BaseUrl=https://localhost:YOUR_PORT`
- Modify the `appsettings.json` file directly

### Storage Locations

The runner stores configuration in:

- **Windows**: `%APPDATA%\AutomatonRunner\`
- **Linux/macOS**: `~/.config/AutomatonRunner/` or `$XDG_CONFIG_HOME/AutomatonRunner/`

Files stored:
- `apikey.json`: API key credentials
- `runner.json`: Runner registration information

## Environment Variables

You can override configuration using environment variables:

### Core Environment Variables

- **`AUTOMATON_API_KEY`**: API key for authentication (most commonly used)
- **`AppConfig__BaseUrl`**: Server base URL (e.g., `https://localhost:7091`)
- **`AppConfig__ApplicationType`**: Application type identifier (default: `ConsoleRunner`)

### Advanced Configuration Variables

You can override any configuration from `appsettings.json` using environment variables with the double underscore (`__`) syntax:

- **`AppConfig__WorkflowHubUrl`**: Workflow SignalR hub URL (default: `api/workflow/hub`)
- **`AppConfig__FlowsUrl`**: Flows API endpoint (default: `api/flows`)
- **`AppConfig__RunnersUrl`**: Runners API endpoint (default: `api/runners`)
- **`AppConfig__LogsUrl`**: Logs API endpoint (default: `api/logs`)
- **`AppConfig__FlowExecutionUrl`**: Flow execution API endpoint (default: `api/flowexecutions`)

### Logging Configuration

Control logging behavior:

- **`Serilog__MinimumLevel__Default`**: Set minimum log level (`Debug`, `Information`, `Warning`, `Error`)
- **`Logging__LogLevel__Default`**: Alternative logging level configuration

### Examples

```bash
# Basic usage with API key
export AUTOMATON_API_KEY="ak_runner_1234567890abcdef"
export AppConfig__BaseUrl="https://automaton.company.com"

# Advanced configuration
export AppConfig__ApplicationType="Production-ConsoleRunner"
export Serilog__MinimumLevel__Default="Warning"

# Run with environment variables
automaton-runner run --name "Production-Runner"
```

## Production Deployment

### Method 1: API Key with Environment Variables (Recommended)

```bash
# Set environment variables
export AUTOMATON_API_KEY="ak_runner_1234567890abcdef"
export AUTOMATON_SERVER_URL="https://automaton.company.com"

# Deploy and run
./automaton-runner run --name "Production-Server-01" --server $AUTOMATON_SERVER_URL
```

### Method 2: Containerized Deployment

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0
COPY . /app
WORKDIR /app
ENV AUTOMATON_API_KEY=""
ENV AUTOMATON_SERVER_URL="https://localhost:7091"
ENTRYPOINT ["./automaton-runner", "run", "--name", "Container-Runner"]
```

```bash
docker run -e AUTOMATON_API_KEY="ak_runner_xxx" -e AUTOMATON_SERVER_URL="https://server.com" automaton-runner
```

### Method 3: Systemd Service (Linux)

```ini
[Unit]
Description=Automaton Runner
After=network.target

[Service]
Type=simple
User=automaton
WorkingDirectory=/opt/automaton-runner
Environment=AUTOMATON_API_KEY=ak_runner_1234567890abcdef
ExecStart=/opt/automaton-runner/automaton-runner run --name "Production-Server-Linux-01"
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

## Logging

The console runner uses Serilog for structured logging with the following features:

### Log Output
- **Console**: Colored output with timestamp and log level
- **File**: Daily rotating log files in the `logs/` directory

### Log Files
- **Location**: `logs/automaton-runner-YYYY-MM-DD.log`
- **Retention**: Files are kept for 7 days and then automatically deleted
- **Format**: Timestamped entries with full exception details

### Log Levels
Available log levels (from most to least verbose):
- `Debug`: Detailed diagnostic information
- `Information`: General application flow (default)
- `Warning`: Potentially harmful situations
- `Error`: Error events that don't stop the application
- `Fatal`: Critical errors that cause application termination

### Configuration
Configure logging in `appsettings.json` under the `Serilog` section:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

Or use environment variables:
```bash
export Serilog__MinimumLevel__Default="Debug"
export Serilog__MinimumLevel__Override__Microsoft="Error"
```

## Examples

### CI/CD Pipeline Example

```yaml
# GitHub Actions example
name: Deploy Automaton Runner
on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Deploy Runner
        env:
          AUTOMATON_API_KEY: ${{ secrets.AUTOMATON_API_KEY }}
        run: |
          ./automaton-runner register --name "CI-Runner-${{ github.run_number }}" --server https://automaton.company.com
          ./automaton-runner run --name "CI-Runner-${{ github.run_number }}" --server https://automaton.company.com
```

### Docker Compose Example

```yaml
version: '3.8'
services:
  automaton-runner:
    image: automaton-runner:latest
    environment:
      - AUTOMATON_API_KEY=ak_runner_1234567890abcdef
      - AUTOMATON_SERVER_URL=https://automaton.company.com
    command: ["run", "--name", "Docker-Runner", "--server", "${AUTOMATON_SERVER_URL}"]
    restart: unless-stopped
```

## Troubleshooting

### Common Issues

1. **Authentication Failed**
   - Verify API key is correct and not expired
   - Check API key scope (should be "Runner" for console runner)
   - Ensure server URL is correct
   - Check that the key starts with `ak_` and is at least 10 characters long

2. **Connection Failed**
   - Check network connectivity to server
   - Verify SignalR hub is enabled on server
   - Check firewall settings

3. **Runner Not Appearing in Studio**
   - Ensure runner is registered successfully
   - Check that runner is connected (use `s` command while running)
   - Verify user has permission to see runners

4. **Command Not Found**
   - Make sure you're using the correct executable name: `automaton-runner`
   - Verify the build/publish process completed successfully
   - Check if the executable has proper permissions (Linux/macOS: `chmod +x automaton-runner`)

5. **Help Not Displaying**
   - Try different help options: `--help`, `-h`, or `-?`
   - Ensure you're using the correct command syntax
   - For command-specific help, use: `automaton-runner <command> --help`

### API Key Issues
- **Invalid format**: API keys should start with `ak_` and be at least 10 characters long
- **Expired key**: Check expiration date in Automaton Studio
- **Revoked key**: Verify the key is still active in the API Keys page
- **Wrong scope**: Ensure the key has "Runner" scope for console runner operations

### Reset Configuration

To start fresh:
```bash
# Remove all stored authentication and runner data
rm -rf ~/.config/AutomatonRunner/  # Linux/macOS
# or
rmdir /s %APPDATA%\AutomatonRunner  # Windows
```

## API Key Scopes

Different API key formats may be used depending on your Automaton Studio configuration:

| Example Format | Description | Use Case |
|----------------|-------------|----------|
| **ak_runner_xxx** | Traditional runner keys | Console runners, automation agents |
| **ak_execute_xxx** | Traditional execution keys | CI/CD pipelines, external integrations |
| **ak_readonly_xxx** | Traditional read-only keys | Monitoring, reporting tools |
| **ak_xxxxxxxxx** | Flexible format keys | Any scope (determined by server configuration) |

**Note**: The console runner now accepts any API key starting with `ak_` and at least 10 characters long. The specific scope and permissions are determined by your Automaton Studio server configuration.

## Architecture

The console runner consists of:

- **API Key Authentication Service**: Handles API key validation and management
- **Hub Service**: SignalR connection to receive workflow execution requests  
- **Flow Execution Service**: Executes received workflows using Automaton.Core
- **Storage Service**: File-based storage for runner configuration
- **Configuration Service**: Manages application configuration

## Comparison with GUI Runner

| Feature | GUI Runner | Console Runner |
|---------|------------|----------------|
| Platform | Windows only (WPF) | Cross-platform |
| Dependencies | WPF, Blazor WebView | None (console only) |
| Deployment | Interactive installation | Copy and run |
| Automation | Manual start | Scriptable/Service |
| Resource Usage | Higher (UI overhead) | Minimal |
| Configuration | UI-based | Command-line/Config files |
| Authentication | API keys | API keys |

The console runner is ideal for:
- Server deployments
- Docker containers
- Automated deployments
- CI/CD pipelines
- Linux/macOS environments
- Headless servers
- Production automation

## Quick Reference

### Essential Commands
```bash
# Show all available commands and options
automaton-runner --help

# Show version
automaton-runner --version

# Authenticate (store API key locally)
automaton-runner auth --api-key ak_runner_xxxxxxxxxxxx --server https://localhost:7091

# Register runner with server
automaton-runner register --name "My-Runner" --server https://localhost:7091

# Start runner
automaton-runner run --name "My-Runner"

# Check status
automaton-runner status

# Clean up stored data
automaton-runner cleanup
```

### Quick Setup
```bash
# 1. Set API key (choose one method)
automaton-runner auth --api-key ak_runner_xxxxxxxxxxxx --server https://localhost:7091
# OR
export AUTOMATON_API_KEY="ak_runner_xxxxxxxxxxxx"

# 2. Register and run
automaton-runner register --name "My-Runner" --server https://localhost:7091
automaton-runner run --name "My-Runner"
```

### Interactive Commands (while running)
- **`s`**: Show detailed status
- **`q`**: Quit gracefully