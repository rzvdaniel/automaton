# Automaton Runner - Service Setup Scripts

This directory contains scripts to install and configure the Automaton Runner as a system service.

## 📋 Overview

The service setup scripts provide:
- ✅ **Automated installation** - One-command service setup
- ✅ **Auto-detection** - Discovers existing configuration from user environment
- ✅ **Smart defaults** - Localhost detection, SSL bypass, service accounts
- ✅ **Comprehensive logging** - Detailed output with security (API keys redacted)
- ✅ **Interactive & non-interactive** - Supports both modes

---

## 🚀 Quick Start

### **Windows**

```powershell
# Run as Administrator (right-click PowerShell → "Run as Administrator")
cd C:\Work\automaton\scripts
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope Process
.\setup-service.ps1
```

**The script will:**
- ✅ Detect your existing configuration (API key, server URL, runner)
- ✅ Install NSSM (Non-Sucking Service Manager) if needed
- ✅ Create the Windows service
- ✅ Configure environment variables
- ✅ Prompt to start the service

### **Linux/macOS**

```bash
# Run with sudo
cd ~/automaton/scripts
sudo ./setup-service.sh
```

**The script will:**
- ✅ Detect your existing configuration (API key, server URL, runner)
- ✅ Detect platform (systemd on Linux, launchd on macOS)
- ✅ Create the system service
- ✅ Configure environment variables
- ✅ Prompt to start the service

---

## 📂 Available Scripts

| Script | Platform | Purpose |
|--------|----------|---------|
| `setup-service.ps1` | Windows | Install/uninstall Windows service |
| `setup-service.sh` | Linux/macOS | Install/uninstall systemd/launchd service |
| `test-service-debug.ps1` | Windows | Development testing (auto-reinstall) |
| `dev-reinstall-cli.ps1` | Windows/PowerShell | Pack and install/update/reinstall/uninstall the local Automaton CLI tool |

---

## Production installation and updates

Use `install-automaton.sh` for a fresh standalone server and the installed `update.sh` for subsequent releases. See [the installation guide](../docs/prod/install-automaton.md).

```bash
sudo bash /opt/automaton-server/update.sh
sudo bash /opt/automaton-server/install.sh --status
```

`host-storage.sh` provides storage inspection and bounded image maintenance. The retired source blue/green deployment helpers have been removed. The separately hosted marketing website is outside the server updater.

CLI releases remain independent: run [Pack CLI and Runner](../.github/workflows/nuget.yml) with an explicit version and publish enabled. See [CLI NuGet publishing](../docs/prod/cli-nuget-publishing.md).

---

## Local CLI Tool Workflow

Use `dev-reinstall-cli.ps1` when you want to install, update, or remove the locally packed `Automaton.Cli` global tool without touching any remote NuGet source.

```powershell
cd C:\Work\automaton\scripts

# Pack + install from .\.tmp\cli-pack
.\dev-reinstall-cli.ps1 -Action install

# Pack + update the already installed global tool
.\dev-reinstall-cli.ps1 -Action update

# Pack + force uninstall + reinstall
.\dev-reinstall-cli.ps1 -Action reinstall

# Remove the global tool
.\dev-reinstall-cli.ps1 -Action uninstall

# Show installed tool version and local package info
.\dev-reinstall-cli.ps1 -Action status
```

Notes:

- `install`, `update`, and `reinstall` pack the CLI from `src/Automaton.Cli`
- the script uses only the local package source under `.tmp\cli-pack`
- add `-SkipTests` if you want to skip `dotnet test` before packing

### Plain `dotnet` commands

If you prefer not to use the PowerShell helper, you can do the same work directly with `dotnet`.

From the repo root:

```powershell
cd C:\Work\automaton

# Optional: run the CLI tests first
dotnet test src\UnitTests\Automaton.Cli.Tests\Automaton.Cli.Tests.csproj

# Stamp a unique local package version
$localCliVersion = "1.0.0-local.$((Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')).$(git rev-parse --short HEAD)"

# Rebuild the local package source cleanly
Remove-Item .\.tmp\cli-pack -Recurse -Force -ErrorAction Ignore
dotnet pack src\Automaton.Cli\Automaton.Cli.csproj -c Release -o .\.tmp\cli-pack -p:Version=$localCliVersion
```

Install the local package as a global tool:

```powershell
dotnet tool install --global --add-source .\.tmp\cli-pack --version $localCliVersion Automaton.Cli
```

Update the already installed global tool from the local package source:

```powershell
dotnet tool update --global --add-source .\.tmp\cli-pack --version $localCliVersion Automaton.Cli
```

Force a clean reinstall:

```powershell
dotnet tool uninstall --global Automaton.Cli
dotnet tool install --global --add-source .\.tmp\cli-pack --version $localCliVersion Automaton.Cli
```

Uninstall the global tool:

```powershell
dotnet tool uninstall --global Automaton.Cli
```

Check what is installed:

```powershell
dotnet tool list --global
automaton --version
```

Important:

- clean `.tmp\cli-pack` before packing if you want predictable installs and updates
- the helper script and manual flow above both stamp a unique local package version for each pack
- `automaton --version` now shows that stamped package version as `Version` and the assembly metadata as `Build version`

---

## 🪟 Windows Service (`setup-service.ps1`)

### **Prerequisites**
- Windows 10/11 or Windows Server 2016+
- PowerShell 5.0 or later
- Administrator privileges
- Automaton Runner installed

### **Basic Usage**

```powershell
# Install with auto-detected configuration
.\setup-service.ps1

# Install with custom configuration
.\setup-service.ps1 -ApiKey "ak_runner_xxx" -ServerUrl "https://automaton.company.com"

# Install with custom runner location
.\setup-service.ps1 -RunnerPath "C:\Tools\AutomatonRunner\automaton-runner.exe"

# Uninstall service
.\setup-service.ps1 -Uninstall

# Show help
.\setup-service.ps1 -Help
```

### **Features**

- ✅ **Auto-detection** - Finds API key from `AUTOMATON_API_KEY` environment variable
- ✅ **Auto-detection** - Finds server URL and runner registration from `%APPDATA%\AutomatonRunner\`
- ✅ **NSSM integration** - Downloads and configures NSSM automatically
- ✅ **Smart service account** - Prompts to run as current user for localhost, LocalSystem for production
- ✅ **SSL bypass** - Automatically enables for localhost development
- ✅ **Log rotation** - 10MB max per log file with automatic rotation

### **Service Management**

```powershell
# Start service
Start-Service AutomatonRunner

# Stop service
Stop-Service AutomatonRunner

# Check status
Get-Service AutomatonRunner

# View logs
Get-Content "C:\Program Files\AutomatonRunner\logs\service-output.log" -Tail 50 -Wait
```

---

## 🐧 Linux/macOS Service (`setup-service.sh`)

### **Prerequisites**
- Linux with systemd (Ubuntu 18.04+, CentOS 7+, etc.) OR macOS 10.14+
- sudo privileges
- Automaton Runner installed

### **Basic Usage**

```bash
# Install with auto-detected configuration
sudo ./setup-service.sh

# Install with custom configuration
sudo ./setup-service.sh --api-key "ak_runner_xxx" --server-url "https://automaton.company.com"

# Install with custom runner location
sudo ./setup-service.sh --runner-path "/opt/automaton/automaton-runner"

# Uninstall service
sudo ./setup-service.sh --uninstall

# Show help
./setup-service.sh --help
```

### **Features**

- ✅ **Auto-detection** - Finds API key from `AUTOMATON_API_KEY` environment variable
- ✅ **Auto-detection** - Finds server URL and runner registration from `~/.config/AutomatonRunner/`
- ✅ **Platform detection** - Automatically uses systemd (Linux) or launchd (macOS)
- ✅ **SSL bypass** - Automatically enables for localhost development
- ✅ **Auto-restart** - Restarts automatically on failure

### **Service Management**

**Linux (systemd):**
```bash
# Start service
sudo systemctl start automaton-runner

# Stop service
sudo systemctl stop automaton-runner

# Check status
sudo systemctl status automaton-runner

# View logs
sudo journalctl -u automaton-runner -f
```

**macOS (launchd):**
```bash
# Start service
sudo launchctl load /Library/LaunchDaemons/com.automaton.runner.plist

# Stop service
sudo launchctl unload /Library/LaunchDaemons/com.automaton.runner.plist

# Check status
sudo launchctl list | grep automaton

# View logs
tail -f /var/log/automaton-runner.log
```

---

## 🔐 Security Features

### **API Key Protection**
- ✅ API keys are **never logged in full**
- ✅ Only first 15 characters shown: `ak_fullaccess_H***REDACTED***`
- ✅ Logs automatically redact API keys in output

### **SSL Certificate Handling**
- ✅ **Localhost**: Auto-detects and bypasses SSL validation (development mode)
- ✅ **Production**: Standard SSL validation (secure)

### **Service Account**
- ✅ **Windows Localhost**: Prompts to run as current user (required for localhost access)
- ✅ **Windows Production**: Runs as LocalSystem (isolated service account)
- ✅ **Linux**: Runs as specified user (default: install user)
- ✅ **macOS**: Runs as root (launchd standard)

---

## 📊 Configuration Summary

After installation, the script displays a comprehensive summary:

```
📋 Service Configuration Summary:
════════════════════════════════
  • Server URL: https://localhost:7091
  • API Key: ak_fullaccess_H***REDACTED***
  • Runner Name: Local
  • Runner ID: a715617d-6181-4cc7-816e-c89d80b19eb0
  • Service Account: Current User
  • Working Directory: C:\Work\automaton\...
  • Logs Directory: C:\Program Files\AutomatonRunner\logs
  • Environment: Development (SSL bypass enabled)
```

---

## 🔄 Recommended Workflow

**Step 1: Test interactively first (recommended)**
```bash
# Windows
automaton-runner

# Linux/macOS
automaton-runner
```

This will:
- ✅ Prompt for and save API key to `AUTOMATON_API_KEY` environment variable
- ✅ Prompt for and save server URL to configuration
- ✅ Register runner with the server
- ✅ Verify connection works

**Step 2: Convert to service**
```bash
# Windows (as Administrator)
.\scripts\setup-service.ps1

# Linux/macOS (with sudo)
sudo ./scripts/setup-service.sh
```

This will:
- ✅ Auto-detect your configuration
- ✅ Create service with proper environment
- ✅ Start the service (if confirmed)

---

## 🐛 Troubleshooting

### **Windows**

#### Service won't start
```powershell
# Check service status
Get-Service AutomatonRunner | Format-List *

# View error logs
Get-Content "C:\Program Files\AutomatonRunner\logs\service-error.log"

# Check NSSM configuration
& "C:\Path\To\nssm.exe" edit AutomatonRunner
```

#### Can't connect to localhost
- ✅ Ensure you chose to run as **current user** (not LocalSystem)
- ✅ Verify `DOTNET_ENVIRONMENT=Development` is set
- ✅ Check Windows Firewall allows localhost connections

### **Linux**

#### Service won't start
```bash
# Check service status
sudo systemctl status automaton-runner

# View logs
sudo journalctl -u automaton-runner -n 50

# Check service configuration
sudo systemctl cat automaton-runner
```

### **macOS**

#### Service won't start
```bash
# Check if loaded
sudo launchctl list | grep automaton

# View logs
tail -f /var/log/automaton-runner.log
tail -f /var/log/automaton-runner-error.log

# Check plist syntax
plutil -lint /Library/LaunchDaemons/com.automaton.runner.plist
```

---

## 📝 Development vs Production

### **Development (Localhost)**
```
✅ Detects localhost automatically
✅ Prompts to run as current user (Windows)
✅ Enables SSL bypass (DOTNET_ENVIRONMENT=Development)
✅ Allows self-signed certificates
```

### **Production (Remote Server)**
```
✅ Runs as system service account
✅ Standard SSL validation
✅ Secure certificate verification
✅ Isolated execution
```

---

## 🔄 Upgrade & Maintenance

### **Upgrading the Runner**

```powershell
# Windows
Stop-Service AutomatonRunner
# Replace runner executable
Start-Service AutomatonRunner
```

```bash
# Linux
sudo systemctl stop automaton-runner
# Replace runner executable
sudo systemctl start automaton-runner
```

### **Reconfiguring**

Simply re-run the setup script - it will detect the existing installation and prompt to reinstall.

---

## 📄 Files Created

### **Windows**
- Service: `AutomatonRunner` (managed by NSSM)
- Logs: `C:\Program Files\AutomatonRunner\logs\`
- NSSM: Downloaded to runner directory

### **Linux (systemd)**
- Service file: `/etc/systemd/system/automaton-runner.service`
- Logs: systemd journal (`journalctl`)

### **macOS (launchd)**
- Plist: `/Library/LaunchDaemons/com.automaton.runner.plist`
- Logs: `/var/log/automaton-runner*.log`

---

## ✅ Success Indicators

After successful installation and startup:

1. ✅ **Service running**: Status shows "Running"
2. ✅ **Logs show connection**: "Connected to server successfully"
3. ✅ **Studio shows runner**: Runner appears in Automaton Studio Runners list
4. ✅ **No errors in logs**: Error log is empty or minimal

---

## 📚 Additional Resources

- **Automaton Studio Documentation**: https://docs.automaton.monster
- **Runner CLI Guide**: See `automaton-runner --help`
- **API Keys**: Create in Automaton Studio → Settings → API Keys
- **Service Configuration**: All via environment variables

---

## 🆘 Getting Help

If you encounter issues:

1. ✅ **Check logs** using commands above
2. ✅ **Verify configuration** - API key, server URL, network
3. ✅ **Review service status** - Is it running? Any errors?
4. ✅ **Test manually** - Run `automaton-runner` interactively first
5. ✅ **Open issue** - GitHub issues with logs and configuration summary

---

**Last Updated**: 2024  
**Version**: 1.0  
**Maintained By**: Automaton Team
