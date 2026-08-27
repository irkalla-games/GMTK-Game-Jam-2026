<#
.SYNOPSIS
    Builds Docs/EnemySheets.xlsx from the enemy/boss/ally prefabs and their decks.

.DESCRIPTION
    Read-only against the Unity project, same guarantee Export-CardSheet.ps1 makes: it parses
    Assets/Prefabs/**/*.prefab and Assets/Data/CardData/**/*.asset and writes only into Docs/. Safe to
    run at any time, including with the Editor open.

    The everyday sync (Tools > Sync Enemies With Sheet in Unity) runs Import-EnemySheet.ps1 first to
    apply sheet edits to the prefabs, then this script to refresh the sheet from the result - so running
    this on its own only makes sense to pick up a change made directly in the Inspector, or to preview
    what the sheet would look like. Health/Actions/Brain/Targeting/Loot Table/Deck are the synced
    fields; Brandon's Power Level and Notes are the two fields this workbook treats as authored IN the
    sheet and preserves across every re-export, the same way Export-CardSheet.ps1 preserves the Ideas
    tabs and card Notes column.

    Roster discovery: the primary roster is every non-Tutorial prefab under Assets/Prefabs/Enemies,
    Assets/Prefabs/Bosses and Assets/Prefabs/Allies that carries a Character component. A second pass
    (repeated to a fixed point, since a newly-discovered body might itself summon something not yet
    seen) walks every deck for Summon cards and adds whatever they summon - a Totem prefab, or another
    Character body outside the primary folders - so a boss's summoned totem gets scored too.

.PARAMETER WorkbookPath
    Defaults to Docs/EnemySheets.xlsx at the repo root.

.PARAMETER NoCsvMirror
    Skip writing Docs/EnemySheets/*.csv. The mirror exists so git diffs of the workbook are readable,
    and is what Import-EnemySheet.ps1 reads when the workbook itself is open in Excel.

.PARAMETER WriteBaseline
    Record the state both sides now agree on into baseline.json. Passed by the sync (Tools > Sync
    Enemies With Sheet), which has just made them agree. Running the export on its own does NOT move
    the baseline, because on its own it has not reconciled anything.

.PARAMETER Force
    Rebuild the sheet even though it holds edits that have not reached the prefabs, discarding them.
    Without this the export refuses rather than silently overwriting your work.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/EnemySheet/Export-EnemySheet.ps1
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
Import-Module (Join-Path $scriptDir 'EnemySheet.Common.psm1') -Force -DisableNameChecking

if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    throw 'The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser'
}
Import-Module ImportExcel -DisableNameChecking

if (-not $WorkbookPath) { $WorkbookPath = Join-Path $repoRoot 'Docs\EnemySheets.xlsx' }
$csvDir = Join-Path $repoRoot 'Docs\EnemySheets'

# Same exclusive-open probe Export-CardSheet.ps1 uses, for the same reason: this rewrites the whole
# workbook, so a copy held open in Excel would otherwise surface as a raw sharing violation after all
# the parsing work below has already run.
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

# ---------------------------------------------------------------------------------------------------
# Read every prefab and card once.
# ---------------------------------------------------------------------------------------------------

Write-Host 'Reading assets...' -ForegroundColor Cyan

$scriptGuids = Get-ScriptGuidIndex -RepoRoot $repoRoot
$assetIndex  = Build-AssetIndex -RepoRoot $repoRoot -RelativeFolders @('Assets\Data', 'Assets\Prefabs') -ScriptGuidIndex $scriptGuids

# ---------------------------------------------------------------------------------------------------
# Roster discovery
# ---------------------------------------------------------------------------------------------------

$roster = Get-DiscoveredRoster -AssetIndex $assetIndex -RepoRoot $repoRoot
$bodyAssets = $roster.BodyAssets
$totemAssets = $roster.TotemAssets

Write-Host "  $($bodyAssets.Count) bodies, $($totemAssets.Count) totems"

# ---------------------------------------------------------------------------------------------------
# Build the full record for every body and totem - deck cards resolved into facts.
# ---------------------------------------------------------------------------------------------------

$bodies = @()
foreach ($name in $bodyAssets.Keys) {
    $asset = $bodyAssets[$name]
    $charBody = Get-CharacterBody -Asset $asset -AssetIndex $assetIndex

    $deckDetail = @()
    foreach ($c in $charBody.Deck) {
        if ($null -eq $c.Asset) {
            $deckDetail += [pscustomobject]@{ Name = $c.Name; Record = $null; Facts = $null; SummonTarget = ''; SummonLifetime = 0 }
            continue
        }

        $rec = Get-CardRecord -Asset $c.Asset -AssetIndex $assetIndex -RepoRoot $repoRoot
        $facts = Get-CardFacts -CardRecord $rec

        $summonTarget = ''
        $summonLifetime = 0
        if ($facts.IsSummon -and $null -ne $facts.SummonEffectAsset) {
            $tRef = Get-NodeField $facts.SummonEffectAsset.Node 'summonedObject'
            $tAsset = Resolve-AssetReference -Reference $tRef -AssetIndex $assetIndex
            if ($null -ne $tAsset) { $summonTarget = $tAsset.Name }
            $summonLifetime = ConvertTo-IntOrDefault (Get-NodeField $facts.SummonEffectAsset.Node 'lifetimeTurns')
        }

        $deckDetail += [pscustomobject]@{
            Name = $c.Name; Record = $rec; Facts = $facts
            SummonTarget = $summonTarget; SummonLifetime = $summonLifetime
        }
    }

    # Every field Get-EnemyMergeRow, Write-EnemyWorkbook or the CSV mirror reads has to be re-listed
    # here: this is a fresh object, not the Get-CharacterBody one, so a field added there and forgotten
    # here is a StrictMode PropertyNotFound at the far end rather than a blank cell. Role is the reason
    # this comment exists. PowerOnAsset/BossOnAsset deliberately stay off - only Import-EnemySheet.ps1
    # reads those, and it calls Get-CharacterBody directly.
    $bodies += [pscustomobject]@{
        Prefab = $charBody.Prefab; Guid = $charBody.Guid; Boss = $charBody.Boss
        DisplayName = $charBody.DisplayName; MaxHealth = $charBody.MaxHealth
        ActionPoints = $charBody.ActionPoints; Brain = $charBody.Brain
        Targeting = $charBody.Targeting; LootTable = $charBody.LootTable
        Role = $charBody.Role
        Deck = $deckDetail
    }
}

$totems = @()
foreach ($name in $totemAssets.Keys) {
    $totems += (Get-TotemBody -Asset $totemAssets[$name] -AssetIndex $assetIndex)
}

# ---------------------------------------------------------------------------------------------------
# Enum sources for the dropdowns
# ---------------------------------------------------------------------------------------------------

# Deck dropdown: any card authored NotOffered, wherever it lives - Rarity is the actual rule ("every
# enemy/boss card is 4") rather than the Enemy/Generic folder convention, which is only where the
# generator happens to have put them. Uses the asset's own resolved Name (what Resolve-AssetName and
# the C# importer's FindByName<CardData> both key on), not the Card Name display text.
$enemyCardAssets = @($assetIndex.All | Where-Object { $_.Type -eq 'CardData' })
$enemyCardNames = @($enemyCardAssets | Where-Object {
    (Get-CardRecord -Asset $_ -AssetIndex $assetIndex -RepoRoot $repoRoot).Row.Rarity -eq 'NotOffered'
} | ForEach-Object { $_.Name })

$lootNames = @($assetIndex.All | Where-Object { $_.Type -eq 'LootTable' } | ForEach-Object { $_.Name })
$targetingNames = @($assetIndex.All | Where-Object { $_.Type -eq 'TargetingPattern' } | ForEach-Object { $_.Name })

# ---------------------------------------------------------------------------------------------------
# Preserve Brandon's Power Level and Notes from whatever workbook is already on disk.
#
# Matched by tab name (= prefab name) first. A tab whose name is not a known prefab - the hand-authored
# "Skeleton Warrior" tab this workbook started from - is matched by Display Name instead, so the one
# real overlap (SkeletonWarrior.prefab's displayName is "Skeleton Warrior") carries its numbers across
# the rename to the prefab-named tab. Anything that matches neither is simply not carried forward.
# ---------------------------------------------------------------------------------------------------

function Get-ExistingBodyPreserved {
    param([string]$WorkbookPath, $Bodies)

    $preserved = @{}
    if (-not (Test-Path -LiteralPath $WorkbookPath)) { return $preserved }

    try { $pkg = Open-ExcelPackage -Path $WorkbookPath }
    catch { Write-Warning "Could not open the existing workbook to preserve Brandon's/Notes: $($_.Exception.Message)"; return $preserved }

    try {
        $skip = @('PowerLevel', 'Roster', 'Enums', 'README')
        foreach ($ws in @($pkg.Workbook.Worksheets)) {
            if ($skip -contains $ws.Name) { continue }

            $tab = Read-BodySheetTab -Worksheet $ws
            if ($null -eq $tab) { continue }

            $key = $ws.Name
            if (-not ($Bodies | Where-Object { $_.Prefab -eq $key })) {
                $match = $Bodies | Where-Object { $_.DisplayName -and ($_.DisplayName -eq $ws.Name -or $_.DisplayName -eq $tab.DisplayName) } | Select-Object -First 1
                if ($match) { $key = $match.Prefab } else { continue }
            }

            # Estimated is a formula (Write-EnemyWorkbook.ps1's own Cells[...].Formula), never
            # preserved by that write - its cached .Text here is whatever Excel last recalculated it
            # to, which is why it is read from the OLD workbook alongside Brandon's/Notes rather than
            # recomputed here. A workbook nobody has opened in Excel since it last changed has no
            # cache yet, which is exactly why EffectivePower below falls back to Brandon's first.
            $preserved[$key] = @{ Brandon = $tab.Brandon; Notes = $tab.Notes; Estimated = $tab.Estimated }
        }
    }
    finally {
        Close-ExcelPackage $pkg -NoSave
    }

    return $preserved
}

$preserved = Get-ExistingBodyPreserved -WorkbookPath $WorkbookPath -Bodies $bodies
Write-Host "  Preserved Brandon's/Notes for $($preserved.Count) tab(s) from the existing workbook"

# ---------------------------------------------------------------------------------------------------
# Preserve the PowerLevel tab - both the pw_* weights and the Range Power table - the same way
# Brandon's/Notes survive above. Without this, retuning a weight directly in Excel and then syncing
# would silently snap it back to Get-PowerWeightDefaults'/Get-RangePowerDefaults' starting numbers,
# since this tab was otherwise always rebuilt from those hardcoded defaults on every export.
# ---------------------------------------------------------------------------------------------------

function Get-ExistingPowerLevelValues {
    param([string]$WorkbookPath)

    $result = @{ Weights = @{}; RangeRows = @() }
    if (-not (Test-Path -LiteralPath $WorkbookPath)) { return $result }

    try { $pkg = Open-ExcelPackage -Path $WorkbookPath }
    catch { Write-Warning "Could not open the existing workbook to preserve PowerLevel values: $($_.Exception.Message)"; return $result }

    try {
        $ws = $pkg.Workbook.Worksheets['PowerLevel']
        if ($null -ne $ws -and $null -ne $ws.Dimension) {
            $last = $ws.Dimension.End.Row
            for ($r = 1; $r -le $last; $r++) {
                $label = ([string]$ws.Cells[$r, 1].Text).Trim()
                # A multi-arg indexer used directly as a method-call argument fails to PARSE at all
                # (not just misparse) - $ws.Cells[$r, 2] must be its own statement first. See the
                # PowerShell EPPlus gotchas memory.
                $cellText = [string]$ws.Cells[$r, 2].Text
                $value = 0.0
                if (-not [double]::TryParse($cellText, [ref]$value)) { continue }

                # A weight name and a Range Power row can never collide (pw_* vs a bare integer), so
                # one pass over the whole tab sorts both out without needing to track which section a
                # row sits in - immune to the section being reordered or a note row moving around.
                if ($label -match '^pw_\w+$') { $result.Weights[$label] = $value }
                elseif ($label -match '^\d+$') { $result.RangeRows += , @([int]$label, $value) }
            }
        }
    }
    finally {
        Close-ExcelPackage $pkg -NoSave
    }

    return $result
}

$existingPowerLevel = Get-ExistingPowerLevelValues -WorkbookPath $WorkbookPath
Write-Host "  Preserved $($existingPowerLevel.Weights.Count) PowerLevel weight(s) and $($existingPowerLevel.RangeRows.Count) Range Power row(s) from the existing workbook"

# ---------------------------------------------------------------------------------------------------
# Refuse to discard unsynced sheet edits
#
# This export rebuilds every synced field from the prefabs, so on its own it is a one-way overwrite.
# Without this guard, editing Health in the sheet and then running the export to pick up a Notes edit
# made in the Inspector-adjacent CSV would silently throw the Health edit away - exactly the workflow
# the sync exists to protect. Mirrors Export-CardSheet.ps1's own guard.
# ---------------------------------------------------------------------------------------------------

if (-not $Force -and (Test-Path -LiteralPath $WorkbookPath)) {
    $baseline = Read-EnemyBaseline -ToolDir $scriptDir
    $mergeColumns = Get-EnemyMergeColumns

    $byGuid = @{}
    foreach ($b in $bodies) { if ($b.Guid) { $byGuid[$b.Guid] = $b } }

    $pending = @()

    try { $pkg = Open-ExcelPackage -Path $WorkbookPath }
    catch { $pkg = $null }

    if ($null -ne $pkg) {
        try {
            $skip = @('PowerLevel', 'Roster', 'Enums', 'README')
            foreach ($ws in @($pkg.Workbook.Worksheets)) {
                if ($skip -contains $ws.Name) { continue }
                $tab = Read-BodySheetTab -Worksheet $ws
                if ($null -eq $tab -or -not $tab.Guid) { continue }
                if (-not $byGuid.ContainsKey($tab.Guid)) { continue }

                $body = $byGuid[$tab.Guid]
                $assetRow = Get-EnemyMergeRow -Body $body
                $sheetRow = [ordered]@{
                    DisplayName = $tab.DisplayName; Health = $tab.Health; ActionsPerTurn = $tab.ActionsPerTurn
                    Brain = $tab.Brain; Targeting = $tab.Targeting; LootTable = $tab.LootTable
                    Deck = ConvertTo-DeckComparable -Names $tab.Deck; Role = $tab.Role
                }

                $hasBase = $baseline.Bodies.ContainsKey($tab.Guid)

                foreach ($col in $mergeColumns) {
                    $baseValue = $null
                    if ($hasBase -and $baseline.Bodies[$tab.Guid].ContainsKey($col)) { $baseValue = $baseline.Bodies[$tab.Guid][$col] }

                    $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $assetRow[$col] -Sheet $sheetRow[$col] -HasBaselineEntry $hasBase
                    if ($verdict -in @('takeSheet', 'conflict', 'noBaseline')) { $pending += "$($ws.Name) -> $col" }
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

        throw ("The sheet holds $($pending.Count) edit(s) that have not reached the prefabs. Rebuilding it " +
               "now would discard them:`n  " + ($shown -join "`n  ") + $more +
               "`n`nRun the sync instead - Tools > Sync Enemies With Sheet in Unity - which applies these " +
               "first and then refreshes the sheet. Pass -Force to overwrite them anyway.")
    }
}

# ---------------------------------------------------------------------------------------------------
# Write the workbook
# ---------------------------------------------------------------------------------------------------

. (Join-Path $scriptDir 'Write-EnemyWorkbook.ps1')

Write-EnemyWorkbook -WorkbookPath $WorkbookPath -Bodies $bodies -Totems $totems `
    -EnemyCardNames $enemyCardNames -LootNames $lootNames -TargetingNames $targetingNames `
    -PreservedWeights $existingPowerLevel.Weights -PreservedRangeRows $existingPowerLevel.RangeRows `
    -Preserved $preserved -RepoRoot $repoRoot

# ---------------------------------------------------------------------------------------------------
# CSV mirror - a flat projection, not a per-tab dump. Roster.csv carries every synced field plus the
# two human ones; Decks.csv is one row per (body, slot, card) so duplicates in the deck stay explicit.
# ---------------------------------------------------------------------------------------------------

if (-not $NoCsvMirror) {
    if (-not (Test-Path -LiteralPath $csvDir)) { New-Item -ItemType Directory -Path $csvDir -Force | Out-Null }

    $rosterRows = @()
    foreach ($b in $bodies) {
        $pv = $null
        if ($preserved.ContainsKey($b.Prefab)) { $pv = $preserved[$b.Prefab] }
        $brandon = if ($pv) { $pv.Brandon } else { '' }
        $estimated = if ($pv) { $pv.Estimated } else { '' }

        # EffectivePower is what Import-EnemySheet.ps1 will write to Character.powerLevel - computed
        # here too, purely so the CSV mirror (and anyone reading it without opening the workbook) can
        # see the same number. A body with neither a usable Brandon's nor a cached Estimated cannot be
        # scored yet, and EncounterRoller can never draw an unscored body - loud, not silent, because
        # that failure mode is otherwise invisible until a level looks emptier than it should.
        $effectivePower = ''
        $brandonValue = 0.0
        if ($brandon -ne '' -and [double]::TryParse($brandon, [ref]$brandonValue)) {
            $effectivePower = $brandonValue
        }
        else {
            $estimatedValue = 0.0
            if ($estimated -ne '' -and [double]::TryParse($estimated, [ref]$estimatedValue)) {
                $effectivePower = $estimatedValue
            }
            else {
                Write-Warning ("$($b.Prefab) has neither a Brandon's Power Level nor a cached Estimated " +
                    "Power Level (open Docs/EnemySheets.xlsx in Excel at least once to populate the " +
                    "latter) - EncounterRoller will never be able to draw it.")
            }
        }

        $rosterRows += [pscustomobject][ordered]@{
            Prefab        = $b.Prefab
            Boss          = if ($b.Boss) { 'Yes' } else { 'No' }
            Role          = $b.Role
            DisplayName   = $b.DisplayName
            Health        = $b.MaxHealth
            ActionsPerTurn = $b.ActionPoints
            Brain         = $b.Brain
            Targeting     = $b.Targeting
            LootTable     = $b.LootTable
            DeckSize      = @($b.Deck).Count
            Deck          = ConvertTo-DeckComparable -Names @($b.Deck | ForEach-Object { $_.Name })
            BrandonsPowerLevel = $brandon
            EffectivePower = $effectivePower
            Notes         = if ($pv) { $pv.Notes } else { '' }
            GUID          = $b.Guid
        }
    }
    $rosterRows | Export-Csv -LiteralPath (Join-Path $csvDir 'Roster.csv') -NoTypeInformation -Encoding utf8

    $deckRows = @()
    foreach ($b in $bodies) {
        $slot = 1
        foreach ($c in $b.Deck) {
            $deckRows += [pscustomobject][ordered]@{ Prefab = $b.Prefab; Slot = $slot; Card = $c.Name }
            $slot++
        }
    }
    $deckRows | Export-Csv -LiteralPath (Join-Path $csvDir 'Decks.csv') -NoTypeInformation -Encoding utf8

    $totemRows = @()
    foreach ($t in $totems) {
        $totemRows += [pscustomobject][ordered]@{
            Prefab = $t.Prefab; Range = $t.Range; Affects = $t.Affects
            Auras = (@($t.Auras) | ForEach-Object { "$($_.Status) $($_.Stacks)" }) -join '; '
            Reactions = (@($t.Reactions) | ForEach-Object { "$($_.Descriptor.Name)" }) -join '; '
            GUID = $t.Guid
        }
    }
    $totemRows | Export-Csv -LiteralPath (Join-Path $csvDir 'Totems.csv') -NoTypeInformation -Encoding utf8

    Write-Host 'CSV mirror written to Docs/EnemySheets/' -ForegroundColor DarkGray
}

if ($WriteBaseline) {
    Write-EnemyBaseline -ToolDir $scriptDir -Bodies $bodies
    Write-Host "Baseline recorded ($($bodies.Count) bodies)" -ForegroundColor DarkGray
}

Write-Host "`nWrote $WorkbookPath" -ForegroundColor Green
