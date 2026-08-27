<#
.SYNOPSIS
    Diffs Docs/EnemySheets.xlsx against the enemy/boss/ally prefabs and writes
    Tools/EnemySheet/enemies.json.

.DESCRIPTION
    Does NOT touch the Unity project. It only produces the work order; Assets/Editor/
    EnemySheetImporter.cs reads that file and writes the prefabs, which is what keeps
    PrefabUtility.EditPrefabContentsScope and SerializedObject writes Unity's business.

    Health, Actions Per Turn, Brain, Targeting, Loot Table, Display Name, Role and Deck are synced by
    three-way merge, per column, exactly like Import-CardSheet.ps1: a column that changed only in the
    sheet is queued as an update; changed only in Unity is left for the next export to pick up; changed
    on both sides since the last sync is a conflict, and disqualifies the WHOLE body rather than
    half-merging it - Deck is one such column even though it is several cells, compared as one joined
    string so a single swapped card cannot look like six independent changes.

    PowerLevel and Boss are different: DERIVED (Brandon's-if-set-else-Estimated, and which folder the
    prefab lives in) rather than something either side authors independently, so they are compared and
    queued as a one-way overwrite instead of merge-arbitrated - see Get-SheetEffectivePower. Every
    other cell on a body tab (the Card Facts block, the averages) is derived in a different sense - pure
    sheet arithmetic never read here at all. Bodies are never created or renamed by the sheet: the
    roster is whatever Get-DiscoveredRoster finds among the actual prefabs, same as the export side, so
    a tab with no matching prefab (should not happen - the export only ever writes one tab per
    discovered body) is skipped rather than guessed at.

.PARAMETER WorkbookPath
    Defaults to Docs/EnemySheets.xlsx at the repo root.

.PARAMETER OutputPath
    Defaults to Tools/EnemySheet/enemies.json.

.PARAMETER FromCsv
    Force reading the Docs/EnemySheets/*.csv mirror even though the workbook is readable. Normally
    unnecessary - see the comment above the exclusive-open probe below.

.PARAMETER OnlyPrefab
    Only consider prefabs whose name matches one of these wildcard patterns.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/EnemySheet/Import-EnemySheet.ps1
#>
[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [string]$OutputPath,
    [switch]$FromCsv,
    [string[]]$OnlyPrefab
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Resolve-Path (Join-Path $scriptDir '..\..')
Import-Module (Join-Path $scriptDir 'EnemySheet.Common.psm1') -Force -DisableNameChecking

if (-not $WorkbookPath) { $WorkbookPath = Join-Path $repoRoot 'Docs\EnemySheets.xlsx' }
if (-not $OutputPath)   { $OutputPath   = Join-Path $scriptDir 'enemies.json' }
$csvDir = Join-Path $repoRoot 'Docs\EnemySheets'

$effectiveFromCsv = [bool]$FromCsv

# Shared-open probe, not the exclusive one Export-EnemySheet.ps1 uses - same reasoning
# Import-CardSheet.ps1 documents: Excel does not hold an .xlsx exclusively just for having it open, so
# probing for exclusive access here would divert to the CSV mirror (only as fresh as the last export)
# on a workbook that was perfectly readable. Fall back only when a read is genuinely impossible.
if (-not $effectiveFromCsv -and (Test-Path -LiteralPath $WorkbookPath)) {
    try {
        $probe = [IO.File]::Open($WorkbookPath, 'Open', 'Read', 'ReadWrite')
        $probe.Close()
    }
    catch {
        Write-Host "$WorkbookPath cannot be read ($($_.Exception.Message.Trim())) - falling back to Docs/EnemySheets/*.csv for this sync." -ForegroundColor Yellow
        $effectiveFromCsv = $true
    }
}

if (-not $effectiveFromCsv) {
    if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
        throw 'The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser'
    }
    Import-Module ImportExcel -DisableNameChecking
}

# ---------------------------------------------------------------------------------------------------
# Read the assets so the diff has something to compare against
# ---------------------------------------------------------------------------------------------------

Write-Host 'Reading assets...' -ForegroundColor Cyan

$scriptGuids = Get-ScriptGuidIndex -RepoRoot $repoRoot
$assetIndex  = Build-AssetIndex -RepoRoot $repoRoot -RelativeFolders @('Assets\Data', 'Assets\Prefabs') -ScriptGuidIndex $scriptGuids
$roster      = Get-DiscoveredRoster -AssetIndex $assetIndex -RepoRoot $repoRoot

$bodiesByPrefab = [ordered]@{}
foreach ($name in $roster.BodyAssets.Keys) {
    $bodiesByPrefab[$name] = Get-CharacterBody -Asset $roster.BodyAssets[$name] -AssetIndex $assetIndex
}

Write-Host "  $($bodiesByPrefab.Count) bodies"

# ---------------------------------------------------------------------------------------------------
# Read the sheet side - one record per body tab, from the workbook or the CSV mirror
# ---------------------------------------------------------------------------------------------------

$sheetRows = @{}   # prefab name -> the same shape Read-BodySheetTab returns

if ($effectiveFromCsv) {
    $rosterCsv = Join-Path $csvDir 'Roster.csv'
    if (Test-Path -LiteralPath $rosterCsv) {
        foreach ($row in (Import-Csv -LiteralPath $rosterCsv)) {
            $prefab = [string]$row.Prefab
            if (-not $prefab) { continue }
            $deck = @()
            if ($row.PSObject.Properties.Name -contains 'Deck' -and $row.Deck) { $deck = @($row.Deck -split '\|') }
            $sheetRows[$prefab] = [pscustomobject]@{
                Prefab = $prefab; Guid = [string]$row.GUID
                DisplayName = [string]$row.DisplayName; Health = [string]$row.Health
                ActionsPerTurn = [string]$row.ActionsPerTurn; Brain = [string]$row.Brain
                Targeting = [string]$row.Targeting; LootTable = [string]$row.LootTable
                Role = [string]$row.Role
                Brandon = [string]$row.BrandonsPowerLevel; EffectivePower = [string]$row.EffectivePower
                Notes = [string]$row.Notes
                Deck = $deck
            }
        }
    }
}
else {
    $pkg = Open-ExcelPackage -Path $WorkbookPath
    try {
        $skip = @('PowerLevel', 'Roster', 'Enums', 'README')
        foreach ($ws in @($pkg.Workbook.Worksheets)) {
            if ($skip -contains $ws.Name) { continue }
            $tab = Read-BodySheetTab -Worksheet $ws
            if ($null -ne $tab) { $sheetRows[$ws.Name] = $tab }
        }
    }
    finally {
        Close-ExcelPackage $pkg -NoSave
    }
}

Write-Host "  $($sheetRows.Count) body tab(s) read from the $(if ($effectiveFromCsv) { 'CSV mirror' } else { 'workbook' })"

# ---------------------------------------------------------------------------------------------------
# Three-way merge, per body
# ---------------------------------------------------------------------------------------------------

$baseline = Read-EnemyBaseline -ToolDir $scriptDir
$mergeColumns = Get-EnemyMergeColumns

$actions    = @()
$conflicts  = @()
$unresolved = @()
$problems   = @()
$filtered   = 0

foreach ($prefab in $bodiesByPrefab.Keys) {
    if ($OnlyPrefab) {
        $match = $false
        foreach ($pattern in $OnlyPrefab) { if ($prefab -like $pattern) { $match = $true; break } }
        if (-not $match) { $filtered++; continue }
    }

    if (-not $sheetRows.ContainsKey($prefab)) {
        $problems += "'$prefab' has no matching tab in the sheet - skipped. Run Export-EnemySheet.ps1 to add it."
        continue
    }

    $body = $bodiesByPrefab[$prefab]
    $sheet = $sheetRows[$prefab]
    $guid = $body.Guid

    $assetRow = Get-EnemyMergeRow -Body $body
    $sheetRow = [ordered]@{
        DisplayName = $sheet.DisplayName; Health = $sheet.Health; ActionsPerTurn = $sheet.ActionsPerTurn
        Brain = $sheet.Brain; Targeting = $sheet.Targeting; LootTable = $sheet.LootTable
        Deck = ConvertTo-DeckComparable -Names $sheet.Deck; Role = $sheet.Role
    }

    $hasBase = $guid -and $baseline.Bodies.ContainsKey($guid)

    $changed = @()
    $conflicted = @()
    $unknown = @()
    $verdicts = @{}

    foreach ($col in $mergeColumns) {
        $baseValue = $null
        if ($hasBase -and $baseline.Bodies[$guid].ContainsKey($col)) { $baseValue = $baseline.Bodies[$guid][$col] }

        $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $assetRow[$col] -Sheet $sheetRow[$col] -HasBaselineEntry $hasBase
        $verdicts[$col] = $verdict

        switch ($verdict) {
            'takeSheet'  { $changed += "$col ('$($assetRow[$col])' -> '$($sheetRow[$col])')" }
            'conflict'   { $conflicted += [ordered]@{ column = $col; unity = [string]$assetRow[$col]; sheet = [string]$sheetRow[$col]; wasLast = [string]$baseValue } }
            'noBaseline' { $unknown += [ordered]@{ column = $col; unity = [string]$assetRow[$col]; sheet = [string]$sheetRow[$col] } }
        }
    }

    if ($conflicted.Count -gt 0) {
        $conflicts += [ordered]@{ key = $prefab; tab = $prefab; columns = @($conflicted) }
        continue
    }

    if ($unknown.Count -gt 0) {
        $unresolved += [ordered]@{ key = $prefab; tab = $prefab; columns = @($unknown) }
        continue
    }

    # PowerLevel and Boss are derived, not merge-arbitrated (see Get-EnemyMergeColumns), so they are
    # compared and queued independently of $changed - a body can need a power/boss update on a sync
    # where nothing else moved (the very first sync after this feature ships, for every prefab at once).
    $computedPower = Get-SheetEffectivePower -SheetRow $sheet
    $powerNeedsUpdate = ($null -ne $computedPower) -and ([Math]::Abs($computedPower - $body.PowerOnAsset) -gt 0.0001)
    $bossNeedsUpdate = ($body.Boss -ne $body.BossOnAsset)

    if ($null -eq $computedPower -and $body.PowerOnAsset -eq 0) {
        $problems += "'$prefab' has no Brandon's or Estimated Power Level yet - EncounterRoller can " +
            'never draw it until one is authored (open Docs/EnemySheets.xlsx in Excel at least once ' +
            'so a formula-only Estimated has something cached to read).'
    }

    if ($powerNeedsUpdate) { $changed += "PowerLevel ('$($body.PowerOnAsset)' -> '$computedPower')" }
    if ($bossNeedsUpdate) { $changed += "Boss ('$($body.BossOnAsset)' -> '$($body.Boss)')" }

    if ($changed.Count -eq 0) { continue }

    $relativePath = $body.Path.Replace([string]$repoRoot, '').TrimStart('\', '/') -replace '\\', '/'

    # Field-by-field, not "the sheet wins the whole row": a column verdicted anything other than
    # takeSheet (agree, or takeAsset - moved in Unity only) keeps the ASSET's current value. Only the
    # column(s) that actually moved in the sheet are taken from it. Without this, editing Health in the
    # sheet while Brain was quietly changed in the Inspector since the last sync would silently regress
    # Brain back to its stale pre-sync value the moment this payload is applied.
    $finalDisplayName = if ($verdicts.DisplayName -eq 'takeSheet') { $sheet.DisplayName } else { $body.DisplayName }
    $finalHealth = if ($verdicts.Health -eq 'takeSheet') { [int](ConvertTo-IntOrDefault $sheet.Health -Default $body.MaxHealth) } else { $body.MaxHealth }
    $finalActions = if ($verdicts.ActionsPerTurn -eq 'takeSheet') { [int](ConvertTo-IntOrDefault $sheet.ActionsPerTurn -Default $body.ActionPoints) } else { $body.ActionPoints }
    $finalBrainName = if ($verdicts.Brain -eq 'takeSheet') { $sheet.Brain } else { $body.Brain }
    $finalTargeting = if ($verdicts.Targeting -eq 'takeSheet') { $sheet.Targeting } else { $body.Targeting }
    $finalLoot = if ($verdicts.LootTable -eq 'takeSheet') { $sheet.LootTable } else { $body.LootTable }
    $finalDeck = if ($verdicts.Deck -eq 'takeSheet') { @($sheet.Deck) } else { @($body.Deck | ForEach-Object { $_.Name }) }
    $finalRoleName = if ($verdicts.Role -eq 'takeSheet') { $sheet.Role } else { $body.Role }

    # PowerLevel/Boss are one-way overwrites (there is no "asset side" value a designer authors
    # independently for either), so unlike the columns above they are never gated by a verdict - the
    # computed/derived value wins outright whenever it differs from what is already on the prefab.
    $finalPower = if ($null -ne $computedPower) { $computedPower } else { $body.PowerOnAsset }

    $actions += [ordered]@{
        prefab       = $prefab
        assetPath    = $relativePath
        guid         = $guid
        displayName  = $finalDisplayName
        maxHealth    = $finalHealth
        actionPoints = $finalActions
        brain        = Get-EnumValue -Table @('None', 'Warrior', 'Ranger', 'Summoner') -Name $finalBrainName
        targeting    = $finalTargeting
        lootTable    = $finalLoot
        deck         = $finalDeck
        battleRole   = ConvertFrom-BattleRoleName $finalRoleName
        powerLevel   = $finalPower
        isBoss       = $body.Boss
        changed      = $changed
    }
}

# ---------------------------------------------------------------------------------------------------

$payload = [ordered]@{
    generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    source       = if ($effectiveFromCsv) { 'csv' } else { 'xlsx' }
    bodies       = @($actions)
    conflicts    = @($conflicts)
    unresolved   = @($unresolved)
    problems     = @($problems)
}

$json = $payload | ConvertTo-Json -Depth 10
Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8

Write-Host ''
Write-Host "Update : $($actions.Count)" -ForegroundColor $(if ($actions.Count) { 'Yellow' } else { 'DarkGray' })
foreach ($a in $actions) { Write-Host "    $($a.prefab)  :  $($a.changed -join '; ')" -ForegroundColor DarkGray }

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
    Write-Host '    Run Export-EnemySheet.ps1 -Force -WriteBaseline to declare Unity correct and start tracking.' -ForegroundColor Red
}

if ($filtered -gt 0) { Write-Host "Prefabs excluded by -OnlyPrefab filter : $filtered" -ForegroundColor DarkGray }
if ($problems.Count -gt 0) {
    Write-Host 'Problems :' -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "    $p" -ForegroundColor Red }
}

Write-Host ''
if ($actions.Count -eq 0) {
    Write-Host 'No sheet edits to apply.' -ForegroundColor Green
}
else {
    Write-Host "Wrote $OutputPath" -ForegroundColor Green
    Write-Host 'Now focus the Unity Editor and pick  Tools > Sync Enemies With Sheet' -ForegroundColor Cyan
}
