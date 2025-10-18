# Automaton Runner - Service Setup Scripts

This directory contains standalone scripts for setting up the Automaton Runner as a system service.

## Overview

These scripts provide an easy way to configure the Automaton Runner as a system service **after** the runner has been installed. They complement the installation scripts by providing a dedicated service setup workflow.

## Scripts

### `setup-service.ps1` (Windows)

PowerShell script for setting up the runner as a Windows service using NSSM (Non-Sucking Service Manager).

**Requirements:**
- PowerShell 5.0 or later
- Administrator privileges
- Automaton Runner already installed

**Usage:**
```powershell
# Recommended: Two-step workflow
# Step 1: Configure runner interactively (do this first!)
automaton-runner
# ↑ Prompts for API key, server URL, runner name and saves everything

# Step 2: Convert to service (detects configuration automatically!)
.\setup-service.ps1
# ↑ No parameters needed - just confirm detected configuration!

# Alternative: Provide configuration manually
.\setup-service.ps1 -ApiKey "ak_runner_xxx" -ServerUrl "https://automaton.company.com" -RunnerName "prod-01"

# Custom runner location
.\setup-service.ps1 -RunnerPath "C:\Tools\AutomatonRunner\automaton-runner.exe"

# Uninstall service
.\setup-service.ps1 -Uninstall
```

**Features:**
- ✅ Automatically downloads and installs NSSM if needed
- ✅ Creates Windows service with automatic startup
- ✅ Configures log rotation (10MB max per log file)
- ✅ Sets up environment variables for API key and server URL
- ✅ **Detects existing runner configuration** and offers to use it
- ✅ Interactive prompts for confirmation and service start

### `setup-service.sh` (Linux/macOS)

Bash script for setting up the runner as a system service using systemd (Linux) or launchd (macOS).

**Requirements:**
- sudo privileges
- systemd (Linux) or launchd (macOS)
- Automaton Runner already installed

**Usage:**
```bash
# Recommended: Two-step workflow
# Step 1: Configure runner interactively (do this first!)
automaton-runner
# ↑ Prompts for API key, server URL, runner name and saves everything

# Step 2: Convert to service (detects configuration automatically!)
sudo ./setup-service.sh
# ↑ No parameters needed - just confirm detected configuration!

# Alternative: Provide configuration manually
sudo ./setup-service.sh --api-key "ak_runner_xxx" --server-url "https://automaton.company.com" --runner-name "prod-01"

# Custom runner location
sudo ./setup-service.sh --runner-path "/opt/automaton/automaton-runner"

# Uninstall service
sudo ./setup-service.sh --uninstall
```

**Features:**
- ✅ Auto-detects platform (Linux/macOS)
- ✅ Creates systemd service (Linux) or launchd service (macOS)
- ✅ Configures automatic startup and restart on failure
- ✅ Sets up environment variables for API key and server URL
- ✅ **Detects existing runner configuration** and offers to use it
- ✅ Interactive prompts for confirmation and service start

## When to Use These Scripts

**Use standalone service setup scripts when:**
- ✅ The runner is already installed and you want to set it up as a service
- ✅ You want to reconfigure an existing service
- ✅ You need to set up a service on a different machine where the runner is already present
- ✅ You prefer a dedicated service setup workflow

**Use installation scripts with `--create-service` when:**
- ✅ You're doing a fresh installation and want both runner and service in one step
- ✅ You're automating deployments with a single command

## Smart Configuration Detection

The scripts are intelligent about existing runner configurations:

**Scenario 1: Runner Already Configured** (Most Common) ✨
```bash
# Step 1: Run the runner interactively first
automaton-runner
# → Prompts for API key, validates it, and AUTOMATICALLY sets AUTOMATON_API_KEY env var
# → Prompts for server URL and saves to ~/.config/AutomatonRunner/appsettings.json
# → Prompts for runner name and registers with the server

# Step 2: Convert to service - that's it!
sudo ./setup-service.sh
# ✅ Script detects: Server URL from ~/.config/AutomatonRunner/appsettings.json
# ✅ Script detects: API Key from AUTOMATON_API_KEY environment variable
# ✅ Prompts: "Use detected configuration?" (just press Enter!)
# ✅ Result: Service uses your existing setup - ZERO re-entry! 🎉
```

**Scenario 2: Fresh Service Setup**
```bash
# Provide configuration directly as parameters
sudo ./setup-service.sh --api-key "ak_xxx" --server-url "https://server.com"
# ✅ Uses provided parameters
# ✅ No prompts needed
```

**Why This Matters:**
- 🎯 **Zero re-entry**: Runner automatically persists API key to environment variable
- ⚡ **Two-step workflow**: 1) Run interactively to configure, 2) Convert to service
- 🔒 **Secure**: API key stored in environment variable (User-level on Windows, shell profile on Linux/macOS)
- 🛡️ **Safe**: Always asks for confirmation before using detected config
- 💡 **Smart**: Detects BOTH server URL AND API key automatically
- 🚀 **Production-ready**: Test interactively, then deploy as service with one command

## Service Management

After running the setup scripts, you can manage the service using platform-specific commands:

**Windows:**
```powershell
Start-Service AutomatonRunner
Stop-Service AutomatonRunner
Get-Service AutomatonRunner
```

**Linux:**
```bash
sudo systemctl start automaton-runner
sudo systemctl stop automaton-runner
sudo systemctl status automaton-runner
sudo journalctl -u automaton-runner -f  # View logs
```

**macOS:**
```bash
sudo launchctl load /Library/LaunchDaemons/com.automaton.runner.plist
sudo launchctl unload /Library/LaunchDaemons/com.automaton.runner.plist
sudo launchctl list | grep automaton
tail -f /var/log/automaton-runner.log  # View logs
```

## Documentation

For detailed manual setup instructions, troubleshooting, and advanced configuration, see:
- [Running Automaton Runner as a System Service](../docs/runner-as-service.md)

## Security Best Practices

1. **API Keys**: 
   - ✅ **Automatically persisted**: Runner sets AUTOMATON_API_KEY environment variable when you configure it
   - ✅ **User-level on Windows**: Stored in user environment variables (persistent across sessions)
   - ✅ **Shell profile on Linux/macOS**: Runner guides you to add to ~/.bashrc or ~/.zshrc
   - ✅ **No plaintext files**: More secure than file-based storage
   - ✅ **Easy to rotate**: Just run `automaton-runner auth` again with a new key
   - ⚠️ Never hardcode API keys in scripts or commit them to version control
2. **File Permissions**: Service configuration files should have restricted permissions (600 or 644).
3. **User Context**: Run services with least privilege (dedicated service account when possible).
4. **Logging**: Monitor service logs regularly for errors and security events.
5. **Updates**: Keep the runner and service management tools up to date.

## Troubleshooting

**Service won't start:**
- Check logs: Windows Event Viewer, `journalctl`, or `/var/log/`
- Verify runner path is correct
- Ensure API key and server URL are valid
- Check file permissions

**Service crashes/restarts:**
- Review service logs for error messages
- Verify network connectivity to server
- Check API key hasn't been revoked
- Ensure sufficient system resources

**Need to change configuration:**
- Windows: Edit with `nssm edit AutomatonRunner`
- Linux: Edit `/etc/systemd/system/automaton-runner.service` and run `sudo systemctl daemon-reload`
- macOS: Edit `/Library/LaunchDaemons/com.automaton.runner.plist` and reload

## Support

For issues or questions:
- Documentation: https://docs.automaton.monster
- GitHub Issues: https://github.com/rzvdaniel/automaton/issues
- Community: https://github.com/rzvdaniel/automaton/discussions
