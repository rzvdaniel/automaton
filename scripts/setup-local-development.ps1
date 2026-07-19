[CmdletBinding()]
param(
    [switch]$Reset,
    [switch]$SkipBuild,
    [switch]$ProvisionTestRunner,
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
    Write-Warning 'Resetting the runtime-local stack will remove its containers, volumes, and hosted-runner containers.'
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

if ($ProvisionTestRunner) {
    Write-Warning 'ProvisionTestRunner requires an existing local administrator account. The script will use the real login, plan-assignment, and hosted-runner API workflow.'
    $email = Read-Host 'Local administrator email'
    $securePassword = Read-Host 'Local administrator password' -AsSecureString
    $passwordPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try {
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPtr)
        $loginBody = @{ email = $email; password = $password } | ConvertTo-Json
        $login = Invoke-RestMethod -Uri 'https://api.automaton.test:8443/api/Account/LoginUser' -Method Post -ContentType 'application/json' -Body $loginBody -SkipCertificateCheck -TimeoutSec 30
        $headers = @{ Authorization = "Bearer $($login.accessToken)" }
        $users = Invoke-RestMethod -Uri 'https://api.automaton.test:8443/api/admin/subscription-plans/users' -Headers $headers -SkipCertificateCheck -TimeoutSec 30
        $user = @($users | Where-Object { $_.email -eq $email }) | Select-Object -First 1
        if (-not $user) { throw "No local user was found for '$email'. Create the account first, then rerun with -ProvisionTestRunner." }
        $plans = Invoke-RestMethod -Uri 'https://api.automaton.test:8443/api/admin/subscription-plans' -Headers $headers -SkipCertificateCheck -TimeoutSec 30
        $plan = @($plans | Where-Object { $_.isDefault -eq $true -and $_.isActive -eq $true }) | Select-Object -First 1
        if (-not $plan) { throw 'No active default subscription plan was found.' }
        if ($user.subscriptionPlanId -ne $plan.id) {
            $assignBody = @{ planId = $plan.id } | ConvertTo-Json
            Invoke-RestMethod -Uri "https://api.automaton.test:8443/api/admin/subscription-plans/users/$($user.userId)/assign" -Method Post -Headers $headers -ContentType 'application/json' -Body $assignBody -SkipCertificateCheck -TimeoutSec 30 | Out-Null
        } else {
            Write-Host "[OK] $email already has the active default plan; no plan mutation was sent." -ForegroundColor Green
        }
        Invoke-RestMethod -Uri "https://api.automaton.test:8443/api/admin/hosted-runners/users/$($user.userId)/provision" -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' -SkipCertificateCheck -TimeoutSec 30 | Out-Null
        $deadline = (Get-Date).AddMinutes(3)
        do {
            Start-Sleep -Seconds 5
            $runners = Invoke-RestMethod -Uri 'https://api.automaton.test:8443/api/admin/hosted-runners/runners' -Headers $headers -SkipCertificateCheck -TimeoutSec 30
            $runner = @($runners | Where-Object { $_.userId -eq $user.userId }) | Select-Object -First 1
        } while (($null -eq $runner -or $runner.status -notin @('running', 'failed')) -and (Get-Date) -lt $deadline)
        if ($null -eq $runner -or $runner.status -ne 'running') { throw "Hosted runner did not reach running state. Current status: $($runner.status)" }
        Write-Host "[OK] Hosted runner is running for $email." -ForegroundColor Green
    } finally {
        if ($passwordPtr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPtr) }
        $password = $null
    }
}

Write-Host ''
Write-Host 'Local Automaton environment is ready.' -ForegroundColor Green
Write-Host 'Studio:  https://studio.automaton.test:8443'
Write-Host 'API:     https://api.automaton.test:8443/healthz'
Write-Host 'Apps:    https://apps.automaton.test:8443'
Write-Host 'RabbitMQ: http://localhost:15673'
Write-Host 'smtp4dev: http://localhost:3001'
