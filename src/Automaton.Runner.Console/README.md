# Automaton Runner Console

A cross-platform command-line runner for the Automaton workflow automation system.

## Features

- **Cross-platform**: Runs on Windows, Linux, and macOS
- **No UI dependencies**: Pure console application
- **Authentication**: Secure login via email/password
- **Auto-reconnection**: Handles network interruptions gracefully
- **File-based storage**: Credentials and runner config stored securely in user profile
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

## Usage

### Quick Start

1. **Login to your Automaton server:**
   ```bash
   automaton-runner login --server https://localhost:7091
   ```
   *Note: You'll be prompted for your email address and password*

2. **Register the runner:**
   ```bash
   automaton-runner register --name "My-Production-Runner" --server https://localhost:7091
   ```

3. **Start the runner:**
   ```bash
   automaton-runner run --name "My-Production-Runner"
   ```

### Commands

#### `login` - Authenticate with Automaton Server

```bash
automaton-runner login [--server <url>]
```

**Options:**
- `--server`: Server URL (optional, uses config default if not specified)

**Example:**
```bash
automaton-runner login --server https://localhost:7091
# You will be prompted for:
# Email: your.email@example.com
# Password: ********
```

#### `register` - Register Runner with Server

```bash
automaton-runner register --name <runner-name> [--server <url>]
```

**Options:**
- `--name`: Runner name (required) - will appear in the Automaton Studio UI
- `--server`: Server URL (optional)

**Example:**
```bash
automaton-runner register --name "Production-Server-01" --server https://localhost:7091
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
- `q`: Quit the runner
- `s`: Show status information

#### `status` - Show Configuration and Status

```bash
automaton-runner status
```

Shows current runner configuration, authentication status, and registration status.

## Configuration

### Configuration File

The runner uses `appsettings.json` for configuration:

```json
{
  "AppConfig": {
    "ApplicationType": "ConsoleRunner",
    "BaseUrl": "https://localhost:7064",
    "WorkflowHubUrl": "api/workflow/hub",
    "FlowsUrl": "api/flows",
    "RunnersUrl": "api/runners",
    "LogsUrl": "api/logs",
    "FlowExecutionUrl": "api/flowexecutions"
  },
  "ClientAuthConfig": {
    "LoginUserUrl": "api/account/loginuser",
    "RefreshAccessTokenUrl": "api/token/refreshaccesstoken",
    "RefreshTokenExpirationMinutesCheck": 2
  }
}
```

### Storage Locations

The runner stores configuration and credentials in:

- **Windows**: `%APPDATA%\AutomatonRunner\`
- **Linux/macOS**: `~/.config/AutomatonRunner/` or `$XDG_CONFIG_HOME/AutomatonRunner/`

Files stored:
- `credentials.json`: Authentication tokens
- `runner.json`: Runner registration information

## Environment Variables

You can override configuration using environment variables:

- `AppConfig__BaseUrl`: Server base URL
- `AppConfig__ApplicationType`: Application type identifier

## Logging

Logs are written to:
- Console (with colored output)
- `logs/automaton-runner-YYYY-MM-DD.log` files (rotated daily, kept for 7 days)

Log levels can be configured in `appsettings.json` under the `Serilog` section.

## Examples

### Production Deployment

1. **Deploy on a server:**
   ```bash
   # Copy the published files to your server
   scp -r bin/Release/net8.0/publish/* user@server:/opt/automaton-runner/

   # SSH to server
   ssh user@server

   # Navigate to runner directory
   cd /opt/automaton-runner

   # Login (first time only)
   ./automaton-runner login --server https://automaton.company.com

   # Register (first time only)
   ./automaton-runner register --name "Production-Server-Linux-01"

   # Start runner
   ./automaton-runner run --name "Production-Server-Linux-01"
   ```

2. **Create a systemd service (Linux):**
   ```ini
   [Unit]
   Description=Automaton Runner
   After=network.target

   [Service]
   Type=simple
   User=automaton
   WorkingDirectory=/opt/automaton-runner
   ExecStart=/opt/automaton-runner/automaton-runner run --name "Production-Server-Linux-01"
   Restart=always
   RestartSec=10

   [Install]
   WantedBy=multi-user.target
   ```

### Development Usage

```bash
# Use local development server
automaton-runner login --server https://localhost:7091
automaton-runner register --name "Dev-Machine-Runner"
automaton-runner run --name "Dev-Machine-Runner"
```

## Troubleshooting

### Common Issues

1. **Authentication Failed**
   - Verify server URL is correct
   - Check your email address and password
   - Ensure server is accessible
   - **Important**: Make sure you're using the correct server port (e.g., https://localhost:7091)
   - **Note**: Use your email address, not username, when prompted for credentials

2. **Connection Failed**
   - Check network connectivity to server
   - Verify SignalR hub is enabled on server
   - Check firewall settings

3. **Runner Not Appearing in Studio**
   - Ensure runner is registered successfully
   - Check that runner is connected (use `s` command while running)
   - Verify user has permission to see runners

### Authentication Requirements

The console runner uses email-based authentication:
- **Login**: Requires your email address and password
- **Server Model**: Uses `SignInUserCommand` with `Email` and `Password` fields
- **Endpoints**: 
  - Login: `api/account/loginuser`
  - Token Refresh: `api/token/refreshaccesstoken`

### Reset Configuration

To start fresh:
```bash
# Remove stored credentials and registration
rm -rf ~/.config/AutomatonRunner/  # Linux/macOS
# or
rmdir /s %APPDATA%\AutomatonRunner  # Windows
```

## Architecture

The console runner consists of:

- **Authentication Service**: Handles login/logout and token management
- **Hub Service**: SignalR connection to receive workflow execution requests
- **Flow Execution Service**: Executes received workflows using Automaton.Core
- **Storage Service**: File-based storage for credentials and configuration
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

The console runner is ideal for:
- Server deployments
- Docker containers
- Automated deployments
- CI/CD pipelines
- Linux/macOS environments
- Headless servers