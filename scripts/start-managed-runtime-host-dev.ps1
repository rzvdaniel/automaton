$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repoRoot ".local-dev\managed-runtime"
$certRoot = Join-Path $runtimeRoot "certs"
$dynamicRoot = Join-Path $runtimeRoot "traefik-dynamic"

New-Item -ItemType Directory -Force -Path $certRoot, $dynamicRoot | Out-Null

$certFile = Join-Path $certRoot "apps.automaton.test.pem"
$keyFile = Join-Path $certRoot "apps.automaton.test-key.pem"
if (-not (Test-Path $certFile) -or -not (Test-Path $keyFile)) {
    throw "Missing local certificate files. Generate $certFile and $keyFile with mkcert before starting the managed-runtime edge."
}

docker network inspect automaton-local *> $null
if ($LASTEXITCODE -ne 0) {
    docker network create automaton-local | Out-Null
}

$env:MANAGED_LOCAL_CERTS_DIR = $certRoot
$env:MANAGED_TRAEFIK_DYNAMIC_ROOT = $dynamicRoot
docker compose -f (Join-Path $repoRoot "docker-compose.managed-local.yml") up -d

Write-Host "Managed-runtime Traefik is ready on https://apps.automaton.test:8443"
Write-Host "Start dotnet watch and npm run dev in separate terminals."
