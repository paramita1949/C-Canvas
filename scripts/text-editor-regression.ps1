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

# 3) Guard: save path uses unified SaveTextEditorStateAsync entry
$toolbarActionsPath = "UI/MainWindow.TextEditor.Toolbar.Actions.cs"
$toolbarActionsText = Get-Content $toolbarActionsPath -Raw
$saveUsesOrchestrator = $toolbarActionsText -match "BtnSaveTextProject_Click\s*\([^)]*\)\s*[\s\S]*?SaveTextEditorStateAsync\s*\("
Add-CheckResult -Lines $report -Name "Save button uses unified save orchestrator entry" -Passed $saveUsesOrchestrator -Detail "UI/MainWindow.TextEditor.Toolbar.Actions.cs"

# 4) Guard: slide switch is awaited async save (no fire-and-forget pattern)
$slidePath = "UI/MainWindow.TextEditor.Slides.cs"
$slideText = Get-Content $slidePath -Raw
$switchIsAsync = $slideText -match "private\s+async\s+void\s+SlideListBox_SelectionChanged"
$noFireForget = -not ($slideText -match "#pragma\s+warning\s+disable\s+CS4014")
$slideUsesOrchestrator = $slideText -match "await\s+SaveTextEditorStateAsync\s*\([\s\S]*SaveTrigger\.SlideSwitch"
Add-CheckResult -Lines $report -Name "Slide switch uses awaited save orchestrator path" -Passed ($switchIsAsync -and $noFireForget -and $slideUsesOrchestrator) -Detail $slidePath

# 4.1) Guard: no sync blocking GetAwaiter().GetResult() in TextEditor UI chain
$blockingCalls = & rg -n "GetAwaiter\(\)\.GetResult\(" UI -g "MainWindow.TextEditor*.cs" 2>$null
$noBlockingAwaiter = ($LASTEXITCODE -ne 0)
$blockingDetail = if ($noBlockingAwaiter) { "UI/MainWindow.TextEditor*.cs has zero blocking awaiter calls." } else { ($blockingCalls -join " | ") }
Add-CheckResult -Lines $report -Name "No GetAwaiter().GetResult() in MainWindow.TextEditor.*" -Passed $noBlockingAwaiter -Detail $blockingDetail

# 4.2) Guard: persistence service should not depend on TextProjectManager directly
$persistencePath = "Services/TextEditor/TextElementPersistenceService.cs"
$persistenceText = Get-Content $persistencePath -Raw
$noManagerInPersistence = -not ($persistenceText -match "TextProjectManager")
Add-CheckResult -Lines $report -Name "TextElementPersistenceService does not depend on TextProjectManager" -Passed $noManagerInPersistence -Detail $persistencePath

# 4.3) Guard: TextEditor UI should not call project service rich-text detail APIs directly
$uiRichTextCalls = & rg -n "_textProjectService\.(SaveRichTextSpansAsync|DeleteRichTextSpansByElementIdAsync|GetElementsBySlideWithRichTextAsync|AddRichTextSpanAsync)" UI -g "MainWindow.TextEditor*.cs" 2>$null
$uiNoRichTextManagerCalls = ($LASTEXITCODE -ne 0)
$uiRichTextDetail = if ($uiNoRichTextManagerCalls) { "MainWindow.TextEditor.* has no direct project service rich-text API calls." } else { ($uiRichTextCalls -join " | ") }
Add-CheckResult -Lines $report -Name "TextEditor UI avoids direct project-service rich-text APIs" -Passed $uiNoRichTextManagerCalls -Detail $uiRichTextDetail

# 5) Guard: thumbnail path keeps snapshot side-effect guard (either local or service implementation)
$thumbnailServicePath = "Services/TextEditor/Rendering/TextEditorThumbnailService.cs"
$thumbnailServiceText = if (Test-Path $thumbnailServicePath) { Get-Content $thumbnailServicePath -Raw } else { "" }
$renderSafetyPath = "Services/TextEditor/Rendering/TextEditorRenderSafetyService.cs"
$renderSafetyText = if (Test-Path $renderSafetyPath) { Get-Content $renderSafetyPath -Raw } else { "" }
$thumbUsesSnapshot = ($slideText -match "GenerateThumbnail\s*\(\)\s*[\s\S]*?CaptureSnapshotForSave\s*\(") -or
    (($thumbnailServiceText -match "CaptureSnapshotForSave\s*\(") -and ($thumbnailServiceText -match "HideDecorations\s*\(")) -or
    (($thumbnailServiceText -match "_renderSafetyService\.Execute") -and ($renderSafetyText -match "CaptureSnapshotForSave\s*\(") -and ($renderSafetyText -match "HideDecorations\s*\("))
Add-CheckResult -Lines $report -Name "Thumbnail generation captures snapshot without edit-mode exit" -Passed $thumbUsesSnapshot -Detail "$slidePath; $thumbnailServicePath; $renderSafetyPath"

# 5.1) Guard: thumbnail path delegates to thumbnail service
$thumbUsesService = $slideText -match "SaveSlideThumbnail\s*\([^)]*\)\s*[\s\S]*?_textEditorThumbnailService\?\.SaveSlideThumbnail"
Add-CheckResult -Lines $report -Name "Thumbnail save uses thumbnail service entry" -Passed $thumbUsesService -Detail $slidePath

# 5.2) Guard: projection update delegates to projection composer
$helpersPath = "UI/MainWindow.TextEditor.Helpers.cs"
$helpersText = Get-Content $helpersPath -Raw
$projectionUsesComposer = $helpersText -match "UpdateProjectionFromCanvas\s*\(\)\s*[\s\S]*?_textEditorProjectionComposer\?\.Compose"
Add-CheckResult -Lines $report -Name "Projection update uses projection composer entry" -Passed $projectionUsesComposer -Detail $helpersPath

# 5.3) Guard: projection cache key/clear is owned by rendering service instead of UI helper
$helpersNoLocalCacheFns = (-not ($helpersText -match "private\s+string\s+GenerateCanvasCacheKey\s*\(")) -and
    (-not ($helpersText -match "private\s+void\s+ClearCanvasRenderCache\s*\("))
$helpersUsesRenderState = ($helpersText -match "_textEditorProjectionRenderStateService\?\.BuildCanvasCacheKey") -and
    ($helpersText -match "_textEditorProjectionRenderStateService\?\.UpdateCache")
Add-CheckResult -Lines $report -Name "Projection cache key/clear moved out of MainWindow helper" -Passed ($helpersNoLocalCacheFns -and $helpersUsesRenderState) -Detail $helpersPath

# 5.4) Guard: render hide/restore template is centralized in rendering safety service
$helpersUsesRenderSafety = $helpersText -match "_textEditorRenderSafetyService\.Execute"
$thumbnailUsesRenderSafety = $thumbnailServiceText -match "_renderSafetyService\.Execute"
Add-CheckResult -Lines $report -Name "Render safety template centralized for projection/thumbnail" -Passed ($helpersUsesRenderSafety -and $thumbnailUsesRenderSafety) -Detail "$helpersPath; $thumbnailServicePath"

# 5.5) Guard: legacy TextProjectManager has been fully migrated out of runtime code
$legacyManagerRefs = & rg -n "TextProjectManager" UI Services Repositories Core Managers -g "*.cs" 2>$null
$legacyManagerGone = ($LASTEXITCODE -ne 0)
$legacyManagerDetail = if ($legacyManagerGone) { "No runtime code references TextProjectManager." } else { ($legacyManagerRefs -join " | ") }
Add-CheckResult -Lines $report -Name "TextProjectManager fully migrated out of runtime code" -Passed $legacyManagerGone -Detail $legacyManagerDetail

# 5.6) Guard: UI does not directly query text-editor entities from DbContext
$uiDirectTextDbRefs = & rg -n "_dbContext\.(TextProjects|Slides|TextElements|RichTextSpans)" UI -g "*.cs" 2>$null
$uiNoDirectTextDbRefs = ($LASTEXITCODE -ne 0)
$uiDirectTextDbDetail = if ($uiNoDirectTextDbRefs) { "UI has no direct DbContext access for text-editor entities." } else { ($uiDirectTextDbRefs -join " | ") }
Add-CheckResult -Lines $report -Name "UI avoids direct DbContext text-editor entity queries" -Passed $uiNoDirectTextDbRefs -Detail $uiDirectTextDbDetail

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

# 8) Guard: exiting edit mode should not rebuild rich-text document (prevents visual drift)
$editModePath = "UI/Controls/DraggableTextBox.EditMode.cs"
$editModeText = Get-Content $editModePath -Raw
$exitNoRichResync = -not ($editModeText -match "snapshot\.RichTextSpans\.Count\s*>\s*0[\s\S]*SyncTextToRichTextBox\s*\(")
Add-CheckResult -Lines $report -Name "ExitEditMode has no rich-text re-render side effect" -Passed $exitNoRichResync -Detail $editModePath

# 9) Guard: performance baseline regression <=10%
$perfScriptPath = "scripts/text-editor-performance-baseline.ps1"
$perfPassed = $false
$perfDetail = ""
if (Test-Path $perfScriptPath) {
    try {
        $perfOutput = & $perfScriptPath -AutoOnly -NoBuild 2>&1
        $perfPassed = ($LASTEXITCODE -eq 0)
        if ($perfPassed) {
            $perfDetail = "Performance baseline check passed."
        } else {
            $perfDetail = "Performance baseline check failed. Last lines: " + (($perfOutput | Select-Object -Last 5) -join " | ")
        }
    }
    catch {
        $perfPassed = $false
        $perfDetail = $_.Exception.Message
    }
} else {
    $perfPassed = $false
    $perfDetail = "Missing performance baseline script: $perfScriptPath"
}
Add-CheckResult -Lines $report -Name "Performance baseline regression <= 10%" -Passed $perfPassed -Detail $perfDetail

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
