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

$composeConfig = docker compose -f $composeFile config --format json | ConvertFrom-Json
$mysqlRootPassword = $composeConfig.services.mysql.environment.MYSQL_ROOT_PASSWORD
if ([string]::IsNullOrWhiteSpace($mysqlRootPassword)) {
    throw 'The effective runtime-local MySQL root password could not be read from Docker Compose configuration.'
}
$bootstrapSql = @'
SELECT CONCAT(
  (SELECT COUNT(*) FROM RunnerHosts WHERE Name = 'runtime-local-docker' AND Provider = 'local' AND ProvisioningTransport = 'localDocker' AND Status = 'active' AND ProvisioningStatus = 'ready' AND HostedRunnerSchedulingEnabled = 1),
  '|', (SELECT COUNT(*) FROM SubscriptionPlans WHERE IsDefault = 1 AND IsActive = 1),
  '|', (SELECT COUNT(*) FROM HostedRunnerSpecifications WHERE Code = 'free' AND IsActive = 1),
  '|', (SELECT COUNT(*) FROM HostedRunnerPlatformSettings WHERE ProvisioningEnabled = 1)
);
'@
$bootstrapState = ($bootstrapSql | docker compose -f $composeFile exec -T mysql mysql -uroot "-p$mysqlRootPassword" -D automaton -N -B 2>$null).Trim()
if ($bootstrapState -ne '1|1|1|1') {
    throw "Local API bootstrap state is incomplete. Expected host, default plan, free specification, and provisioning enabled; got '$bootstrapState'."
}

Write-Host '[OK] API, Studio, Website, Traefik, and required runtime services are ready.' -ForegroundColor Green
Write-Host '[OK] Local runner host, seeded plan/specification, and provisioning setting are valid.' -ForegroundColor Green
