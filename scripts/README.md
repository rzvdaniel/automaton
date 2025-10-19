# Automaton Runner - Service Setup Scripts

This directory contains standalone scripts for setting up the Automaton Runner as a system service.

## Overview

These scripts provide an easy way to configure the Automaton Runner as a system service **after** the runner has been installed. They automatically detect your existing runner configuration, making service setup a simple one-command operation.

## Scripts

### `setup-service.ps1` (Windows)

PowerShell script for setting up the runner as a Windows service using NSSM (Non-Sucking Service Manager).

**Requirements:**
- PowerShell 5.0 or later
- Administrator privileges
- Automaton Runner already installed

**Usage:**

**✨ Recommended Workflow (Zero Configuration Required):**
```powershell
# Step 1: Run the runner interactively (ONE TIME SETUP)
automaton-runner
# → Prompts for API key, validates it, and saves to AUTOMATON_API_KEY environment variable
# → Prompts for server URL and saves to user configuration
# → Prompts for runner name and registers with the server

# Step 2: Convert to Windows service (AUTOMATIC DETECTION!)
.\scripts\setup-service.ps1
# ✅ Auto-detects API key from user environment variable
# ✅ Auto-detects server URL from saved configuration
# ✅ Prompts: "Use detected configuration?" → Just press Enter!
# ✅ Creates service with proper environment variables
# ✅ Asks if you want to start the service now

# That's it! Your runner is now a Windows service. 🎉
```

**Advanced Options (If Needed):**
```powershell
# Specify custom runner installation path (if not in default location)
.\scripts\setup-service.ps1 -RunnerPath "C:\CustomPath\automaton-runner.exe"

# The script will also prompt interactively if it can't find the runner at the default location

# Override auto-detected configuration (not recommended)
.\scripts\setup-service.ps1 -ApiKey "ak_runner_xxx" -ServerUrl "https://automaton.company.com"

# Uninstall the service
.\scripts\setup-service.ps1 -Uninstall

# Show help
.\scripts\setup-service.ps1 -Help
```

**Service Management:**
```powershell
# Start the service
Start-Service AutomatonRunner

# Stop the service
Stop-Service AutomatonRunner

# Check service status
Get-Service AutomatonRunner

# View service logs
Get-Content "$env:ProgramFiles\AutomatonRunner\logs\service-output.log" -Tail 50 -Wait
```

**Features:**
- ✅ **Smart Auto-Detection**: Automatically finds API key and server URL from your configuration
- ✅ **Zero Re-Entry**: Uses credentials you already configured interactively
- ✅ **NSSM Integration**: Downloads and installs NSSM automatically if needed
- ✅ **Service-Level Environment**: Passes API key via NSSM (accessible to SYSTEM account)
- ✅ **Correct Working Directory**: Automatically sets working directory so runner can find config files
- ✅ **Automatic Startup**: Service starts with Windows
- ✅ **Log Rotation**: 10MB max per log file with automatic rotation
- ✅ **Interactive Prompts**: Confirms configuration before proceeding
- ✅ **Debug-Friendly**: Works with debug builds in project folders or production installs

**Important Note for Windows:**
Windows services run under the SYSTEM account and cannot access user-level environment variables. This script automatically configures the API key as a service-level environment variable via NSSM, ensuring the runner can connect to Automaton Studio. The working directory is also set to the runner's location so it can find `appsettings.json` and other configuration files.

### `setup-service.sh` (Linux/macOS)

Bash script for setting up the runner as a system service using systemd (Linux) or launchd (macOS).

**Requirements:**
- sudo privileges
- systemd (Linux) or launchd (macOS)
- Automaton Runner already installed

**Usage:**

**✨ Recommended Workflow (Zero Configuration Required):**
```bash
# Step 1: Run the runner interactively (ONE TIME SETUP)
automaton-runner
# → Prompts for API key, validates it, and saves to AUTOMATON_API_KEY environment variable
# → Prompts for server URL and saves to user configuration
# → Prompts for runner name and registers with the server

# Step 2: Convert to system service (AUTOMATIC DETECTION!)
sudo ./scripts/setup-service.sh
# ✅ Auto-detects API key from your environment variable
# ✅ Auto-detects server URL from saved configuration
# ✅ Prompts: "Use detected configuration?" → Just press Enter!
# ✅ Creates systemd/launchd service with environment variables
# ✅ Asks if you want to start the service now

# That's it! Your runner is now a system service. 🎉
```

**Advanced Options (If Needed):**
```bash
# Specify custom runner installation path (if not in default location)
sudo ./scripts/setup-service.sh --runner-path "/opt/automaton/automaton-runner"

# The script will also prompt interactively if it can't find the runner at the default location

# Override auto-detected configuration (not recommended)
sudo ./scripts/setup-service.sh --api-key "ak_runner_xxx" --server-url "https://automaton.company.com"

# Uninstall the service
sudo ./scripts/setup-service.sh --uninstall

# Show help
./scripts/setup-service.sh --help
```

**Service Management (Linux - systemd):**
```bash
# Start the service
sudo systemctl start automaton-runner

# Stop the service
sudo systemctl stop automaton-runner

# Check service status
sudo systemctl status automaton-runner

# Enable automatic start at boot (already done by setup script)
sudo systemctl enable automaton-runner

# View live logs
sudo journalctl -u automaton-runner -f

# View recent logs
sudo journalctl -u automaton-runner --since "1 hour ago"
```

**Service Management (macOS - launchd):**
```bash
# Start the service
sudo launchctl load /Library/LaunchDaemons/com.automaton.runner.plist

# Stop the service
sudo launchctl unload /Library/LaunchDaemons/com.automaton.runner.plist

# Check if service is running
sudo launchctl list | grep automaton

# View logs
tail -f /var/log/automaton-runner.log

# View error logs
tail -f /var/log/automaton-runner-error.log
```

**Features:**
- ✅ **Smart Auto-Detection**: Automatically finds API key and server URL from your configuration
- ✅ **Zero Re-Entry**: Uses credentials you already configured interactively
- ✅ **Platform Auto-Detection**: Automatically detects Linux (systemd) or macOS (launchd)
- ✅ **Service-Level Environment**: Configures environment variables in service file
- ✅ **Correct Working Directory**: Automatically sets working directory so runner can find config files
- ✅ **Automatic Startup**: Service starts on system boot
- ✅ **Auto-Restart**: Restarts automatically on failure (10-second delay)
- ✅ **Centralized Logging**: systemd journal (Linux) or /var/log (macOS)
- ✅ **Interactive Prompts**: Confirms configuration before proceeding
- ✅ **Debug-Friendly**: Works with custom paths for testing or production installs

## How Smart Auto-Detection Works

### What Gets Auto-Detected?

1. **API Key** 🔑
   - **Windows**: Reads from `AUTOMATON_API_KEY` user environment variable
   - **Linux/macOS**: Reads from `AUTOMATON_API_KEY` environment variable
   - Set automatically when you run `automaton-runner` and configure your API key

2. **Server URL** 🌐
   - Reads from: `~/.config/AutomatonRunner/appsettings.json` (or `%APPDATA%\AutomatonRunner` on Windows)
   - Saved automatically when you run `automaton-runner` and configure your server

3. **Runner Registration** 📋
   - Reads from: `~/.config/AutomatonRunner/runner-config.json`
   - Created automatically when you register your runner

### Why This Workflow Works Perfectly

**The Two-Step Process:**

```
┌─────────────────────────────────────────────────────────┐
│ Step 1: Interactive Setup (ONE TIME)                    │
├─────────────────────────────────────────────────────────┤
│ $ automaton-runner                                      │
│                                                          │
│ ✅ Validates API key with server                        │
│ ✅ Sets AUTOMATON_API_KEY environment variable          │
│ ✅ Saves server URL to config file                      │
│ ✅ Registers runner with Automaton Studio               │
│ ✅ Tests connection and starts runner                   │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│ Step 2: Service Conversion (ONE COMMAND)                │
├─────────────────────────────────────────────────────────┤
│ Windows: .\scripts\setup-service.ps1                    │
│ Linux:   sudo ./scripts/setup-service.sh                │
│                                                          │
│ ✅ Auto-detects API key from environment                │
│ ✅ Auto-detects server URL from config                  │
│ ✅ Prompts for confirmation                             │
│ ✅ Creates service with proper environment              │
│ ✅ Starts service (if confirmed)                        │
└─────────────────────────────────────────────────────────┘
                          ↓
              🎉 Runner is now a service!
```

**Benefits:**
- 🎯 **Test First**: Verify everything works interactively before making it a service
- 🔒 **Secure**: API key never typed in multiple places or stored in plaintext
- ⚡ **Fast**: Two commands total - that's it!
- 🛡️ **Safe**: Always confirms detected configuration before proceeding
- 🚀 **Production-Ready**: What works interactively will work as a service

## When to Use These Scripts

**Use standalone service setup scripts when:**
- ✅ The runner is already installed and configured
- ✅ You want to test the runner interactively first (recommended!)
- ✅ You need to reconfigure an existing service
- ✅ You prefer a dedicated two-step workflow

**Use installation scripts with `--create-service` when:**
- ✅ You're doing a fresh installation and have all credentials ready
- ✅ You're automating deployments with Infrastructure as Code
- ✅ You want a single-command installation (less common)

## Troubleshooting

### Service Won't Start

**Windows:**
```powershell
# Check service logs
Get-Content "$env:ProgramFiles\AutomatonRunner\logs\service-error.log"

# Check NSSM configuration
& "$env:ProgramFiles\AutomatonRunner\nssm.exe" get AutomatonRunner AppEnvironmentExtra

# Verify API key is set for service
# Should show: AUTOMATON_API_KEY=ak_...
```

**Linux:**
```bash
# Check service status
sudo systemctl status automaton-runner

# View detailed logs
sudo journalctl -u automaton-runner -n 100 --no-pager

# Check service file
cat /etc/systemd/system/automaton-runner.service | grep -A 5 Environment
```

**macOS:**
```bash
# Check if service is loaded
sudo launchctl list | grep automaton

# View logs
cat /var/log/automaton-runner-error.log

# Check plist file
sudo cat /Library/LaunchDaemons/com.automaton.runner.plist
```

### Common Issues

**"Runner not found at default location"**
- **Don't panic!** The script will interactively prompt you for the correct path
- Common locations to check:
  - Windows: `C:\Program Files\AutomatonRunner\automaton-runner.exe`
  - Linux: `/usr/local/bin/automaton-runner`
  - macOS: `/usr/local/bin/automaton-runner`
  - Custom: Check where you extracted or installed the runner
- Alternatively, run the script with an explicit path:
  - Windows: `.\scripts\setup-service.ps1 -RunnerPath "C:\YourPath\automaton-runner.exe"`
  - Linux/macOS: `sudo ./scripts/setup-service.sh --runner-path "/your/path/automaton-runner"`

**"No API key found"**
- Run `automaton-runner` first to configure your API key
- The runner will set the `AUTOMATON_API_KEY` environment variable automatically
