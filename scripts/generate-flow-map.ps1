param(
    [string]$Root = "."
)

$ErrorActionPreference = "Stop"

Set-Location $Root

$docsDir = "docs"
if (-not (Test-Path $docsDir)) {
    New-Item -ItemType Directory -Path $docsDir | Out-Null
}

$tmpDir = ".tmp"
if (-not (Test-Path $tmpDir)) {
    New-Item -ItemType Directory -Path $tmpDir | Out-Null
}

$mainXaml = "UI/MainWindow.xaml"
$xamlFiles = Get-ChildItem UI -Recurse -Filter *.xaml | Select-Object -ExpandProperty FullName
$mwFiles = Get-ChildItem UI -Filter "MainWindow*.cs" | Select-Object -ExpandProperty FullName

function Extract-EventsFromXaml([string]$path) {
    $content = Get-Content -Raw $path
    $pattern = 'x:Name="(?<name>[^"]+)"[^>]*?(?<evt>Click|SelectionChanged|Checked|Unchecked|PreviewKeyDown|KeyDown|Mouse\w+|PreviewMouse\w+)="(?<handler>[^"]+)"'
    [regex]::Matches($content, $pattern, "Singleline") | ForEach-Object {
        [PSCustomObject]@{
            Source = $path
            Control = $_.Groups["name"].Value
            Event = $_.Groups["evt"].Value
            Handler = $_.Groups["handler"].Value
        }
    }
}

$allEvents = @()
foreach ($x in $xamlFiles) {
    $allEvents += Extract-EventsFromXaml $x
}

$allEvents = $allEvents | Sort-Object Control, Event, Handler -Unique
$allEvents | Export-Csv -NoTypeInformation -Encoding UTF8 "$docsDir/event-map.csv"

$handlerMap = @()
foreach ($evt in $allEvents) {
    if ([string]::IsNullOrWhiteSpace($evt.Handler) -or $evt.Handler.StartsWith("{")) {
        continue
    }
    $pat = "\b$([regex]::Escape($evt.Handler))\s*\("
    $hit = Select-String -Path $mwFiles -Pattern $pat | Select-Object -First 1
    if ($hit) {
        $handlerMap += [PSCustomObject]@{
            Control = $evt.Control
            Event = $evt.Event
            Handler = $evt.Handler
            File = (Resolve-Path $hit.Path).Path.Replace((Resolve-Path ".").Path + "\", "")
            Line = $hit.LineNumber
        }
    } else {
        $handlerMap += [PSCustomObject]@{
            Control = $evt.Control
            Event = $evt.Event
            Handler = $evt.Handler
            File = "NOT_FOUND"
            Line = ""
        }
    }
}

$handlerMap | Sort-Object Control, Handler -Unique | Export-Csv -NoTypeInformation -Encoding UTF8 "$docsDir/event-handler-map.csv"

$ctxHits = Select-String -Path "UI/MainWindow.ContextMenu.cs" -Pattern 'new MenuItem\s*\{\s*Header\s*=\s*"([^"]+)"|private void\s+([A-Za-z0-9_]+)\s*\(' -AllMatches
$ctxLines = @()
foreach ($h in $ctxHits) {
    $ctxLines += "{0}:{1}`t{2}" -f ($h.Path.Replace((Resolve-Path ".").Path + "\", "")), $h.LineNumber, $h.Line.Trim()
}
$ctxLines | Set-Content -Encoding UTF8 "$docsDir/contextmenu-scan.txt"

$kbd = Select-String -Path "UI/MainWindow.KeyboardInput.cs" -Pattern 'Key\.[A-Za-z0-9_]+' -AllMatches
$keys = $kbd.Matches.Value | Sort-Object -Unique
$keys | Set-Content -Encoding UTF8 "$docsDir/keyboard-keys.txt"

Write-Host "Generated:"
Write-Host " - $docsDir/event-map.csv"
Write-Host " - $docsDir/event-handler-map.csv"
Write-Host " - $docsDir/contextmenu-scan.txt"
Write-Host " - $docsDir/keyboard-keys.txt"
