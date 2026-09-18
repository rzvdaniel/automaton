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
    Write-Warning 'ProvisionTestRunner requires an existing local administrator account and a non-production eligible Infrastructure Runner host. It does not change the account plan or reprovision an existing Hosted Runner.'

    $composeConfigJson = docker compose -f $composeFile config --format json
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the effective runtime-local Compose configuration.' }
    $composeConfig = ($composeConfigJson -join [Environment]::NewLine) | ConvertFrom-Json
    $hostedRunnerImage = [string]$composeConfig.services.api.environment.HostedRunners__DockerImage
    if ($hostedRunnerImage -notmatch '^.+@sha256:[a-f0-9]{64}$') {
        throw 'Set LOCAL_RUNTIME_HOSTED_RUNNER_IMAGE to an immutable repository@sha256:<64-hex-digest> image before provisioning a test Hosted Runner.'
    }

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
        $plan = @($plans | Where-Object { $_.id -eq $user.subscriptionPlanId -and $_.isActive -eq $true }) | Select-Object -First 1
        if (-not $plan) { throw 'The account must already have an active subscription plan. This setup helper does not change plan assignments.' }
        $hostedRunnerCapability = $plan.capabilities.hostedRunner
        if (-not $hostedRunnerCapability.enabled -or -not $hostedRunnerCapability.specificationId) {
            throw "The account's existing plan '$($plan.name)' does not have an enabled Hosted Runner capability and specification. Assign a suitable test plan explicitly before retrying."
        }

        $specifications = Invoke-RestMethod -Uri 'https://api.automaton.test:8443/api/admin/hosted-runner-specifications' -Headers $headers -SkipCertificateCheck -TimeoutSec 30
        $specification = @($specifications | Where-Object { $_.id -eq $hostedRunnerCapability.specificationId -and $_.isActive -eq $true }) | Select-Object -First 1
        if (-not $specification) { throw 'The account plan must reference an active Hosted Runner specification before provisioning.' }

        $hosts = Invoke-RestMethod -Uri 'https://api.automaton.test:8443/api/admin/hosted-runners/hosts' -Headers $headers -SkipCertificateCheck -TimeoutSec 30
        $schedulableHosts = @($hosts | Where-Object {
            $_.isSchedulingEligible -and
                $_.provisioningTransport -eq 'infrastructureRunner' -and
                $_.infrastructureRunnerId -and
                $_.approvedAtUtc
        })
        if ($schedulableHosts.Count -eq 0) {
            throw 'No eligible non-production Infrastructure Runner host is available. Enroll an Infrastructure Runner, then validate and approve its host.'
        }

        $incompatibleHosts = @($schedulableHosts | Where-Object {
            $protocol = 0
            $hasProtocol = [int]::TryParse([string]$_.infrastructureRunnerApiCompatibilityVersion, [ref]$protocol)
            $_.infrastructureRunnerStatus -ne 'Online' -or
                -not ($hasProtocol -and $protocol -ge 4) -or
                $_.infrastructureRunnerCapabilities -notcontains 'HostContainerExecution' -or
                [string]::IsNullOrWhiteSpace([string]$_.privateIp) -or
                ($_.runtimeConnectivityMode -ne 'localDockerNetwork' -and [string]::IsNullOrWhiteSpace([string]$_.publicIp))
        })
        if ($incompatibleHosts.Count -gt 0) {
            $hostNames = ($incompatibleHosts | ForEach-Object { $_.name }) -join ', '
            throw "These schedulable local Runner Hosts are not ready for Hosted Runner execution: $hostNames. Each must have a private runtime address and an online protocol-4 Infrastructure Runner with HostContainerExecution."
        }
        $eligibleHost = $schedulableHosts | Select-Object -First 1

        $existingRunners = Invoke-RestMethod -Uri 'https://api.automaton.test:8443/api/admin/hosted-runners/runners' -Headers $headers -SkipCertificateCheck -TimeoutSec 30
        $existingRunner = @($existingRunners | Where-Object { $_.userId -eq $user.userId }) | Select-Object -First 1
        if ($existingRunner) {
            throw "A Hosted Runner already exists for '$email'. This helper will not reprovision it; inspect the Runner and use the explicit Admin Reprovision action only after confirming the eligible host and image digest."
        }

        Write-Host "[OK] Existing plan '$($plan.name)' includes active Hosted Runner specification '$($specification.name)'." -ForegroundColor Green
        Write-Host "[OK] Eligible non-production Runner Host '$($eligibleHost.name)' has an online protocol-4 Infrastructure Runner with HostContainerExecution." -ForegroundColor Green
        Write-Host '[OK] Hosted Runner image is pinned by immutable digest.' -ForegroundColor Green

        Invoke-RestMethod -Uri "https://api.automaton.test:8443/api/admin/hosted-runners/users/$($user.userId)/provision" -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' -SkipCertificateCheck -TimeoutSec 30 | Out-Null
        $deadline = (Get-Date).AddMinutes(3)
        do {
            Start-Sleep -Seconds 5
            $runners = Invoke-RestMethod -Uri 'https://api.automaton.test:8443/api/admin/hosted-runners/runners' -Headers $headers -SkipCertificateCheck -TimeoutSec 30
            $runner = @($runners | Where-Object { $_.userId -eq $user.userId }) | Select-Object -First 1
        } while (($null -eq $runner -or $runner.status -notin @('running', 'failed') -or $runner.runnerConnectivityStatus -ne 'Online') -and (Get-Date) -lt $deadline)
        if ($null -eq $runner -or $runner.status -ne 'running' -or $runner.runnerConnectivityStatus -ne 'Online') {
            throw "Hosted Runner did not reach Running and Online. Current lifecycle status: $($runner.status); connectivity: $($runner.runnerConnectivityStatus)."
        }
        Write-Host "[OK] Hosted Runner is Running and Online for $email." -ForegroundColor Green
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
