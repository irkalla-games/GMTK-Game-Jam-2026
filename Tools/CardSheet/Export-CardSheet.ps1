<#
.SYNOPSIS
    Builds Docs/CardDesign.xlsx from the authored card assets.

.DESCRIPTION
    Read-only against the Unity project: it parses Assets/Data/**/*.asset and writes only into Docs/.
    Safe to run at any time, including with the Editor open.

    Anything you author in the workbook survives a re-run - the Ideas tabs, the Notes column on every
    class tab, and the Model weights are all read back out of the existing workbook and written again.
    Only the columns that mirror an asset are refreshed.

.PARAMETER WorkbookPath
    Defaults to Docs/CardDesign.xlsx at the repo root.

.PARAMETER NoCsvMirror
    Skip writing Docs/CardDesign/*.csv. The mirror exists so git diffs of the workbook are readable.

.PARAMETER WriteBaseline
    Record the state both sides now agree on into baseline.json. Passed by the sync, which has just
    made them agree. Running the export on its own does NOT move the baseline, because on its own it
    has not reconciled anything.

.PARAMETER Force
    Rebuild the sheet even though it holds edits that have not reached the assets, discarding them.
    Without this the export refuses rather than silently overwriting your work.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/CardSheet/Export-CardSheet.ps1
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
Import-Module (Join-Path $scriptDir 'CardSheet.Common.psm1') -Force -DisableNameChecking

if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    throw "The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser"
}
Import-Module ImportExcel -DisableNameChecking

if (-not $WorkbookPath) { $WorkbookPath = Join-Path $repoRoot 'Docs\CardDesign.xlsx' }
$csvDir = Join-Path $repoRoot 'Docs\CardDesign'

# Fail fast and in plain language if the workbook is open. The export rewrites the file wholesale, so
# an open copy in Excel otherwise surfaces as a raw .NET sharing violation from Remove-Item after all
# the parsing work has already been done - and, worse, Excel would still be holding stale content.
if (Test-Path -LiteralPath $WorkbookPath) {
    try {
        $probe = [IO.File]::Open($WorkbookPath, 'Open', 'ReadWrite', 'None')
        $probe.Close()
    }
    catch {
        throw "$WorkbookPath is open in another program (probably Excel). Close it and run this again - " +
              "the export rewrites the whole workbook, so it cannot share the file."
    }
}

# Column order comes from CardSheet.Common (Get-CardColumns), which is also what the merge compares -
# one list, so the sheet, the CSV mirror and the three-way merge can never disagree about what a
# column is called. Nothing reads a card row positionally: Import-Excel and Import-Csv both key by
# header text, which is what keeps the order a purely cosmetic choice.
$script:CardColumns = @(Get-CardColumns)

# The four extra columns that make an Ideas tab a backlog rather than a card list.
$script:IdeaColumns = $script:CardColumns + @(Get-IdeaBacklogColumns)

$script:ClassTabs = [ordered]@{
    Knight  = @('Knight')
    Mage    = @('Mage')
    Rogue   = @('Rogue')
    Neutral = @('Enemy', 'Generic')
}

# ---------------------------------------------------------------------------------------------------
# Reading back what the human authored
# ---------------------------------------------------------------------------------------------------

function Get-ExistingSheet {
    <#
        Reads one worksheet out of the current workbook, or the CSV mirror when no workbook exists yet.
        The CSV fallback is what lets the Ideas tabs be seeded from files in source control.
    #>
    param([string]$SheetName)

    if (Test-Path -LiteralPath $WorkbookPath) {
        try {
            $rows = @(Import-Excel -Path $WorkbookPath -WorksheetName $SheetName -ErrorAction Stop -WarningAction SilentlyContinue)
            if ($rows.Count -gt 0) { return $rows }
        }
        catch {
            Write-Verbose "No '$SheetName' sheet in the existing workbook."
        }
    }

    $csv = Join-Path $csvDir "$SheetName.csv"
    if (Test-Path -LiteralPath $csv) {
        return @(Import-Csv -LiteralPath $csv)
    }

    return @()
}

function Get-PreservedNotes {
    <# Key -> Notes, so hand-written design intent survives a refresh. #>
    param([string]$SheetName)

    $map = @{}
    foreach ($row in (Get-ExistingSheet -SheetName $SheetName)) {
        if (-not ($row.PSObject.Properties.Name -contains 'Key')) { continue }
        if (-not ($row.PSObject.Properties.Name -contains 'Notes')) { continue }
        $key = [string]$row.Key
        if ($key -and $row.Notes) { $map[$key] = [string]$row.Notes }
    }
    return $map
}

# ---------------------------------------------------------------------------------------------------
# Balance maths
#
# The facts (how much damage, how many tiles) are computed here and written as static values. The
# opinions (what a point of shield is worth) live on the Model tab as live formulas, so retuning a
# weight recalculates every card without re-running this script.
# ---------------------------------------------------------------------------------------------------

$script:FriendlyStatuses = @('Strength', 'DoubleShield', 'DoubleNextAttack', 'Shield', 'Block', 'Parry')
$script:HostileStatuses  = @('Poison', 'Weaken', 'Frozen', 'Rooted', 'Taunt')

# How many times a summoned totem is assumed to pay out over its life. Applied here rather than as a
# Model weight because it multiplies only the totem-derived part of a card, and splitting every output
# column into direct-vs-totem to keep that live would double the width of the Balance sheet for one
# number. Documented on the Model tab so it is not a hidden assumption.
$script:TotemActivations = 3

function Get-BalanceRecord {
    param($Card, [string]$SourceTab, $AssetIndex)

    $row = $Card.Row
    $entries = @($Card.Entries)

    $damage = 0; $heal = 0; $shield = 0; $blockCharges = 0; $parry = 0
    $poisonStacks = 0; $strength = 0; $weaken = 0; $draw = 0; $move = 0; $combo = 0
    $enemyTiles = 1; $allyTiles = 1
    $control = @()
    $summons = @()
    $hasRider = $false

    foreach ($e in $entries) {
        $d = $e.Descriptor
        $tiles = [int]$e.Tiles
        $hostile = $true

        switch -Regex ($d.Kind) {
            '^Damage$'  { $damage += $d.Amount }
            '^Heal$'    { $heal   += $d.Amount; $hostile = $false }
            '^Shield$'  { $shield += $d.Amount; $hostile = $false }
            '^Block$'   { $blockCharges += $d.Amount; $hostile = $false }
            '^Parry$'   { $parry  += $d.Amount; $hostile = $false }
            '^Draw$'    { $draw   += $d.Amount; $hostile = $false }
            '^Move$'    { $move   += 1;         $hostile = $false }
            '^Taunt$'   { $control += "Taunt";  $hasRider = $true }
            '^Animate$' { $hostile = $false }
            '^Swap$'    { $control += "Swap";   $hasRider = $true; $hostile = $false }
            '^Tile:' {
                $tile = $d.Kind.Substring(5)
                if ($tile -eq 'WallOfFlames') {
                    # Descriptor already multiplied magnitude by the tile's lifetime, so this is the
                    # full unblockable payout of standing in it.
                    $damage += $d.Amount
                }
                else {
                    $control += $tile
                    $hasRider = $true
                    $hostile = $false
                }
            }
            '^Summon$' {
                $hostile = $false
                $hasRider = $true

                # A totem is not a black box: read what it projects and fold that in, so a card that
                # grants Shield 3 on every nearby ally attack is worth more than "summons a thing".
                $totem = Get-TotemProjection -SummonEffectAsset $e.EffectAsset -AssetIndex $AssetIndex
                if ($null -eq $totem) {
                    $summons += 'Summon'
                    $control += 'Summon'
                }
                else {
                    # SummonEffect.lifetimeTurns is authoritative when set; 0 means it never expires,
                    # in which case fall back to the assumed horizon.
                    $ticks = $script:TotemActivations
                    if ($null -ne $e.EffectAsset) {
                        $life = ConvertTo-IntOrDefault (Get-NodeField $e.EffectAsset.Node 'lifetimeTurns')
                        if ($life -gt 0) { $ticks = $life }
                    }
                    $parts = @()
                    foreach ($a in @($totem.Auras)) {
                        $parts += "$($a.Status) $($a.Stacks) aura"
                        switch ($a.Status) {
                            'Strength' { $strength += $a.Stacks * $ticks }
                            'Weaken'   { $weaken   += $a.Stacks * $ticks }
                            'Poison'   { $poisonStacks += $a.Stacks }
                            'Shield'   { $shield   += $a.Stacks * $ticks }
                            default    { $control += "$($a.Status) aura"; }
                        }
                    }
                    foreach ($rx in @($totem.Reactions)) {
                        $rd = $rx.Descriptor
                        $parts += "$($rd.Name) on trigger"
                        switch -Regex ($rd.Kind) {
                            '^Damage$' { $damage += $rd.Amount * $ticks }
                            '^Heal$'   { $heal   += $rd.Amount * $ticks }
                            '^Shield$' { $shield += $rd.Amount * $ticks }
                            '^Draw$'   { $draw   += $rd.Amount * $ticks }
                            '^Block$'  { $blockCharges += $rd.Amount * $ticks }
                            default    { $control += $rd.Kind }
                        }
                    }
                    $summons += "$($totem.Prefab) [$($totem.Range)]: " + ($parts -join ', ')
                }
            }
            '^Status:' {
                $status = $d.Kind.Substring(7)
                switch ($status) {
                    'Poison'   { $poisonStacks += $d.Amount }
                    'Strength' { $strength += $d.Amount; $hostile = $false }
                    'Weaken'   { $weaken   += $d.Amount }
                    'Shield'   { $shield   += $d.Amount; $hostile = $false }
                    'DoubleNextAttack' { $combo += $d.Amount; $hostile = $false }
                    'DoubleShield'     { $combo += $d.Amount; $hostile = $false }
                    default {
                        $control += $status
                        if ($script:FriendlyStatuses -contains $status) { $hostile = $false }
                        if (-not $d.Scoreable) { $hasRider = $true }
                    }
                }
            }
            default { $hasRider = $true }
        }

        if ($tiles -gt 1) {
            if ($hostile) {
                if ($tiles -gt $enemyTiles) { $enemyTiles = $tiles }
            }
            else {
                if ($tiles -gt $allyTiles) { $allyTiles = $tiles }
            }
        }
    }

    # PoisonStatus deals `stacks` damage at turn end and then decays by one, so the lifetime total of
    # n stacks is the triangular number, not n. That is a fact about the rule, not a tunable.
    $poisonTotal = [int](($poisonStacks * ($poisonStacks + 1)) / 2)

    # BlockStatus.AmountPerHit is a const 5; a charge prevents up to that much.
    $blockTotal = $blockCharges * 5

    # Three states, not two. A card with no numeric output at all (Freeze, Entangle, a bare summon) has
    # no place on the curve. But Ice Shard deals 5 damage AND freezes: dropping it entirely would hide a
    # real attack, while scoring it as if the freeze were free would read as underpowered. Partial is
    # scored and plotted, but not flagged OVER/UNDER - the number is a floor, not a verdict.
    $numeric = ($damage + $heal + $shield + $blockTotal + $parry + $poisonTotal +
                $strength + $weaken + $draw + $move + $combo) -gt 0

    $quantifiable = 'No'
    if ($numeric) {
        if ($hasRider) { $quantifiable = 'Partial' } else { $quantifiable = 'Yes' }
    }

    return [ordered]@{
        Key            = $row.Key
        'Card Name'    = $row.'Card Name'
        Class          = $row.Class
        Cost           = $row.Cost
        Rarity         = $row.Rarity
        Source         = $SourceTab
        'Enemy Tiles'  = $enemyTiles
        'Ally Tiles'   = $allyTiles
        Damage         = $damage
        Heal           = $heal
        Shield         = $shield
        'Block Total'  = $blockTotal
        Parry          = $parry
        'Poison Total' = $poisonTotal
        Strength       = $strength
        Weaken         = $weaken
        Draw           = $draw
        Move           = $move
        Combo          = $combo
        Summons        = ($summons -join ' | ')
        Control        = ($control -join ', ')
        Quantifiable   = $quantifiable
        Power          = $null
        'Power/Energy' = $null
        Expected       = $null
        Delta          = $null
        Flag           = $null
        Issues         = (Get-CardIssues -Card $Card)
        'Pw Common'    = $null
        'Pw Uncommon'  = $null
        'Pw Rare'      = $null
        'Pw Legendary' = $null
        'Eff Knight'   = $null
        'Eff Mage'     = $null
        'Eff Rogue'    = $null
        'Exp Plot'     = $null
        'Pw Idea'      = $null
    }
}

# ---------------------------------------------------------------------------------------------------
# Gather
# ---------------------------------------------------------------------------------------------------

Write-Host "Reading assets..." -ForegroundColor Cyan

$scriptGuids = Get-ScriptGuidIndex -RepoRoot $repoRoot
$assetIndex  = Build-AssetIndex -RepoRoot $repoRoot -RelativeFolders @('Assets\Data', 'Assets\Prefabs') -ScriptGuidIndex $scriptGuids

$allCards = @()
foreach ($asset in ($assetIndex.All | Where-Object { $_.Type -eq 'CardData' })) {
    $allCards += (Get-CardRecord -Asset $asset -AssetIndex $assetIndex -RepoRoot $repoRoot)
}
Write-Host "  $($allCards.Count) cards, $($assetIndex.All.Count) assets total"

# ---------------------------------------------------------------------------------------------------
# Refuse to discard unsynced sheet edits
#
# This export rebuilds every authoring column from the assets, so on its own it is a one-way overwrite.
# Before this guard existed, rewording cards in the sheet and then running the export to pick up a card
# authored in the Inspector silently threw the rewording away - the exact workflow the sync is for.
# ---------------------------------------------------------------------------------------------------

if (-not $Force -and (Test-Path -LiteralPath $WorkbookPath)) {
    $baseline = Read-Baseline -ToolDir $scriptDir
    $byGuid = @{}
    foreach ($card in $allCards) {
        $g = [string]$card.Row['GUID']
        if ($g) { $byGuid[$g] = $card }
    }

    $pending = @()

    foreach ($tab in $script:ClassTabs.Keys) {
        foreach ($row in (Get-ExistingSheet -SheetName $tab)) {
            $guid = ''
            if ($row.PSObject.Properties.Name -contains 'GUID') { $guid = ([string]$row.GUID).Trim() }

            $key = ''
            if ($row.PSObject.Properties.Name -contains 'Key') { $key = ([string]$row.Key).Trim() }

            # A row with no GUID is a card the sheet is asking to create - unsynced by definition.
            if (-not $guid) {
                if ($key) { $pending += "$key (new row, not yet created)" }
                continue
            }

            if (-not $byGuid.ContainsKey($guid)) { continue }
            $card = $byGuid[$guid]
            $hasBase = $baseline.Cards.ContainsKey($guid)

            foreach ($col in (Get-MergeColumns)) {
                $sheetValue = ''
                if ($row.PSObject.Properties.Name -contains $col) { $sheetValue = $row.$col }

                $baseValue = $null
                if ($hasBase -and $baseline.Cards[$guid].ContainsKey($col)) { $baseValue = $baseline.Cards[$guid][$col] }

                $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $card.Row[$col] -Sheet $sheetValue `
                                            -HasBaselineEntry $hasBase

                if ($verdict -in @('takeSheet', 'conflict', 'noBaseline')) {
                    $pending += "$key -> $col"
                }
            }
        }
    }

    if ($pending.Count -gt 0) {
        $shown = @($pending | Select-Object -First 12)
        $more = if ($pending.Count -gt 12) { "`n  ... and $($pending.Count - 12) more" } else { '' }

        throw ("The sheet holds $($pending.Count) edit(s) that have not reached the assets. Rebuilding it " +
               "now would discard them:`n  " + ($shown -join "`n  ") + $more +
               "`n`nRun the sync instead - Tools > Sync With Sheet in Unity - which applies these " +
               "first and then refreshes the sheet. Pass -Force to overwrite them anyway.")
    }
}

# ---------------------------------------------------------------------------------------------------
# Build sheet data
# ---------------------------------------------------------------------------------------------------

$sheets      = [ordered]@{}
$balanceRows = @()

foreach ($tab in $script:ClassTabs.Keys) {
    $dirs  = $script:ClassTabs[$tab]
    $notes = Get-PreservedNotes -SheetName $tab

    $rows = @()
    foreach ($card in ($allCards | Where-Object { $dirs -contains $_.ClassDir } | Sort-Object { $_.Row.Folder }, { $_.Row.Cost }, { $_.Row.Key })) {
        $r = [ordered]@{}
        foreach ($col in $script:CardColumns) { $r[$col] = $card.Row[$col] }
        if ($notes.ContainsKey($card.Row.Key)) { $r['Notes'] = $notes[$card.Row.Key] }
        $rows += [pscustomobject]$r

        $balanceRows += [pscustomobject](Get-BalanceRecord -Card $card -SourceTab $tab -AssetIndex $assetIndex)
    }

    $sheets[$tab] = $rows
    Write-Host "  $tab : $($rows.Count) cards"
}

# An index of effect assets by name, so an Ideas row naming "Damage 7" can be scored without an asset
# behind the card itself.
$effectsByName = @{}
foreach ($asset in $assetIndex.All) {
    if ($asset.Type -like '*Effect' -and $asset.Type -ne 'EffectPattern') { $effectsByName[$asset.Name] = $asset }
}

function New-CardRecordFromRow {
    <#
        .SYNOPSIS
            Builds the same shape Get-CardRecord returns, but from a spreadsheet row rather than an
            asset - so a proposed card can be plotted against the curve before it exists.

        .DESCRIPTION
            Returns $null when no effect column resolves to a real effect asset. That is the honest
            answer for a backlog row whose mechanic has not been built: there is no number to score,
            so it stays off the chart rather than plotting as zero.
    #>
    param($Row, $AssetIndex, $EffectsByName, [string]$ClassDir)

    $entries = @()
    for ($i = 1; $i -le 3; $i++) {
        $name = ''
        if ($Row.PSObject.Properties.Name -contains "Effect $i") { $name = ([string]$Row."Effect $i").Trim() }
        if ($name -eq '') { continue }

        # Prefer the real asset. Fall back to the naming convention so a proposal asking for a
        # magnitude nobody has authored yet ("Damage 7") still lands on the curve.
        $effect = $null
        $descriptor = $null
        if ($EffectsByName.ContainsKey($name)) {
            $effect = $EffectsByName[$name]
            $descriptor = Get-EffectDescriptor -Asset $effect
        }
        else {
            $descriptor = Get-EffectDescriptorFromName -Name $name
            if ($null -eq $descriptor) { continue }
        }

        $areaText = ''
        if ($Row.PSObject.Properties.Name -contains "Area $i") { $areaText = [string]$Row."Area $i" }
        $aim = 'Tile'
        if ($Row.PSObject.Properties.Name -contains "Aim $i" -and $Row."Aim $i") { $aim = [string]$Row."Aim $i" }

        $entries += [pscustomobject]@{
            EffectName  = $name
            Aim         = $aim
            Area        = $areaText
            Tiles       = Measure-AreaTilesFromText -Text $areaText -AssetIndex $AssetIndex
            Descriptor  = $descriptor
            EffectAsset = $effect
            IsNullRef   = $false
        }
    }

    if ($entries.Count -eq 0) { return $null }

    $record = [ordered]@{}
    foreach ($col in $script:CardColumns) {
        $value = ''
        if ($Row.PSObject.Properties.Name -contains $col) { $value = $Row.$col }
        $record[$col] = $value
    }
    $record['Cost'] = ConvertTo-IntOrDefault $record['Cost']

    return [pscustomobject]@{
        Row       = $record
        Entries   = $entries
        ClassDir  = $ClassDir
        AssetPath = ''
        Asset     = $null
    }
}

# Ideas tabs are pure passthrough for the authoring columns - hand-authored, never derived from assets.
# Their rows are still scored onto Balance where the effects resolve, so a proposal can be checked
# against the same curve as a shipped card.
foreach ($tab in @('Knight Ideas', 'Mage Ideas', 'Rogue Ideas')) {
    $existing = Get-ExistingSheet -SheetName $tab
    $classDir = ($tab -split ' ')[0]

    $rows = @()
    $scored = 0
    foreach ($row in $existing) {
        $r = [ordered]@{}
        foreach ($col in $script:IdeaColumns) {
            $value = ''
            if ($row.PSObject.Properties.Name -contains $col) { $value = $row.$col }
            $r[$col] = $value
        }
        $rows += [pscustomobject]$r

        $synth = New-CardRecordFromRow -Row $row -AssetIndex $assetIndex -EffectsByName $effectsByName -ClassDir $classDir
        if ($null -ne $synth) {
            $balanceRows += [pscustomobject](Get-BalanceRecord -Card $synth -SourceTab $tab -AssetIndex $assetIndex)
            $scored++
        }
    }

    $sheets[$tab] = $rows
    Write-Host "  $tab : $($rows.Count) ideas ($scored scored onto Balance)"
}

# Effects catalogue, with a usage count so dead effect assets are visible.
$effectTypes = @('DamageEffect', 'HealEffect', 'ShieldEffect', 'BlockEffect', 'ParryEffect',
                 'DrawEffect', 'MoveEffect', 'SummonEffect', 'TauntEffect', 'AnimateEffect',
                 'ApplyStatusEffect')

$usage = @{}
foreach ($card in $allCards) {
    foreach ($e in @($card.Entries)) {
        if ($e.EffectName) {
            if (-not $usage.ContainsKey($e.EffectName)) { $usage[$e.EffectName] = 0 }
            $usage[$e.EffectName] = $usage[$e.EffectName] + 1
        }
    }
}

$effectRows = @()
foreach ($asset in ($assetIndex.All | Where-Object { $effectTypes -contains $_.Type } | Sort-Object Type, Name)) {
    $d = Get-EffectDescriptor -Asset $asset
    $used = 0
    if ($usage.ContainsKey($asset.Name)) { $used = $usage[$asset.Name] }

    $rel = $asset.Path
    $rel = $rel.Replace([string]$repoRoot, '').TrimStart('\') -replace '\\', '/'

    $effectRows += [pscustomobject][ordered]@{
        Name        = $asset.Name
        Class       = $asset.Type
        Kind        = $d.Kind
        Amount      = $d.Amount
        Detail      = $d.Detail
        Scoreable   = if ($d.Scoreable) { 'Yes' } else { 'No' }
        'Used By'   = $used
        Path        = $rel
        GUID        = $asset.Guid
    }
}
$sheets['Effects'] = $effectRows

# Patterns get their own small block on the Effects tab is overkill; fold them in as rows instead.
foreach ($asset in ($assetIndex.All | Where-Object { $_.Type -eq 'EffectPattern' } | Sort-Object Name)) {
    $cells = Measure-PackedBoolCount ([string](Get-NodeField $asset.Node 'cardinalCells'))
    $rel = $asset.Path.Replace([string]$repoRoot, '').TrimStart('\') -replace '\\', '/'
    $sheets['Effects'] += [pscustomobject][ordered]@{
        Name      = $asset.Name
        Class     = 'EffectPattern'
        Kind      = 'Pattern'
        Amount    = $cells
        Detail    = "$(Get-NodeField $asset.Node 'width')x$(Get-NodeField $asset.Node 'height') grid, $cells painted"
        Scoreable = 'n/a'
        'Used By' = 0
        Path      = $rel
        GUID      = $asset.Guid
    }
}

# Decks: one row per (deck, card) with a count, which sorts and pivots better than a card-per-column grid.
$deckRows = @()
foreach ($asset in ($assetIndex.All | Where-Object { $_.Type -eq 'DeckData' } | Sort-Object Name)) {
    $cardsNode = Get-NodeField $asset.Node 'cards'
    $counts = [ordered]@{}

    if ($cardsNode -is [System.Collections.IEnumerable] -and -not ($cardsNode -is [string])) {
        foreach ($ref in $cardsNode) {
            $name = Resolve-AssetName -Reference $ref -AssetIndex $assetIndex
            if (-not $name) { $name = '<missing>' }
            if (-not $counts.Contains($name)) { $counts[$name] = 0 }
            $counts[$name] = $counts[$name] + 1
        }
    }

    foreach ($name in $counts.Keys) {
        $deckRows += [pscustomobject][ordered]@{
            Deck      = $asset.Name
            'For'     = ConvertTo-ClassName (Get-NodeField $asset.Node 'forClass')
            Card      = $name
            Count     = $counts[$name]
            Locked    = if ((ConvertTo-IntOrDefault (Get-NodeField $asset.Node 'locked')) -ne 0) { 'Yes' } else { 'No' }
        }
    }
}
$sheets['Decks'] = $deckRows

# Tooltip wording, editable here and syncable back - the same reason card descriptions live in the
# sheet rather than only in the Inspector.
$glossaryRows = @(Read-GlossaryRows -RepoRoot $repoRoot -ScriptGuidIndex $scriptGuids)
$sheets['Glossary'] = $glossaryRows

$sheets['Balance'] = $balanceRows

Write-Host "  Effects : $($effectRows.Count)  Decks : $($deckRows.Count)  Glossary : $($glossaryRows.Count)  Balance : $($balanceRows.Count)"

. (Join-Path $scriptDir 'Write-CardWorkbook.ps1')

Write-CardWorkbook -WorkbookPath $WorkbookPath -Sheets $sheets -RepoRoot $repoRoot

if (-not $NoCsvMirror) {
    if (-not (Test-Path -LiteralPath $csvDir)) { New-Item -ItemType Directory -Path $csvDir -Force | Out-Null }
    foreach ($name in $sheets.Keys) {
        $path = Join-Path $csvDir "$name.csv"
        if (@($sheets[$name]).Count -eq 0) {
            # Headers only, so the file still documents the shape and can seed the tab later.
            $cols = if ($name -like '*Ideas') { $script:IdeaColumns } else { @('Key') }
            ($cols -join ',') | Set-Content -LiteralPath $path -Encoding utf8
        }
        else {
            $sheets[$name] | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding utf8
        }
    }
    Write-Host "CSV mirror written to Docs/CardDesign/" -ForegroundColor DarkGray
}

if ($WriteBaseline) {
    Write-Baseline -ToolDir $scriptDir -CardRecords $allCards -GlossaryRows $glossaryRows
    Write-Host "Baseline recorded ($($allCards.Count) cards, $($glossaryRows.Count) glossary entries)" -ForegroundColor DarkGray
}

Write-Host "`nWrote $WorkbookPath" -ForegroundColor Green
