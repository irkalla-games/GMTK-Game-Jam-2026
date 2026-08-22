<#
.SYNOPSIS
    Builds Docs/LevelDesign.xlsx from the LevelData and RunData assets.

.DESCRIPTION
    Read-only against the Unity project: it parses Assets/Data/LevelData/**/*.asset and
    Assets/Data/RunData/**/*.asset and writes only into Docs/. Safe to run at any time, including with
    the Editor open.

    The everyday sync (Tools > Sync Levels With Sheet in Unity) runs Import-LevelSheet.ps1 first to
    apply sheet edits to the assets, then this script to refresh the sheet from the result. The board on
    every level tab is one merge column, "Layout" - see Format-LevelLayout in LevelSheet.Common.psm1 for
    why the whole board is treated as one unit rather than merged cell by cell. Notes is the one field
    this workbook treats as authored IN the sheet and preserves across every re-export.

.PARAMETER WorkbookPath
    Defaults to Docs/LevelDesign.xlsx at the repo root.

.PARAMETER NoCsvMirror
    Skip writing Docs/LevelDesign/*.csv.

.PARAMETER WriteBaseline
    Record the state both sides now agree on into baseline.json. Passed by the sync, which has just
    made them agree.

.PARAMETER Force
    Rebuild the sheet even though it holds edits that have not reached the assets, discarding them.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/LevelSheet/Export-LevelSheet.ps1
#>
[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [switch]$NoCsvMirror,
    [switch]$WriteBaseline,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Resolve-Path (Join-Path $scriptDir '..\..')
Import-Module (Join-Path $scriptDir 'LevelSheet.Common.psm1') -Force -DisableNameChecking

if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    throw 'The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser'
}
Import-Module ImportExcel -DisableNameChecking

if (-not $WorkbookPath) { $WorkbookPath = Join-Path $repoRoot 'Docs\LevelDesign.xlsx' }
$csvDir = Join-Path $repoRoot 'Docs\LevelDesign'

if (Test-Path -LiteralPath $WorkbookPath) {
    try {
        $probe = [IO.File]::Open($WorkbookPath, 'Open', 'ReadWrite', 'None')
        $probe.Close()
    }
    catch {
        throw "$WorkbookPath is open in another program (probably Excel). Close it and run this again - " +
              'the export rewrites the whole workbook, so it cannot share the file.'
    }
}

Write-Host 'Reading assets...' -ForegroundColor Cyan

$scriptGuids = Get-ScriptGuidIndex -RepoRoot $repoRoot
$assetIndex  = Build-AssetIndex -RepoRoot $repoRoot -RelativeFolders @('Assets\Data', 'Assets\Prefabs') -ScriptGuidIndex $scriptGuids

$levels = @()
foreach ($asset in ($assetIndex.All | Where-Object { $_.Type -eq 'LevelData' } | Sort-Object Name)) {
    $levels += (Read-LevelAsset -Asset $asset -AssetIndex $assetIndex)
}

$runs = @()
foreach ($asset in ($assetIndex.All | Where-Object { $_.Type -eq 'RunData' } | Sort-Object Name)) {
    $runs += (Read-RunAsset -Asset $asset -AssetIndex $assetIndex)
}

Write-Host "  $($levels.Count) levels, $($runs.Count) runs"

$prefabNames = @((Get-AllCharacterPrefabNames -AssetIndex $assetIndex).Keys)
$lootNames = @($assetIndex.All | Where-Object { $_.Type -eq 'LootTable' } | ForEach-Object { $_.Name })

# ---------------------------------------------------------------------------------------------------
# Preserve Notes from whatever workbook is already on disk. Matched by tab name (= level asset name) -
# levels are never renamed by the sheet, so there is no fuzzy fallback to make here (unlike the enemy
# sheet's Display-Name match, which existed only to carry the one hand-authored file across its rename).
# ---------------------------------------------------------------------------------------------------

function Get-ExistingLevelNotes {
    param([string]$WorkbookPath)

    $notes = @{}
    if (-not (Test-Path -LiteralPath $WorkbookPath)) { return $notes }

    try { $pkg = Open-ExcelPackage -Path $WorkbookPath }
    catch { Write-Warning "Could not open the existing workbook to preserve Notes: $($_.Exception.Message)"; return $notes }

    try {
        $skip = @('Runs', 'Enums', 'README')
        foreach ($ws in @($pkg.Workbook.Worksheets)) {
            if ($skip -contains $ws.Name) { continue }
            $tab = Read-LevelSheetTab -Worksheet $ws
            if ($null -ne $tab -and $tab.Notes) { $notes[$ws.Name] = $tab.Notes }
        }
    }
    finally {
        Close-ExcelPackage $pkg -NoSave
    }

    return $notes
}

$preservedNotes = Get-ExistingLevelNotes -WorkbookPath $WorkbookPath
Write-Host "  Preserved Notes for $($preservedNotes.Count) tab(s) from the existing workbook"

# ---------------------------------------------------------------------------------------------------
# Refuse to discard unsynced sheet edits - same rule Export-EnemySheet.ps1 follows.
# ---------------------------------------------------------------------------------------------------

if (-not $Force -and (Test-Path -LiteralPath $WorkbookPath)) {
    $baseline = Read-LevelBaseline -ToolDir $scriptDir
    $pending = @()

    try { $pkg = Open-ExcelPackage -Path $WorkbookPath }
    catch { $pkg = $null }

    if ($null -ne $pkg) {
        try {
            $levelsByGuid = @{}
            foreach ($l in $levels) { if ($l.Guid) { $levelsByGuid[$l.Guid] = $l } }

            $skip = @('Runs', 'Enums', 'README')
            foreach ($ws in @($pkg.Workbook.Worksheets)) {
                if ($skip -contains $ws.Name) { continue }
                $tab = Read-LevelSheetTab -Worksheet $ws
                if ($null -eq $tab -or -not $tab.Guid -or -not $levelsByGuid.ContainsKey($tab.Guid)) { continue }

                $level = $levelsByGuid[$tab.Guid]
                $assetRow = Get-LevelMergeRow -Level $level
                $sheetRow = [ordered]@{
                    BoardWidth = $tab.BoardWidth; BoardHeight = $tab.BoardHeight
                    TurnsToSurvive = $tab.TurnsToSurvive; HandSize = $tab.HandSize
                    LootTable = $tab.LootTable; ClearRewardTable = $tab.ClearRewardTable
                    TileSets = ($tab.TileSets -join '|')
                    Layout = Format-LevelLayout -Placements $tab.Placements -PartySpawn $tab.PartySpawn
                }

                $hasBase = $baseline.Levels.ContainsKey($tab.Guid)
                foreach ($col in (Get-LevelMergeColumns)) {
                    $baseValue = $null
                    if ($hasBase -and $baseline.Levels[$tab.Guid].ContainsKey($col)) { $baseValue = $baseline.Levels[$tab.Guid][$col] }
                    $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $assetRow[$col] -Sheet $sheetRow[$col] -HasBaselineEntry $hasBase
                    if ($verdict -in @('takeSheet', 'conflict', 'noBaseline')) { $pending += "$($ws.Name) -> $col" }
                }
            }

            $runsTab = $pkg.Workbook.Worksheets['Runs']
            if ($null -ne $runsTab -and $null -ne $runsTab.Dimension) {
                $runsByGuid = @{}
                foreach ($r in $runs) { if ($r.Guid) { $runsByGuid[$r.Guid] = $r } }

                $last = $runsTab.Dimension.End.Row
                for ($rr = 2; $rr -le $last; $rr++) {
                    $guid = [string]$runsTab.Cells[$rr, 4].Text
                    if (-not $guid -or -not $runsByGuid.ContainsKey($guid)) { continue }

                    $run = $runsByGuid[$guid]
                    $assetRow = Get-RunMergeRow -Run $run
                    $sheetRow = [ordered]@{
                        Levels = (([string]$runsTab.Cells[$rr, 2].Text) -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) -join '|'
                        CarryDamageBetweenLevels = ([string]$runsTab.Cells[$rr, 3].Text).Trim().ToUpperInvariant()
                    }

                    $hasBase = $baseline.Runs.ContainsKey($guid)
                    foreach ($col in (Get-RunMergeColumns)) {
                        $baseValue = $null
                        if ($hasBase -and $baseline.Runs[$guid].ContainsKey($col)) { $baseValue = $baseline.Runs[$guid][$col] }
                        $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $assetRow[$col] -Sheet $sheetRow[$col] -HasBaselineEntry $hasBase
                        if ($verdict -in @('takeSheet', 'conflict', 'noBaseline')) { $pending += "Runs/$($run.Name) -> $col" }
                    }
                }
            }
        }
        finally {
            Close-ExcelPackage $pkg -NoSave
        }
    }

    if ($pending.Count -gt 0) {
        $shown = @($pending | Select-Object -First 12)
        $more = if ($pending.Count -gt 12) { "`n  ... and $($pending.Count - 12) more" } else { '' }

        throw ("The sheet holds $($pending.Count) edit(s) that have not reached the assets. Rebuilding it " +
               "now would discard them:`n  " + ($shown -join "`n  ") + $more +
               "`n`nRun the sync instead - Tools > Sync Levels With Sheet in Unity - which applies these " +
               "first and then refreshes the sheet. Pass -Force to overwrite them anyway.")
    }
}

# ---------------------------------------------------------------------------------------------------

. (Join-Path $scriptDir 'Write-LevelWorkbook.ps1')

Write-LevelWorkbook -WorkbookPath $WorkbookPath -Levels $levels -Runs $runs `
    -PrefabNames $prefabNames -LootNames $lootNames -PreservedNotes $preservedNotes -RepoRoot $repoRoot

if (-not $NoCsvMirror) {
    if (-not (Test-Path -LiteralPath $csvDir)) { New-Item -ItemType Directory -Path $csvDir -Force | Out-Null }

    $levelRows = @()
    foreach ($l in $levels) {
        $levelRows += [pscustomobject][ordered]@{
            Level = $l.Name; BoardWidth = $l.BoardWidth; BoardHeight = $l.BoardHeight
            TurnsToSurvive = $l.TurnsToSurvive; HandSize = $l.HandSize
            LootTable = $l.LootTable; ClearRewardTable = $l.ClearRewardTable
            TileSets = ($l.TileSets -join '|')
            Notes = if ($preservedNotes.ContainsKey($l.Name)) { $preservedNotes[$l.Name] } else { '' }
            GUID = $l.Guid
        }
    }
    $levelRows | Export-Csv -LiteralPath (Join-Path $csvDir 'Levels.csv') -NoTypeInformation -Encoding utf8

    $spawnRows = @()
    foreach ($l in $levels) {
        foreach ($p in ($l.Placements | Sort-Object Turn, Row, Col)) {
            $spawnRows += [pscustomobject][ordered]@{
                Level = $l.Name; Turn = $p.Turn; Col = $p.Col; Row = $p.Row
                Prefab = $p.Prefab; Deck = ($p.Deck -join '|')
            }
        }
        foreach ($s in ($l.PartySpawn | Sort-Object Index)) {
            $spawnRows += [pscustomobject][ordered]@{
                Level = $l.Name; Turn = 'Party'; Col = $s.Col; Row = $s.Row
                Prefab = "P$($s.Index)"; Deck = ''
            }
        }
    }
    $spawnRows | Export-Csv -LiteralPath (Join-Path $csvDir 'Spawns.csv') -NoTypeInformation -Encoding utf8

    $runRows = @()
    foreach ($r in $runs) {
        $runRows += [pscustomobject][ordered]@{
            Run = $r.Name; Levels = ($r.Levels -join '|')
            CarryDamageBetweenLevels = if ($r.CarryDamageBetweenLevels) { 'TRUE' } else { 'FALSE' }
            GUID = $r.Guid
        }
    }
    $runRows | Export-Csv -LiteralPath (Join-Path $csvDir 'Runs.csv') -NoTypeInformation -Encoding utf8

    Write-Host 'CSV mirror written to Docs/LevelDesign/' -ForegroundColor DarkGray
}

if ($WriteBaseline) {
    Write-LevelBaseline -ToolDir $scriptDir -Levels $levels -Runs $runs
    Write-Host "Baseline recorded ($($levels.Count) levels, $($runs.Count) runs)" -ForegroundColor DarkGray
}

Write-Host "`nWrote $WorkbookPath" -ForegroundColor Green
