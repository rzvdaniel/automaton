# Test Service Installation with Debugging
# Run as Administrator

Write-Host "=== Automaton Service Debugging Script ===" -ForegroundColor Cyan
Write-Host ""

# Change to the workspace root directory
Set-Location (Split-Path -Parent $PSScriptRoot)

# 1. Stop and uninstall existing service
Write-Host "Step 1: Uninstalling existing service..." -ForegroundColor Yellow
& ".\scripts\setup-service.ps1" -Uninstall

Write-Host ""
Write-Host "Step 2: Reinstalling service with debug logging..." -ForegroundColor Yellow
& ".\scripts\setup-service.ps1" -RunnerPath "C:\Work\automaton\src\Automaton.Runner.Console\bin\Debug\net8.0\automaton-runner.exe"

Write-Host ""
Write-Host "Step 3: Checking NSSM configuration..." -ForegroundColor Yellow
$nssmPath = "C:\Work\automaton\src\Automaton.Runner.Console\bin\Debug\net8.0\nssm.exe"

Write-Host "  Application:" -ForegroundColor Cyan
& $nssmPath get AutomatonRunner Application

Write-Host ""
Write-Host "  Working Directory:" -ForegroundColor Cyan
$workingDir = & $nssmPath get AutomatonRunner AppDirectory
Write-Host $workingDir

Write-Host ""
Write-Host "  Environment Variables:" -ForegroundColor Cyan
$env = & $nssmPath get AutomatonRunner AppEnvironmentExtra
$env -split "`r?`n" | ForEach-Object {
    if ($_ -match "AUTOMATON_API_KEY=") {
        Write-Host "    $($_ -replace '(AUTOMATON_API_KEY=).+', '$1***REDACTED***')" -ForegroundColor Gray
    } elseif ($_ -ne "") {
        Write-Host "    $_" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "  Runner Config Files:" -ForegroundColor Cyan
$workingDirClean = ($workingDir -replace "`r", "" -replace "`n", "").Trim()
if ($workingDirClean) {
    $configDir = Join-Path $workingDirClean "config"
    if (Test-Path $configDir) {
        Write-Host "    Config directory exists: $configDir" -ForegroundColor Gray
        $runnerConfigFile = Join-Path $configDir "runner.json"
        if (Test-Path $runnerConfigFile) {
            Write-Host "    ? runner.json found" -ForegroundColor Green
            $runnerConfig = Get-Content $runnerConfigFile -Raw | ConvertFrom-Json
            Write-Host "      Runner Name: $($runnerConfig.RunnerName)" -ForegroundColor Gray
            Write-Host "      Runner ID: $($runnerConfig.RunnerId)" -ForegroundColor Gray
        } else {
            Write-Host "    ? runner.json NOT found (service uses environment variables)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "    ? Config directory NOT found: $configDir (service uses environment variables)" -ForegroundColor Yellow
    }
} else {
    Write-Host "    ?? Could not determine working directory" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Step 4: Starting service and monitoring logs..." -ForegroundColor Yellow
Start-Service AutomatonRunner
Start-Sleep 3

Write-Host ""
Write-Host "  Service Status:" -ForegroundColor Cyan
Get-Service AutomatonRunner | Format-List Name, Status, StartType

Write-Host ""
Write-Host "  Log Files Location:" -ForegroundColor Cyan
Write-Host "    C:\Program Files\AutomatonRunner\logs\service-output.log" -ForegroundColor Gray
Write-Host "    C:\Program Files\AutomatonRunner\logs\service-error.log" -ForegroundColor Gray

Write-Host ""
Write-Host "Step 5: Showing last 30 lines of output log..." -ForegroundColor Yellow
Get-Content "C:\Program Files\AutomatonRunner\logs\service-output.log" -Tail 30

Write-Host ""
Write-Host "=== Debugging Complete ===" -ForegroundColor Green
Write-Host "Press Enter to watch live logs (Ctrl+C to exit)..."
Read-Host

Get-Content "C:\Program Files\AutomatonRunner\logs\service-output.log" -Tail 50 -Wait
