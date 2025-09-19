# Automaton Studio Desktop Installation Script for Windows
# Downloads and installs the latest version of Automaton Studio Desktop
# Run this script in PowerShell

param(
    [string]$InstallPath = "$env:ProgramFiles\AutomatonStudio",
    [switch]$CreateDesktopShortcut = $true,
    [switch]$CreateStartMenuShortcut = $true,
    [switch]$Portable = $false,
    [switch]$Help = $false,
    [string]$Architecture = "auto",
    [string]$DownloadUrl = "https://studio.automaton.monster/downloads/desktop"
)

# Colors for console output
$ColorInfo = "Green"
$ColorWarning = "Yellow"
$ColorError = "Red"
$ColorHeader = "Cyan"

function Write-Header {
    Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor $ColorHeader
    Write-Host "║             Automaton Studio Desktop Installer          ║" -ForegroundColor $ColorHeader
    Write-Host "║          Visual workflow design and execution           ║" -ForegroundColor $ColorHeader
    Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor $ColorHeader
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
Automaton Studio Desktop Installation Script for Windows

USAGE:
    iwr -useb https://studio.automaton.monster/downloads/install-desktop.ps1 | iex
    # OR
    .\install-desktop.ps1 [OPTIONS]

OPTIONS:
    -InstallPath PATH       Installation directory (default: Program Files\AutomatonStudio)
    -CreateDesktopShortcut  Create desktop shortcut (default: true)
    -CreateStartMenuShortcut Create Start Menu shortcuts (default: true)
    -Portable               Install as portable app (no shortcuts or registry)
    -Architecture ARCH      Force architecture (x64/arm64/auto)
    -Help                   Show this help message

EXAMPLES:
    # Basic installation
    iwr -useb https://studio.automaton.monster/downloads/install-desktop.ps1 | iex

    # Portable installation
    .\install-desktop.ps1 -InstallPath "C:\Tools\AutomatonStudio" -Portable

    # Custom installation path
    .\install-desktop.ps1 -InstallPath "C:\AutomatonStudio" -CreateDesktopShortcut:$false

REQUIREMENTS:
    - PowerShell 5.0 or later
    - Windows 10 version 1809 or later / Windows 11
    - Administrator privileges (for system-wide installation)
    - Internet connection

"@
}

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-SystemArchitecture {
    if ($Architecture -ne "auto") {
        return $Architecture.ToLower()
    }
    
    $arch = $env:PROCESSOR_ARCHITECTURE
    $wow64 = $env:PROCESSOR_ARCHITEW6432
    
    if ($wow64) {
        $arch = $wow64
    }
    
    switch ($arch) {
        "AMD64" { return "x64" }
        "ARM64" { return "arm64" }
        default { return "x64" }
    }
}

function Get-LatestVersion {
    try {
        $versionUrl = "$DownloadUrl/latest/version.json"
        Write-Step "Checking latest version from $versionUrl"
        
        $response = Invoke-RestMethod -Uri $versionUrl -ErrorAction Stop
        return $response
    }
    catch {
        Write-Error "Failed to get version information: $($_.Exception.Message)"
        return $null
    }
}

function Download-File {
    param(
        [string]$Url,
        [string]$OutputPath
    )
    
    try {
        Write-Step "Downloading from $Url"
        Write-Step "Saving to $OutputPath"
        
        # Create directory if it doesn't exist
        $dir = Split-Path -Parent $OutputPath
        if (!(Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        
        # Download with progress
        $webClient = New-Object System.Net.WebClient
        $webClient.DownloadFile($Url, $OutputPath)
        $webClient.Dispose()
        
        Write-Step "Download completed: $((Get-Item $OutputPath).Length / 1MB) MB"
        return $true
    }
    catch {
        Write-Error "Download failed: $($_.Exception.Message)"
        return $false
    }
}

function Install-Desktop {
    param(
        [string]$InstallerPath,
        [bool]$IsPortable
    )
    
    try {
        if ($IsPortable) {
            # Extract ZIP for portable installation
            Write-Step "Extracting portable application to $InstallPath"
            
            # Create installation directory
            if (!(Test-Path $InstallPath)) {
                New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
            }
            
            # Extract ZIP
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($InstallerPath, $InstallPath)
            
            Write-Step "Portable installation completed"
            
            # Create shortcuts if requested
            if ($CreateDesktopShortcut -or $CreateStartMenuShortcut) {
                Create-Shortcuts -ExecutablePath (Join-Path $InstallPath "Automaton.Studio.Desktop.exe")
            }
        }
        else {
            # Run the installer
            Write-Step "Running installer: $InstallerPath"
            
            $process = Start-Process -FilePath $InstallerPath -ArgumentList "/S" -Wait -PassThru
            
            if ($process.ExitCode -eq 0) {
                Write-Step "Installation completed successfully"
            }
            else {
                throw "Installer exited with code: $($process.ExitCode)"
            }
        }
        
        return $true
    }
    catch {
        Write-Error "Installation failed: $($_.Exception.Message)"
        return $false
    }
}

function Create-Shortcuts {
    param([string]$ExecutablePath)
    
    try {
        $shell = New-Object -ComObject WScript.Shell
        
        if ($CreateDesktopShortcut) {
            $desktopPath = [Environment]::GetFolderPath("Desktop")
            $shortcutPath = Join-Path $desktopPath "Automaton Studio.lnk"
            
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = $ExecutablePath
            $shortcut.WorkingDirectory = Split-Path -Parent $ExecutablePath
            $shortcut.Description = "Automaton Studio Desktop"
            $shortcut.Save()
            
            Write-Step "Created desktop shortcut: $shortcutPath"
        }
        
        if ($CreateStartMenuShortcut) {
            $startMenuPath = [Environment]::GetFolderPath("StartMenu")
            $programsPath = Join-Path $startMenuPath "Programs\Automaton Studio"
            
            if (!(Test-Path $programsPath)) {
                New-Item -ItemType Directory -Path $programsPath -Force | Out-Null
            }
            
            $shortcutPath = Join-Path $programsPath "Automaton Studio.lnk"
            
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = $ExecutablePath
            $shortcut.WorkingDirectory = Split-Path -Parent $ExecutablePath
            $shortcut.Description = "Automaton Studio Desktop"
            $shortcut.Save()
            
            Write-Step "Created Start Menu shortcut: $shortcutPath"
        }
    }
    catch {
        Write-Warning "Failed to create shortcuts: $($_.Exception.Message)"
    }
}

function Show-CompletionMessage {
    param([string]$ExecutablePath)
    
    Write-Host ""
    Write-Step "Installation completed successfully!"
    Write-Host ""
    Write-Host "🎉 Next Steps:" -ForegroundColor $ColorInfo
    Write-Host ""
    
    if ($CreateDesktopShortcut) {
        Write-Host "• Launch from Desktop shortcut: 'Automaton Studio'" -ForegroundColor $ColorInfo
    }
    
    if ($CreateStartMenuShortcut) {
        Write-Host "• Launch from Start Menu: Search for 'Automaton Studio'" -ForegroundColor $ColorInfo
    }
    
    if ($Portable) {
        Write-Host "• Launch directly: $ExecutablePath" -ForegroundColor $ColorInfo
    }
    
    Write-Host ""
    Write-Host "🎯 Getting Started:" -ForegroundColor $ColorHeader
    Write-Host "• The application runs completely offline" -ForegroundColor $ColorInfo
    Write-Host "• Use the visual designer to create workflows" -ForegroundColor $ColorInfo
    Write-Host "• All workflows execute locally on your machine" -ForegroundColor $ColorInfo
    Write-Host "• No server connection or account required" -ForegroundColor $ColorInfo
    Write-Host ""
    Write-Host "📚 Documentation: https://github.com/rzvdaniel/automaton/wiki" -ForegroundColor $ColorInfo
    Write-Host "🐛 Support: https://github.com/rzvdaniel/automaton/issues" -ForegroundColor $ColorInfo
    Write-Host ""
}

# Main installation flow
function Start-Installation {
    Write-Header
    
    if ($Help) {
        Show-Help
        return
    }
    
    # Check if running as administrator for system-wide installation
    if (!$Portable -and $InstallPath.StartsWith("$env:ProgramFiles") -and !(Test-Administrator)) {
        Write-Error "Administrator privileges required for system-wide installation."
        Write-Host "Please run PowerShell as Administrator or use -Portable for user installation." -ForegroundColor $ColorWarning
        return
    }
    
    # Get system architecture
    $arch = Get-SystemArchitecture
    Write-Step "Detected architecture: $arch"
    
    # Get latest version info
    $versionInfo = Get-LatestVersion
    if (!$versionInfo) {
        Write-Error "Unable to get version information. Installation aborted."
        return
    }
    
    Write-Step "Latest version: $($versionInfo.version)"
    Write-Step "Build date: $($versionInfo.buildDate)"
    
    # Determine download URLs and installer type
    $isInstallerAvailable = $false
    $downloadUrl = ""
    $fileName = ""
    
    if (!$Portable -and $arch -eq "x64" -and $versionInfo.packageTypes -contains "exe") {
        # Try to use the installer first
        $fileName = "AutomatonStudioDesktop-win-x64-$($versionInfo.version)-Setup.exe"
        $downloadUrl = "$DownloadUrl/latest/$fileName"
        $isInstallerAvailable = $true
    }
    else {
        # Use portable ZIP
        $fileName = "AutomatonStudioDesktop-win-$arch-$($versionInfo.version).zip"
        $downloadUrl = "$DownloadUrl/latest/$fileName"
        $Portable = $true
    }
    
    Write-Step "Installation method: $(if ($isInstallerAvailable) { 'Installer (EXE)' } else { 'Portable (ZIP)' })"
    Write-Step "Download URL: $downloadUrl"
    
    # Create temporary directory for download
    $tempDir = Join-Path $env:TEMP "AutomatonStudio-Install"
    if (!(Test-Path $tempDir)) {
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    }
    
    $downloadPath = Join-Path $tempDir $fileName
    
    # Download the package
    if (!(Download-File -Url $downloadUrl -OutputPath $downloadPath)) {
        Write-Error "Download failed. Installation aborted."
        return
    }
    
    # Verify the download (if checksums are available)
    # TODO: Add checksum verification
    
    # Install the application
    if (!(Install-Desktop -InstallerPath $downloadPath -IsPortable $Portable)) {
        Write-Error "Installation failed."
        return
    }
    
    # Clean up temporary files
    try {
        Remove-Item $downloadPath -Force
        Write-Step "Cleaned up temporary files"
    }
    catch {
        Write-Warning "Failed to clean up temporary file: $downloadPath"
    }
    
    # Show completion message
    $executablePath = if ($Portable) {
        Join-Path $InstallPath "Automaton.Studio.Desktop.exe"
    } else {
        Join-Path $env:ProgramFiles "AutomatonStudio\Automaton.Studio.Desktop.exe"
    }
    
    Show-CompletionMessage -ExecutablePath $executablePath
}

# Run the installation
Start-Installation