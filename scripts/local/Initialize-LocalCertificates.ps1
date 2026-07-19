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
    try {
        $caRoot = (& mkcert -CAROOT | ForEach-Object { $_.Trim() })
        $rootCertificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $caRoot 'rootCA.pem')
        $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $certFile
        $certificateWithKey = [System.Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($certFile, $keyFile)
        $sanText = ($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' } | ForEach-Object { $_.Format($true) }) -join "`n"
        $privateKeyParameters = $certificateWithKey.PrivateKey.ExportParameters($false)
        $publicKeyParameters = $certificate.PublicKey.Key.ExportParameters($false)
        $keyMatches = [Convert]::ToBase64String($privateKeyParameters.Modulus) -eq [Convert]::ToBase64String($publicKeyParameters.Modulus) -and
            [Convert]::ToBase64String($privateKeyParameters.Exponent) -eq [Convert]::ToBase64String($publicKeyParameters.Exponent)
        $needsCertificate = $certificate.NotBefore -gt (Get-Date) -or
            $certificate.NotAfter -le (Get-Date) -or
            $certificate.Issuer -ne $rootCertificate.Subject -or
            -not $certificateWithKey.HasPrivateKey -or
            -not $keyMatches -or
            @($domains | Where-Object { $sanText -notmatch [regex]::Escape("DNS Name=$_") }).Count -gt 0
    } catch {
        $needsCertificate = $true
    }
}

if ($needsCertificate) {
    mkcert -cert-file $certFile -key-file $keyFile @domains | Out-Null
    Write-Host '[OK] Generated the local SAN certificate.' -ForegroundColor Green
} else {
    Write-Host '[OK] Local SAN certificate already exists.' -ForegroundColor Green
}

[pscustomobject]@{ CertFile = $certFile; KeyFile = $keyFile }
