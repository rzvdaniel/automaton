[CmdletBinding()]
param(
    [switch]$Reset,
    [switch]$SkipBuild,
    [switch]$NoHostsFileChanges,
    [switch]$ResetCertificates
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot 'docker-compose.runtime-local.yml'
$localRoot = Join-Path $repoRoot '.local-runtime'
$helpers = Join-Path $PSScriptRoot 'local'

& (Join-Path $helpers 'Test-LocalPrerequisites.ps1')
& (Join-Path $helpers 'Initialize-LocalDns.ps1') -NoHostsFileChanges:$NoHostsFileChanges
& (Join-Path $helpers 'Initialize-LocalCertificates.ps1') -RepositoryRoot $repoRoot | Out-Null

if ($Reset) {
    Write-Warning 'Resetting the runtime-local stack will remove its containers, volumes, and explicitly labelled local Runner containers.'
    docker compose -f $composeFile down --volumes --remove-orphans
    $runnerNames = @(docker ps -a --filter 'label=automaton.environment=runtime-local' --filter 'label=automaton.compose-project=automaton-runtime-local' --format '{{.Names}}')
    foreach ($runnerName in $runnerNames) {
        if ($runnerName) { docker rm -f $runnerName | Out-Null }
    }
    $dynamicRoot = Join-Path $localRoot 'traefik-dynamic'
    if (Test-Path -LiteralPath $dynamicRoot) {
        Get-ChildItem -LiteralPath $dynamicRoot -Force | Remove-Item -Force -Recurse
    }
    if ($ResetCertificates) {
        Remove-Item -LiteralPath (Join-Path $localRoot 'certs') -Force -Recurse -ErrorAction SilentlyContinue
        & (Join-Path $helpers 'Initialize-LocalCertificates.ps1') -RepositoryRoot $repoRoot | Out-Null
    }
}

$buildServices = @('api', 'worker', 'studio', 'website', 'runner')
$redeployParams = @{ Services = $buildServices }
if ($SkipBuild) { $redeployParams.NoBuild = $true }
& (Join-Path $PSScriptRoot 'local-runtime-redeploy.ps1') @redeployParams
& (Join-Path $helpers 'Test-LocalRuntime.ps1') -RepositoryRoot $repoRoot

Write-Host ''
Write-Host 'Local Automaton environment is ready.' -ForegroundColor Green
Write-Host 'Studio:  https://studio.automaton.test:8443'
Write-Host 'API:     https://api.automaton.test:8443/healthz'
Write-Host 'Apps:    https://apps.automaton.test:8443'
Write-Host 'RabbitMQ: http://localhost:15673'
Write-Host 'smtp4dev: http://localhost:3001'

Write-Host 'Install a Machine Management Runner using Studio, then explicitly assign an app deployment target. Per-user Hosted provisioning is retired.'
