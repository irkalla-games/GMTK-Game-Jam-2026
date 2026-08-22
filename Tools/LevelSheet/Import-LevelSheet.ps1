<#
.SYNOPSIS
    Diffs Docs/LevelDesign.xlsx against the LevelData/RunData assets and writes
    Tools/LevelSheet/levels.json.

.DESCRIPTION
    Does NOT touch the Unity project. It only produces the work order; Assets/Editor/
    LevelSheetImporter.cs reads that file and writes the assets.

    Every level's board (placements, waves, party spawn cells, deck overrides) is ONE merge column,
    "Layout" - see Format-LevelLayout in LevelSheet.Common.psm1. The other columns (Board Width/Height,
    Turns To Survive, Hand Size, Loot Table, Clear Reward Table, Tile Sets) are independent of it and of
    each other.

    Field-by-field, not "the sheet wins the whole row": a column that did not itself move to the sheet
    keeps the ASSET's current value when the payload is built, even if a sibling column on the same
    level did move. Without this, editing Hand Size in the sheet while Board Width was quietly changed
    in the Inspector since the last sync would regress Board Width back to its stale pre-sync value the
    moment the payload is applied.

.PARAMETER WorkbookPath
    Defaults to Docs/LevelDesign.xlsx at the repo root.

.PARAMETER OutputPath
    Defaults to Tools/LevelSheet/levels.json.

.PARAMETER FromCsv
    Force reading the Docs/LevelDesign/*.csv mirror even though the workbook is readable.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/LevelSheet/Import-LevelSheet.ps1
#>
[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [string]$OutputPath,
    [switch]$FromCsv
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Resolve-Path (Join-Path $scriptDir '..\..')
Import-Module (Join-Path $scriptDir 'LevelSheet.Common.psm1') -Force -DisableNameChecking

if (-not $WorkbookPath) { $WorkbookPath = Join-Path $repoRoot 'Docs\LevelDesign.xlsx' }
if (-not $OutputPath)   { $OutputPath   = Join-Path $scriptDir 'levels.json' }
$csvDir = Join-Path $repoRoot 'Docs\LevelDesign'

$effectiveFromCsv = [bool]$FromCsv

if (-not $effectiveFromCsv -and (Test-Path -LiteralPath $WorkbookPath)) {
    try {
        $probe = [IO.File]::Open($WorkbookPath, 'Open', 'Read', 'ReadWrite')
        $probe.Close()
    }
    catch {
        Write-Host "$WorkbookPath cannot be read ($($_.Exception.Message.Trim())) - falling back to Docs/LevelDesign/*.csv for this sync." -ForegroundColor Yellow
        $effectiveFromCsv = $true
    }
}

if (-not $effectiveFromCsv) {
    if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
        throw 'The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser'
    }
    Import-Module ImportExcel -DisableNameChecking
}

Write-Host 'Reading assets...' -ForegroundColor Cyan

$scriptGuids = Get-ScriptGuidIndex -RepoRoot $repoRoot
$assetIndex  = Build-AssetIndex -RepoRoot $repoRoot -RelativeFolders @('Assets\Data', 'Assets\Prefabs') -ScriptGuidIndex $scriptGuids
$knownPrefabs = Get-AllCharacterPrefabNames -AssetIndex $assetIndex

$levelsByName = [ordered]@{}
foreach ($asset in ($assetIndex.All | Where-Object { $_.Type -eq 'LevelData' })) {
    $levelsByName[$asset.Name] = Read-LevelAsset -Asset $asset -AssetIndex $assetIndex
}

$runsByName = [ordered]@{}
foreach ($asset in ($assetIndex.All | Where-Object { $_.Type -eq 'RunData' })) {
    $runsByName[$asset.Name] = Read-RunAsset -Asset $asset -AssetIndex $assetIndex
}

$knownLevelNames = @($levelsByName.Keys)

Write-Host "  $($levelsByName.Count) levels, $($runsByName.Count) runs"

# ---------------------------------------------------------------------------------------------------
# Read the sheet side
# ---------------------------------------------------------------------------------------------------

$sheetLevels = @{}
$sheetRuns = @()
$problems = @()

if ($effectiveFromCsv) {
    $levelsCsv = Join-Path $csvDir 'Levels.csv'
    $spawnsCsv = Join-Path $csvDir 'Spawns.csv'
    $runsCsv = Join-Path $csvDir 'Runs.csv'

    $spawnsByLevel = @{}
    if (Test-Path -LiteralPath $spawnsCsv) {
        foreach ($row in (Import-Csv -LiteralPath $spawnsCsv)) {
            if (-not $spawnsByLevel.ContainsKey($row.Level)) { $spawnsByLevel[$row.Level] = @() }
            $spawnsByLevel[$row.Level] += $row
        }
    }

    if (Test-Path -LiteralPath $levelsCsv) {
        foreach ($row in (Import-Csv -LiteralPath $levelsCsv)) {
            $placements = @()
            $partySpawn = @()
            if ($spawnsByLevel.ContainsKey($row.Level)) {
                foreach ($s in $spawnsByLevel[$row.Level]) {
                    if ($s.Turn -eq 'Party') {
                        $idx = [int]([string]$s.Prefab).TrimStart('P')
                        $partySpawn += [pscustomobject]@{ Index = $idx; Col = [int]$s.Col; Row = [int]$s.Row }
                    }
                    else {
                        $deck = if ($s.Deck) { @($s.Deck -split '\|') } else { @() }
                        $placements += [pscustomobject]@{ Turn = [int]$s.Turn; Col = [int]$s.Col; Row = [int]$s.Row; Prefab = $s.Prefab; Deck = $deck }
                    }
                }
            }

            $sheetLevels[$row.Level] = [pscustomobject]@{
                Name = $row.Level; Guid = [string]$row.GUID
                BoardWidth = [string]$row.BoardWidth; BoardHeight = [string]$row.BoardHeight
                TurnsToSurvive = [string]$row.TurnsToSurvive; HandSize = [string]$row.HandSize
                LootTable = [string]$row.LootTable; ClearRewardTable = [string]$row.ClearRewardTable
                TileSets = @(([string]$row.TileSets) -split '\|' | Where-Object { $_ })
                Placements = $placements; PartySpawn = $partySpawn; Problems = @()
            }
        }
    }

    if (Test-Path -LiteralPath $runsCsv) {
        foreach ($row in (Import-Csv -LiteralPath $runsCsv)) {
            $sheetRuns += [pscustomobject]@{
                Name = [string]$row.Run; Guid = [string]$row.GUID
                Levels = @(([string]$row.Levels) -split '\|' | Where-Object { $_ })
                CarryDamageBetweenLevels = ([string]$row.CarryDamageBetweenLevels).Trim().ToUpperInvariant() -eq 'TRUE'
            }
        }
    }
}
else {
    $pkg = Open-ExcelPackage -Path $WorkbookPath
    try {
        $skip = @('Runs', 'Enums', 'README')
        foreach ($ws in @($pkg.Workbook.Worksheets)) {
            if ($skip -contains $ws.Name) { continue }
            $tab = Read-LevelSheetTab -Worksheet $ws
            if ($null -ne $tab) {
                $sheetLevels[$ws.Name] = $tab
                foreach ($p in $tab.Problems) { $problems += "'$($ws.Name)': $p" }
            }
        }

        $runsTab = $pkg.Workbook.Worksheets['Runs']
        if ($null -ne $runsTab -and $null -ne $runsTab.Dimension) {
            $last = $runsTab.Dimension.End.Row
            for ($r = 2; $r -le $last; $r++) {
                $name = [string]$runsTab.Cells[$r, 1].Text
                if (-not $name) { continue }
                $sheetRuns += [pscustomobject]@{
                    Name = $name; Guid = [string]$runsTab.Cells[$r, 4].Text
                    Levels = @(([string]$runsTab.Cells[$r, 2].Text) -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
                    CarryDamageBetweenLevels = ([string]$runsTab.Cells[$r, 3].Text).Trim().ToUpperInvariant() -eq 'TRUE'
                }
            }
        }
    }
    finally {
        Close-ExcelPackage $pkg -NoSave
    }
}

Write-Host "  $($sheetLevels.Count) level tab(s), $($sheetRuns.Count) run row(s) read from the $(if ($effectiveFromCsv) { 'CSV mirror' } else { 'workbook' })"

# ---------------------------------------------------------------------------------------------------
# Three-way merge - levels
# ---------------------------------------------------------------------------------------------------

$baseline = Read-LevelBaseline -ToolDir $scriptDir
$levelMergeColumns = Get-LevelMergeColumns
$runMergeColumns = Get-RunMergeColumns

$levelActions = @()
$conflicts = @()
$unresolved = @()

foreach ($name in $levelsByName.Keys) {
    if (-not $sheetLevels.ContainsKey($name)) {
        $problems += "'$name' has no matching tab in the sheet - skipped. Run Export-LevelSheet.ps1 to add it."
        continue
    }

    $level = $levelsByName[$name]
    $sheet = $sheetLevels[$name]
    $guid = $level.Guid

    $assetRow = Get-LevelMergeRow -Level $level
    $sheetRow = [ordered]@{
        BoardWidth = $sheet.BoardWidth; BoardHeight = $sheet.BoardHeight
        TurnsToSurvive = $sheet.TurnsToSurvive; HandSize = $sheet.HandSize
        LootTable = $sheet.LootTable; ClearRewardTable = $sheet.ClearRewardTable
        TileSets = ($sheet.TileSets -join '|')
        Layout = Format-LevelLayout -Placements $sheet.Placements -PartySpawn $sheet.PartySpawn
    }

    $hasBase = $guid -and $baseline.Levels.ContainsKey($guid)
    $changed = @()
    $conflicted = @()
    $unknown = @()
    $verdicts = @{}

    foreach ($col in $levelMergeColumns) {
        $baseValue = $null
        if ($hasBase -and $baseline.Levels[$guid].ContainsKey($col)) { $baseValue = $baseline.Levels[$guid][$col] }

        $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $assetRow[$col] -Sheet $sheetRow[$col] -HasBaselineEntry $hasBase
        $verdicts[$col] = $verdict

        switch ($verdict) {
            'takeSheet'  { $changed += "$col" }
            'conflict'   { $conflicted += [ordered]@{ column = $col; unity = [string]$assetRow[$col]; sheet = [string]$sheetRow[$col]; wasLast = [string]$baseValue } }
            'noBaseline' { $unknown += [ordered]@{ column = $col; unity = [string]$assetRow[$col]; sheet = [string]$sheetRow[$col] } }
        }
    }

    if ($conflicted.Count -gt 0) { $conflicts += [ordered]@{ key = $name; tab = $name; columns = @($conflicted) }; continue }
    if ($unknown.Count -gt 0) { $unresolved += [ordered]@{ key = $name; tab = $name; columns = @($unknown) }; continue }
    if ($changed.Count -eq 0) { continue }

    # Validate prefab names before committing to writing this level at all - an unresolvable name would
    # otherwise leave a half-built board with a hole in it.
    $badPrefabs = @()
    if ($verdicts.Layout -eq 'takeSheet') {
        foreach ($p in $sheet.Placements) {
            if (-not $knownPrefabs.Contains($p.Prefab)) { $badPrefabs += $p.Prefab }
        }
    }
    if ($badPrefabs.Count -gt 0) {
        $problems += "'$name': unknown prefab name(s) on the board - $($badPrefabs -join ', '). Check the Enums tab's Prefab list for the exact spelling. Level left unchanged."
        continue
    }

    $finalWidth = if ($verdicts.BoardWidth -eq 'takeSheet') { [int](ConvertTo-IntOrDefault $sheet.BoardWidth -Default $level.BoardWidth) } else { $level.BoardWidth }
    $finalHeight = if ($verdicts.BoardHeight -eq 'takeSheet') { [int](ConvertTo-IntOrDefault $sheet.BoardHeight -Default $level.BoardHeight) } else { $level.BoardHeight }
    $finalTurns = if ($verdicts.TurnsToSurvive -eq 'takeSheet') { [int](ConvertTo-IntOrDefault $sheet.TurnsToSurvive -Default $level.TurnsToSurvive) } else { $level.TurnsToSurvive }
    $finalHand = if ($verdicts.HandSize -eq 'takeSheet') { [int](ConvertTo-IntOrDefault $sheet.HandSize -Default $level.HandSize) } else { $level.HandSize }
    $finalLoot = if ($verdicts.LootTable -eq 'takeSheet') { $sheet.LootTable } else { $level.LootTable }
    $finalClearReward = if ($verdicts.ClearRewardTable -eq 'takeSheet') { $sheet.ClearRewardTable } else { $level.ClearRewardTable }
    $finalTileSets = if ($verdicts.TileSets -eq 'takeSheet') { @($sheet.TileSets) } else { @($level.TileSets) }

    if ($verdicts.Layout -eq 'takeSheet') {
        $finalPlacements = @($sheet.Placements | ForEach-Object { [ordered]@{ turn = $_.Turn; col = $_.Col; row = $_.Row; prefab = $_.Prefab; deck = @($_.Deck) } })
        $finalPartySpawn = @($sheet.PartySpawn | Sort-Object Index | ForEach-Object { [ordered]@{ col = $_.Col; row = $_.Row } })
    }
    else {
        $finalPlacements = @($level.Placements | ForEach-Object { [ordered]@{ turn = $_.Turn; col = $_.Col; row = $_.Row; prefab = $_.Prefab; deck = @($_.Deck) } })
        $finalPartySpawn = @($level.PartySpawn | Sort-Object Index | ForEach-Object { [ordered]@{ col = $_.Col; row = $_.Row } })
    }

    $relativePath = $level.Path.Replace([string]$repoRoot, '').TrimStart('\', '/') -replace '\\', '/'

    $levelActions += [ordered]@{
        level = $name; assetPath = $relativePath; guid = $guid
        boardWidth = $finalWidth; boardHeight = $finalHeight
        turnsToSurvive = $finalTurns; handSize = $finalHand
        lootTable = $finalLoot; clearRewardTable = $finalClearReward
        tileSets = $finalTileSets
        placements = $finalPlacements
        partySpawn = $finalPartySpawn
        changed = $changed
    }
}

# ---------------------------------------------------------------------------------------------------
# Three-way merge - runs
# ---------------------------------------------------------------------------------------------------

$sheetRunsByName = @{}
foreach ($r in $sheetRuns) { $sheetRunsByName[$r.Name] = $r }

$runActions = @()

foreach ($name in $runsByName.Keys) {
    if (-not $sheetRunsByName.ContainsKey($name)) {
        $problems += "Run '$name' has no matching row on the Runs tab - skipped."
        continue
    }

    $run = $runsByName[$name]
    $sheet = $sheetRunsByName[$name]
    $guid = $run.Guid

    $assetRow = Get-RunMergeRow -Run $run
    $sheetRow = [ordered]@{
        Levels = ($sheet.Levels -join '|')
        CarryDamageBetweenLevels = if ($sheet.CarryDamageBetweenLevels) { 'TRUE' } else { 'FALSE' }
    }

    $hasBase = $guid -and $baseline.Runs.ContainsKey($guid)
    $changed = @()
    $conflicted = @()
    $unknown = @()
    $verdicts = @{}

    foreach ($col in $runMergeColumns) {
        $baseValue = $null
        if ($hasBase -and $baseline.Runs[$guid].ContainsKey($col)) { $baseValue = $baseline.Runs[$guid][$col] }

        $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $assetRow[$col] -Sheet $sheetRow[$col] -HasBaselineEntry $hasBase
        $verdicts[$col] = $verdict

        switch ($verdict) {
            'takeSheet'  { $changed += "$col" }
            'conflict'   { $conflicted += [ordered]@{ column = $col; unity = [string]$assetRow[$col]; sheet = [string]$sheetRow[$col]; wasLast = [string]$baseValue } }
            'noBaseline' { $unknown += [ordered]@{ column = $col; unity = [string]$assetRow[$col]; sheet = [string]$sheetRow[$col] } }
        }
    }

    if ($conflicted.Count -gt 0) { $conflicts += [ordered]@{ key = "Runs/$name"; tab = 'Runs'; columns = @($conflicted) }; continue }
    if ($unknown.Count -gt 0) { $unresolved += [ordered]@{ key = "Runs/$name"; tab = 'Runs'; columns = @($unknown) }; continue }
    if ($changed.Count -eq 0) { continue }

    $badLevels = @()
    if ($verdicts.Levels -eq 'takeSheet') {
        foreach ($l in $sheet.Levels) { if ($knownLevelNames -notcontains $l) { $badLevels += $l } }
    }
    if ($badLevels.Count -gt 0) {
        $problems += "Run '$name': unknown level name(s) - $($badLevels -join ', '). Run left unchanged."
        continue
    }

    $finalLevels = if ($verdicts.Levels -eq 'takeSheet') { @($sheet.Levels) } else { @($run.Levels) }
    $finalCarry = if ($verdicts.CarryDamageBetweenLevels -eq 'takeSheet') { $sheet.CarryDamageBetweenLevels } else { $run.CarryDamageBetweenLevels }

    $relativePath = $run.Path.Replace([string]$repoRoot, '').TrimStart('\', '/') -replace '\\', '/'

    $runActions += [ordered]@{
        run = $name; assetPath = $relativePath; guid = $guid
        levels = $finalLevels; carryDamageBetweenLevels = $finalCarry
        changed = $changed
    }
}

# ---------------------------------------------------------------------------------------------------

$payload = [ordered]@{
    generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    source       = if ($effectiveFromCsv) { 'csv' } else { 'xlsx' }
    levels       = @($levelActions)
    runs         = @($runActions)
    conflicts    = @($conflicts)
    unresolved   = @($unresolved)
    problems     = @($problems)
}

$json = $payload | ConvertTo-Json -Depth 12
Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8

Write-Host ''
Write-Host "Levels updated : $($levelActions.Count)" -ForegroundColor $(if ($levelActions.Count) { 'Yellow' } else { 'DarkGray' })
foreach ($a in $levelActions) { Write-Host "    $($a.level)  :  $($a.changed -join '; ')" -ForegroundColor DarkGray }

Write-Host "Runs updated   : $($runActions.Count)" -ForegroundColor $(if ($runActions.Count) { 'Yellow' } else { 'DarkGray' })
foreach ($a in $runActions) { Write-Host "    $($a.run)  :  $($a.changed -join '; ')" -ForegroundColor DarkGray }

if ($conflicts.Count -gt 0) {
    Write-Host "CONFLICTS : $($conflicts.Count) - changed in BOTH Unity and the sheet, left untouched on both sides" -ForegroundColor Red
    foreach ($c in $conflicts) {
        Write-Host "    $($c.key)" -ForegroundColor Red
        foreach ($col in $c.columns) {
            Write-Host "        $($col.column):" -ForegroundColor DarkGray
            Write-Host "            was    '$($col.wasLast)'" -ForegroundColor DarkGray
            Write-Host "            Unity  '$($col.unity)'" -ForegroundColor DarkGray
            Write-Host "            sheet  '$($col.sheet)'" -ForegroundColor DarkGray
        }
    }
    Write-Host '    Resolve by making both sides agree, or edit only one side and sync again.' -ForegroundColor Red
}

if ($unresolved.Count -gt 0) {
    Write-Host "UNRESOLVED : $($unresolved.Count) - differ, but there is no baseline saying which side moved" -ForegroundColor Red
    foreach ($u in $unresolved) {
        Write-Host "    $($u.key): $((@($u.columns | ForEach-Object { $_.column })) -join ', ')" -ForegroundColor DarkGray
    }
    Write-Host '    Run Export-LevelSheet.ps1 -Force -WriteBaseline to declare Unity correct and start tracking.' -ForegroundColor Red
}

if ($problems.Count -gt 0) {
    Write-Host 'Problems :' -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "    $p" -ForegroundColor Red }
}

Write-Host ''
if ($levelActions.Count -eq 0 -and $runActions.Count -eq 0) {
    Write-Host 'No sheet edits to apply.' -ForegroundColor Green
}
else {
    Write-Host "Wrote $OutputPath" -ForegroundColor Green
    Write-Host 'Now focus the Unity Editor and pick  Tools > Sync Levels With Sheet' -ForegroundColor Cyan
}
