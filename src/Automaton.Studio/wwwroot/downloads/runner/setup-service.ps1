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
    Write-Host "================================================================" -ForegroundColor $ColorHeader
    Write-Host "      Automaton Runner - Windows Service Setup                 " -ForegroundColor $ColorHeader
    Write-Host "================================================================" -ForegroundColor $ColorHeader
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
    $runnerConfigPath = Join-Path $configDir "runner.json"
    
    $result = @{
        ServerUrl = $null
        ApiKey = $null
        RunnerId = $null
        RunnerName = $null
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
    
    # Read runner registration from runner.json
    if (Test-Path $runnerConfigPath) {
        try {
            $config = Get-Content $runnerConfigPath -Raw | ConvertFrom-Json
            if ($config.RunnerId) {
                $result.RunnerId = $config.RunnerId
            }
            if ($config.RunnerName) {
                $result.RunnerName = $config.RunnerName
            }
        } catch {
            Write-Warning "Found runner config file but couldn't parse it: $runnerConfigPath"
        }
    }
    
    # IMPORTANT: Check for API key in user environment variable
    # Note: We need to read this NOW because the service won't have access to it
    $envApiKey = [System.Environment]::GetEnvironmentVariable('AUTOMATON_API_KEY', 'User')
    if ($envApiKey) {
        Write-Step "Found API key in user environment variable"
        $result.ApiKey = $envApiKey
    }
    
    if ($result.ServerUrl -or $result.ApiKey -or $result.RunnerName) {
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
        # Install service with NSSM - use 'service' command for non-interactive mode
        & $nssmPath install $ServiceName $RunnerPath service
        
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
            
            if ($existingConfig.RunnerName) {
                Write-Host "   Runner Name: $($existingConfig.RunnerName)" -ForegroundColor Cyan
                Write-Host "   Runner ID: $($existingConfig.RunnerId)" -ForegroundColor Cyan
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
        
        # NOW configure service account based on detected server URL
        Write-Host ""
        if ($effectiveServerUrl -like "*localhost*" -or $effectiveServerUrl -like "*127.0.0.1*") {
            Write-Host "??  Localhost server detected!" -ForegroundColor Yellow
            Write-Host "   Windows services running as LocalSystem cannot always access localhost servers." -ForegroundColor Yellow
            Write-Host ""
            Write-Host "   Options:" -ForegroundColor Cyan
            Write-Host "   1. Run service as your user account (recommended for localhost)" -ForegroundColor White
            Write-Host "   2. Run service as LocalSystem" -ForegroundColor White
            Write-Host ""
            
            $runAsUser = Read-Host "Run service as current user? (Y/n)"
            if ($runAsUser -ne 'n' -and $runAsUser -ne 'N') {
                # Get current user
                $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
                Write-Host "   Service will run as: $currentUser" -ForegroundColor Cyan
                Write-Host "   You'll need to provide your Windows password" -ForegroundColor Yellow
                Write-Host ""
                
                # Use Read-Host with -AsSecureString for password input
                Write-Host "Enter password for $currentUser" -ForegroundColor Cyan
                $securePassword = Read-Host "Password" -AsSecureString
                
                if ($securePassword.Length -gt 0) {
                    # Convert SecureString to plain text for NSSM
                    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
                    $password = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
                    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
                    
                    & $nssmPath set $ServiceName ObjectName $currentUser $password
                    
                    # Clear password from memory
                    $password = $null
                    [System.GC]::Collect()
                    
                    Write-Step "Service configured to run as: $currentUser"
                } else {
                    Write-Warning "No password provided - falling back to LocalSystem"
                    & $nssmPath set $ServiceName ObjectName "LocalSystem"
                }
            } else {
                & $nssmPath set $ServiceName ObjectName "LocalSystem"
                Write-Warning "Service will run as LocalSystem - may have connection issues with localhost"
            }
        } else {
            # Production/remote server - use LocalSystem
            & $nssmPath set $ServiceName ObjectName "LocalSystem"
            Write-Step "Service configured to run as LocalSystem"
        }
        
        # CRITICAL: Set environment variables via NSSM so the SERVICE can access them
        Write-Host ""
        Write-Step "Configuring service environment..."
        
        # Build environment variables list
        $envVars = @()
        
        # Add server URL (required)
        $envVars += "AppConfig__BaseUrl=$effectiveServerUrl"
        Write-Step "Server URL will be set via environment variable"
        
        # Add API key (required)
        if ($effectiveApiKey) {
            $envVars += "AUTOMATON_API_KEY=$effectiveApiKey"
            Write-Step "API key will be set via environment variable"
        } else {
            Write-Warning "No API key configured - service will not connect"
        }
        
        # Add runner registration if it exists (to avoid auto-registration)
        if ($existingConfig -and $existingConfig.RunnerId -and $existingConfig.RunnerName) {
            $envVars += "AUTOMATON_RUNNER_ID=$($existingConfig.RunnerId)"
            $envVars += "AUTOMATON_RUNNER_NAME=$($existingConfig.RunnerName)"
            Write-Step "Runner registration will be set via environment variables"
            Write-Host "  Runner Name: $($existingConfig.RunnerName)" -ForegroundColor DarkGray
            Write-Host "  Runner ID: $($existingConfig.RunnerId)" -ForegroundColor DarkGray
        } else {
            Write-Warning "No runner registration found - service will auto-register with name 'Service-$env:COMPUTERNAME'"
        }
        
        # Add development environment for SSL bypass (for localhost)
        if ($effectiveServerUrl -like "*localhost*" -or $effectiveServerUrl -like "*127.0.0.1*") {
            $envVars += "DOTNET_ENVIRONMENT=Development"
            Write-Step "Development environment enabled (for SSL bypass on localhost)"
        }
        
        # Set ALL environment variables via NSSM
        if ($envVars.Count -gt 0) {
            Write-Step "Setting environment variables via NSSM..."
            
            # CRITICAL: NSSM's AppEnvironmentExtra parameter needs ALL variables in ONE call
            # Multiple variables must be separated by newlines in a single string
            $envString = $envVars -join "`r`n"
            
            & $nssmPath set $ServiceName AppEnvironmentExtra $envString
            
            if ($LASTEXITCODE -eq 0) {
                Write-Step "Environment variables configured successfully"
                Write-Host ""
                Write-Host "  ?? Service Configuration Summary:" -ForegroundColor Cyan
                Write-Host "  ????????????????????????????????" -ForegroundColor Cyan
                Write-Host "  • Server URL: $effectiveServerUrl" -ForegroundColor DarkGray
                if ($effectiveApiKey) {
                    $keyPreview = $effectiveApiKey.Substring(0, [Math]::Min(15, $effectiveApiKey.Length))
                    Write-Host "  • API Key: ${keyPreview}***REDACTED***" -ForegroundColor DarkGray
                }
                if ($existingConfig -and $existingConfig.RunnerName) {
                    Write-Host "  • Runner Name: $($existingConfig.RunnerName)" -ForegroundColor DarkGray
                    Write-Host "  • Runner ID: $($existingConfig.RunnerId)" -ForegroundColor DarkGray
                }
                $serviceAccount = if ($effectiveServerUrl -like "*localhost*") { "Current User" } else { "LocalSystem" }
                Write-Host "  • Service Account: $serviceAccount" -ForegroundColor DarkGray
                Write-Host "  • Working Directory: $runnerDirectory" -ForegroundColor DarkGray
                Write-Host "  • Logs Directory: $LogsPath" -ForegroundColor DarkGray
                if ($effectiveServerUrl -like "*localhost*") {
                    Write-Host "  • Environment: Development (SSL bypass enabled)" -ForegroundColor DarkGray
                }
                Write-Host ""
            } else {
                Write-Warning "Failed to set environment variables (exit code: $LASTEXITCODE)"
            }
        }
        
        # Copy runner registration if it exists in user config (backup method)
        if ($existingConfig -and $existingConfig.RunnerId -and $existingConfig.RunnerName) {
            Write-Step "Copying runner registration to service directory..."
            
            $serviceConfigDir = Join-Path $runnerDirectory "config"
            if (!(Test-Path $serviceConfigDir)) {
                New-Item -ItemType Directory -Path $serviceConfigDir -Force | Out-Null
            }
            
            $serviceRunnerConfig = Join-Path $serviceConfigDir "runner.json"
            $runnerConfigContent = @{
                RunnerId = $existingConfig.RunnerId
                RunnerName = $existingConfig.RunnerName
                RegisteredAt = (Get-Date).ToUniversalTime().ToString("o")
                CopiedFromUserConfig = $true
            } | ConvertTo-Json
            
            Set-Content -Path $serviceRunnerConfig -Value $runnerConfigContent -Force
            Write-Step "Runner registration copied: $($existingConfig.RunnerName) ($($existingConfig.RunnerId))"
            Write-Host "  Location: $serviceRunnerConfig" -ForegroundColor DarkGray
        } else {
            Write-Warning "No runner registration found in user config"
            Write-Host "  The service will prompt for runner name on first start" -ForegroundColor Yellow
            Write-Host "  Tip: Run 'automaton-runner register' first to pre-register" -ForegroundColor Cyan
        }
        
        Write-Step "Windows service '$ServiceName' created successfully"
        Write-Host ""
        Write-Host "================================================================" -ForegroundColor $ColorHeader
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
                sleep 3
                
                $service = Get-Service $ServiceName
                if ($service.Status -eq 'Running') {
                    Write-Host ""
                    Write-Host "? Service started successfully and is running" -ForegroundColor Green
                    Write-Host ""
                    
                    # Show initial log output
                    Write-Host "?? Initial service logs (last 10 lines):" -ForegroundColor Cyan
                    Write-Host "????????????????????????????????????????" -ForegroundColor Cyan
                    if (Test-Path "$LogsPath\service-output.log") {
                        Get-Content "$LogsPath\service-output.log" -Tail 10 -ErrorAction SilentlyContinue | ForEach-Object {
                            # Redact API keys in log output
                            $logLine = $_
                            if ($logLine -match "API Key:") {
                                $logLine = $logLine -replace '(API Key:)\s*\w+', '$1 ***REDACTED***'
                            }
                            Write-Host "  $_" -ForegroundColor Gray
                        }
                    } else {
                        Write-Host "  (Log file not yet created - service is starting up)" -ForegroundColor DarkGray
                    }
                    Write-Host ""
                    Write-Host "?? Monitor logs in real-time with:" -ForegroundColor Cyan
                    Write-Host "   Get-Content '$LogsPath\service-output.log' -Tail 50 -Wait" -ForegroundColor White
                    Write-Host ""
                    Write-Host "? Check the runner status in Automaton Studio to verify connection" -ForegroundColor Cyan
                } else {
                    Write-Warning "Service status: $($service.Status)"
                    Write-Host ""
                    Write-Host "Troubleshooting:" -ForegroundColor Yellow
                    Write-Host "  • Check logs: $LogsPath" -ForegroundColor White
                    Write-Host "  • View output: Get-Content '$LogsPath\service-output.log'" -ForegroundColor White
                    Write-Host "  • View errors: Get-Content '$LogsPath\service-error.log'" -ForegroundColor White
                    Write-Host "  • Service status: Get-Service $ServiceName | Format-List *" -ForegroundColor White
                }
            }
        } else {
            Write-Host ""
            Write-Host "??  Service created but NOT started (no API key configured)" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "?? To configure and start the service:" -ForegroundColor Cyan
            Write-Host "   1. Edit the service configuration:" -ForegroundColor White
            Write-Host "      nssm edit $ServiceName" -ForegroundColor DarkGray
            Write-Host "   2. Add AUTOMATON_API_KEY to 'Environment' tab" -ForegroundColor White
            Write-Host "   3. Start the service:" -ForegroundColor White
            Write-Host "      Start-Service $ServiceName" -ForegroundColor DarkGray
        }
        
        Write-Host ""
        Write-Host "================================================================" -ForegroundColor $ColorHeader
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
