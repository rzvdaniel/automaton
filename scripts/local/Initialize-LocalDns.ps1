[CmdletBinding()]
param(
    [switch]$NoHostsFileChanges
)

$ErrorActionPreference = 'Stop'
$requiredHostnames = @('api.automaton.test', 'studio.automaton.test', 'apps.automaton.test', 'website.automaton.test')
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$content = Get-Content -LiteralPath $hostsPath -ErrorAction Stop
$mappedHostnames = @(
    foreach ($line in $content) {
        $withoutComment = ($line -split '#', 2)[0].Trim()
        if ([string]::IsNullOrWhiteSpace($withoutComment)) { continue }
        $tokens = @($withoutComment -split '\s+' | Where-Object { $_ })
        if ($tokens.Count -ge 2 -and $tokens[0] -in @('127.0.0.1', '::1')) {
            $tokens[1..($tokens.Count - 1)]
        }
    }
)

$missing = @($requiredHostnames | Where-Object { $_ -notin $mappedHostnames })
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

Add-Content -LiteralPath $hostsPath -Value "`r`n# Automaton local runtime`r`n$(($missing | ForEach-Object { "127.0.0.1 $_" }) -join "`r`n")"
Write-Host '[OK] Added Automaton local runtime host entries.' -ForegroundColor Green
