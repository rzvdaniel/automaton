# Automaton Studio Desktop Installation Script for Windows
# Run this script in PowerShell as Administrator

param(
    [string]$InstallPath = "$env:ProgramFiles\AutomatonDesktop",
    [switch]$CreateShortcut = $true,
    [switch]$Help = $false,
    [string]$ForceArch = "",
    [string]$DownloadUrl = "https://studio.automaton.monster/downloads/desktop/latest"
)

# Colors for console output
$ColorInfo = "Green"
$ColorWarning = "Yellow"
$ColorError = "Red"
$ColorHeader = "Cyan"

function Write-Header {
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor $ColorHeader
    Write-Host "       Automaton Studio Desktop Installer (Windows)        " -ForegroundColor $ColorHeader
    Write-Host "          Visual Workflow Designer & Local Execution       " -ForegroundColor $ColorHeader
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor $ColorHeader
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
    iwr -useb https://studio.automaton.monster/downloads/desktop/latest/install.ps1 | iex
    # OR
    .\install.ps1 [OPTIONS]

OPTIONS:
    -InstallPath PATH       Installation directory (default: Program Files\AutomatonDesktop)
    -CreateShortcut         Create desktop and start menu shortcuts (default: true)
    -ForceArch ARCH         Force architecture (x64/arm64)
    -Help                   Show this help message

EXAMPLES:
    # Basic installation (recommended)
    iwr -useb https://studio.automaton.monster/downloads/desktop/latest/install.ps1 | iex

    # Install to custom directory
    .\install.ps1 -InstallPath "C:\Tools\AutomatonStudio"

    # Install without shortcuts
    .\install.ps1 -CreateShortcut:`$false

REQUIREMENTS:
    - Windows 10 version 1809 or later / Windows 11
    - PowerShell 5.0 or later
    - Administrator privileges (for Program Files installation)
    - Internet connection
    - Minimum 4GB RAM
    - 500MB available disk space

PLATFORM SUPPORT:
    - Windows x64 (Intel/AMD 64-bit) - Most Common
    - Windows ARM64 (Surface Pro X, etc.)
    - Linux and macOS are NOT supported

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
        default { return "x64" }
    }
}

function Get-DownloadUrl {
    param([string]$Architecture)
    
    switch ($Architecture) {
        "x64" { return "$DownloadUrl/AutomatonStudioDesktop-win-x64.zip" }
        "arm64" { return "$DownloadUrl/AutomatonStudioDesktop-win-arm64.zip" }
        default { 
            Write-Error "Unsupported architecture: $Architecture"
            return $null
        }
    }
}

function Test-SystemRequirements {
    Write-Step "Checking system requirements..."
    
    # Check Windows version
    $osVersion = [System.Environment]::OSVersion.Version
    if ($osVersion.Major -lt 10) {
        Write-Error "Windows 10 or later is required. Current version: $osVersion"
        return $false
    }
    
    if ($osVersion.Major -eq 10 -and $osVersion.Build -lt 17763) {
        Write-Warning "Windows 10 version 1809 or later is recommended. Current build: $($osVersion.Build)"
    }
    
    # Check available disk space
    $drive = Split-Path $InstallPath -Qualifier
    $driveInfo = Get-PSDrive $drive.TrimEnd(':')
    $freeSpaceGB = [math]::Round($driveInfo.Free / 1GB, 2)
    
    if ($freeSpaceGB -lt 1) {
        Write-Error "Insufficient disk space. Required: 1GB, Available: ${freeSpaceGB}GB"
        return $false
    }
    
    Write-Step "System requirements met (Free space: ${freeSpaceGB}GB)"
    return $true
}

function Download-AndExtract {
    param(
        [string]$Url,
        [string]$DestinationPath
    )
    
    $tempZip = Join-Path $env:TEMP "automaton-studio-desktop.zip"
    $tempExtract = Join-Path $env:TEMP "automaton-studio-desktop-extract"
    
    try {
        Write-Step "Downloading from $Url"
        Write-Host "This may take a few minutes depending on your connection..." -ForegroundColor Yellow
        
        Invoke-WebRequest -Uri $Url -OutFile $tempZip -UseBasicParsing
        
        $fileSizeMB = [math]::Round((Get-Item $tempZip).Length / 1MB, 2)
        Write-Step "Downloaded ${fileSizeMB}MB successfully"
        
        Write-Step "Extracting archive to $DestinationPath"
        if (Test-Path $tempExtract) {
            Remove-Item $tempExtract -Recurse -Force
        }
        Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force
        
        # Create destination directory
        if (!(Test-Path $DestinationPath)) {
            New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
        } else {
            Write-Warning "Installation directory already exists. Contents will be replaced."
            Remove-Item "$DestinationPath\*" -Recurse -Force -ErrorAction SilentlyContinue
        }
        
        # Copy all files to destination
        Copy-Item -Path "$tempExtract\*" -Destination $DestinationPath -Recurse -Force
        
        Write-Step "Files extracted to $DestinationPath"
        
    } catch {
        Write-Error "Failed to download or extract: $($_.Exception.Message)"
        throw
    } finally {
        # Cleanup
        if (Test-Path $tempZip) { Remove-Item $tempZip -Force -ErrorAction SilentlyContinue }
        if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Test-Installation {
    param([string]$ExecutablePath)
    
    if (!(Test-Path $ExecutablePath)) {
        Write-Error "Executable not found at: $ExecutablePath"
        return $false
    }
    
    Write-Step "Installation files verified successfully"
    return $true
}

function Create-Shortcuts {
    param([string]$ExecutablePath)
    
    if (!$CreateShortcut) {
        Write-Step "Skipping shortcut creation"
        return $true
    }
    
    try {
        $WshShell = New-Object -ComObject WScript.Shell
        
        # Create desktop shortcut
        $desktopPath = [Environment]::GetFolderPath("CommonDesktopDirectory")
        $desktopShortcut = Join-Path $desktopPath "Automaton Studio.lnk"
        
        Write-Step "Creating desktop shortcut"
        $shortcut = $WshShell.CreateShortcut($desktopShortcut)
        $shortcut.TargetPath = $ExecutablePath
        $shortcut.WorkingDirectory = Split-Path $ExecutablePath
        $shortcut.Description = "Automaton Studio Desktop - Visual Workflow Designer"
        $shortcut.Save()
        
        # Create Start Menu shortcut
        $startMenuPath = [Environment]::GetFolderPath("CommonPrograms")
        $startMenuFolder = Join-Path $startMenuPath "Automaton Studio"
        
        if (!(Test-Path $startMenuFolder)) {
            New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null
        }
        
        $startMenuShortcut = Join-Path $startMenuFolder "Automaton Studio Desktop.lnk"
        
        Write-Step "Creating Start Menu shortcut"
        $shortcut = $WshShell.CreateShortcut($startMenuShortcut)
        $shortcut.TargetPath = $ExecutablePath
        $shortcut.WorkingDirectory = Split-Path $ExecutablePath
        $shortcut.Description = "Automaton Studio Desktop - Visual Workflow Designer"
        $shortcut.Save()
        
        # Create uninstall shortcut
        $uninstallScript = Join-Path (Split-Path $ExecutablePath) "Uninstall.ps1"
        Create-UninstallScript $uninstallScript
        
        $uninstallShortcut = Join-Path $startMenuFolder "Uninstall Automaton Studio.lnk"
        $shortcut = $WshShell.CreateShortcut($uninstallShortcut)
        $shortcut.TargetPath = "powershell.exe"
        $shortcut.Arguments = "-ExecutionPolicy Bypass -File `"$uninstallScript`""
        $shortcut.WorkingDirectory = Split-Path $ExecutablePath
        $shortcut.Description = "Uninstall Automaton Studio Desktop"
        $shortcut.Save()
        
        Write-Step "Shortcuts created successfully"
        return $true
        
    } catch {
        Write-Warning "Failed to create shortcuts: $($_.Exception.Message)"
        return $false
    }
}

function Create-UninstallScript {
    param([string]$UninstallScriptPath)
    
    $uninstallContent = @"
# Automaton Studio Desktop Uninstaller
# This script removes Automaton Studio Desktop from your system

`$ErrorActionPreference = "Stop"
`$installPath = Split-Path `$PSScriptRoot
`$appName = "Automaton Studio Desktop"

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "       Automaton Studio Desktop Uninstaller                " -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "This will remove `$appName from:" -ForegroundColor Yellow
Write-Host "  `$installPath" -ForegroundColor Yellow
Write-Host ""

`$confirm = Read-Host "Continue with uninstallation? (Y/N)"
if (`$confirm -ne 'Y' -and `$confirm -ne 'y') {
    Write-Host "Uninstallation cancelled" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[INFO] Removing shortcuts..." -ForegroundColor Green

# Remove desktop shortcut
`$desktopShortcut = Join-Path ([Environment]::GetFolderPath("CommonDesktopDirectory")) "Automaton Studio.lnk"
if (Test-Path `$desktopShortcut) {
    Remove-Item `$desktopShortcut -Force
}

# Remove Start Menu folder
`$startMenuFolder = Join-Path ([Environment]::GetFolderPath("CommonPrograms")) "Automaton Studio"
if (Test-Path `$startMenuFolder) {
    Remove-Item `$startMenuFolder -Recurse -Force
}

Write-Host "[INFO] Removing application files..." -ForegroundColor Green

# Remove installation directory
try {
    Set-Location `$env:USERPROFILE
    Start-Sleep -Seconds 1
    Remove-Item `$installPath -Recurse -Force
    Write-Host ""
    Write-Host "✓ Uninstallation completed successfully!" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "[ERROR] Failed to remove installation directory: `$(`$_.Exception.Message)" -ForegroundColor Red
    Write-Host "Please manually delete: `$installPath" -ForegroundColor Yellow
}

Read-Host "Press Enter to close"
"@
    
    Set-Content -Path $UninstallScriptPath -Value $uninstallContent -Encoding UTF8
}

function Add-ToWindowsApps {
    param([string]$ExecutablePath)
    
    try {
        # Add to Windows "Add/Remove Programs" via registry
        $uninstallKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AutomatonStudioDesktop"
        
        if (!(Test-Path $uninstallKey)) {
            New-Item -Path $uninstallKey -Force | Out-Null
        }
        
        $uninstallScript = Join-Path (Split-Path $ExecutablePath) "Uninstall.ps1"
        
        Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "Automaton Studio Desktop"
        Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "1.0.0"
        Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "Automaton"
        Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value (Split-Path $ExecutablePath)
        Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$uninstallScript`""
        Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value $ExecutablePath
        Set-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -Type DWord
        Set-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -Type DWord
        
        Write-Step "Registered in Windows Apps & Features"
        
    } catch {
        Write-Warning "Failed to register in Windows Apps: $($_.Exception.Message)"
    }
}

function Show-CompletionMessage {
    param([string]$ExecutablePath)
    
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor $ColorHeader
    Write-Step "Installation completed successfully!"
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor $ColorHeader
    Write-Host ""
    Write-Host "📍 Installation Location:" -ForegroundColor $ColorInfo
    Write-Host "   $InstallPath" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "🚀 To Launch Automaton Studio Desktop:" -ForegroundColor $ColorInfo
    Write-Host "   • Double-click the desktop shortcut, or" -ForegroundColor Cyan
    Write-Host "   • Search for 'Automaton Studio' in Start Menu, or" -ForegroundColor Cyan
    Write-Host "   • Run: $ExecutablePath" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "✨ Features:" -ForegroundColor $ColorInfo
    Write-Host "   • Visual workflow designer with drag-and-drop interface" -ForegroundColor White
    Write-Host "   • Local workflow execution (no server required)" -ForegroundColor White
    Write-Host "   • Integrated workflow management" -ForegroundColor White
    Write-Host "   • Self-contained (all dependencies included)" -ForegroundColor White
    Write-Host ""
    Write-Host "📝 To Uninstall:" -ForegroundColor $ColorInfo
    Write-Host "   • Use Windows Settings → Apps → Automaton Studio Desktop, or" -ForegroundColor Cyan
    Write-Host "   • Run the uninstall shortcut from Start Menu" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "📚 Documentation: https://docs.automaton.monster" -ForegroundColor $ColorHeader
    Write-Host "💬 Support: https://github.com/rzvdaniel/automaton/issues" -ForegroundColor $ColorHeader
    Write-Host ""
}

# Main installation logic
function Main {
    if ($Help) {
        Show-Help
        return
    }

    Write-Header
    
    # Check if running as administrator (only if installing to Program Files)
    if ($InstallPath.StartsWith($env:ProgramFiles)) {
        if (!(Test-Administrator)) {
            Write-Error "Administrator privileges required for Program Files installation."
            Write-Host "Options:" -ForegroundColor Yellow
            Write-Host "  1. Run PowerShell as Administrator, or" -ForegroundColor Cyan
            Write-Host "  2. Install to a user directory: -InstallPath `"$env:USERPROFILE\Applications\AutomatonStudio`"" -ForegroundColor Cyan
            exit 1
        }
    }
    
    # Check system requirements
    if (!(Test-SystemRequirements)) {
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
    
    # Test installation
    $executablePath = Join-Path $InstallPath "Automaton.Studio.Desktop.exe"
    if (!(Test-Installation $executablePath)) {
        exit 1
    }
    
    # Create shortcuts
    Create-Shortcuts $executablePath
    
    # Add to Windows Apps
    Add-ToWindowsApps $executablePath
    
    # Show completion message
    Show-CompletionMessage $executablePath
}

# Run main function
Main
