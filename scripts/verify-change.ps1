param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "ImageColorChanger.csproj"
$tests = Join-Path $repoRoot "Canvas.TextEditor.Tests/Canvas.TextEditor.Tests.csproj"
$gateDoc = Join-Path $repoRoot "docs/engineering-change-gate.md"

Write-Host "[verify-change] Repo:" $repoRoot
Write-Host "[verify-change] Config:" $Configuration

if (-not (Test-Path $project)) {
    throw "Project file not found: $project"
}

if (-not (Test-Path $gateDoc)) {
    throw "Gate doc missing: $gateDoc"
}

Write-Host "[verify-change] Build app"
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "App build failed"
}

if (Test-Path $tests) {
    Write-Host "[verify-change] Build tests"
    dotnet build $tests -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Test project build failed"
    }

    Write-Host "[verify-change] Run targeted AI-related tests"
    dotnet test $tests -c $Configuration --no-build --filter "FullyQualifiedName~Ai" --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        throw "Targeted tests failed"
    }
} else {
    Write-Warning "Test project not found, skipped tests: $tests"
}

Write-Host "[verify-change] Completed: build + targeted tests + gate doc present"
