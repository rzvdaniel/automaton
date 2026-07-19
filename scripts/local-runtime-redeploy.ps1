param(
    [switch]$NoBuild,
    [string[]]$Services = @("api", "worker", "studio", "website", "runner")
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot "docker-compose.runtime-local.yml"
$runtimeRoot = Join-Path $repoRoot ".local-runtime"
$certRoot = Join-Path $runtimeRoot "certs"
$traefikDynamicRoot = Join-Path $runtimeRoot "traefik-dynamic"
$localEdgeConfig = Join-Path $repoRoot "docker\traefik\local-runtime\edge.yml"
$certFile = Join-Path $certRoot "automaton.test.pem"
$certKeyFile = Join-Path $certRoot "automaton.test-key.pem"

if (-not $env:LOCAL_RUNTIME_MYSQL_PORT) {
    $env:LOCAL_RUNTIME_MYSQL_PORT = "3311"
}

New-Item -ItemType Directory -Force -Path $certRoot | Out-Null
New-Item -ItemType Directory -Force -Path $traefikDynamicRoot | Out-Null
Copy-Item -LiteralPath $localEdgeConfig -Destination (Join-Path $traefikDynamicRoot "local-edge.yml") -Force

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
$servicesToBuild = @($Services | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })

if ($servicesToBuild.Count -eq 0) {
    throw "At least one service name must be provided when using -Services."
}

Write-Host "[INFO] Using LOCAL_RUNTIME_MYSQL_PORT=$($env:LOCAL_RUNTIME_MYSQL_PORT)" -ForegroundColor Cyan
Write-Host "[INFO] Build services: $($servicesToBuild -join ', ')" -ForegroundColor Cyan

if (-not $NoBuild) {
    Write-Host "[INFO] Building selected runtime-local images sequentially" -ForegroundColor Cyan
    foreach ($service in $servicesToBuild) {
        Write-Host "[INFO] Building service: $service" -ForegroundColor Cyan
        docker @composeArgs build $service
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose build failed for service '$service' with exit code $LASTEXITCODE"
        }
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
Write-Host "[INFO] Managed hosted runners are enabled for runtime-local." -ForegroundColor Yellow
Write-Host "       Deploying a backend-capable app can provision a runner container automatically."
