[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = 'Stop'
$certRoot = Join-Path $RepositoryRoot '.local-runtime\certs'
$certFile = Join-Path $certRoot 'automaton.test.pem'
$keyFile = Join-Path $certRoot 'automaton.test-key.pem'
$domains = @('api.automaton.test', 'studio.automaton.test', 'apps.automaton.test', 'website.automaton.test')

New-Item -ItemType Directory -Force -Path $certRoot | Out-Null
mkcert -install | Out-Null

$needsCertificate = -not (Test-Path -LiteralPath $certFile) -or -not (Test-Path -LiteralPath $keyFile)
if (-not $needsCertificate) {
    $certificateText = (& mkcert -CAROOT | ForEach-Object { $_.Trim() })
    $needsCertificate = -not (Test-Path -LiteralPath (Join-Path $certificateText 'rootCA.pem'))
}

if ($needsCertificate) {
    mkcert -cert-file $certFile -key-file $keyFile @domains | Out-Null
    Write-Host '[OK] Generated the local SAN certificate.' -ForegroundColor Green
} else {
    Write-Host '[OK] Local SAN certificate already exists.' -ForegroundColor Green
}

[pscustomobject]@{ CertFile = $certFile; KeyFile = $keyFile }
