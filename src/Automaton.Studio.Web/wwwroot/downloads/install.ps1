# Automaton Runner Installation Script for Windows
# Run this script in PowerShell as Administrator

param(
    [string]$ApiKey = "",
    [string]$ServerUrl = "https://localhost:7091",
    [string]$RunnerName = "$env:COMPUTERNAME-Runner",
    [string]$InstallPath = "$env:ProgramFiles\AutomatonRunner",
    [switch]$CreateService = $false,
    [switch]$Help = $false,
    [string]$ForceArch = "",
    [string]$DownloadUrl = "https://studio.automaton.monster/downloads/runner/latest"
)

# Colors for console output
$ColorInfo = "Green"
$ColorWarning = "Yellow"
$ColorError = "Red"
$ColorHeader = "Cyan"

function Write-Header {
    Write-Host "????????????????????????????????????????????????????????????????" -ForegroundColor $ColorHeader
    Write-Host "?                  Automaton Runner Installer                 ?" -ForegroundColor $ColorHeader
    Write-Host "?            Cross-platform workflow automation               ?" -ForegroundColor $ColorHeader
    Write-Host "????????????????????????????????????????????????????????????????" -ForegroundColor $ColorHeader
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
Automaton Runner Installation Script for Windows

USAGE:
    iwr -useb https://studio.automaton.monster/downloads/install.ps1 | iex
    # OR
    .\install.ps1 [OPTIONS]

OPTIONS:
    -ApiKey KEY             API key for authentication
    -ServerUrl URL          Server URL (default: https://api.automaton.monster)
    -RunnerName NAME        Runner name (default: COMPUTERNAME-Runner)
    -InstallPath PATH       Installation directory (default: Program Files\AutomatonRunner)
    -CreateService          Create Windows service using NSSM
    -ForceArch ARCH         Force architecture (x64/arm64)
    -Help                   Show this help message

EXAMPLES:
    # Basic installation
    iwr -useb https://studio.automaton.monster/downloads/install.ps1 | iex

    # Install with configuration
    .\install.ps1 -ApiKey "ak_runner_your_key_here" -ServerUrl "https://automaton.company.com" -RunnerName "production-server-01" -CreateService

    # Install to custom directory
    .\install.ps1 -InstallPath "C:\Tools\AutomatonRunner" -CreateService

REQUIREMENTS:
    - PowerShell 5.0 or later
    - Administrator privileges (for system-wide installation)
    - Internet connection
    - NSSM (for Windows service creation - will be downloaded if needed)

"@
}

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-Architecture {
    if ($ForceArch) {
        return $ForceArch
    }
    
    $arch = $env:PROCESSOR_ARCHITECTURE
    switch ($arch) {
        "AMD64" { return "x64" }
        "ARM64" { return "arm64" }
        "x86" { return "x86" }
        default { return "x64" }
    }
}

function Get-DownloadUrl {
    param([string]$Architecture)
    
    switch ($Architecture) {
        "x64" { return "$DownloadUrl/automaton-runner-win-x64.zip" }
        "arm64" { return "$DownloadUrl/automaton-runner-win-arm64.zip" }
        default { 
            Write-Error "Unsupported architecture: $Architecture"
            return $null
        }
    }
}

function Download-AndExtract {
    param(
        [string]$Url,
        [string]$DestinationPath
    )
    
    $tempZip = Join-Path $env:TEMP "automaton-runner.zip"
    $tempExtract = Join-Path $env:TEMP "automaton-runner-extract"
    
    try {
        Write-Step "Downloading from $Url"
        Invoke-WebRequest -Uri $Url -OutFile $tempZip -UseBasicParsing
        
        Write-Step "Extracting archive"
        if (Test-Path $tempExtract) {
            Remove-Item $tempExtract -Recurse -Force
        }
        Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force
        
        # Create destination directory
        if (!(Test-Path $DestinationPath)) {
            New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
        }
        
        # Copy all files to destination
        Get-ChildItem -Path $tempExtract -Recurse | Copy-Item -Destination $DestinationPath -Force
        
        Write-Step "Files extracted to $DestinationPath"
        
    } catch {
        Write-Error "Failed to download or extract: $($_.Exception.Message)"
        throw
    } finally {
        # Cleanup
        if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
        if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
    }
}

function Add-ToPath {
    param([string]$PathToAdd)
    
    $currentPath = [Environment]::GetEnvironmentVariable("PATH", "Machine")
    if ($currentPath -notlike "*$PathToAdd*") {
        [Environment]::SetEnvironmentVariable("PATH", "$currentPath;$PathToAdd", "Machine")
        Write-Step "Added $PathToAdd to system PATH"
        
        # Also add to current session
        $env:PATH += ";$PathToAdd"
    } else {
        Write-Step "Path already exists in system PATH"
    }
}

function Test-Installation {
    param([string]$ExecutablePath)
    
    try {
        $version = & $ExecutablePath --version 2>$null
        Write-Step "Installation verified successfully"
        return $true
    } catch {
        Write-Error "Installation verification failed"
        return $false
    }
}

function Configure-Runner {
    param([string]$ExecutablePath)
    
    if ($ApiKey) {
        Write-Step "Configuring runner with provided API key"
        
        try {
            & $ExecutablePath auth --api-key $ApiKey --server $ServerUrl
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to authenticate with API key"
                return $false
            }
            
            & $ExecutablePath register --name $RunnerName --server $ServerUrl
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to register runner"
                return $false
            }
            
            Write-Step "Runner configured and registered as '$RunnerName'"
            return $true
        } catch {
            Write-Error "Configuration failed: $($_.Exception.Message)"
            return $false
        }
    } else {
        Write-Warning "No API key provided. You'll need to configure manually."
        return $true
    }
}

function Install-NSSM {
    $nssmPath = Join-Path $InstallPath "nssm.exe"
    
    if (Test-Path $nssmPath) {
        Write-Step "NSSM already exists"
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
        $arch = Get-Architecture
        $nssmArch = if ($arch -eq "x64") { "win64" } else { "win32" }
        $nssmSource = Get-ChildItem -Path $tempExtract -Filter "nssm.exe" -Recurse | Where-Object { $_.FullName -like "*$nssmArch*" } | Select-Object -First 1
        
        if ($nssmSource) {
            Copy-Item $nssmSource.FullName $nssmPath
            Write-Step "NSSM installed"
        } else {
            Write-Error "Could not find NSSM executable for architecture: $arch"
            return $null
        }
        
    } catch {
        Write-Error "Failed to download NSSM: $($_.Exception.Message)"
        return $null
    } finally {
        if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
        if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
    }
    
    return $nssmPath
}

function Create-WindowsService {
    param([string]$ExecutablePath)
    
    $serviceName = "AutomatonRunner"
    
    # Check if service already exists
    $existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Warning "Service '$serviceName' already exists. Stopping and removing..."
        Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $serviceName
        Start-Sleep 2
    }
    
    # Install NSSM
    $nssmPath = Install-NSSM
    if (!$nssmPath) {
        Write-Error "Failed to install NSSM"
        return $false
    }
    
    Write-Step "Creating Windows service '$serviceName'"
    
    try {
        # Install service with NSSM
        & $nssmPath install $serviceName $ExecutablePath "run" "--name" $RunnerName
        
        # Configure service
        & $nssmPath set $serviceName Description "Automaton workflow runner for cross-platform automation"
        & $nssmPath set $serviceName Start SERVICE_AUTO_START
        & $nssmPath set $serviceName AppStdout "$InstallPath\logs\service-output.log"
        & $nssmPath set $serviceName AppStderr "$InstallPath\logs\service-error.log"
        & $nssmPath set $serviceName AppRotateFiles 1
        & $nssmPath set $serviceName AppRotateOnline 1
        & $nssmPath set $serviceName AppRotateBytes 10485760  # 10MB
        
        # Set environment variables
        if ($ApiKey) {
            & $nssmPath set $serviceName AppEnvironmentExtra "AUTOMATON_API_KEY=$ApiKey" "AppConfig__BaseUrl=$ServerUrl"
        } else {
            & $nssmPath set $serviceName AppEnvironmentExtra "AppConfig__BaseUrl=$ServerUrl"
        }
        
        # Create logs directory
        $logsPath = Join-Path $InstallPath "logs"
        if (!(Test-Path $logsPath)) {
            New-Item -ItemType Directory -Path $logsPath -Force | Out-Null
        }
        
        Write-Step "Windows service '$serviceName' created successfully"
        Write-Step "Start service: Start-Service '$serviceName'"
        Write-Step "Stop service: Stop-Service '$serviceName'"
        Write-Step "Service logs: $logsPath"
        
        return $true
        
    } catch {
        Write-Error "Failed to create Windows service: $($_.Exception.Message)"
        return $false
    }
}

function Show-CompletionMessage {
    param([string]$ExecutablePath)
    
    Write-Host ""
    Write-Step "Installation completed successfully!"
    Write-Host ""
    Write-Host "?? Next Steps:" -ForegroundColor $ColorInfo
    
    if (!$ApiKey) {
        Write-Host ""
        Write-Host "EASY START (Recommended):" -ForegroundColor $ColorHeader
        Write-Host "1. Get an API key from your Automaton Studio (Settings ? API Keys)" -ForegroundColor Cyan
        Write-Host "2. Run: $ExecutablePath" -ForegroundColor Cyan
        Write-Host "   ? The runner will interactively guide you through setup!" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "ADVANCED (For Scripting):" -ForegroundColor $ColorWarning
        Write-Host "1. Configure: $ExecutablePath auth --api-key YOUR_KEY --server $ServerUrl" -ForegroundColor Yellow
        Write-Host "2. Register: $ExecutablePath register --name YOUR_RUNNER_NAME" -ForegroundColor Yellow
        Write-Host "3. Run: $ExecutablePath" -ForegroundColor Yellow
    } else {
        Write-Host "1. Start the runner: $ExecutablePath" -ForegroundColor Cyan
        if ($CreateService) {
            Write-Host "2. Or start as service: Start-Service 'AutomatonRunner'" -ForegroundColor Cyan
            Write-Host "3. Check service status: Get-Service 'AutomatonRunner'" -ForegroundColor Cyan
        }
    }
    
    Write-Host ""
    Write-Host "Documentation: https://docs.automaton.monster" -ForegroundColor $ColorHeader
    Write-Host "Support: https://github.com/your-org/automaton/issues" -ForegroundColor $ColorHeader
}

# Main installation logic
function Main {
    if ($Help) {
        Show-Help
        return
    }

    Write-Header
    
    # Check if running as administrator
    if (!(Test-Administrator)) {
        Write-Error "This script must be run as Administrator for system-wide installation."
        Write-Host "Please run PowerShell as Administrator and try again." -ForegroundColor Yellow
        exit 1
    }
    
    # Detect architecture
    $architecture = Get-Architecture
    Write-Step "Detected architecture: $architecture"
    
    # Get download URL
    $downloadUrl = Get-DownloadUrl $architecture
    if (!$downloadUrl) {
        exit 1
    }
    
    # Download and extract
    try {
        Download-AndExtract $downloadUrl $InstallPath
    } catch {
        Write-Error "Installation failed during download/extraction"
        exit 1
    }
    
    # Add to PATH
    Add-ToPath $InstallPath
    
    # Test installation
    $executablePath = Join-Path $InstallPath "automaton-runner.exe"
    if (!(Test-Installation $executablePath)) {
        exit 1
    }
    
    # Configure runner
    if (!(Configure-Runner $executablePath)) {
        Write-Warning "Runner installation completed but configuration failed"
    }
    
    # Create service if requested
    if ($CreateService) {
        if (!(Create-WindowsService $executablePath)) {
            Write-Warning "Runner installation completed but service creation failed"
        }
    }
    
    # Show completion message
    Show-CompletionMessage $executablePath
}

# Run main function
Main