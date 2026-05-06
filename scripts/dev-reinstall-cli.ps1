param(
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

if (Test-Path $packageOutput) {
    Write-Host "Removing existing package output: $packageOutput"
    Remove-Item $packageOutput -Recurse -Force
}

Push-Location $repoRoot
try {
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

    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-cli" value="$packageOutput" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $tempNuGetConfig -Encoding UTF8

    Write-Host "Uninstalling existing global tool..."
    & dotnet tool uninstall --global Automaton.Cli | Out-Host

    Write-Host "Installing freshly packed global tool version $packageVersion from local package source only..."
    & dotnet tool install --global --configfile $tempNuGetConfig --add-source $packageOutput --version $packageVersion Automaton.Cli
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool install failed with exit code $LASTEXITCODE"
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