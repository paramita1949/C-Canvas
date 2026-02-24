param(
    [string]$TestProjectPath = "Canvas.TextEditor.Tests/Canvas.TextEditor.Tests.csproj",
    [string]$BaselinePath = "docs/text-editor-performance-baseline.json",
    [string]$OutputPath = "docs/text-editor-performance-latest.md",
    [switch]$AutoOnly,
    [switch]$UpdateBaseline,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Stabilize CI/local timing by disabling first-time experience side effects.
if (-not $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE) {
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
}
if (-not $env:DOTNET_CLI_TELEMETRY_OPTOUT) {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
}

function Invoke-TestMetric {
    param(
        [string]$Name,
        [string]$Filter
    )

    $args = @("test", $TestProjectPath, "-c", "Debug", "--nologo")
    if ($NoBuild) {
        $args += "--no-build"
    }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $args += @("--filter", $Filter)
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & dotnet @args 2>&1
    $exitCode = $LASTEXITCODE
    $sw.Stop()

    return [pscustomobject]@{
        name = $Name
        filter = $Filter
        seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        passed = ($exitCode -eq 0)
        tail = (($output | Select-Object -Last 5) -join " | ")
    }
}

$metrics = @(
    (Invoke-TestMetric -Name "AllTextEditorTests" -Filter ""),
    (Invoke-TestMetric -Name "RenderingTests" -Filter "FullyQualifiedName~ImageColorChanger.CanvasTextEditor.Tests.Rendering")
)

$failedMetrics = @($metrics | Where-Object { -not $_.passed })
$testsPassed = $failedMetrics.Count -eq 0

if (-not (Test-Path (Split-Path -Parent $OutputPath))) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
}

$comparisonResults = @()
$comparisonPassed = $true
$baselineCreated = $false

if ($UpdateBaseline -or -not (Test-Path $BaselinePath)) {
    if (-not (Test-Path (Split-Path -Parent $BaselinePath))) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $BaselinePath) -Force | Out-Null
    }

    $baselinePayload = [ordered]@{
        generatedAt = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        metrics = @($metrics | ForEach-Object {
                [ordered]@{
                    name = $_.name
                    seconds = $_.seconds
                }
            })
    }

    $baselinePayload | ConvertTo-Json -Depth 5 | Set-Content -Path $BaselinePath -Encoding UTF8
    $baselineCreated = $true
} else {
    $baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json
    $baselineMap = @{}
    foreach ($m in $baseline.metrics) {
        $baselineMap[$m.name] = [double]$m.seconds
    }

    foreach ($metric in $metrics) {
        if (-not $baselineMap.ContainsKey($metric.name)) {
            $comparisonResults += [pscustomobject]@{
                name = $metric.name
                baseline = [double]::NaN
                current = $metric.seconds
                deltaPct = [double]::NaN
                passed = $false
                note = "missing baseline metric"
            }
            $comparisonPassed = $false
            continue
        }

        $baselineSeconds = [double]$baselineMap[$metric.name]
        if ($baselineSeconds -le 0) {
            $comparisonResults += [pscustomobject]@{
                name = $metric.name
                baseline = $baselineSeconds
                current = $metric.seconds
                deltaPct = [double]::NaN
                passed = $false
                note = "invalid baseline metric"
            }
            $comparisonPassed = $false
            continue
        }

        $deltaPct = (($metric.seconds - $baselineSeconds) / $baselineSeconds) * 100.0
        $passed = $deltaPct -le 10.0
        if (-not $passed) {
            $comparisonPassed = $false
        }

        $comparisonResults += [pscustomobject]@{
            name = $metric.name
            baseline = [Math]::Round($baselineSeconds, 2)
            current = $metric.seconds
            deltaPct = [Math]::Round($deltaPct, 2)
            passed = $passed
            note = ""
        }
    }
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add("# Text Editor Performance Report")
$report.Add("")
$report.Add("- Timestamp: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")")
$report.Add("- Baseline: $BaselinePath")
$report.Add("- Baseline created this run: $baselineCreated")
$report.Add("")
$report.Add("## Runtime Metrics")
$report.Add("")
$report.Add("| Metric | Seconds | Status |")
$report.Add("|---|---:|---|")
foreach ($metric in $metrics) {
    $status = if ($metric.passed) { "PASS" } else { "FAIL" }
    $report.Add("| $($metric.name) | $($metric.seconds) | $status |")
}

if (-not $baselineCreated) {
    $report.Add("")
    $report.Add("## Baseline Comparison (<= 10% Allowed)")
    $report.Add("")
    $report.Add("| Metric | Baseline(s) | Current(s) | Delta(%) | Status | Note |")
    $report.Add("|---|---:|---:|---:|---|---|")
    foreach ($row in $comparisonResults) {
        $baselineText = if ([double]::IsNaN($row.baseline)) { "N/A" } else { "$($row.baseline)" }
        $deltaText = if ([double]::IsNaN($row.deltaPct)) { "N/A" } else { "$($row.deltaPct)" }
        $status = if ($row.passed) { "PASS" } else { "FAIL" }
        $report.Add("| $($row.name) | $baselineText | $($row.current) | $deltaText | $status | $($row.note) |")
    }
}

$report | Set-Content -Path $OutputPath -Encoding UTF8

$overallPassed = $testsPassed -and ($baselineCreated -or $comparisonPassed)
Write-Host "Performance report written to: $OutputPath"
if ($overallPassed) {
    Write-Host "Performance baseline check passed."
    exit 0
}

Write-Host "Performance baseline check failed."
if (-not $AutoOnly) {
    Write-Host "Use -UpdateBaseline to refresh baseline after confirming expected change."
}
exit 1
