[CmdletBinding()]
param(
    [switch]$NoHostsFileChanges
)

$ErrorActionPreference = 'Stop'
$requiredEntries = @(
    '127.0.0.1 api.automaton.test',
    '127.0.0.1 studio.automaton.test',
    '127.0.0.1 apps.automaton.test',
    '127.0.0.1 website.automaton.test'
)
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$content = Get-Content -LiteralPath $hostsPath -ErrorAction Stop

$missing = @($requiredEntries | Where-Object { $_ -notin $content })
if ($missing.Count -eq 0) {
    Write-Host '[OK] Local Automaton host entries are present.' -ForegroundColor Green
    return
}

if ($NoHostsFileChanges) {
    throw "Missing hosts-file entries. Re-run without -NoHostsFileChanges or add them manually: $($missing -join ', ')"
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Administrator privileges are required to update $hostsPath. Re-run PowerShell as Administrator or use -NoHostsFileChanges after adding the entries manually."
}

Add-Content -LiteralPath $hostsPath -Value "`r`n# Automaton local runtime`r`n$($missing -join "`r`n")"
Write-Host '[OK] Added Automaton local runtime host entries.' -ForegroundColor Green
