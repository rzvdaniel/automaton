param(
    [string]$Role,
    [string]$Model,
    [string]$JsonlPath,
    [string]$MetricsPath
)

if (-not (Test-Path $JsonlPath)) {
    Write-Host "No JSONL file found for $Role"
    return
}

$usageLine = Get-Content $JsonlPath |
    Where-Object { $_ -match '"type":"turn.completed"' } |
    Select-Object -Last 1

if (-not $usageLine) {
    Write-Host "No usage found for $Role"
    return
}

$event = $usageLine | ConvertFrom-Json
$usage = $event.usage

$metrics = [ordered]@{
    role = $Role
    model = $Model
    input_tokens = $usage.input_tokens
    cached_input_tokens = $usage.cached_input_tokens
    output_tokens = $usage.output_tokens
    reasoning_output_tokens = $usage.reasoning_output_tokens
    total_tokens = $usage.input_tokens + $usage.output_tokens
    captured_at = (Get-Date).ToString("o")
}

$metrics | ConvertTo-Json -Depth 5 | Set-Content $MetricsPath