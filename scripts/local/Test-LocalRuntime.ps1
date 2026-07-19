[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$composeFile = Join-Path $RepositoryRoot 'docker-compose.runtime-local.yml'
$baseUrls = @(
    'https://api.automaton.test:8443/healthz',
    'https://studio.automaton.test:8443',
    'https://apps.automaton.test:8443/healthz'
)

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
do {
    $apiReady = $false
    try {
        $response = Invoke-WebRequest -Uri $baseUrls[0] -UseBasicParsing -SkipCertificateCheck -TimeoutSec 10
        $apiReady = $response.StatusCode -eq 200
    } catch { }
    if ($apiReady) { break }
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline)

if (-not $apiReady) { throw "API did not become ready within $TimeoutSeconds seconds." }

foreach ($url in $baseUrls[1..2]) {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -SkipCertificateCheck -TimeoutSec 10
        if ($response.StatusCode -ge 500) { throw "HTTP $($response.StatusCode)" }
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -lt 200 -or $statusCode -ge 500) {
            throw "Local runtime endpoint failed: $url ($($_.Exception.Message))"
        }
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
$bootstrapState = ($bootstrapSql | docker exec -i automaton-runtime-local-mysql mysql -uroot "-p$mysqlRootPassword" -D automaton -N -B 2>$null).Trim()
if ($bootstrapState -ne '1|1|1|1') {
    throw "Local API bootstrap state is incomplete. Expected host, default plan, free specification, and provisioning enabled; got '$bootstrapState'."
}

Write-Host '[OK] API, Studio, Apps, and required runtime services are ready.' -ForegroundColor Green
Write-Host '[OK] Local runner host, seeded plan/specification, and provisioning setting are valid.' -ForegroundColor Green
