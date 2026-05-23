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
$script:LastPackedPackagePath = $null

Write-Host "Repo root: $repoRoot"

function Write-Section {
    param([string]$Title)

    Write-Host ""
    Write-Host ("=" * 72)
    Write-Host $Title
    Write-Host ("=" * 72)
}

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

function Get-CliBasePackageVersion {
    [xml]$projectXml = Get-Content -Path $cliProject -Raw
    $versionPrefix = $projectXml.Project.PropertyGroup | Where-Object { $_.VersionPrefix } | Select-Object -ExpandProperty VersionPrefix -First 1

    if ([string]::IsNullOrWhiteSpace($versionPrefix)) {
        throw "Could not determine CLI VersionPrefix from $cliProject"
    }

    return $versionPrefix.Trim()
}

function Get-LocalPackageVersion {
    $baseVersion = Get-CliBasePackageVersion
    $utcNow = (Get-Date).ToUniversalTime()
    return "$baseVersion.$($utcNow.ToString("yyyyMMdd")).$($utcNow.ToString("HHmmss"))"
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
    $packageVersion = Get-LocalPackageVersion

    if (Test-Path $packageOutput) {
        Write-Host "Removing existing package output: $packageOutput"
        Remove-Item $packageOutput -Recurse -Force
    }

    if (-not $SkipTests) {
        Write-Host "Running CLI tests..."
        & dotnet test $cliTests --configuration $Configuration --verbosity minimal | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed with exit code $LASTEXITCODE"
        }
    }

    Write-Section "Packing Local CLI"
    Write-Host "Package version: $packageVersion"
    Write-Host "Output folder:    $packageOutput"
    & dotnet pack $cliProject --configuration $Configuration --output $packageOutput -p:Version=$packageVersion | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed with exit code $LASTEXITCODE"
    }

    $nupkg = Get-ChildItem $packageOutput -Filter "Automaton.Cli.$packageVersion.nupkg" | Select-Object -First 1
    if (-not $nupkg) {
        throw "No Automaton.Cli nupkg was produced in $packageOutput for version $packageVersion"
    }

    $script:LastPackedPackagePath = $nupkg.FullName
    Write-Host "Packed nupkg:     $($nupkg.FullName)"

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
        Write-Section "Installing Local CLI"
        Write-Host "Installing exact package version: $PackageVersion"
        Write-Host "Command:"
        Write-Host "  dotnet tool install --global --configfile `"$tempNuGetConfig`" --add-source `"$packageOutput`" --version $PackageVersion Automaton.Cli"
        & dotnet tool install --global --configfile $tempNuGetConfig --add-source $packageOutput --version $PackageVersion Automaton.Cli
    }
    else {
        Write-Section "Updating Local CLI"
        Write-Host "Installed version:          $installedVersion"
        Write-Host "Target exact package ver.:  $PackageVersion"
        Write-Host "Command:"
        Write-Host "  dotnet tool update --global --configfile `"$tempNuGetConfig`" --add-source `"$packageOutput`" --version $PackageVersion Automaton.Cli"
        & dotnet tool update --global --configfile $tempNuGetConfig --add-source $packageOutput --version $PackageVersion Automaton.Cli
    }

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool install/update failed with exit code $LASTEXITCODE"
    }
}

function Show-CliStatus {
    Write-Section "CLI Status"

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

    Write-Section "Installed CLI Version"
    if ($script:LastPackedPackagePath) {
        Write-Host "Last packed nupkg: $script:LastPackedPackagePath"
    }
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
