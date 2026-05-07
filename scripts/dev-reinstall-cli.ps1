param(
    [ValidateSet("install", "update", "reinstall", "uninstall", "status")]
    [string]$Action = "reinstall",
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageOutput = Join-Path $repoRoot ".tmp\cli-pack"
$cliProject = Join-Path $repoRoot "src\Automaton.Cli\Automaton.Cli.csproj"
$cliTests = Join-Path $repoRoot "src\UnitTests\Automaton.Cli.Tests\Automaton.Cli.Tests.csproj"
$tempNuGetConfig = Join-Path ([System.IO.Path]::GetTempPath()) ("automaton-cli-local-source-" + [guid]::NewGuid().ToString("N") + ".config")

Write-Host "Repo root: $repoRoot"

function New-TempNuGetConfig {
    param(
        [string]$Path,
        [string]$PackageSource
    )

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-cli" value="$PackageSource" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $Path -Encoding UTF8
}

function Get-InstalledCliVersion {
    $toolList = & dotnet tool list --global 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($toolList)) {
        return $null
    }

    $match = $toolList | Select-String -Pattern '^automaton\.cli\s+(?<version>\S+)'
    if (-not $match) {
        return $null
    }

    return $match.Matches[0].Groups['version'].Value
}

function Remove-InstalledCli {
    $installedVersion = Get-InstalledCliVersion
    if ([string]::IsNullOrWhiteSpace($installedVersion)) {
        Write-Host "Automaton.Cli is not installed globally."
        return
    }

    Write-Host "Uninstalling global Automaton.Cli $installedVersion..."
    & dotnet tool uninstall --global Automaton.Cli | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool uninstall failed with exit code $LASTEXITCODE"
    }
}

function Build-LocalCliPackage {
    if (Test-Path $packageOutput) {
        Write-Host "Removing existing package output: $packageOutput"
        Remove-Item $packageOutput -Recurse -Force
    }

    if (-not $SkipTests) {
        Write-Host "Running CLI tests..."
        & dotnet test $cliTests --configuration $Configuration --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed with exit code $LASTEXITCODE"
        }
    }

    Write-Host "Packing CLI..."
    & dotnet pack $cliProject --configuration $Configuration --output $packageOutput
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed with exit code $LASTEXITCODE"
    }

    $nupkg = Get-ChildItem $packageOutput -Filter "Automaton.Cli.*.nupkg" | Select-Object -First 1
    if (-not $nupkg) {
        throw "No Automaton.Cli nupkg was produced in $packageOutput"
    }

    $packageVersion = [System.Text.RegularExpressions.Regex]::Match($nupkg.BaseName, '^Automaton\.Cli\.(.+)$').Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($packageVersion)) {
        throw "Could not determine package version from $($nupkg.Name)"
    }

    return $packageVersion
}

function Install-OrUpdateCli {
    param(
        [string]$PackageVersion,
        [bool]$ForceReinstall
    )

    New-TempNuGetConfig -Path $tempNuGetConfig -PackageSource $packageOutput

    $installedVersion = Get-InstalledCliVersion
    if ($ForceReinstall -and -not [string]::IsNullOrWhiteSpace($installedVersion)) {
        Remove-InstalledCli
        $installedVersion = $null
    }

    if ([string]::IsNullOrWhiteSpace($installedVersion)) {
        Write-Host "Installing local Automaton.Cli $PackageVersion from $packageOutput..."
        & dotnet tool install --global --configfile $tempNuGetConfig --add-source $packageOutput --version $PackageVersion Automaton.Cli
    }
    else {
        Write-Host "Updating global Automaton.Cli from $installedVersion to $PackageVersion using local package source..."
        & dotnet tool update --global --configfile $tempNuGetConfig --add-source $packageOutput --version $PackageVersion Automaton.Cli
    }

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool install/update failed with exit code $LASTEXITCODE"
    }
}

function Show-CliStatus {
    $installedVersion = Get-InstalledCliVersion
    if ([string]::IsNullOrWhiteSpace($installedVersion)) {
        Write-Host "Global tool: not installed"
    }
    else {
        Write-Host "Installed global tool package version: $installedVersion"
        Write-Host "CLI reported version:"
        & automaton --version
        if ($LASTEXITCODE -ne 0) {
            throw "automaton --version failed with exit code $LASTEXITCODE"
        }
    }

    if (Test-Path $packageOutput) {
        $localPackage = Get-ChildItem $packageOutput -Filter "Automaton.Cli.*.nupkg" | Select-Object -First 1
        if ($localPackage) {
            Write-Host "Local package: $($localPackage.Name)"
        }
    }
}

Push-Location $repoRoot
try {
    switch ($Action) {
        "uninstall" {
            Remove-InstalledCli
            return
        }

        "status" {
            Show-CliStatus
            return
        }

        "install" {
            $packageVersion = Build-LocalCliPackage
            Install-OrUpdateCli -PackageVersion $packageVersion -ForceReinstall:$false
        }

        "update" {
            $packageVersion = Build-LocalCliPackage
            Install-OrUpdateCli -PackageVersion $packageVersion -ForceReinstall:$false
        }

        "reinstall" {
            $packageVersion = Build-LocalCliPackage
            Install-OrUpdateCli -PackageVersion $packageVersion -ForceReinstall:$true
        }
    }

    Write-Host "Installed CLI version:"
    & automaton --version
    if ($LASTEXITCODE -ne 0) {
        throw "automaton --version failed with exit code $LASTEXITCODE"
    }
}
finally {
    if (Test-Path $tempNuGetConfig) {
        Remove-Item $tempNuGetConfig -Force
    }

    Pop-Location
}