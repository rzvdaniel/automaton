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
    $currentShLine = $content | Select-String 'SERVER_URL="[^"]*"' | ForEach-Object { $_.Line.Trim() }
    Write-Host "Current SERVER_URL line: $currentShLine" -ForegroundColor Cyan
    
    $originalContent = $content
    $content = $content -replace 'SERVER_URL="https://localhost:7091"', "SERVER_URL=`"$ProductionUrl`""
    
    if ($originalContent -eq $content) {
        Write-Host "No changes made to install.sh - pattern not found" -ForegroundColor Red
        # Try alternative patterns
        Write-Host "Searching for alternative patterns..." -ForegroundColor Yellow
        $serverUrlMatches = $content | Select-String 'SERVER_URL="[^"]*"' -AllMatches
        foreach ($match in $serverUrlMatches) {
            Write-Host "Found: $($match.Line.Trim())" -ForegroundColor Magenta
        }
    } else {
        Set-Content $installShPath $content -NoNewline
        Write-Host "Updated $installShPath" -ForegroundColor Green
        
        # Verify the change
        $newContent = Get-Content $installShPath -Raw
        $newShLine = $newContent | Select-String 'SERVER_URL="[^"]*"' | ForEach-Object { $_.Line.Trim() }
        Write-Host "New SERVER_URL line: $newShLine" -ForegroundColor Cyan
    }
} else {
    Write-Host "File not found: $installShPath" -ForegroundColor Red
}

# Update install.ps1 (assuming it exists)
$installPs1Path = "src/Automaton.Studio.Web/wwwroot/downloads/install.ps1"
if (Test-Path $installPs1Path) {
    Write-Host "Processing $installPs1Path..." -ForegroundColor Yellow
    
    $content = Get-Content $installPs1Path -Raw
    Write-Host "Original content length: $($content.Length) characters"
    
    # Show current ServerUrl line
    $currentPs1Line = $content | Select-String '\$ServerUrl = "[^"]*"' | ForEach-Object { $_.Line.Trim() }
    Write-Host "Current ServerUrl line: $currentPs1Line" -ForegroundColor Cyan
    
    $originalContent = $content
    # Fix the regex pattern to match the actual format in the file
    $content = $content -replace '\[string\]\$ServerUrl = "https://localhost:7091"', "[string]`$ServerUrl = `"$ProductionUrl`""
    
    if ($originalContent -eq $content) {
        Write-Host "First pattern didn't match, trying alternative patterns..." -ForegroundColor Yellow
        
        # Try without the [string] type
        $content = $content -replace '\$ServerUrl = "https://localhost:7091"', "`$ServerUrl = `"$ProductionUrl`""
        
        if ($originalContent -eq $content) {
            Write-Host "No changes made to install.ps1 - pattern not found" -ForegroundColor Red
            
            # Show all lines containing ServerUrl for debugging
            Write-Host "All lines containing 'ServerUrl':" -ForegroundColor Yellow
            $serverUrlLines = $content -split "`n" | Where-Object { $_ -match "ServerUrl" }
            foreach ($line in $serverUrlLines) {
                Write-Host "  $($line.Trim())" -ForegroundColor Magenta
            }
        } else {
            Set-Content $installPs1Path $content -NoNewline
            Write-Host "Updated $installPs1Path with alternative pattern" -ForegroundColor Green
        }
    } else {
        Set-Content $installPs1Path $content -NoNewline
        Write-Host "Updated $installPs1Path" -ForegroundColor Green
    }
    
    # Always verify the final result
    $finalContent = Get-Content $installPs1Path -Raw
    $finalPs1Line = $finalContent | Select-String '\$ServerUrl = "[^"]*"' | ForEach-Object { $_.Line.Trim() }
    Write-Host "Final ServerUrl line: $finalPs1Line" -ForegroundColor Cyan
    
} else {
    Write-Host "File not found: $installPs1Path" -ForegroundColor Red
}

Write-Host "Installer URL updates completed" -ForegroundColor Green
