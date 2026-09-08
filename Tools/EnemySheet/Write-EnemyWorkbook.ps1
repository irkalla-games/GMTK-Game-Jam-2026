<#
.SYNOPSIS
    Turns the gathered enemy/body data into the formatted workbook. Dot-sourced by Export-EnemySheet.ps1.

.DESCRIPTION
    Same split as Tools/CardSheet/Write-CardWorkbook.ps1: facts (how much damage, how many tiles) arrive
    already computed from the assets; every number that reflects an OPINION of how much that fact is
    worth is written as an Excel formula against a named pw_* cell on the PowerLevel tab. Retuning a
    weight there recalculates every body tab and the Roster comparison with no need to re-run this
    script.

    One tab per body (Character prefab) and one per Totem, all following the SAME fixed row layout
    ($script:BodyRow / $script:TotemRow below) so the Roster tab can reference any body's cells by a
    plain 'TabName'!B15-style address instead of re-deriving row numbers. A summon card's payoff crosses
    tabs the same lightweight way: every body and totem tab registers a workbook-wide named cell
    `pow_<PrefabName>` at its own Estimated Power Level / Totem Power cell, and a card that summons that
    prefab just writes `=pow_<PrefabName>` (scaled by lifetime). There is no cycle risk in the current
    data - totems summon nothing, and every summoned character's own deck is plain attacks - so this
    skips the graph-walk a fully general version would need; a genuine cycle would show up as Excel's
    own circular-reference warning rather than something this script has to detect.
#>

# ---------------------------------------------------------------------------------------------------
# Fixed row layout, shared between the tab writer and the Roster reader.
# ---------------------------------------------------------------------------------------------------

$script:BodyRow = [ordered]@{
    EnemyName = 1; Boss = 2; Guid = 3; Sync = 4
    DisplayName = 7; Health = 8; Actions = 9; Brain = 10; Targeting = 11; Loot = 12; Role = 13
    Brandon = 15; Estimated = 16; Delta = 17
    AvgCardPower = 19; AvgDamage = 20; AvgStatus = 21; AvgSummon = 22
    DeckHeader = 24; ActionsFromCard = 25; Counted = 26
    FactsHeader = 28
    Cost = 29; RangeMax = 30; TilesHit = 31; Damage = 32; RangePower = 33; PoisonTotal = 34; Heal = 35
    Shield = 36; BlockTotal = 37; Parry = 38; Dodge = 39; Strength = 40; Weaken = 41; Vulnerable = 42
    Frozen = 43; Rooted = 44; Taunt = 45; Stealth = 46; PoisonBlade = 47; DoubleNextAttack = 48
    DoubleShield = 49; Draw = 50; MoveTiles = 51; SummonPower = 52; Cooldown = 53; CardPower = 54
    StatusPower = 55
    Notes = 57
}

$script:TotemRow = [ordered]@{
    Name = 1; Guid = 2; Range = 3; Affects = 4
    Power = 6
    AuraHeader = 8
    Notes = 100   # set precisely once the reaction table's real extent is known
}

function Get-ExcelColumnName {
    param([int]$Index)   # 1-based

    $name = ''
    $n = $Index
    while ($n -gt 0) {
        $rem = ($n - 1) % 26
        $name = [char](65 + $rem) + $name
        $n = [int](($n - $rem - 1) / 26)
    }
    return $name
}

function Add-NamedCell {
    param($Package, $Worksheet, [string]$Name, [string]$Address)

    if ($Package.Workbook.Names.ContainsKey($Name)) { $Package.Workbook.Names.Remove($Name) }
    [void]$Package.Workbook.Names.Add($Name, $Worksheet.Cells[$Address])
}

function Format-ActionSummary {
    <# The pulled, human-readable "what this card does" text next to the deck strip. #>
    param($Facts)

    $parts = @()
    if ($Facts.Damage -gt 0) { $parts += "$($Facts.Damage) damage" }
    if ($Facts.PoisonStacks -gt 0) { $parts += "Poison $($Facts.PoisonStacks)" }
    if ($Facts.Heal -gt 0) { $parts += "Heal $($Facts.Heal)" }
    if ($Facts.Shield -gt 0) { $parts += "Shield $($Facts.Shield)" }
    if ($Facts.BlockCharges -gt 0) { $parts += "Block x$($Facts.BlockCharges)" }
    if ($Facts.Parry -gt 0) { $parts += "Parry x$($Facts.Parry)" }
    if ($Facts.Dodge -gt 0) { $parts += "Dodge x$($Facts.Dodge)" }
    if ($Facts.Strength -gt 0) { $parts += "Strength $($Facts.Strength)" }
    if ($Facts.Weaken -gt 0) { $parts += "Weaken $($Facts.Weaken)" }
    if ($Facts.Vulnerable -gt 0) { $parts += "Vulnerable $($Facts.Vulnerable)" }
    if ($Facts.Frozen -gt 0) { $parts += "Freeze $($Facts.Frozen)t" }
    if ($Facts.Rooted -gt 0) { $parts += "Root $($Facts.Rooted)t" }
    if ($Facts.Taunt -gt 0) { $parts += "Taunt $($Facts.Taunt)t" }
    if ($Facts.Stealth -gt 0) { $parts += "Stealth $($Facts.Stealth)t" }
    if ($Facts.PoisonBlade -gt 0) { $parts += "Poison Blade $($Facts.PoisonBlade)" }
    if ($Facts.DoubleNextAttack -gt 0) { $parts += 'Double Next Attack' }
    if ($Facts.DoubleShield -gt 0) { $parts += 'Double Shield' }
    if ($Facts.Draw -gt 0) { $parts += "Draw $($Facts.Draw)" }
    if ($Facts.MoveTiles -gt 0) { $parts += "Move $($Facts.MoveTiles)" }
    if ($Facts.IsSummon) { $parts += 'Summon' }

    if ($parts.Count -eq 0) { return '-' }
    return ($parts -join ', ')
}

function Write-EnemyWorkbook {
    param(
        [string]$WorkbookPath,
        $Bodies,          # ordered array from Export-EnemySheet.ps1
        $Totems,
        $EnemyCardNames,
        $LootNames,
        $TargetingNames,
        $PreservedWeights,    # hashtable: pw_* name -> current Value, read back from the old workbook
        $PreservedRangeRows,  # array of @(Range, Power) pairs, same idea
        $Preserved,       # hashtable: GUID -> @{ Brandon = ...; Notes = ... } (GUID, not Prefab, so a
                          # renamed body's old tab is still found under its new name - see
                          # Get-ExistingBodyPreserved in Export-EnemySheet.ps1)
        [string]$RepoRoot,
        $Domain,              # Get-RosterDomain result - every label and path that differs per workbook
        $SummonedReferences,  # array of @{ Prefab; Power; SummonedBy } - bodies owned by the OTHER workbook
        [switch]$PowerLevelLocked
    )

    $dir = Split-Path -Parent $WorkbookPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    if (Test-Path -LiteralPath $WorkbookPath) { Remove-Item -LiteralPath $WorkbookPath -Force }

    Write-Host 'Writing workbook...' -ForegroundColor Cyan

    $pkg = Open-ExcelPackage -Path $WorkbookPath -Create

    $summoned = @($SummonedReferences)

    # A summoned body's pow_* cell lives on the Summoned tab rather than a tab of its own, but it is the
    # same name in the same workbook scope - so Add-BodySheet's =pow_<Target> branch cannot tell the
    # difference, and needs no special case.
    $allNames = @($Bodies | ForEach-Object { $_.Prefab }) + @($Totems | ForEach-Object { $_.Prefab }) +
                @($summoned | ForEach-Object { $_.Prefab })

    Add-PowerLevelSheet -Package $pkg -PreservedWeights $PreservedWeights -PreservedRangeRows $PreservedRangeRows `
        -Locked:$PowerLevelLocked -LockedSource $Domain.PowerLevelSourceRel

    # Named ranges (list_Brain, list_Targeting, list_EnemyCard, pow_*, ...) have to exist before any body
    # tab references them in a dropdown or a formula - same ordering Write-CardWorkbook.ps1 follows.
    Add-EnumsSheet -Package $pkg -EnemyCardNames $EnemyCardNames -LootNames $LootNames -TargetingNames $TargetingNames -Bodies $Bodies -Totems $Totems

    if ($summoned.Count -gt 0) {
        Add-SummonedSheet -Package $pkg -SummonedReferences $summoned -Domain $Domain
    }

    foreach ($body in $Bodies) {
        Add-BodySheet -Package $pkg -Body $body -Preserved $Preserved -AllPowerNames $allNames -Domain $Domain
    }

    foreach ($totem in $Totems) {
        Add-TotemSheet -Package $pkg -Totem $totem -Preserved $Preserved
    }

    Add-RosterSheet -Package $pkg -Bodies $Bodies -Preserved $Preserved
    Add-ReadmeSheet -Package $pkg -Domain $Domain -HasSummoned ($summoned.Count -gt 0) -PowerLevelLocked:$PowerLevelLocked

    Set-SheetOrder -Package $pkg -Bodies $Bodies -Totems $Totems -HasSummoned ($summoned.Count -gt 0)

    Close-ExcelPackage $pkg
}

# ---------------------------------------------------------------------------------------------------
# PowerLevel - the tuning surface
# ---------------------------------------------------------------------------------------------------

function Add-PowerLevelSheet {
    <#
        Values are read back from whatever is already in the workbook (PreservedWeights/
        PreservedRangeRows, gathered by Export-EnemySheet.ps1's Get-ExistingPowerLevelValues) the same
        way a body tab's Brandon's Power Level and Notes survive a re-export. Get-PowerWeightDefaults
        still owns the row order, the set of weights that exist and their "why" notes - only the Value
        column is ever overridden by what is already on disk, and only for a name that already has an
        entry there, so a weight added to the code later still appears with its coded starting value.

        -Locked flips where "already on disk" means: the caller passes the OTHER workbook's values, and
        this tab becomes a read-only mirror of them. Boss power is only meaningful on the same scale as
        enemy power, so Docs/BossDesign.xlsx does not get its own tuning surface - editing this tab
        there does nothing and is snapped back on the next export, which the banner says out loud
        because a silently-reverted edit is the worst possible version of that.
    #>
    param(
        $Package, $PreservedWeights, $PreservedRangeRows,
        [switch]$Locked, [string]$LockedSource
    )

    $ws = $Package.Workbook.Worksheets.Add('PowerLevel')

    # Row 1 is the banner when locked, so everything below shifts by one. Nothing reads this tab by row
    # number - the weights are reached through their pw_* named cells - so the shift is free.
    $top = 1
    if ($Locked) {
        $ws.Cells[1, 1].Value = "READ-ONLY - mirrored from $LockedSource on every export. Edit the weights THERE; " +
                                'anything typed here is discarded on the next sync.'
        $ws.Cells[1, 1].Style.Font.Bold = $true
        $ws.Cells[1, 1].Style.Font.Color.SetColor([System.Drawing.Color]::FromArgb(150, 40, 40))
        $top = 2
    }

    $ws.Cells[$top, 1].Value = 'Term'
    $ws.Cells[$top, 2].Value = 'Value'
    $ws.Cells[$top, 3].Value = 'What this counts'
    $ws.Cells[$top, 1, $top, 3].Style.Font.Bold = $true

    $weights = @(Get-PowerWeightDefaults)
    for ($i = 0; $i -lt $weights.Count; $i++) {
        $r = $top + 1 + $i
        $wname = $weights[$i][0]
        $wvalue = [double]$weights[$i][1]
        if ($null -ne $PreservedWeights -and $PreservedWeights.ContainsKey($wname)) { $wvalue = [double]$PreservedWeights[$wname] }

        $ws.Cells[$r, 1].Value = $wname
        $ws.Cells[$r, 2].Value = $wvalue
        $ws.Cells[$r, 3].Value = $weights[$i][2]
        Add-NamedCell -Package $Package -Worksheet $ws -Name $wname -Address "B$r"
    }

    $ws.Cells["B$($top + 1):B200"].Style.Numberformat.Format = '0.00'
    $ws.Column(1).Width = 20
    $ws.Column(2).Width = 11
    $ws.Column(3).Width = 90
    $ws.Column(3).Style.WrapText = $true
    $ws.View.FreezePanes($top + 1, 1)

    $note = $top + $weights.Count + 2
    $ws.Cells[$note, 1].Value = if ($Locked) {
        "Read-only. These weights are whatever $LockedSource holds - retune them there and re-sync, so both workbooks keep scoring on one scale."
    }
    else {
        'Edit the Value column - every body tab and the Roster tab recalculate. Nothing here is baked into the export script.'
    }
    $ws.Cells[$note, 1].Style.Font.Italic = $true

    # Range Power - looked up per exact value (VLOOKUP approximate match) rather than a per-tile rate,
    # since reach is not linear: melee-to-just-ranged matters more than Range 4-to-5. See the Card Power
    # formula below and Get-RangePowerDefaults' own comment for the approximate-match rule.
    $rangeHeaderRow = $note + 2
    $ws.Cells[($rangeHeaderRow - 1), 1].Value = 'Range Power'
    $ws.Cells[($rangeHeaderRow - 1), 1].Style.Font.Bold = $true
    $ws.Cells[$rangeHeaderRow, 1].Value = 'Range'
    $ws.Cells[$rangeHeaderRow, 2].Value = 'Power'
    $ws.Cells["A$rangeHeaderRow`:B$rangeHeaderRow"].Style.Font.Bold = $true

    # Unlike the weights above (a fixed set of names the code owns), the Range Power table is entirely
    # user-extensible - the whole point is being able to add a Range 7 row by hand. So the WHOLE
    # existing table survives a re-export verbatim; Get-RangePowerDefaults only seeds it on the very
    # first export, when there is nothing yet to read back.
    $rangeRows = if (@($PreservedRangeRows).Count -gt 0) { @($PreservedRangeRows | Sort-Object { $_[0] }) } else { @(Get-RangePowerDefaults) }
    for ($i = 0; $i -lt $rangeRows.Count; $i++) {
        $r = $rangeHeaderRow + 1 + $i
        $ws.Cells[$r, 1].Value = [int]$rangeRows[$i][0]
        $ws.Cells[$r, 2].Value = [double]$rangeRows[$i][1]
        $ws.Cells[$r, 2].Style.Numberformat.Format = '0.00'
    }
    $rangeLastRow = $rangeHeaderRow + $rangeRows.Count
    Add-NamedCell -Package $Package -Worksheet $ws -Name 'range_Power' -Address "A$($rangeHeaderRow + 1):B$rangeLastRow"

    $rangeNote = $rangeLastRow + 1
    $ws.Cells[$rangeNote, 1].Value = 'Looked up by nearest Range at or below a card''s own Range Max - add a row for a new value; anything past the last row scores the same as it.'
    $ws.Cells[$rangeNote, 1].Style.Font.Italic = $true
}

# ---------------------------------------------------------------------------------------------------
# One tab per body
# ---------------------------------------------------------------------------------------------------

function Add-BodySheet {
    param($Package, $Body, [hashtable]$Preserved, [string[]]$AllPowerNames, $Domain)

    $row = $script:BodyRow
    $ws = $Package.Workbook.Worksheets.Add($Body.Prefab)

    $ws.Cells[$row.EnemyName, 1].Value = if ($Domain) { $Domain.BodyLabel } else { 'Enemy Name' }
    $ws.Cells[$row.EnemyName, 2].Value = $Body.Prefab
    $ws.Cells[$row.Boss, 1].Value = 'Boss'
    $ws.Cells[$row.Boss, 2].Value = if ($Body.Boss) { 'Yes' } else { 'No' }
    $ws.Cells[$row.Guid, 1].Value = 'GUID'
    $ws.Cells[$row.Guid, 2].Value = $Body.Guid
    $ws.Cells[$row.Sync, 1].Value = 'Sync'
    $ws.Cells[$row.Sync, 2].Formula = "IF(`$B`$$($row.Guid)=`"`",`"NEW`",`"Live`")"
    $ws.Cells["A$($row.EnemyName):A$($row.Sync)"].Style.Font.Bold = $true

    $statsHeaderRow = $row.DisplayName - 1
    $ws.Cells[$statsHeaderRow, 1].Value = 'Stats (synced to Unity)'
    $ws.Cells[$statsHeaderRow, 1].Style.Font.Bold = $true
    $ws.Cells[$row.DisplayName, 1].Value = 'Display Name'
    $ws.Cells[$row.DisplayName, 2].Value = $Body.DisplayName
    $ws.Cells[$row.Health, 1].Value = 'Health'
    $ws.Cells[$row.Health, 2].Value = $Body.MaxHealth
    $ws.Cells[$row.Actions, 1].Value = 'Actions Per Turn'
    $ws.Cells[$row.Actions, 2].Value = $Body.ActionPoints
    $ws.Cells[$row.Brain, 1].Value = 'Brain'
    $ws.Cells[$row.Brain, 2].Value = $Body.Brain
    $ws.Cells[$row.Targeting, 1].Value = 'Targeting'
    $ws.Cells[$row.Targeting, 2].Value = $Body.Targeting
    $ws.Cells[$row.Loot, 1].Value = 'Loot Table'
    $ws.Cells[$row.Loot, 2].Value = $Body.LootTable
    $ws.Cells[$row.Role, 1].Value = 'Role'
    $ws.Cells[$row.Role, 2].Value = $Body.Role
    foreach ($r in @($row.DisplayName, $row.Health, $row.Actions, $row.Brain, $row.Targeting, $row.Loot, $row.Role)) {
        $ws.Cells[$r, 1].Style.Font.Italic = $true
    }

    foreach ($v in @(@($row.Brain, 'list_Brain'), @($row.Targeting, 'list_Targeting'), @($row.Loot, 'list_Loot'), @($row.Role, 'list_Role'))) {
        $dv = $ws.DataValidations.AddListValidation("B$($v[0])")
        $dv.Formula.ExcelFormula = "=$($v[1])"
        $dv.ShowErrorMessage = $false
        $dv.AllowBlank = $true
    }

    $pv = $null
    if ($Preserved.ContainsKey($Body.Guid)) { $pv = $Preserved[$Body.Guid] }
    $ws.Cells[$row.Brandon, 1].Value = "Brandon's Power Level"
    if ($null -ne $pv -and $pv.Brandon -ne '' -and $null -ne $pv.Brandon) {
        $d = 0.0
        if ([double]::TryParse([string]$pv.Brandon, [ref]$d)) { $ws.Cells[$row.Brandon, 2].Value = $d }
    }
    $ws.Cells[$row.Brandon, 1].Style.Font.Bold = $true

    $ws.Cells[$row.Estimated, 1].Value = 'Estimated Power Level'
    $ws.Cells[$row.Estimated, 1].Style.Font.Bold = $true
    $ws.Cells[$row.Delta, 1].Value = 'Delta (Estimated - Brandon''s)'
    $ws.Cells[$row.Delta, 2].Formula = "IF(`$B`$$($row.Brandon)=`"`",`"`",ROUND(`$B`$$($row.Estimated)-`$B`$$($row.Brandon),3))"

    $ws.Cells[$row.AvgCardPower, 1].Value = 'Average Card Power'
    $ws.Cells[$row.AvgDamage, 1].Value = 'Average Damage'
    $ws.Cells[$row.AvgStatus, 1].Value = 'Average Status Power'
    $ws.Cells[$row.AvgSummon, 1].Value = 'Average Summon Power'

    $deck = @($Body.Deck)
    $lastCol = [Math]::Max(2, $deck.Count + 1)
    $bLetter = Get-ExcelColumnName 2
    $lastLetter = Get-ExcelColumnName $lastCol

    $ws.Cells[$row.Estimated, 2].Formula =
        "IF(`$B`$$($row.Health)=`"`",`"`",ROUND((`$B`$$($row.AvgCardPower)*`$B`$$($row.Actions)*pw_ActionScale+`$B`$$($row.Health)*pw_Health)/pw_Divisor,3))"

    if ($deck.Count -gt 0) {
        $countedRange = "$bLetter`$$($row.Counted):$lastLetter`$$($row.Counted)"
        $ws.Cells[$row.AvgCardPower, 2].Formula = "IFERROR(AVERAGEIF($countedRange,`"Yes`",$bLetter`$$($row.CardPower):$lastLetter`$$($row.CardPower)),0)"
        $ws.Cells[$row.AvgDamage, 2].Formula   = "IFERROR(AVERAGEIF($countedRange,`"Yes`",$bLetter`$$($row.Damage):$lastLetter`$$($row.Damage)),0)"
        $ws.Cells[$row.AvgStatus, 2].Formula   = "IFERROR(AVERAGEIF($countedRange,`"Yes`",$bLetter`$$($row.StatusPower):$lastLetter`$$($row.StatusPower)),0)"
        $ws.Cells[$row.AvgSummon, 2].Formula   = "IFERROR(AVERAGEIF($countedRange,`"Yes`",$bLetter`$$($row.SummonPower):$lastLetter`$$($row.SummonPower)),0)"
    }
    else {
        foreach ($r in @($row.AvgCardPower, $row.AvgDamage, $row.AvgStatus, $row.AvgSummon)) { $ws.Cells[$r, 2].Value = 0 }
    }

    $ws.Cells["B$($row.Estimated):B$($row.AvgSummon)"].Style.Numberformat.Format = '0.00'
    Add-NamedCell -Package $Package -Worksheet $ws -Name "pow_$($Body.Prefab)" -Address "B$($row.Estimated)"

    $ws.Cells[$row.DeckHeader, 1].Value = 'Deck'
    $ws.Cells[$row.DeckHeader, 1].Style.Font.Bold = $true
    $ws.Cells[$row.ActionsFromCard, 1].Value = 'Actions from Card'
    $ws.Cells[$row.Counted, 1].Value = 'Counted'
    $ws.Cells[$row.FactsHeader, 1].Value = 'Card Facts'
    $ws.Cells[$row.FactsHeader, 1].Style.Font.Bold = $true

    $factLabels = [ordered]@{
        Cost = 'Cost'; RangeMax = 'Range Max'; TilesHit = 'Tiles Hit'
        Damage = 'Damage'; RangePower = 'Range'
        PoisonTotal = 'Poison Total'; Heal = 'Heal'; Shield = 'Shield'
        BlockTotal = 'Block Total'; Parry = 'Parry'; Dodge = 'Dodge'
        Strength = 'Strength'; Weaken = 'Weaken'; Vulnerable = 'Vulnerable'
        Frozen = 'Frozen'; Rooted = 'Rooted'; Taunt = 'Taunt'; Stealth = 'Stealth'
        PoisonBlade = 'Poison Blade'; DoubleNextAttack = 'Double Next Attack'; DoubleShield = 'Double Shield'
        Draw = 'Draw'; MoveTiles = 'Move Tiles'; SummonPower = 'Summon Power'; Cooldown = 'Cooldown'
        CardPower = 'Card Power'; StatusPower = 'Status Power'
    }
    foreach ($k in $factLabels.Keys) { $ws.Cells[$row.$k, 1].Value = $factLabels[$k] }
    $ws.Cells[$row.CardPower, 1].Style.Font.Bold = $true

    $ws.Cells[$row.Notes, 1].Value = 'Notes'
    $ws.Cells[$row.Notes, 1].Style.Font.Bold = $true
    if ($null -ne $pv -and $pv.Notes) { $ws.Cells[$row.Notes, 2].Value = $pv.Notes }
    $ws.Cells[$row.Notes, 2].Style.WrapText = $true

    for ($i = 0; $i -lt $deck.Count; $i++) {
        $col = $i + 2
        $letter = Get-ExcelColumnName $col
        $entry = $deck[$i]
        $facts = $entry.Facts

        $ws.Cells[$row.DeckHeader, $col].Value = $entry.Name

        $dvDeck = $ws.DataValidations.AddListValidation("$letter$($row.DeckHeader)")
        $dvDeck.Formula.ExcelFormula = '=list_EnemyCard'
        $dvDeck.ShowErrorMessage = $false
        $dvDeck.AllowBlank = $true

        if ($null -eq $facts) {
            # Deck references something Get-CardRecord could not resolve (a missing asset). Surfaced,
            # not silently dropped - the rest of the row is left blank so it visibly contributes nothing.
            $ws.Cells[$row.ActionsFromCard, $col].Value = '<unresolved>'
            $ws.Cells[$row.Counted, $col].Value = 'No'
            continue
        }

        $ws.Cells[$row.ActionsFromCard, $col].Value = Format-ActionSummary -Facts $facts
        $counted = if ($facts.IsMove -or $facts.IsCooldown) { 'No' } else { 'Yes' }
        $ws.Cells[$row.Counted, $col].Value = $counted

        $ws.Cells[$row.Cost, $col].Value = [int]$entry.Record.Row.Cost
        $ws.Cells[$row.RangeMax, $col].Value = [int]$entry.Record.Row.'Range Max'
        $ws.Cells[$row.TilesHit, $col].Value = [int]$facts.TilesHit
        $ws.Cells[$row.Damage, $col].Value = [int]$facts.Damage
        $ws.Cells[$row.RangePower, $col].Formula = "IFERROR(VLOOKUP($letter`$$($row.RangeMax),range_Power,2,TRUE),0)"
        $ws.Cells[$row.PoisonTotal, $col].Value = [int]$facts.PoisonTotal
        $ws.Cells[$row.Heal, $col].Value = [int]$facts.Heal
        $ws.Cells[$row.Shield, $col].Value = [int]$facts.Shield
        $ws.Cells[$row.BlockTotal, $col].Value = [int]$facts.BlockTotal
        $ws.Cells[$row.Parry, $col].Value = [int]$facts.Parry
        $ws.Cells[$row.Dodge, $col].Value = [int]$facts.Dodge
        $ws.Cells[$row.Strength, $col].Value = [int]$facts.Strength
        $ws.Cells[$row.Weaken, $col].Value = [int]$facts.Weaken
        $ws.Cells[$row.Vulnerable, $col].Value = [int]$facts.Vulnerable
        $ws.Cells[$row.Frozen, $col].Value = [int]$facts.Frozen
        $ws.Cells[$row.Rooted, $col].Value = [int]$facts.Rooted
        $ws.Cells[$row.Taunt, $col].Value = [int]$facts.Taunt
        $ws.Cells[$row.Stealth, $col].Value = [int]$facts.Stealth
        $ws.Cells[$row.PoisonBlade, $col].Value = [int]$facts.PoisonBlade
        $ws.Cells[$row.DoubleNextAttack, $col].Value = [int]$facts.DoubleNextAttack
        $ws.Cells[$row.DoubleShield, $col].Value = [int]$facts.DoubleShield
        $ws.Cells[$row.Draw, $col].Value = [int]$facts.Draw
        $ws.Cells[$row.MoveTiles, $col].Value = [int]$facts.MoveTiles
        $ws.Cells[$row.Cooldown, $col].Value = [int]$facts.CooldownTurns

        if ($facts.IsSummon -and $entry.SummonTarget -and ($AllPowerNames -contains $entry.SummonTarget)) {
            $powName = "pow_$($entry.SummonTarget)"
            if ($entry.SummonLifetime -gt 0) {
                $ws.Cells[$row.SummonPower, $col].Formula = "MIN($($entry.SummonLifetime),pw_SummonHorizon)/pw_SummonHorizon*$powName"
            }
            else {
                $ws.Cells[$row.SummonPower, $col].Formula = "$powName"
            }
        }
        elseif ($facts.IsSummon) {
            # Summons something outside this workbook's discovered roster (should not happen with the
            # current data - see the discovery loop in Export-EnemySheet.ps1) - scored as 0 rather than
            # left as a broken formula, and called out so it does not go unnoticed.
            $ws.Cells[$row.SummonPower, $col].Value = 0
            Write-Warning "$($Body.Prefab) / $($entry.Name) summons '$($entry.SummonTarget)', which has no tab - scored as 0."
        }
        else {
            $ws.Cells[$row.SummonPower, $col].Value = 0
        }

        $ws.Cells[$row.CardPower, $col].Formula =
            "$letter`$$($row.Damage)*pw_Damage+$letter`$$($row.RangePower)+$letter`$$($row.PoisonTotal)*pw_Poison+$letter`$$($row.Heal)*pw_Heal" +
            "+$letter`$$($row.Shield)*pw_Shield+$letter`$$($row.BlockTotal)*pw_Block+$letter`$$($row.Parry)*pw_Parry" +
            "+$letter`$$($row.Dodge)*pw_Dodge+$letter`$$($row.Strength)*pw_Strength*pw_Horizon" +
            "+$letter`$$($row.Weaken)*pw_Weaken*pw_Horizon+$letter`$$($row.Vulnerable)*pw_Vulnerable*pw_Horizon" +
            "+$letter`$$($row.Frozen)*pw_Frozen+$letter`$$($row.Rooted)*pw_Rooted+$letter`$$($row.Taunt)*pw_Taunt" +
            "+$letter`$$($row.Stealth)*pw_Stealth+$letter`$$($row.PoisonBlade)*pw_PoisonBlade*pw_Horizon" +
            "+$letter`$$($row.DoubleNextAttack)*pw_DoubleNextAttack+$letter`$$($row.DoubleShield)*pw_DoubleShield" +
            "+$letter`$$($row.Draw)*pw_Draw+$letter`$$($row.MoveTiles)*pw_Move+$letter`$$($row.SummonPower)*pw_Summon" +
            "+MAX(0,$letter`$$($row.TilesHit)-1)*pw_Area"

        # Status Power is "everything that isn't Damage, Range or Summon" - all three now have their own
        # visible row/average, so none of the three should also hide inside this one.
        $ws.Cells[$row.StatusPower, $col].Formula =
            "$letter`$$($row.CardPower)-$letter`$$($row.Damage)*pw_Damage-$letter`$$($row.RangePower)-$letter`$$($row.SummonPower)*pw_Summon"
    }

    if ($lastCol -ge 2) {
        $ws.Cells["B$($row.Cost):$lastLetter$($row.StatusPower)"].Style.Numberformat.Format = '0.00'
        foreach ($intRow in @($row.Cost, $row.RangeMax, $row.TilesHit, $row.Cooldown)) {
            $ws.Cells["B$($intRow):$lastLetter$intRow"].Style.Numberformat.Format = '0'
        }
    }

    $ws.Column(1).Width = 24
    for ($c = 2; $c -le $lastCol; $c++) { $ws.Column($c).Width = 16 }
    $ws.View.FreezePanes(1, 2)
}

# ---------------------------------------------------------------------------------------------------
# One tab per totem
# ---------------------------------------------------------------------------------------------------

function Add-TotemSheet {
    param($Package, $Totem, [hashtable]$Preserved)

    $row = $script:TotemRow
    $ws = $Package.Workbook.Worksheets.Add($Totem.Prefab)

    $ws.Cells[$row.Name, 1].Value = 'Totem Name'
    $ws.Cells[$row.Name, 2].Value = $Totem.Prefab
    $ws.Cells[$row.Guid, 1].Value = 'GUID'
    $ws.Cells[$row.Guid, 2].Value = $Totem.Guid
    $ws.Cells[$row.Range, 1].Value = 'Range'
    $ws.Cells[$row.Range, 2].Value = $Totem.Range
    $ws.Cells[$row.Affects, 1].Value = 'Affects'
    $ws.Cells[$row.Affects, 2].Value = $Totem.Affects
    $ws.Cells["A$($row.Name):A$($row.Affects)"].Style.Font.Bold = $true

    $ws.Cells[$row.Power, 1].Value = 'Totem Power'
    $ws.Cells[$row.Power, 1].Style.Font.Bold = $true

    $ws.Cells[$row.AuraHeader, 1].Value = 'Auras (maintained - active the whole time something stands in range)'
    $ws.Cells[$row.AuraHeader, 1].Style.Font.Bold = $true
    $auraLabelRow = $row.AuraHeader + 1
    $ws.Cells[$auraLabelRow, 1].Value = 'Status'
    $ws.Cells[$auraLabelRow, 2].Value = 'Stacks'
    $ws.Cells[$auraLabelRow, 3].Value = 'Weighted'
    $ws.Cells["A$($auraLabelRow):C$($auraLabelRow)"].Style.Font.Italic = $true

    $weightedCells = @()
    $r = $row.AuraHeader + 2
    foreach ($aura in @($Totem.Auras)) {
        $weightName = $null
        if ($script:StatusWeightMap.Contains($aura.Status)) { $weightName = $script:StatusWeightMap[$aura.Status] }

        $amount = [int]$aura.Stacks
        if ($aura.Status -eq 'Poison') { $amount = Get-PoisonTotal -Stacks $amount }
        if ($aura.Status -eq 'Block') { $amount = $amount * 5 }

        $ws.Cells[$r, 1].Value = $aura.Status
        $ws.Cells[$r, 2].Value = $amount
        if ($weightName) {
            $ws.Cells[$r, 3].Formula = "B$r*$weightName"
            $weightedCells += "C$r"
        }
        else {
            $ws.Cells[$r, 3].Value = 0
        }
        $r++
    }
    if (@($Totem.Auras).Count -eq 0) { $ws.Cells[$r, 1].Value = '(none)'; $r++ }

    $r++
    $reactionHeaderRow = $r
    $ws.Cells[$r, 1].Value = 'Reactions (one-shot - fire once per matching action while covered)'
    $ws.Cells[$r, 1].Style.Font.Bold = $true
    $r++
    $ws.Cells[$r, 1].Value = 'Trigger'
    $ws.Cells[$r, 2].Value = 'Effect'
    $ws.Cells[$r, 3].Value = 'Amount'
    $ws.Cells[$r, 4].Value = 'Weighted'
    $ws.Cells["A$r`:D$r"].Style.Font.Italic = $true
    $r++

    foreach ($rx in @($Totem.Reactions)) {
        $d = $rx.Descriptor
        $weightName = $null
        $amount = [int]$d.Amount
        switch -Regex ($d.Kind) {
            '^Damage$' { $weightName = 'pw_Damage' }
            '^Heal$'   { $weightName = 'pw_Heal' }
            '^Shield$' { $weightName = 'pw_Shield' }
            '^Draw$'   { $weightName = 'pw_Draw' }
            '^Block$'  { $weightName = 'pw_Block'; $amount = $amount * 5 }
            '^Status:' {
                $status = $d.Kind.Substring(7)
                if ($status -eq 'Poison') { $amount = Get-PoisonTotal -Stacks $amount }
                if ($script:StatusWeightMap.Contains($status)) { $weightName = $script:StatusWeightMap[$status] }
            }
        }

        $ws.Cells[$r, 1].Value = Get-TriggerName $rx.Trigger
        $ws.Cells[$r, 2].Value = $d.Name
        $ws.Cells[$r, 3].Value = $amount
        if ($weightName) {
            $ws.Cells[$r, 4].Formula = "C$r*$weightName"
            $weightedCells += "D$r"
        }
        else {
            $ws.Cells[$r, 4].Value = 0
        }
        $r++
    }
    if (@($Totem.Reactions).Count -eq 0) { $ws.Cells[$r, 1].Value = '(none)'; $r++ }

    if ($weightedCells.Count -gt 0) {
        $ws.Cells[$row.Power, 2].Formula = '(' + ($weightedCells -join '+') + ')*pw_TotemActivations'
    }
    else {
        $ws.Cells[$row.Power, 2].Value = 0
    }
    $ws.Cells[$row.Power, 2].Style.Numberformat.Format = '0.00'
    Add-NamedCell -Package $Package -Worksheet $ws -Name "pow_$($Totem.Prefab)" -Address "B$($row.Power)"

    $r++
    $notesRow = $r
    $ws.Cells[$notesRow, 1].Value = 'Notes'
    $ws.Cells[$notesRow, 1].Style.Font.Bold = $true
    if ($Preserved.ContainsKey($Totem.Guid) -and $Preserved[$Totem.Guid].Notes) {
        $ws.Cells[$notesRow, 2].Value = $Preserved[$Totem.Guid].Notes
    }
    $ws.Cells[$notesRow, 2].Style.WrapText = $true

    $ws.Column(1).Width = 26
    $ws.Column(2).Width = 40
    $ws.Column(3).Width = 14
    $ws.Column(4).Width = 14
    $ws.View.FreezePanes(1, 2)
}

# ---------------------------------------------------------------------------------------------------
# Roster - every body compared side by side
# ---------------------------------------------------------------------------------------------------

function Add-RosterSheet {
    <#
        .SYNOPSIS
            The bulk-edit grid: one row per body, Name/GUID/Boss identity up front, then the eight
            designer-authored fields as PLAIN VALUES (not cross-tab formulas - EPPlus cannot read a
            formula back out, so a column has to choose between being live and being editable), then the
            derived scoring block, self-contained per row so it keeps updating as you type.

        .DESCRIPTION
            Name is the one column with a side effect: Import-EnemySheet.ps1 reads it against the GUID in
            column B, and a changed Name is a rename request - see RosterSheetSync.WriteBody. Every other
            editable column (Display Name, Health, Actions, Brain, Targeting, Loot Table, Role, Brandon's)
            feeds the same three-way merge Read-BodySheetTab's copy already does, arbitrated one layer
            earlier by Resolve-SheetPair against whatever the body tab holds for the same field.

            Boss and Deck Size stay plain readouts - Boss is folder-derived (see Get-CharacterBody) and
            Deck Size only exists on the body tab, whose Deck this feature does not make editable. Avg
            Damage/Status/Summon/Card Power stay cross-tab formulas reading the body tab's own arithmetic
            for the same reason - only Estimated/Effective Power/Delta/Flag are recomputed locally, from
            this row's OWN Health/Actions/Brandon's, which is what keeps them live while you type instead
            of reading stale until the next sync.

            No Sync/NEW column: creating a body from this tab is out of scope (see RosterSheetSync's
            class doc comment), so every row here is already Live and a Sync column would just be noise.
    #>
    param($Package, $Bodies, [hashtable]$Preserved = @{})

    $ws = $Package.Workbook.Worksheets.Add('Roster')
    $row = $script:BodyRow

    $headers = @('Name', 'GUID', 'Boss', 'Display Name', 'Health', 'Actions', 'Brain', 'Targeting',
                 'Loot Table', 'Role', 'Deck Size', 'Avg Damage', 'Avg Status', 'Avg Summon',
                 'Avg Card Power', 'Estimated', "Brandon's", 'Effective Power', 'Delta', 'Flag')
    for ($c = 0; $c -lt $headers.Count; $c++) { $ws.Cells[1, ($c + 1)].Value = $headers[$c] }
    $ws.Cells['A1:T1'].Style.Font.Bold = $true

    $ordered = @($Bodies | Sort-Object { $_.Prefab })
    for ($i = 0; $i -lt $ordered.Count; $i++) {
        $b = $ordered[$i]
        $r = $i + 2
        $name = "'$($b.Prefab)'"

        $ws.Cells[$r, 1].Value = $b.Prefab
        $ws.Cells[$r, 2].Value = $b.Guid
        $ws.Cells[$r, 3].Value = if ($b.Boss) { 'Yes' } else { 'No' }
        $ws.Cells[$r, 4].Value = $b.DisplayName
        $ws.Cells[$r, 5].Value = $b.MaxHealth
        $ws.Cells[$r, 6].Value = $b.ActionPoints
        $ws.Cells[$r, 7].Value = $b.Brain
        $ws.Cells[$r, 8].Value = $b.Targeting
        $ws.Cells[$r, 9].Value = $b.LootTable
        $ws.Cells[$r, 10].Value = $b.Role
        $ws.Cells[$r, 11].Value = @($b.Deck).Count
        $ws.Cells[$r, 12].Formula = "$name!B$($row.AvgDamage)"
        $ws.Cells[$r, 13].Formula = "$name!B$($row.AvgStatus)"
        $ws.Cells[$r, 14].Formula = "$name!B$($row.AvgSummon)"
        $ws.Cells[$r, 15].Formula = "$name!B$($row.AvgCardPower)"
        # Local, not '<Prefab>'!B<row> - Health/Actions are now typed on THIS row, so reading the body
        # tab's own Estimated would show a stale number until the next sync. Same formula shape as
        # Add-BodySheet's, just pointed at this row's own cells instead of $B$<row>.
        $ws.Cells[$r, 16].Formula = "IF(E$r=`"`",`"`",ROUND((O$r*F$r*pw_ActionScale+E$r*pw_Health)/pw_Divisor,3))"

        $pv = $null
        if ($Preserved.ContainsKey($b.Guid)) { $pv = $Preserved[$b.Guid] }
        if ($null -ne $pv -and $pv.Brandon -ne '' -and $null -ne $pv.Brandon) {
            $d = 0.0
            if ([double]::TryParse([string]$pv.Brandon, [ref]$d)) { $ws.Cells[$r, 17].Value = $d }
        }

        # Effective Power: what EncounterRoller actually draws against - Brandon's if authored, else
        # the Estimated formula's own result. Mirrors the resolution Import-EnemySheet.ps1 computes
        # independently when it writes Character.powerLevel, so this column is a live preview of that,
        # not the value's source.
        $ws.Cells[$r, 18].Formula = "IF(Q$r=`"`",P$r,Q$r)"
        $ws.Cells[$r, 19].Formula = "IF(Q$r=`"`",`"`",ROUND(P$r-Q$r,3))"
        $ws.Cells[$r, 20].Formula =
            "IF(S$r=`"`",`"`",IF(ABS(S$r)<=P$r*pw_Tolerance,`"OK`",IF(S$r>0,`"OVER`",`"UNDER`")))"
    }

    $last = $ordered.Count + 1
    if ($ordered.Count -gt 0) {
        $ws.Cells["L2:S$last"].Style.Numberformat.Format = '0.00'

        foreach ($v in @(@(7, 'list_Brain'), @(8, 'list_Targeting'), @(9, 'list_Loot'), @(10, 'list_Role'))) {
            $letter = Get-ExcelColumnName $v[0]
            $dv = $ws.DataValidations.AddListValidation("$letter`2:$letter$last")
            $dv.Formula.ExcelFormula = "=$($v[1])"
            $dv.ShowErrorMessage = $false
            $dv.AllowBlank = $true
        }

        $verdicts = @(
            @('OVER',  @(255, 199, 206), @(156, 0, 6)),
            @('UNDER', @(255, 235, 156), @(156, 101, 0)),
            @('OK',    @(198, 239, 206), @(0, 97, 0))
        )
        foreach ($v in $verdicts) {
            $fmt = $ws.ConditionalFormatting.AddEqual($ws.Cells["T2:T$last"])
            $fmt.Formula = '"' + $v[0] + '"'
            $fmt.Style.Fill.BackgroundColor.Color = [System.Drawing.Color]::FromArgb($v[1][0], $v[1][1], $v[1][2])
            $fmt.Style.Font.Color.Color = [System.Drawing.Color]::FromArgb($v[2][0], $v[2][1], $v[2][2])
        }

        $ws.Cells["A1:T$last"].AutoFilter = $true
    }

    $ws.Column(1).Width = 22
    $ws.Column(2).Width = 10
    $ws.Column(4).Width = 16
    $ws.Column(8).Width = 18
    $ws.View.FreezePanes(2, 4)
}

# ---------------------------------------------------------------------------------------------------
# Summoned - pow_* cells for bodies this workbook does not own
# ---------------------------------------------------------------------------------------------------

function Add-SummonedSheet {
    <#
        .SYNOPSIS
            One row per body summoned by this workbook's roster but owned by the other workbook.

        .DESCRIPTION
            Seven of the eight bosses summon a plain enemy, and Summon Power on a body tab is a live
            =pow_<Prefab> reference to that body's own power cell. Excel names are workbook-scoped, so
            in a bosses-only workbook those seven references would have no target.

            Giving each summoned enemy a real tab here would fix the formula and break something worse:
            the same prefab would then be described by two workbooks with two independent baselines, and
            both importers would queue writes for it on every sync. So it gets a power number and
            nothing else - and deliberately NO 'GUID' row, which is what makes Read-BodySheetTab return
            $null for this sheet and every reader skip it without needing to know it exists.

            The Power column is a snapshot taken at export time from the owning workbook (or, failing
            that, the prefab's own Character.powerLevel). It refreshes on every sync, so it is never
            more stale than the last time the two tools were run.
    #>
    param($Package, $SummonedReferences, $Domain)

    $ws = $Package.Workbook.Worksheets.Add('Summoned')

    $source = if ($Domain -and $Domain.PowerLevelSourceRel) { $Domain.PowerLevelSourceRel } else { 'the other roster workbook' }

    $ws.Cells[1, 1].Value = "READ-ONLY - bodies summoned by this workbook's roster but owned by $source. " +
                            'Refreshed on every export; edit them there.'
    $ws.Cells[1, 1].Style.Font.Bold = $true
    $ws.Cells[1, 1].Style.Font.Color.SetColor([System.Drawing.Color]::FromArgb(150, 40, 40))

    $ws.Cells[2, 1].Value = 'Prefab'
    $ws.Cells[2, 2].Value = 'Power'
    $ws.Cells[2, 3].Value = 'Summoned by'
    $ws.Cells[2, 1, 2, 3].Style.Font.Bold = $true

    $r = 3
    foreach ($ref in @($SummonedReferences | Sort-Object Prefab)) {
        $ws.Cells[$r, 1].Value = $ref.Prefab
        $ws.Cells[$r, 2].Value = [double]$ref.Power
        $ws.Cells[$r, 2].Style.Numberformat.Format = '0.000'
        $ws.Cells[$r, 3].Value = (@($ref.SummonedBy) -join ', ')

        # The whole point of this tab: the same pow_<Prefab> name a real body tab would have registered,
        # so Add-BodySheet's summon branch resolves without knowing where the cell lives.
        Add-NamedCell -Package $Package -Worksheet $ws -Name "pow_$($ref.Prefab)" -Address "B$r"
        $r++
    }

    $ws.Cells[2, 1, ($r - 1), 3].Style.Fill.PatternType = [OfficeOpenXml.Style.ExcelFillStyle]::Solid
    $ws.Cells[2, 1, ($r - 1), 3].Style.Fill.BackgroundColor.SetColor([System.Drawing.Color]::FromArgb(242, 242, 242))

    $ws.Column(1).Width = 22
    $ws.Column(2).Width = 11
    $ws.Column(3).Width = 46
}

# ---------------------------------------------------------------------------------------------------
# Enums - dropdown sources
# ---------------------------------------------------------------------------------------------------

function Add-EnumsSheet {
    param($Package, [string[]]$EnemyCardNames, [string[]]$LootNames, [string[]]$TargetingNames, $Bodies, $Totems)

    $ws = $Package.Workbook.Worksheets.Add('Enums')

    $lists = [ordered]@{
        # BrainType is a C# enum - it only changes when the enum does, unlike Targeting/Loot/cards which
        # are assets and are read fresh off disk every export.
        list_Brain     = @('None', 'Warrior', 'Ranger', 'Summoner')
        list_Targeting = @('') + @($TargetingNames | Sort-Object -Unique)
        list_Loot      = @('') + @($LootNames | Sort-Object -Unique)
        list_Role      = @('None', 'Frontline', 'Backline', 'Both')
        list_Bool      = @('Yes', 'No')
        list_EnemyCard = @($EnemyCardNames | Sort-Object -Unique)
    }

    $col = 1
    foreach ($name in $lists.Keys) {
        $values = @($lists[$name])
        $ws.Cells[1, $col].Value = $name -replace '^list_', ''
        $ws.Cells[1, $col].Style.Font.Bold = $true
        for ($i = 0; $i -lt $values.Count; $i++) { $ws.Cells[($i + 2), $col].Value = $values[$i] }
        if ($values.Count -gt 0) {
            $letter = Get-ExcelColumnName $col
            Add-NamedCell -Package $Package -Worksheet $ws -Name $name -Address "$letter`2:$letter$($values.Count + 1)"
        }
        $ws.Column($col).Width = 22
        $col++
    }

    $ws.Cells[1, $col].Value = 'Drives the dropdowns on every body tab. Regenerated on every export.'
    $ws.Cells[1, $col].Style.Font.Italic = $true
}

# ---------------------------------------------------------------------------------------------------
# README
# ---------------------------------------------------------------------------------------------------

function Add-ReadmeSheet {
    param($Package, $Domain, [bool]$HasSummoned, [switch]$PowerLevelLocked)

    $ws = $Package.Workbook.Worksheets.Add('README')

    $title    = if ($Domain) { $Domain.Title } else { 'Enemy Design Workbook' }
    $noun     = if ($Domain) { $Domain.BodyNoun } else { 'enemy' }
    $syncMenu = if ($Domain) { $Domain.SyncMenu } else { 'Tools > Sync Enemies With Sheet' }
    $refresh  = if ($Domain) { $Domain.RefreshMenu } else { 'Tools > Enemies > Refresh Sheet From Unity' }
    $powerSrc = if ($Domain -and $Domain.PowerLevelSourceRel) { $Domain.PowerLevelSourceRel } else { '' }

    $coverage = if ($noun -eq 'boss') {
        'One tab per boss and totem, all built from the actual prefabs and cards under Assets/Prefabs/Bosses and Assets/Data/CardData/Enemy - this workbook only READS them today. Ordinary enemies and the ally live in Docs/EnemySheets.xlsx instead.'
    }
    else {
        'One tab per enemy, ally and totem, all built from the actual prefabs and cards under Assets/Prefabs and Assets/Data/CardData/Enemy - this workbook only READS them today. Bosses live in Docs/BossDesign.xlsx instead.'
    }

    $powerLevelLine = if ($PowerLevelLocked) {
        "READ-ONLY here - the pw_* weights are mirrored from $powerSrc on every export so both workbooks score on one scale. Retune them there; anything typed on that tab here is discarded on the next sync."
    }
    else {
        'The tuning surface. Every pw_* weight here is a named cell - change one and every body tab and Roster recalculate immediately, no re-export needed.'
    }

    $lines = @(
        @($title, 'title'),
        @('', ''),
        @($coverage, ''),
        @('', ''),
        @('The tabs', 'head'),
        @('PowerLevel', $powerLevelLine),
        @('One tab per body', 'Health, Actions Per Turn, Brain, Targeting, Loot Table, Role and Deck mirror the prefab''s Character component. Card Facts below the deck strip are read from each card''s own CardData/CardEffect assets - edit a card''s numbers on Docs/CardDesign.xlsx, not here.'),
        @('One tab per totem', 'A totem has no health or deck - Totem Power is its auras and reactions, weighted the same way a body''s Card Power is.'),
        @('Roster', 'Every body side by side, sorted by name - the bulk-edit grid. Display Name, Health, Actions Per Turn, Brain, Targeting, Loot Table, Role and Brandon''s Power Level are plain values here, synced to the matching body tab exactly like the Inspector - edit either one. Renaming a body is done by typing its new name in the Name column. Boss, Deck Size and the averages stay read-only; Estimated/Effective Power/Delta/Flag recalculate from this row''s own Health/Actions/Brandon''s as you type, so they never go stale while you''re still editing.'),
        @('Enums', 'Dropdown sources for Brain, Targeting, Loot Table and Deck cells.')
    )

    if ($HasSummoned) {
        $lines += , @('Summoned', "Read-only. Bodies this roster summons but does not own - their power only, so Summon Power on a body tab has something to point at. Their real tabs are in $powerSrc; edit them there.")
    }

    $lines += @(
        @('', ''),
        @('Reading a body tab', 'head'),
        @('Brandon''s Power Level', 'Hand-authored ground truth - type in what a body feels like it should be worth. Preserved across re-exports.'),
        @('Estimated Power Level', '(Average Card Power * Actions Per Turn * pw_ActionScale + Health * pw_Health) / pw_Divisor. Tune the PowerLevel weights until Delta sits near zero for the bodies you already trust, then trust the estimate for the rest.'),
        @('Role', 'Which spawn row(s) this body is eligible for - None (unconstrained), Frontline, Backline or Both. Synced to Character.battleRole; drives BattleManager.SpawnParty and EncounterRoller placement.'),
        @('Counted', 'No for a Move-only card or a Cooldown card - excluded from every average. Cooldown cards get scored separately later.'),
        @('Summon Power', 'A live reference to the summoned body''s (or totem''s) own power cell, scaled down if its lifetime is shorter than pw_SummonHorizon.'),
        @('', ''),
        @('Syncing back to Unity', 'head'),
        @('One button', "In Unity: $syncMenu. Applies your edits - typed on a body tab OR the Roster tab, whichever you used - to the matching prefab: Display Name, Health, Actions Per Turn, Brain, Targeting, Loot Table, Role, Brandon''s Power Level and Deck. Renaming a body''s Name on the Roster tab renames the prefab too, keeping its GUID (so decks and levels still point at it). Then refreshes this workbook so anything changed in the Inspector shows up here."),
        @('Deck', 'One card name per column, left to right, no gaps - the first blank cell ends the deck. Duplicates are fine and expected (four Enemy Slash cards is four cells). Pick names from the dropdown so a typo cannot silently drop a card. Only editable on the body tab - the Roster tab does not carry a Deck column.'),
        @('Notes', 'Never syncs anywhere - authored HERE, body tab only, and preserved across every re-export.'),
        @('Brandon''s Power Level', 'Syncs between the body tab and the Roster tab - whichever one you typed in wins - but never reaches a prefab field directly. It only feeds Effective Power, which is what gets written to Character.powerLevel.'),
        @('Card Facts, Card Power, averages', 'Never hand-edit - fully derived from the prefab and its cards, rebuilt on every export. On the Roster tab this is Avg Damage/Status/Summon/Card Power, Deck Size and Boss.'),
        @('Effective Power and Boss', 'Not sheet columns you edit directly - Effective Power (Brandon''s if set, else Estimated) and Boss (which folder the prefab lives in) are written to Character.powerLevel/isBoss one-way, every sync, regardless of whether anything else on the tab changed. A body with neither Brandon''s nor a usable Estimated result is skipped with a warning and can never be drawn by EncounterRoller.'),
        @('If both sides changed', 'That body is left alone on BOTH sides and named in the Unity console, the same conflict rule Docs/CardDesign.xlsx follows. Make them agree, or change only one, then sync again.'),
        @('If the body tab and Roster disagree', 'Same idea, one layer earlier: whichever of the two changed since the last sync wins, but if BOTH changed - differently - since then, that body is left alone everywhere and named in the console. Make the two tabs agree, or change only one, then sync again.'),
        @('Regenerating without syncing', "$refresh discards any sheet edit that has not been synced yet - only for when the sheet is known to be wrong."),
        @('The other roster workbook', 'Enemies, the ally and bosses are two separate workbooks driven by the same engine - Docs/EnemySheets.xlsx and Docs/BossDesign.xlsx - each with its own baseline and its own sync button. A prefab belongs to exactly one of them, decided by which folder it sits in under Assets/Prefabs.')
    )

    $r = 1
    foreach ($line in $lines) {
        $ws.Cells[$r, 1].Value = $line[0]
        if ($line[1] -eq 'title') {
            $ws.Cells[$r, 1].Style.Font.Size = 16
            $ws.Cells[$r, 1].Style.Font.Bold = $true
        }
        elseif ($line[1] -eq 'head') {
            $ws.Cells[$r, 1].Style.Font.Bold = $true
            $ws.Cells[$r, 1].Style.Font.Size = 12
        }
        elseif ($line[1] -ne '') {
            $ws.Cells[$r, 1].Style.Font.Bold = $true
            $ws.Cells[$r, 2].Value = $line[1]
        }
        $r++
    }

    $ws.Column(1).Width = 28
    $ws.Column(2).Width = 110
    $ws.Column(2).Style.WrapText = $true
}

function Set-SheetOrder {
    param($Package, $Bodies, $Totems, [bool]$HasSummoned)

    $order = @('README', 'PowerLevel', 'Roster') + @($Bodies | Sort-Object Prefab | ForEach-Object { $_.Prefab }) +
             @($Totems | Sort-Object Prefab | ForEach-Object { $_.Prefab })
    if ($HasSummoned) { $order += 'Summoned' }
    $order += 'Enums'

    foreach ($name in $order) {
        $ws = $Package.Workbook.Worksheets[$name]
        if ($null -ne $ws) { $Package.Workbook.Worksheets.MoveToEnd($name) }
    }

    $Package.Workbook.Worksheets['README'].Select()
}
