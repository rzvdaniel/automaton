<#
.SYNOPSIS
    Automaton Runner - Windows Service Setup Script

.DESCRIPTION
    This script configures the Automaton Runner as a Windows service using NSSM.

.NOTES
    HOW TO RUN THIS SCRIPT:
    1. Open PowerShell as Administrator (right-click PowerShell ? "Run as Administrator")
    2. Navigate to the scripts directory: cd c:\Work\automaton\scripts
    3. Allow script execution: Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope Process
    4. Run the script: .\setup-service.ps1
    
    DO NOT double-click this file - it will open in an editor instead of executing.

.EXAMPLE
    .\setup-service.ps1
    Install service with default settings

.EXAMPLE
    .\setup-service.ps1 -Uninstall
    Uninstall the service
#>

# Automaton Runner - Windows Service Setup Script
# Run this script in PowerShell as Administrator to configure the runner as a Windows service

param(
    [string]$RunnerPath = "$env:ProgramFiles\AutomatonRunner\automaton-runner.exe",
    [string]$ServiceName = "AutomatonRunner",
    [string]$ApiKey = "",
    [string]$ServerUrl = "https://api.automaton.monster",
    [string]$RunnerName = "$env:COMPUTERNAME-Runner",
    [string]$LogsPath = "$env:ProgramFiles\AutomatonRunner\logs",
    [switch]$Uninstall = $false,
    [switch]$Help = $false
)

$ColorInfo = "Green"
$ColorWarning = "Yellow"
$ColorError = "Red"
$ColorHeader = "Cyan"

function Write-Header {
    Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor $ColorHeader
    Write-Host "      Automaton Runner - Windows Service Setup             " -ForegroundColor $ColorHeader
    Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor $ColorHeader
    Write-Host ""
}

function Write-Step {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor $ColorInfo
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor $ColorWarning
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor $ColorError
}

function Show-Help {
    @"
Automaton Runner - Windows Service Setup Script

USAGE:
    .\setup-service.ps1 [OPTIONS]

OPTIONS:
    -RunnerPath PATH        Path to automaton-runner.exe (default: Program Files\AutomatonRunner\automaton-runner.exe)
    -ServiceName NAME       Service name (default: AutomatonRunner)
    -ApiKey KEY            API key for authentication (optional - will auto-detect from environment)
    -ServerUrl URL         Server URL (default: https://api.automaton.monster)
    -RunnerName NAME       Runner name (default: COMPUTERNAME-Runner)
    -LogsPath PATH         Logs directory (default: Program Files\AutomatonRunner\logs)
    -Uninstall             Remove the service
    -Help                  Show this help message

EXAMPLES:
    # Install service with default settings (auto-detects configuration)
    .\setup-service.ps1

    # Install with custom configuration
    .\setup-service.ps1 -ApiKey "ak_runner_xxx" -ServerUrl "https://automaton.company.com" -RunnerName "prod-01"

    # Install with custom runner location
    .\setup-service.ps1 -RunnerPath "C:\Tools\AutomatonRunner\automaton-runner.exe"

    # Uninstall service
    .\setup-service.ps1 -Uninstall

REQUIREMENTS:
    - PowerShell 5.0 or later
    - Administrator privileges
    - NSSM (Non-Sucking Service Manager) - will be downloaded if needed
    - Automaton Runner already installed

IMPORTANT NOTE:
    Windows services run under SYSTEM account and cannot access user-level environment variables.
    This script will pass the API key directly to the service via NSSM environment configuration.

"@
}

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ExistingConfig {
    # Try to find existing runner configuration
    $configDir = Join-Path $env:APPDATA "AutomatonRunner"
    $serverConfigPath = Join-Path $configDir "server.json"
    
    $result = @{
        ServerUrl = $null
        ApiKey = $null
    }
    
    # Read server URL from server.json (new format)
    if (Test-Path $serverConfigPath) {
        try {
            $config = Get-Content $serverConfigPath -Raw | ConvertFrom-Json
            if ($config.ServerUrl) {
                $result.ServerUrl = $config.ServerUrl
            }
        } catch {
            Write-Warning "Found server config file but couldn't parse it: $serverConfigPath"
        }
    }
    
    # IMPORTANT: Check for API key in user environment variable
    # Note: We need to read this NOW because the service won't have access to it
    $envApiKey = [System.Environment]::GetEnvironmentVariable('AUTOMATON_API_KEY', 'User')
    if ($envApiKey) {
        Write-Step "Found API key in user environment variable"
        $result.ApiKey = $envApiKey
    }
    
    if ($result.ServerUrl -or $result.ApiKey) {
        Write-Step "Found existing runner configuration in: $configDir"
        return $result
    }
    
    return $null
}

function Install-NSSM {
    $installDir = Split-Path $RunnerPath
    $nssmPath = Join-Path $installDir "nssm.exe"
    
    if (Test-Path $nssmPath) {
        Write-Step "NSSM already exists at $nssmPath"
        return $nssmPath
    }
    
    Write-Step "Downloading NSSM (Non-Sucking Service Manager)"
    
    $nssmUrl = "https://nssm.cc/release/nssm-2.24.zip"
    $tempZip = Join-Path $env:TEMP "nssm.zip"
    $tempExtract = Join-Path $env:TEMP "nssm-extract"
    
    try {
        Invoke-WebRequest -Uri $nssmUrl -OutFile $tempZip -UseBasicParsing
        Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force
        
        # Find the correct NSSM executable based on architecture
        $arch = $env:PROCESSOR_ARCHITECTURE
        $nssmArch = if ($arch -eq "AMD64") { "win64" } else { "win32" }
        $nssmSource = Get-ChildItem -Path $tempExtract -Filter "nssm.exe" -Recurse | Where-Object { $_.FullName -like "*$nssmArch*" } | Select-Object -First 1
        
        if ($nssmSource) {
            Copy-Item $nssmSource.FullName $nssmPath
            Write-Step "NSSM installed to $nssmPath"
        } else {
            Write-Error "Could not find NSSM executable for architecture: $arch"
            return $null
        }
        
    } catch {
        Write-Error "Failed to download NSSM: $($_.Exception.Message)"
        return $null
    } finally {
        if (Test-Path $tempZip) { Remove-Item $tempZip -Force -ErrorAction SilentlyContinue }
        if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force -ErrorAction SilentlyContinue }
    }
    
    return $nssmPath
}

function Remove-Service {
    # Check if service exists
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (!$service) {
        Write-Warning "Service '$ServiceName' does not exist"
        return $true
    }
    
    Write-Step "Stopping service '$ServiceName'"
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
    
    Write-Step "Removing service '$ServiceName'"
    & sc.exe delete $ServiceName
    
    if ($LASTEXITCODE -eq 0) {
        Write-Step "Service '$ServiceName' removed successfully"
        return $true
    } else {
        Write-Error "Failed to remove service"
        return $false
    }
}

function Install-Service {
    # Verify runner exists
    if (!(Test-Path $RunnerPath)) {
        Write-Warning "Runner not found at default location: $RunnerPath"
        Write-Host ""
        Write-Host "The runner executable was not found at the expected location." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Common installation locations:" -ForegroundColor Cyan
        Write-Host "  - $env:ProgramFiles\AutomatonRunner\automaton-runner.exe" -ForegroundColor Cyan
        Write-Host "  - C:\Tools\AutomatonRunner\automaton-runner.exe" -ForegroundColor Cyan
        Write-Host "  - C:\Work\automaton\src\Automaton.Runner.Console\bin\Debug\net8.0\automaton-runner.exe (debug)" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "You can either:" -ForegroundColor Cyan
        Write-Host "  1. Enter the path below, OR" -ForegroundColor White
        Write-Host "  2. Re-run this script with the -RunnerPath parameter:" -ForegroundColor White
        Write-Host ""
        Write-Host '     .\scripts\setup-service.ps1 -RunnerPath "C:\Path\To\automaton-runner.exe"' -ForegroundColor Green
        Write-Host ""
        
        # Prompt for custom path
        Write-Host "Please provide the full path to automaton-runner.exe:" -ForegroundColor Yellow
        Write-Host "(or press Ctrl+C to exit and re-run with -RunnerPath parameter)" -ForegroundColor DarkGray
        $customPath = Read-Host "Path"
        
        if ([string]::IsNullOrWhiteSpace($customPath)) {
            Write-Error "No path provided. Cannot continue without runner executable."
            return $false
        }
        
        # Validate the custom path
        if (!(Test-Path $customPath)) {
            Write-Error "Runner not found at specified path: $customPath"
            Write-Host "Please verify the path and try again." -ForegroundColor Yellow
            return $false
        }
        
        # Verify it's actually the runner executable
        if ($customPath -notlike "*automaton-runner.exe") {
            Write-Warning "The specified file doesn't appear to be automaton-runner.exe"
            $confirm = Read-Host "Continue anyway? (y/N)"
            if ($confirm -ne 'y' -and $confirm -ne 'Y') {
                Write-Step "Installation cancelled"
                return $false
            }
        }
        
        $RunnerPath = $customPath
        Write-Step "Using runner at: $RunnerPath"
        Write-Host ""
    } else {
        Write-Step "Found runner at: $RunnerPath"
    }
    
    # Check if service already exists
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Warning "Service '$ServiceName' already exists"
        $response = Read-Host "Do you want to reinstall? (y/N)"
        if ($response -ne 'y' -and $response -ne 'Y') {
            Write-Step "Installation cancelled"
            return $false
        }
        
        Write-Step "Removing existing service"
        if (!(Remove-Service)) {
            return $false
        }
        Start-Sleep 2
    }
    
    # Install NSSM
    $nssmPath = Install-NSSM
    if (!$nssmPath) {
        Write-Error "Failed to install NSSM"
        return $false
    }
    
    Write-Step "Creating Windows service '$ServiceName'"
    
    try {
        # Install service with NSSM
        & $nssmPath install $ServiceName $RunnerPath
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to install service"
            return $false
        }
        
        # CRITICAL: Set working directory to the runner's directory
        # This ensures appsettings.json and other config files can be found
        $runnerDirectory = Split-Path $RunnerPath -Parent
        & $nssmPath set $ServiceName AppDirectory $runnerDirectory
        Write-Step "Set working directory: $runnerDirectory"
        
        # Configure service description
        & $nssmPath set $ServiceName Description "Automaton workflow runner for cross-platform automation"
        
        # Set startup type to automatic
        & $nssmPath set $ServiceName Start SERVICE_AUTO_START
        
        # Create logs directory if it doesn't exist
        if (!(Test-Path $LogsPath)) {
            New-Item -ItemType Directory -Path $LogsPath -Force | Out-Null
            Write-Step "Created logs directory: $LogsPath"
        }
        
        # Configure logging
        & $nssmPath set $ServiceName AppStdout "$LogsPath\service-output.log"
        & $nssmPath set $ServiceName AppStderr "$LogsPath\service-error.log"
        & $nssmPath set $ServiceName AppRotateFiles 1
        & $nssmPath set $ServiceName AppRotateOnline 1
        & $nssmPath set $ServiceName AppRotateBytes 10485760  # 10MB
        
        # Detect existing configuration if parameters not provided
        $existingConfig = Get-ExistingConfig
        $effectiveServerUrl = $ServerUrl
        $effectiveApiKey = $ApiKey
        
        if ($existingConfig) {
            Write-Host ""
            Write-Host "Detected existing runner configuration!" -ForegroundColor Green
            
            if ($existingConfig.ServerUrl) {
                $effectiveServerUrl = $existingConfig.ServerUrl
                Write-Host "   Server URL: $effectiveServerUrl" -ForegroundColor Cyan
            }
            
            if ($existingConfig.ApiKey -and !$ApiKey) {
                $keyPreview = $existingConfig.ApiKey.Substring(0, [Math]::Min(15, $existingConfig.ApiKey.Length))
                Write-Host "   API Key (from environment): ${keyPreview}..." -ForegroundColor Cyan
                $effectiveApiKey = $existingConfig.ApiKey
            }
            
            Write-Host ""
            $useExisting = Read-Host "Use this configuration for the service? (Y/n)"
            if ($useExisting -eq 'n' -or $useExisting -eq 'N') {
                Write-Host "Please provide configuration manually:" -ForegroundColor Yellow
                
                if (!$ApiKey) {
                    $effectiveApiKey = Read-Host "Enter API key"
                }
                
                $customUrl = Read-Host "Enter server URL (press Enter for $ServerUrl)"
                if ($customUrl) {
                    $effectiveServerUrl = $customUrl
                }
            } else {
                Write-Step "Using detected configuration for service"
            }
        } elseif (!$ApiKey) {
            Write-Host ""
            Write-Host "No API key found!" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Windows services run under SYSTEM account and cannot access user-level environment variables." -ForegroundColor Yellow
            Write-Host "You need to provide the API key to this script so it can be configured for the service." -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Tip: Run 'automaton-runner' first to configure your API key, then run this script." -ForegroundColor Cyan
            Write-Host ""
            $effectiveApiKey = Read-Host "Enter your API key (or press Enter to skip - service won't work without it)"
        }
        
        # CRITICAL: Set environment variables via NSSM so the SERVICE can access them
        # User-level environment variables are NOT accessible to Windows services
        Write-Host ""
        Write-Step "Configuring service environment variables..."
        
        $envVars = @("AppConfig__BaseUrl=$effectiveServerUrl")
        if ($effectiveApiKey) {
            $envVars += "AUTOMATON_API_KEY=$effectiveApiKey"
            Write-Step "API key will be passed to service via NSSM environment"
        } else {
            Write-Warning "No API key configured! The service will NOT be able to connect."
            Write-Host "   You can reconfigure the service later with:" -ForegroundColor Yellow
            Write-Host "   nssm set $ServiceName AppEnvironmentExtra AUTOMATON_API_KEY=your_key_here AppConfig__BaseUrl=$effectiveServerUrl" -ForegroundColor Yellow
        }
        
        & $nssmPath set $ServiceName AppEnvironmentExtra $envVars
        
        Write-Step "Windows service '$ServiceName' created successfully"
        Write-Host ""
        Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor $ColorHeader
        Write-Host ""
        Write-Host "Service Management Commands:" -ForegroundColor $ColorHeader
        Write-Host "  Start:   Start-Service '$ServiceName'" -ForegroundColor Cyan
        Write-Host "  Stop:    Stop-Service '$ServiceName'" -ForegroundColor Cyan
        Write-Host "  Status:  Get-Service '$ServiceName'" -ForegroundColor Cyan
        Write-Host "  Logs:    $LogsPath" -ForegroundColor Cyan
        Write-Host ""
        
        if ($effectiveApiKey) {
            # Ask if user wants to start the service now
            $startNow = Read-Host "Start the service now? (Y/n)"
            if ($startNow -ne 'n' -and $startNow -ne 'N') {
                Write-Step "Starting service..."
                Start-Service $ServiceName
                Start-Sleep 3
                
                $service = Get-Service $ServiceName
                if ($service.Status -eq 'Running') {
                    Write-Host ""
                    Write-Host "Service started successfully and is running" -ForegroundColor Green
                    Write-Host ""
                    Write-Host "Check the runner status in Automaton Studio to verify connection" -ForegroundColor Cyan
                } else {
                    Write-Warning "Service status: $($service.Status)"
                    Write-Host "Check logs at: $LogsPath" -ForegroundColor Yellow
                }
            }
        } else {
            Write-Host ""
            Write-Host "Service created but NOT started (no API key configured)" -ForegroundColor Yellow
            Write-Host "   Configure the API key and start manually when ready" -ForegroundColor Yellow
        }
        
        Write-Host ""
        Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor $ColorHeader
        Write-Host ""
        
        return $true
        
    } catch {
        Write-Error "Failed to create Windows service: $($_.Exception.Message)"
        return $false
    }
}

# Main logic
function Main {
    if ($Help) {
        Show-Help
        return
    }

    Write-Header
    
    # Check if running as administrator
    if (!(Test-Administrator)) {
        Write-Error "This script must be run as Administrator"
        Write-Host "Please run PowerShell as Administrator and try again" -ForegroundColor Yellow
        exit 1
    }
    
    if ($Uninstall) {
        if (Remove-Service) {
            Write-Step "Service uninstallation completed"
        } else {
            exit 1
        }
    } else {
        if (Install-Service) {
            Write-Step "Service installation completed"
        } else {
            exit 1
        }
    }
}

# Run main function
Main
