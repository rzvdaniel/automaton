[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$composeFile = Join-Path $RepositoryRoot 'docker-compose.runtime-local.yml'
$expectedEndpoints = @(
    @{ Name = 'API'; Uri = 'https://api.automaton.test:8443/healthz' },
    @{ Name = 'Studio'; Uri = 'https://studio.automaton.test:8443' },
    @{ Name = 'Website'; Uri = 'https://website.automaton.test:8443' },
    @{ Name = 'Traefik'; Uri = 'http://localhost:8082/ping' }
)

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
do {
    $apiReady = $false
    try {
        $response = Invoke-WebRequest -Uri $expectedEndpoints[0].Uri -UseBasicParsing -SkipCertificateCheck -TimeoutSec 10
        $apiReady = $response.StatusCode -eq 200
    } catch { }
    if ($apiReady) { break }
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline)

if (-not $apiReady) { throw "API did not become ready within $TimeoutSeconds seconds." }

foreach ($endpoint in $expectedEndpoints) {
    $url = $endpoint.Uri
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -SkipCertificateCheck -TimeoutSec 10
        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) { throw "HTTP $($response.StatusCode)" }
    } catch {
        throw "Local runtime endpoint failed: $($endpoint.Name) $url ($($_.Exception.Message))"
    }
}

$services = docker compose -f $composeFile ps --status running --services
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the runtime-local Compose stack.' }
$requiredServices = @('api', 'worker', 'studio', 'website', 'traefik', 'mysql', 'rabbitmq', 'smtp4dev')
$missing = @($requiredServices | Where-Object { $_ -notin $services })
if ($missing.Count -gt 0) { throw "Required runtime services are not running: $($missing -join ', ')" }

Write-Host '[OK] API, Studio, Website, Traefik, and required runtime services are ready.' -ForegroundColor Green
Write-Host '[INFO] Hosted Runner provisioning is not validated by this endpoint check.' -ForegroundColor Yellow
Write-Host '[INFO] Use setup-local-development.ps1 -ProvisionTestRunner to verify the plan, image digest, and eligible Infrastructure Runner host before provisioning.'
