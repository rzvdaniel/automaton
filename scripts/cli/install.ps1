# Automaton CLI installer for Windows: downloads the self-contained automaton.exe, no .NET needed.
# Usage (PowerShell):
#   irm https://github.com/rzvdaniel/automaton/releases/latest/download/install.ps1 | iex
# Options (environment): AUTOMATON_VERSION=server-v0.2.90 (default: latest), AUTOMATON_INSTALL_DIR

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repo = "rzvdaniel/automaton"
$dir = if ($env:AUTOMATON_INSTALL_DIR) { $env:AUTOMATON_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA "Automaton\bin" }
$asset = "automaton-win-x64.exe"
$base = if ($env:AUTOMATON_VERSION) { "https://github.com/$repo/releases/download/$($env:AUTOMATON_VERSION)" } else { "https://github.com/$repo/releases/latest/download" }

if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne "X64") {
    Write-Host "Only 64-bit Intel/AMD Windows has a standalone build; install with 'dotnet tool install -g Automaton.Cli' instead." -ForegroundColor Red
    exit 1
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("automaton-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    Write-Host "Downloading $asset..." -ForegroundColor Cyan
    Invoke-WebRequest "$base/$asset" -OutFile (Join-Path $tmp "automaton.exe")
    $expected = ((Invoke-WebRequest "$base/$asset.sha256").Content.ToString().Trim() -split "\s+")[0]
    $actual = (Get-FileHash (Join-Path $tmp "automaton.exe") -Algorithm SHA256).Hash
    if ($expected -ine $actual) { throw "Checksum mismatch; nothing was installed." }

    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Move-Item (Join-Path $tmp "automaton.exe") (Join-Path $dir "automaton.exe") -Force
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (($userPath -split ";") -notcontains $dir) {
    [Environment]::SetEnvironmentVariable("Path", "$userPath;$dir", "User")
    $env:Path = "$env:Path;$dir"
    Write-Host "Added $dir to your user PATH; open a new terminal to use it everywhere."
}

Write-Host "Installed: $(& (Join-Path $dir 'automaton.exe') --version | Select-Object -First 1)" -ForegroundColor Green
Write-Host "Next: automaton config --server https://your-automaton-server --key ak_execute_xxx"
