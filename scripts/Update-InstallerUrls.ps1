param(
    [Parameter(Mandatory=$true)]
    [string]$ProductionUrl = "https://api.automaton.monster"
)

Write-Host "Updating installer scripts with production URL: $ProductionUrl" -ForegroundColor Green

# Update install.sh
$installShPath = "src/Automaton.Studio.Web/wwwroot/downloads/install.sh"
if (Test-Path $installShPath) {
    Write-Host "Processing $installShPath..." -ForegroundColor Yellow
    
    $content = Get-Content $installShPath -Raw
    Write-Host "Original content length: $($content.Length) characters"
    
    # Show current SERVER_URL line
    if ($content -match 'SERVER_URL="[^"]*"') {
        $currentShLine = $matches[0]
        Write-Host "Current SERVER_URL line: $currentShLine" -ForegroundColor Cyan
    }
    
    $originalContent = $content
    $content = $content -replace 'SERVER_URL="https://localhost:7091"', "SERVER_URL=`"$ProductionUrl`""
    
    if ($originalContent -ne $content) {
        Set-Content $installShPath $content -NoNewline
        Write-Host "Updated $installShPath" -ForegroundColor Green
    } else {
        Write-Host "No changes made to install.sh - pattern not found" -ForegroundColor Red
    }
} else {
    Write-Host "File not found: $installShPath" -ForegroundColor Red
}

# Update install.ps1
$installPs1Path = "src/Automaton.Studio.Web/wwwroot/downloads/install.ps1"
if (Test-Path $installPs1Path) {
    Write-Host "Processing $installPs1Path..." -ForegroundColor Yellow
    
    $content = Get-Content $installPs1Path -Raw
    Write-Host "Original content length: $($content.Length) characters"
    
    # Show the first 20 lines to see the parameter block
    $lines = $content -split "`r?`n"
    Write-Host "First 20 lines of install.ps1:" -ForegroundColor Yellow
    for ($i = 0; $i -lt [Math]::Min(20, $lines.Count); $i++) {
        Write-Host "  $($i+1): $($lines[$i])" -ForegroundColor Gray
    }
    
    $originalContent = $content
    
    # Try multiple patterns to match the parameter declaration
    $patterns = @(
        '(\[string\]\$ServerUrl\s*=\s*"https://localhost:7091")',
        '(\$ServerUrl\s*=\s*"https://localhost:7091")',
        '(\[string\]\$ServerUrl = "https://localhost:7091")',
        '(\$ServerUrl = "https://localhost:7091")'
    )
    
    $patternFound = $false
    foreach ($pattern in $patterns) {
        Write-Host "Trying pattern: $pattern" -ForegroundColor Yellow
        
        if ($content -match $pattern) {
            Write-Host "Pattern matched: $($matches[1])" -ForegroundColor Green
            $replacement = $matches[1] -replace 'https://localhost:7091', $ProductionUrl
            $content = $content -replace [regex]::Escape($matches[1]), $replacement
            $patternFound = $true
            break
        }
    }
    
    if ($patternFound -and $originalContent -ne $content) {
        Set-Content $installPs1Path $content -NoNewline
        Write-Host "Updated $installPs1Path" -ForegroundColor Green
        
        # Verify the change by showing the updated lines
        $newLines = $content -split "`r?`n"
        Write-Host "Updated first 20 lines:" -ForegroundColor Cyan
        for ($i = 0; $i -lt [Math]::Min(20, $newLines.Count); $i++) {
            if ($newLines[$i] -match 'ServerUrl') {
                Write-Host "  $($i+1): $($newLines[$i])" -ForegroundColor Green
            }
        }
    } else {
        Write-Host "No changes made to install.ps1 - no pattern matched" -ForegroundColor Red
        
        # Show all lines containing 'ServerUrl' for debugging
        Write-Host "All lines containing 'ServerUrl':" -ForegroundColor Yellow
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'ServerUrl') {
                Write-Host "  Line $($i+1): $($lines[$i])" -ForegroundColor Magenta
            }
        }
    }
} else {
    Write-Host "File not found: $installPs1Path" -ForegroundColor Red
}

Write-Host "Installer URL updates completed" -ForegroundColor Green
