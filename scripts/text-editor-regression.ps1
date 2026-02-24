param(
    [string]$ProjectPath = "ImageColorChanger.csproj",
    [string]$OutputPath = "docs/text-editor-regression-latest.md",
    [switch]$AutoOnly,
    [switch]$RunManualLoop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-Section {
    param([string]$Title)
    return "## $Title"
}

function Add-CheckResult {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )
    $status = if ($Passed) { "PASS" } else { "FAIL" }
    $Lines.Add("- [$status] $Name")
    if ($Detail) {
        $Lines.Add("  - $Detail")
    }
}

function Invoke-RgHasMatch {
    param([string]$Pattern, [string]$Path)
    $null = & rg -n $Pattern $Path 2>$null
    return ($LASTEXITCODE -eq 0)
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add("# Text Editor Regression Report")
$report.Add("")
$report.Add("- Timestamp: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")")
$report.Add("- Workspace: $(Get-Location)")
$report.Add("")

$report.Add((New-Section -Title "Automatic Checks"))
$report.Add("")

# 1) Build check
$buildOk = $false
$buildDetail = ""
try {
    $buildOutput = & dotnet build $ProjectPath -c Debug 2>&1
    $buildOk = ($LASTEXITCODE -eq 0)
    if ($buildOk) {
        $buildDetail = "dotnet build succeeded."
    } else {
        $buildDetail = "dotnet build failed. Last lines: " + (($buildOutput | Select-Object -Last 5) -join " | ")
    }
} catch {
    $buildOk = $false
    $buildDetail = $_.Exception.Message
}
Add-CheckResult -Lines $report -Name "Build" -Passed $buildOk -Detail $buildDetail

# 2) Guard: HideDecorations must not call ExitEditMode
$lifecyclePath = "UI/Controls/DraggableTextBox.Lifecycle.cs"
$lifecycleText = Get-Content $lifecyclePath -Raw
$hideNoExit = -not ($lifecycleText -match "HideDecorations\s*\([^)]*\)\s*[\s\S]*ExitEditMode\s*\(")
Add-CheckResult -Lines $report -Name "HideDecorations has no ExitEditMode side effect" -Passed $hideNoExit -Detail $lifecyclePath

# 3) Guard: save path uses PersistTextElementsAsync
$toolbarActionsPath = "UI/MainWindow.TextEditor.Toolbar.Actions.cs"
$toolbarActionsText = Get-Content $toolbarActionsPath -Raw
$saveUsesPersistence = $toolbarActionsText -match "BtnSaveTextProject_Click\s*\([^)]*\)\s*[\s\S]*?PersistTextElementsAsync\s*\("
Add-CheckResult -Lines $report -Name "Save button uses persistence service entry" -Passed $saveUsesPersistence -Detail "UI/MainWindow.TextEditor.Toolbar.Actions.cs"

# 4) Guard: slide switch is awaited async save (no fire-and-forget pattern)
$slidePath = "UI/MainWindow.TextEditor.Slides.cs"
$slideText = Get-Content $slidePath -Raw
$switchIsAsync = $slideText -match "private\s+async\s+void\s+SlideListBox_SelectionChanged"
$noFireForget = -not ($slideText -match "#pragma\s+warning\s+disable\s+CS4014")
$slidePersists = $slideText -match "await\s+PersistTextElementsAsync\("
Add-CheckResult -Lines $report -Name "Slide switch uses awaited save path" -Passed ($switchIsAsync -and $noFireForget -and $slidePersists) -Detail $slidePath

# 5) Guard: thumbnail path uses CaptureSnapshotForSave and HideDecorations
$thumbUsesSnapshot = $slideText -match "GenerateThumbnail\s*\(\)\s*[\s\S]*?CaptureSnapshotForSave\s*\("
Add-CheckResult -Lines $report -Name "Thumbnail generation captures snapshot without edit-mode exit" -Passed $thumbUsesSnapshot -Detail $slidePath

# 6) Guard: RichTextSpan has v2 fields
$modelPath = "Database/Models/RichTextSpan.cs"
$hasParagraphIndex = Invoke-RgHasMatch -Pattern "ParagraphIndex" -Path $modelPath
$hasRunIndex = Invoke-RgHasMatch -Pattern "RunIndex" -Path $modelPath
$hasFormatVersion = Invoke-RgHasMatch -Pattern "FormatVersion" -Path $modelPath
Add-CheckResult -Lines $report -Name "RichTextSpan contains v2 schema fields" -Passed ($hasParagraphIndex -and $hasRunIndex -and $hasFormatVersion) -Detail $modelPath

# 7) Guard: migration/context include v2 schema upgrade
$ctxPath = "Database/CanvasDbContext.cs"
$migPath = "Database/Migrations/DatabaseMigrationRunner.cs"
$ctxHasEnsure = Invoke-RgHasMatch -Pattern "EnsureRichTextSpansV2SchemaExists" -Path $ctxPath
$migHasUpgrade = Invoke-RgHasMatch -Pattern "MigrateUpgradeRichTextSpansV2Schema" -Path $migPath
Add-CheckResult -Lines $report -Name "DB upgrade path includes v2 columns/indexes" -Passed ($ctxHasEnsure -and $migHasUpgrade) -Detail "$ctxPath; $migPath"

$report.Add("")
$report.Add((New-Section -Title "Manual Acceptance Checklist"))
$report.Add("")
$report.Add("- [ ] 1) Edit mixed-size multi-paragraph text and save: first-line anchor does not move.")
$report.Add("- [ ] 2) Trigger thumbnail generation while editing: editor stays in editing mode and content does not jump.")
$report.Add("- [ ] 3) Switch slide away and back: same element layout remains stable.")
$report.Add("- [ ] 4) Repeat 50 cycles (edit -> save -> switch -> back -> save): no cumulative drift.")
$report.Add("- [ ] 5) Load v1 rich text data and save: upgraded to v2 with visual parity.")
$report.Add("- [ ] 6) Force save failure scenario: clear error feedback appears and unsaved content is retained.")
$report.Add("- [ ] 7) Performance: save/switch remains within <=10% baseline regression.")

if ($RunManualLoop) {
    $report.Add("")
    $report.Add((New-Section -Title "Manual 50-cycle Log"))
    $report.Add("")
    $driftCount = 0
    for ($i = 1; $i -le 50; $i++) {
        $answer = Read-Host "Cycle $i drift observed? (y/N)"
        $isDrift = $answer -match "^(y|Y)$"
        if ($isDrift) { $driftCount++ }
        $cycleResult = if ($isDrift) { "DRIFT" } else { "OK" }
        $report.Add(("- Cycle {0}: {1}" -f $i, $cycleResult))
    }
    $report.Add("")
    $report.Add("- Drift count: $driftCount / 50")
}

if (-not (Test-Path (Split-Path -Parent $OutputPath))) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) | Out-Null
}
$report | Set-Content -Path $OutputPath -Encoding UTF8

Write-Host "Regression report written to: $OutputPath"
if ($AutoOnly) {
    Write-Host "Auto-only mode complete."
}
