<#
.SYNOPSIS
    Diffs a roster design workbook against its prefabs and writes the matching work order -
    Docs/EnemySheets.xlsx -> Tools/EnemySheet/enemies.json by default, or Docs/BossDesign.xlsx ->
    Tools/BossSheet/bosses.json with -Domain Bosses.

.DESCRIPTION
    Does NOT touch the Unity project. It only produces the work order; Assets/Editor/
    EnemySheetImporter.cs or BossSheetImporter.cs reads that file and writes the prefabs, which is what
    keeps PrefabUtility.EditPrefabContentsScope and SerializedObject writes Unity's business.

    This one script serves BOTH roster workbooks; everything that differs comes from Get-RosterDomain
    in EnemySheet.Common.psm1. Tools/BossSheet/Import-BossSheet.ps1 is a forwarder passing
    -Domain Bosses. Because each domain has its own roster, workbook and baseline, the two can never
    queue a write for the same prefab - which is exactly why a boss's summoned enemy is a read-only row
    on the Summoned tab rather than a body tab in the boss workbook.

    Health, Actions Per Turn, Brain, Targeting, Loot Table, Display Name, Role and Deck can be typed on
    EITHER the body tab or the Roster tab - Resolve-SheetPair collapses the two into one sheet-side value
    first (same verdict logic as the three-way merge below, one layer earlier), which is then merged by
    three-way, per column, exactly like Import-CardSheet.ps1: a column that changed only in the sheet is
    queued as an update; changed only in Unity is left for the next export to pick up; changed on both
    sides since the last sync is a conflict, and disqualifies the WHOLE body rather than half-merging it
    - Deck is one such column even though it is several cells, compared as one joined string so a single
    swapped card cannot look like six independent changes (and is body-tab-only - the Roster tab carries
    no Deck column, so it is never a second opinion on it).

    PowerLevel and Boss are different: DERIVED (Brandon's-if-set-else-Estimated, and which folder the
    prefab lives in) rather than something either side authors independently, so they are compared and
    queued as a one-way overwrite instead of merge-arbitrated - see Get-SheetEffectivePower. Every
    other cell on a body tab (the Card Facts block, the averages) is derived in a different sense - pure
    sheet arithmetic never read here at all.

    Bodies are never CREATED by the sheet: the roster is whatever Get-DiscoveredRoster finds among the
    actual prefabs, same as the export side, so a tab with no matching prefab (should not happen - the
    export only ever writes one tab per discovered body) is skipped rather than guessed at. Renaming an
    EXISTING body is the one exception - typing a new Name on its Roster row (validated by
    Test-EnemyRenameCandidate, then applied via AssetDatabase.RenameAsset by RosterSheetSync.WriteBody)
    queues a rename rather than a create, since the GUID it is keyed on already exists.

.PARAMETER Domain
    Which roster workbook to read: Enemies (default) or Bosses.

.PARAMETER WorkbookPath
    Overrides the domain's default workbook path.

.PARAMETER OutputPath
    Overrides the domain's default work-order path.

.PARAMETER FromCsv
    Force reading the domain's CSV mirror even though the workbook is readable. Normally unnecessary -
    see the comment above the exclusive-open probe below.

.PARAMETER OnlyPrefab
    Only consider prefabs whose name matches one of these wildcard patterns.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/EnemySheet/Import-EnemySheet.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Enemies', 'Bosses')]
    [string]$Domain = 'Enemies',
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

$rosterDomain = Get-RosterDomain -Name $Domain -RepoRoot $repoRoot

if (-not $WorkbookPath) { $WorkbookPath = $rosterDomain.Workbook }
if (-not $OutputPath)   { $OutputPath   = $rosterDomain.JsonPath }
$csvDir = $rosterDomain.CsvDir

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
        Write-Host "$WorkbookPath cannot be read ($($_.Exception.Message.Trim())) - falling back to $($rosterDomain.CsvDirRel)/*.csv for this sync." -ForegroundColor Yellow
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
$roster      = Get-DiscoveredRoster -AssetIndex $assetIndex -RepoRoot $repoRoot `
    -Folders $rosterDomain.Folders -SummonsAsReference:$rosterDomain.SummonsAsReference

$bodiesByPrefab = [ordered]@{}
foreach ($name in $roster.BodyAssets.Keys) {
    $bodiesByPrefab[$name] = Get-CharacterBody -Asset $roster.BodyAssets[$name] -AssetIndex $assetIndex
}

Write-Host "  $($bodiesByPrefab.Count) bodies"

# ---------------------------------------------------------------------------------------------------
# Read the sheet side - one record per body tab, from the workbook or the CSV mirror
# ---------------------------------------------------------------------------------------------------

$sheetRows = @{}    # prefab name -> the same shape Read-BodySheetTab returns
$rosterRows = $null # GUID -> the same shape Read-RosterSheetTab returns; $null on the -FromCsv path,
                    # which has no Roster tab equivalent to read - every Resolve-SheetPair call below
                    # treats that the same as "no Roster row for this GUID", same as an old workbook
                    # from before this feature would.

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
        $skip = Get-NonBodyTabNames
        foreach ($ws in @($pkg.Workbook.Worksheets)) {
            if ($skip -contains $ws.Name) { continue }
            $tab = Read-BodySheetTab -Worksheet $ws
            if ($null -ne $tab) { $sheetRows[$ws.Name] = $tab }
        }

        $rosterRows = Read-RosterSheetTab -Worksheet $pkg.Workbook.Worksheets['Roster']
    }
    finally {
        Close-ExcelPackage $pkg -NoSave
    }
}

Write-Host "  $($sheetRows.Count) body tab(s) read from the $(if ($effectiveFromCsv) { 'CSV mirror' } else { 'workbook' })"
if ($null -ne $rosterRows) { Write-Host "  $($rosterRows.Count) Roster tab row(s) read" }

# ---------------------------------------------------------------------------------------------------
# Three-way merge, per body
# ---------------------------------------------------------------------------------------------------

$baseline = Read-EnemyBaseline -ToolDir $rosterDomain.ToolDir
$mergeColumns = Get-EnemyMergeColumns

# Every prefab currently under Assets/Prefabs/{Enemies,Bosses,Allies} - what a rename must not collide
# with. Both domains share one folder-agnostic name space (the pow_* named-cell scope spans both
# workbooks via the Summoned tab), so this is deliberately NOT filtered to $rosterDomain.Folders.
$allPrefabNames = Get-AllCharacterPrefabNames -AssetIndex $assetIndex
$namesUsedThisBatch = @{}   # target name -> true, for every rename already queued THIS run - stops two
                            # different bodies both being renamed to the same new name in one sync

$actions       = @()
$conflicts     = @()
$unresolved    = @()
$sheetConflicts = @()   # body tab vs Roster row disagreements - see Resolve-SheetPair
$problems      = @()
$filtered      = 0

foreach ($prefab in $bodiesByPrefab.Keys) {
    if ($OnlyPrefab) {
        $match = $false
        foreach ($pattern in $OnlyPrefab) { if ($prefab -like $pattern) { $match = $true; break } }
        if (-not $match) { $filtered++; continue }
    }

    if (-not $sheetRows.ContainsKey($prefab)) {
        $problems += "'$prefab' has no matching tab in the sheet - skipped. Run $($rosterDomain.ExportScriptName) to add it."
        continue
    }

    $body = $bodiesByPrefab[$prefab]
    $sheet = $sheetRows[$prefab]
    $guid = $body.Guid

    $roster = if ($rosterRows -and $rosterRows.Contains($guid)) { $rosterRows[$guid] } else { $null }

    $assetRow = Get-EnemyMergeRow -Body $body
    $bodyTabRow = [ordered]@{
        DisplayName = $sheet.DisplayName; Health = $sheet.Health; ActionsPerTurn = $sheet.ActionsPerTurn
        Brain = $sheet.Brain; Targeting = $sheet.Targeting; LootTable = $sheet.LootTable
        Deck = ConvertTo-DeckComparable -Names $sheet.Deck; Role = $sheet.Role
    }
    # Roster carries no Deck column, so it is never a second opinion on it - Deck here always mirrors
    # the body tab's own, which makes Resolve-SheetPair a guaranteed 'agree' for that one column below.
    $rosterRowVals = if ($roster) {
        [ordered]@{
            DisplayName = $roster.DisplayName; Health = $roster.Health; ActionsPerTurn = $roster.ActionsPerTurn
            Brain = $roster.Brain; Targeting = $roster.Targeting; LootTable = $roster.LootTable
            Deck = $bodyTabRow.Deck; Role = $roster.Role
        }
    } else { $null }

    $hasBase = $guid -and $baseline.Bodies.ContainsKey($guid)

    $changed = @()
    $conflicted = @()
    $unknown = @()
    $sheetConflicted = @()
    $verdicts = @{}
    $sheetRow = [ordered]@{}   # the COLLAPSED body-tab/Roster value per column - see Resolve-SheetPair

    foreach ($col in $mergeColumns) {
        $baseValue = $null
        if ($hasBase -and $baseline.Bodies[$guid].ContainsKey($col)) { $baseValue = $baseline.Bodies[$guid][$col] }

        $rosterValue = if ($rosterRowVals) { $rosterRowVals[$col] } else { $null }
        $pair = Resolve-SheetPair -Baseline $baseValue -BodyTab $bodyTabRow[$col] -Roster $rosterValue -HasBaselineEntry $hasBase
        if ($pair.Verdict -eq 'sheetConflict') {
            $sheetConflicted += [ordered]@{
                column = $col; bodyTab = [string]$bodyTabRow[$col]; roster = [string]$rosterValue; wasLast = [string]$baseValue
            }
            continue
        }

        $sheetRow[$col] = $pair.Value

        $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $assetRow[$col] -Sheet $sheetRow[$col] -HasBaselineEntry $hasBase
        $verdicts[$col] = $verdict

        switch ($verdict) {
            'takeSheet'  { $changed += "$col ('$($assetRow[$col])' -> '$($sheetRow[$col])')" }
            'conflict'   { $conflicted += [ordered]@{ column = $col; unity = [string]$assetRow[$col]; sheet = [string]$sheetRow[$col]; wasLast = [string]$baseValue } }
            'noBaseline' { $unknown += [ordered]@{ column = $col; unity = [string]$assetRow[$col]; sheet = [string]$sheetRow[$col] } }
        }
    }

    # Name: the Roster tab is the only sheet-side source for it - a body tab's own name row is a plain
    # readout of the same worksheet name, never a second copy to reconcile - so this is a direct
    # three-way check against the asset's current name rather than a Resolve-SheetPair layer.
    #
    # -HasBaselineEntry is checked PER COLUMN here (unlike the loop above, which uses $hasBase for the
    # whole body) because baseline.json only gained a Name column when this feature shipped: a body
    # renamed in Unity before then would otherwise misreport as a 'conflict' (baseline exists for the
    # GUID, just not for Name) instead of the more honest 'noBaseline'. One-time; gone after the next
    # Refresh Sheet From Unity or a successful sync writes a Name entry for every body.
    $nameHasBase = $hasBase -and $baseline.Bodies[$guid].ContainsKey('Name')
    $nameBase = if ($nameHasBase) { $baseline.Bodies[$guid]['Name'] } else { $null }
    $rosterName = if ($roster -and $roster.Name) { $roster.Name } else { $body.Prefab }
    $nameVerdict = Resolve-ThreeWay -Baseline $nameBase -Asset $body.Prefab -Sheet $rosterName -HasBaselineEntry $nameHasBase
    switch ($nameVerdict) {
        'conflict'   { $conflicted += [ordered]@{ column = 'Name'; unity = $body.Prefab; sheet = $rosterName; wasLast = [string]$nameBase } }
        'noBaseline' { $unknown += [ordered]@{ column = 'Name'; unity = $body.Prefab; sheet = $rosterName } }
    }

    # Brandon's Power Level: never an asset field at all (see Write-EnemyBaseline's doc comment), so its
    # two sheet copies are compared directly rather than through Resolve-ThreeWay - there is no "asset
    # side" to reconcile against, only body-tab-vs-Roster.
    $brandonHasBase = $hasBase -and $baseline.Bodies[$guid].ContainsKey('Brandon')
    $brandonBase = if ($brandonHasBase) { $baseline.Bodies[$guid]['Brandon'] } else { $null }
    $rosterBrandon = if ($roster) { $roster.Brandon } else { $null }
    $brandonPair = Resolve-SheetPair -Baseline $brandonBase -BodyTab $sheet.Brandon -Roster $rosterBrandon -HasBaselineEntry $brandonHasBase
    if ($brandonPair.Verdict -eq 'sheetConflict') {
        $sheetConflicted += [ordered]@{
            column = "Brandon's Power Level"; bodyTab = [string]$sheet.Brandon; roster = [string]$rosterBrandon; wasLast = [string]$brandonBase
        }
    }

    if ($sheetConflicted.Count -gt 0) {
        $sheetConflicts += [ordered]@{ key = $prefab; tab = $prefab; columns = @($sheetConflicted) }
        continue
    }

    if ($conflicted.Count -gt 0) {
        $conflicts += [ordered]@{ key = $prefab; tab = $prefab; columns = @($conflicted) }
        continue
    }

    if ($unknown.Count -gt 0) {
        $unresolved += [ordered]@{ key = $prefab; tab = $prefab; columns = @($unknown) }
        continue
    }

    # Safe to resolve the rename now that nothing about this body is ambiguous. A name that fails
    # validation is reported and dropped - NOT a reason to also skip the Health/Brain/etc. changes
    # above, since it is a validation failure rather than a merge ambiguity (there is nothing unclear
    # about what the sheet wants; it just cannot be honored as typed).
    $finalName = $body.Prefab
    if ($nameVerdict -eq 'takeSheet') {
        $renameError = Test-EnemyRenameCandidate -NewName $rosterName -AllNames $allPrefabNames -UsedThisBatch $namesUsedThisBatch
        if ($renameError) {
            $problems += "'$prefab' cannot be renamed to '$rosterName': $renameError. Left as '$prefab'."
        }
        else {
            $finalName = $rosterName
            $namesUsedThisBatch[$rosterName] = $true
            $changed += "Name ('$($body.Prefab)' -> '$rosterName')"
        }
    }

    # PowerLevel and Boss are derived, not merge-arbitrated (see Get-EnemyMergeColumns), so they are
    # compared and queued independently of $changed - a body can need a power/boss update on a sync
    # where nothing else moved (the very first sync after this feature ships, for every prefab at once).
    # Brandon's Power Level feeds this as $brandonPair.Value - the resolved body-tab/Roster value, not
    # $sheet.Brandon directly, since the Roster tab may be the side that actually changed it.
    #
    # A copy of $sheet with Brandon overridden, not a fresh object - $sheet carries EITHER .Estimated
    # (read from an open workbook tab) OR .EffectivePower (the CSV mirror's pre-resolved column, on the
    # -FromCsv path), never both, and Get-SheetEffectivePower already branches on which one is present.
    # Hard-coding "Estimated" here would throw under Set-StrictMode on the CSV path, where that property
    # does not exist at all.
    $effectiveRow = $sheet.PSObject.Copy()
    $effectiveRow.Brandon = $brandonPair.Value
    $computedPower = Get-SheetEffectivePower -SheetRow $effectiveRow
    $powerNeedsUpdate = ($null -ne $computedPower) -and ([Math]::Abs($computedPower - $body.PowerOnAsset) -gt 0.0001)
    $bossNeedsUpdate = ($body.Boss -ne $body.BossOnAsset)

    if ($null -eq $computedPower -and $body.PowerOnAsset -eq 0) {
        $problems += "'$prefab' has no Brandon's or Estimated Power Level yet - EncounterRoller can " +
            "never draw it until one is authored (open $($rosterDomain.WorkbookRel) in Excel at least once " +
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
    #
    # $sheetRow (the COLLAPSED body-tab/Roster value), not $sheet directly - the value that actually
    # moved may have come from the Roster tab. Deck is the one exception: Roster carries no Deck column,
    # so its sheet-side value is always the body tab's own $sheet.Deck (an array of card names, not the
    # joined-string form $sheetRow.Deck holds for comparison purposes).
    $finalDisplayName = if ($verdicts.DisplayName -eq 'takeSheet') { $sheetRow.DisplayName } else { $body.DisplayName }
    $finalHealth = if ($verdicts.Health -eq 'takeSheet') { [int](ConvertTo-IntOrDefault $sheetRow.Health -Default $body.MaxHealth) } else { $body.MaxHealth }
    $finalActions = if ($verdicts.ActionsPerTurn -eq 'takeSheet') { [int](ConvertTo-IntOrDefault $sheetRow.ActionsPerTurn -Default $body.ActionPoints) } else { $body.ActionPoints }
    $finalBrainName = if ($verdicts.Brain -eq 'takeSheet') { $sheetRow.Brain } else { $body.Brain }
    $finalTargeting = if ($verdicts.Targeting -eq 'takeSheet') { $sheetRow.Targeting } else { $body.Targeting }
    $finalLoot = if ($verdicts.LootTable -eq 'takeSheet') { $sheetRow.LootTable } else { $body.LootTable }
    $finalDeck = if ($verdicts.Deck -eq 'takeSheet') { @($sheet.Deck) } else { @($body.Deck | ForEach-Object { $_.Name }) }
    $finalRoleName = if ($verdicts.Role -eq 'takeSheet') { $sheetRow.Role } else { $body.Role }

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
        newName      = if ($finalName -ne $body.Prefab) { $finalName } else { '' }
        changed      = $changed
    }
}

# ---------------------------------------------------------------------------------------------------

$payload = [ordered]@{
    generatedUtc   = (Get-Date).ToUniversalTime().ToString('o')
    source         = if ($effectiveFromCsv) { 'csv' } else { 'xlsx' }
    bodies         = @($actions)
    conflicts      = @($conflicts)
    unresolved     = @($unresolved)
    sheetConflicts = @($sheetConflicts)
    problems       = @($problems)
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
    Write-Host "    Run $($rosterDomain.ExportScriptName) -Force -WriteBaseline to declare Unity correct and start tracking." -ForegroundColor Red
}

if ($sheetConflicts.Count -gt 0) {
    Write-Host "SHEET CONFLICTS : $($sheetConflicts.Count) - changed on BOTH the body tab and the Roster tab, left untouched on both" -ForegroundColor Red
    foreach ($c in $sheetConflicts) {
        Write-Host "    $($c.key)" -ForegroundColor Red
        foreach ($col in $c.columns) {
            Write-Host "        $($col.column):" -ForegroundColor DarkGray
            Write-Host "            was       '$($col.wasLast)'" -ForegroundColor DarkGray
            Write-Host "            body tab  '$($col.bodyTab)'" -ForegroundColor DarkGray
            Write-Host "            Roster    '$($col.roster)'" -ForegroundColor DarkGray
        }
    }
    Write-Host '    Resolve by making the body tab and Roster agree, or edit only one of them and sync again.' -ForegroundColor Red
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
    Write-Host "Now focus the Unity Editor and pick  $($rosterDomain.SyncMenu)" -ForegroundColor Cyan
}
