[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or newer is required.'
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker CLI was not found. Install Docker Desktop with Linux containers enabled.'
}

if (-not (Get-Command mkcert -ErrorAction SilentlyContinue)) {
    throw 'mkcert was not found. Install mkcert and run mkcert -install.'
}

$dockerOs = docker info --format '{{.OSType}}' 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Desktop is not running or the current user cannot access Docker.'
}

if ($dockerOs.Trim() -ne 'linux') {
    throw "Docker is using '$($dockerOs.Trim())' containers. Switch Docker Desktop to Linux containers."
}

$compose = docker compose version 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Compose is not available.'
}

Write-Host '[OK] PowerShell, Docker Desktop, Linux containers, Docker Compose, and mkcert are available.' -ForegroundColor Green
