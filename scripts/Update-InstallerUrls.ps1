param(
    [Parameter(Mandatory=$true)]
    [string]$ProductionUrl = "https://api.automaton.monster"
)

Write-Host "Updating installer scripts with production URL: $ProductionUrl"

# Update install.sh
$installShPath = "src/Automaton.Studio.Web/wwwroot/downloads/install.sh"
if (Test-Path $installShPath) {
    $content = Get-Content $installShPath -Raw
    $content = $content -replace 'SERVER_URL="https://localhost:7091"', "SERVER_URL=`"$ProductionUrl`""
    Set-Content $installShPath $content -NoNewline
    Write-Host "Updated $installShPath"
}

# Update install.ps1 (assuming it exists)
$installPs1Path = "src/Automaton.Studio.Web/wwwroot/downloads/install.ps1"
if (Test-Path $installPs1Path) {
    $content = Get-Content $installPs1Path -Raw
    $content = $content -replace '\$ServerUrl = "https://localhost:7091"', "`$ServerUrl = `"$ProductionUrl`""
    Set-Content $installPs1Path $content -NoNewline
    Write-Host "Updated $installPs1Path"
}

Write-Host "Installer URL updates completed"
