param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot "docker-compose.runtime-local.yml"
$runtimeRoot = Join-Path $repoRoot ".local-runtime"
$certRoot = Join-Path $runtimeRoot "certs"
$staticRuntimeRoot = Join-Path $runtimeRoot "static-runtime"
$traefikDynamicRoot = Join-Path $runtimeRoot "traefik-dynamic"
$certFile = Join-Path $certRoot "automaton.test.pem"
$certKeyFile = Join-Path $certRoot "automaton.test-key.pem"

if (-not $env:LOCAL_RUNTIME_MYSQL_PORT) {
    $env:LOCAL_RUNTIME_MYSQL_PORT = "3311"
}

New-Item -ItemType Directory -Force -Path $certRoot | Out-Null
New-Item -ItemType Directory -Force -Path $staticRuntimeRoot | Out-Null
New-Item -ItemType Directory -Force -Path $traefikDynamicRoot | Out-Null

if (-not (Test-Path $certFile) -or -not (Test-Path $certKeyFile)) {
    Write-Host "[ERROR] Missing local TLS certificate files:" -ForegroundColor Red
    Write-Host "  $certFile"
    Write-Host "  $certKeyFile"
    Write-Host ""
    Write-Host "Generate them with:" -ForegroundColor Yellow
    Write-Host "  mkcert -cert-file `"$certFile`" -key-file `"$certKeyFile`" api.automaton.test studio.automaton.test apps.automaton.test website.automaton.test"
    exit 1
}

$composeArgs = @("compose", "-f", $composeFile)

Write-Host "[INFO] Using LOCAL_RUNTIME_MYSQL_PORT=$($env:LOCAL_RUNTIME_MYSQL_PORT)" -ForegroundColor Cyan

if (-not $NoBuild) {
    Write-Host "[INFO] Building runtime-local images" -ForegroundColor Cyan
    docker @composeArgs build
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose build failed with exit code $LASTEXITCODE"
    }
}

Write-Host "[INFO] Starting runtime-local stack" -ForegroundColor Cyan
docker @composeArgs up -d --remove-orphans
if ($LASTEXITCODE -ne 0) {
    throw "docker compose up failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "[INFO] Runtime-local stack is up" -ForegroundColor Green
Write-Host "Studio:  https://studio.automaton.test:8443"
Write-Host "API:     https://api.automaton.test:8443/healthz"
Write-Host "Apps:    https://apps.automaton.test:8443/healthz"
Write-Host "Website: https://website.automaton.test:8443"
Write-Host "RabbitMQ management: http://localhost:15673"
Write-Host "smtp4dev:           http://localhost:3001"
Write-Host "Traefik ping:       http://localhost:8082/ping"
Write-Host ""
Write-Host "[INFO] Runner is intentionally not started by this script." -ForegroundColor Yellow
Write-Host "       Managed runtime backend routing also requires the corresponding Runner and HostedRunner database state."
