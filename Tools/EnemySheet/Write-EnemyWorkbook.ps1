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
    DisplayName = 7; Health = 8; Actions = 9; Brain = 10; Targeting = 11; Loot = 12
    Brandon = 14; Estimated = 15; Delta = 16
    AvgCardPower = 18; AvgDamage = 19; AvgStatus = 20; AvgSummon = 21
    DeckHeader = 23; ActionsFromCard = 24; Counted = 25
    FactsHeader = 27
    Cost = 28; RangeMax = 29; TilesHit = 30; Damage = 31; PoisonTotal = 32; Heal = 33; Shield = 34
    BlockTotal = 35; Parry = 36; Dodge = 37; Strength = 38; Weaken = 39; Vulnerable = 40; Frozen = 41
    Rooted = 42; Taunt = 43; Stealth = 44; PoisonBlade = 45; DoubleNextAttack = 46; DoubleShield = 47
    Draw = 48; MoveTiles = 49; SummonPower = 50; Cooldown = 51; CardPower = 52; StatusPower = 53
    Notes = 55
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
        $Preserved,       # hashtable: Prefab -> @{ Brandon = ...; Notes = ... }
        [string]$RepoRoot
    )

    $dir = Split-Path -Parent $WorkbookPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    if (Test-Path -LiteralPath $WorkbookPath) { Remove-Item -LiteralPath $WorkbookPath -Force }

    Write-Host 'Writing workbook...' -ForegroundColor Cyan

    $pkg = Open-ExcelPackage -Path $WorkbookPath -Create

    $allNames = @($Bodies | ForEach-Object { $_.Prefab }) + @($Totems | ForEach-Object { $_.Prefab })

    Add-PowerLevelSheet -Package $pkg

    foreach ($body in $Bodies) {
        Add-BodySheet -Package $pkg -Body $body -Preserved $Preserved -AllPowerNames $allNames
    }

    foreach ($totem in $Totems) {
        Add-TotemSheet -Package $pkg -Totem $totem -Preserved $Preserved
    }

    Add-RosterSheet -Package $pkg -Bodies $Bodies
    Add-EnumsSheet -Package $pkg -EnemyCardNames $EnemyCardNames -LootNames $LootNames -Bodies $Bodies -Totems $Totems
    Add-ReadmeSheet -Package $pkg

    Set-SheetOrder -Package $pkg -Bodies $Bodies -Totems $Totems

    Close-ExcelPackage $pkg
}

# ---------------------------------------------------------------------------------------------------
# PowerLevel - the tuning surface
# ---------------------------------------------------------------------------------------------------

function Add-PowerLevelSheet {
    param($Package)

    $ws = $Package.Workbook.Worksheets.Add('PowerLevel')

    $ws.Cells['A1'].Value = 'Term'
    $ws.Cells['B1'].Value = 'Value'
    $ws.Cells['C1'].Value = 'What this counts'
    $ws.Cells['A1:C1'].Style.Font.Bold = $true

    $weights = @(Get-PowerWeightDefaults)
    for ($i = 0; $i -lt $weights.Count; $i++) {
        $r = $i + 2
        $ws.Cells[$r, 1].Value = $weights[$i][0]
        $ws.Cells[$r, 2].Value = [double]$weights[$i][1]
        $ws.Cells[$r, 3].Value = $weights[$i][2]
        Add-NamedCell -Package $Package -Worksheet $ws -Name $weights[$i][0] -Address "B$r"
    }

    $ws.Cells['B2:B200'].Style.Numberformat.Format = '0.00'
    $ws.Column(1).Width = 20
    $ws.Column(2).Width = 11
    $ws.Column(3).Width = 90
    $ws.Column(3).Style.WrapText = $true
    $ws.View.FreezePanes(2, 1)

    $note = $weights.Count + 3
    $ws.Cells[$note, 1].Value = 'Edit the Value column - every body tab and the Roster tab recalculate. Nothing here is baked into the export script.'
    $ws.Cells[$note, 1].Style.Font.Italic = $true
}

# ---------------------------------------------------------------------------------------------------
# One tab per body
# ---------------------------------------------------------------------------------------------------

function Add-BodySheet {
    param($Package, $Body, [hashtable]$Preserved, [string[]]$AllPowerNames)

    $row = $script:BodyRow
    $ws = $Package.Workbook.Worksheets.Add($Body.Prefab)

    $ws.Cells[$row.EnemyName, 1].Value = 'Enemy Name'
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
    foreach ($r in @($row.DisplayName, $row.Health, $row.Actions, $row.Brain, $row.Targeting, $row.Loot)) {
        $ws.Cells[$r, 1].Style.Font.Italic = $true
    }

    $pv = $null
    if ($Preserved.ContainsKey($Body.Prefab)) { $pv = $Preserved[$Body.Prefab] }
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
        Damage = 'Damage'; PoisonTotal = 'Poison Total'; Heal = 'Heal'; Shield = 'Shield'
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
            "$letter`$$($row.Damage)*pw_Damage+$letter`$$($row.PoisonTotal)*pw_Poison+$letter`$$($row.Heal)*pw_Heal" +
            "+$letter`$$($row.Shield)*pw_Shield+$letter`$$($row.BlockTotal)*pw_Block+$letter`$$($row.Parry)*pw_Parry" +
            "+$letter`$$($row.Dodge)*pw_Dodge+$letter`$$($row.Strength)*pw_Strength*pw_Horizon" +
            "+$letter`$$($row.Weaken)*pw_Weaken*pw_Horizon+$letter`$$($row.Vulnerable)*pw_Vulnerable*pw_Horizon" +
            "+$letter`$$($row.Frozen)*pw_Frozen+$letter`$$($row.Rooted)*pw_Rooted+$letter`$$($row.Taunt)*pw_Taunt" +
            "+$letter`$$($row.Stealth)*pw_Stealth+$letter`$$($row.PoisonBlade)*pw_PoisonBlade*pw_Horizon" +
            "+$letter`$$($row.DoubleNextAttack)*pw_DoubleNextAttack+$letter`$$($row.DoubleShield)*pw_DoubleShield" +
            "+$letter`$$($row.Draw)*pw_Draw+$letter`$$($row.MoveTiles)*pw_Move+$letter`$$($row.SummonPower)*pw_Summon" +
            "+MAX(0,$letter`$$($row.TilesHit)-1)*pw_Area+MAX(0,$letter`$$($row.RangeMax)-1)*pw_Range"

        $ws.Cells[$row.StatusPower, $col].Formula =
            "$letter`$$($row.CardPower)-$letter`$$($row.Damage)*pw_Damage-$letter`$$($row.SummonPower)*pw_Summon"
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
    if ($Preserved.ContainsKey($Totem.Prefab) -and $Preserved[$Totem.Prefab].Notes) {
        $ws.Cells[$notesRow, 2].Value = $Preserved[$Totem.Prefab].Notes
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
    param($Package, $Bodies)

    $ws = $Package.Workbook.Worksheets.Add('Roster')
    $row = $script:BodyRow

    $headers = @('Name', 'Boss', 'Health', 'Actions', 'Brain', 'Targeting', 'Deck Size',
                 'Avg Damage', 'Avg Status', 'Avg Summon', 'Avg Card Power',
                 'Estimated', "Brandon's", 'Delta', 'Flag')
    for ($c = 0; $c -lt $headers.Count; $c++) { $ws.Cells[1, ($c + 1)].Value = $headers[$c] }
    $ws.Cells['A1:O1'].Style.Font.Bold = $true

    $ordered = @($Bodies | Sort-Object { $_.Prefab })
    for ($i = 0; $i -lt $ordered.Count; $i++) {
        $b = $ordered[$i]
        $r = $i + 2
        $name = "'$($b.Prefab)'"

        $ws.Cells[$r, 1].Value = $b.Prefab
        $ws.Cells[$r, 2].Value = if ($b.Boss) { 'Yes' } else { 'No' }
        $ws.Cells[$r, 3].Formula = "$name!B$($row.Health)"
        $ws.Cells[$r, 4].Formula = "$name!B$($row.Actions)"
        $ws.Cells[$r, 5].Formula = "$name!B$($row.Brain)"
        $ws.Cells[$r, 6].Formula = "$name!B$($row.Targeting)"
        $ws.Cells[$r, 7].Value = @($b.Deck).Count
        $ws.Cells[$r, 8].Formula = "$name!B$($row.AvgDamage)"
        $ws.Cells[$r, 9].Formula = "$name!B$($row.AvgStatus)"
        $ws.Cells[$r, 10].Formula = "$name!B$($row.AvgSummon)"
        $ws.Cells[$r, 11].Formula = "$name!B$($row.AvgCardPower)"
        $ws.Cells[$r, 12].Formula = "$name!B$($row.Estimated)"
        $ws.Cells[$r, 13].Formula = "$name!B$($row.Brandon)"
        $ws.Cells[$r, 14].Formula = "$name!B$($row.Delta)"
        $ws.Cells[$r, 15].Formula =
            "IF(N$r=`"`",`"`",IF(ABS(N$r)<=L$r*pw_Tolerance,`"OK`",IF(N$r>0,`"OVER`",`"UNDER`")))"
    }

    $last = $ordered.Count + 1
    if ($ordered.Count -gt 0) {
        $ws.Cells["H2:N$last"].Style.Numberformat.Format = '0.00'

        $verdicts = @(
            @('OVER',  @(255, 199, 206), @(156, 0, 6)),
            @('UNDER', @(255, 235, 156), @(156, 101, 0)),
            @('OK',    @(198, 239, 206), @(0, 97, 0))
        )
        foreach ($v in $verdicts) {
            $fmt = $ws.ConditionalFormatting.AddEqual($ws.Cells["O2:O$last"])
            $fmt.Formula = '"' + $v[0] + '"'
            $fmt.Style.Fill.BackgroundColor.Color = [System.Drawing.Color]::FromArgb($v[1][0], $v[1][1], $v[1][2])
            $fmt.Style.Font.Color.Color = [System.Drawing.Color]::FromArgb($v[2][0], $v[2][1], $v[2][2])
        }

        $ws.Cells["A1:O$last"].AutoFilter = $true
    }

    $ws.Column(1).Width = 22
    $ws.Column(6).Width = 18
    $ws.View.FreezePanes(2, 2)
}

# ---------------------------------------------------------------------------------------------------
# Enums - dropdown sources
# ---------------------------------------------------------------------------------------------------

function Add-EnumsSheet {
    param($Package, [string[]]$EnemyCardNames, [string[]]$LootNames, $Bodies, $Totems)

    $ws = $Package.Workbook.Worksheets.Add('Enums')

    $lists = [ordered]@{
        list_Brain     = @('None', 'Warrior', 'Ranger', 'Summoner')
        list_Targeting = @('') + @('Closest', 'Furthest', 'Random', 'RangerTargeting', 'Strongest', 'WarriorTargeting', 'Weakest')
        list_Loot      = @('') + @($LootNames | Sort-Object -Unique)
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
    param($Package)

    $ws = $Package.Workbook.Worksheets.Add('README')

    $lines = @(
        @('Enemy Design Workbook', 'title'),
        @('', ''),
        @('One tab per enemy, boss, ally and totem, all built from the actual prefabs and cards under Assets/Prefabs and Assets/Data/CardData/Enemy - this workbook only READS them today.', ''),
        @('', ''),
        @('The tabs', 'head'),
        @('PowerLevel', 'The tuning surface. Every pw_* weight here is a named cell - change one and every body tab and Roster recalculate immediately, no re-export needed.'),
        @('One tab per body', 'Health, Actions Per Turn, Brain, Targeting, Loot Table and Deck mirror the prefab''s Character component. Card Facts below the deck strip are read from each card''s own CardData/CardEffect assets - edit a card''s numbers on Docs/CardDesign.xlsx, not here.'),
        @('One tab per totem', 'A totem has no health or deck - Totem Power is its auras and reactions, weighted the same way a body''s Card Power is.'),
        @('Roster', 'Every body side by side, sorted by name, so you can see how they stack up. Fully formula-driven off the individual tabs.'),
        @('Enums', 'Dropdown sources for Brain, Targeting, Loot Table and Deck cells.'),
        @('', ''),
        @('Reading a body tab', 'head'),
        @('Brandon''s Power Level', 'Hand-authored ground truth - type in what a body feels like it should be worth. Preserved across re-exports.'),
        @('Estimated Power Level', '(Average Card Power * Actions Per Turn * pw_ActionScale + Health * pw_Health) / pw_Divisor. Tune the PowerLevel weights until Delta sits near zero for the bodies you already trust, then trust the estimate for the rest.'),
        @('Counted', 'No for a Move-only card or a Cooldown card - excluded from every average. Cooldown cards get scored separately later.'),
        @('Summon Power', 'A live reference to the summoned body''s (or totem''s) own power cell, scaled down if its lifetime is shorter than pw_SummonHorizon.'),
        @('', ''),
        @('Syncing back to Unity', 'head'),
        @('One button', 'In Unity: Tools > Sync Enemies With Sheet. Applies your edits to Health, Actions Per Turn, Brain, Targeting, Loot Table and Deck to the matching prefab, then refreshes this workbook so anything changed in the Inspector shows up here too.'),
        @('Deck', 'One card name per column, left to right, no gaps - the first blank cell ends the deck. Duplicates are fine and expected (four Enemy Slash cards is four cells). Pick names from the dropdown so a typo cannot silently drop a card.'),
        @('Brandon''s Power Level and Notes', 'Never sync anywhere - they are authored HERE and preserved across every re-export.'),
        @('Card Facts, Card Power, averages, Roster', 'Never hand-edit - fully derived from the prefab and its cards, rebuilt on every export.'),
        @('If both sides changed', 'That body is left alone on BOTH sides and named in the Unity console, the same conflict rule Docs/CardDesign.xlsx follows. Make them agree, or change only one, then sync again.'),
        @('Regenerating without syncing', 'Tools > Enemies > Refresh Sheet From Unity discards any sheet edit that has not been synced yet - only for when the sheet is known to be wrong.')
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
    param($Package, $Bodies, $Totems)

    $order = @('README', 'PowerLevel', 'Roster') + @($Bodies | Sort-Object Prefab | ForEach-Object { $_.Prefab }) +
             @($Totems | Sort-Object Prefab | ForEach-Object { $_.Prefab }) + @('Enums')

    foreach ($name in $order) {
        $ws = $Package.Workbook.Worksheets[$name]
        if ($null -ne $ws) { $Package.Workbook.Worksheets.MoveToEnd($name) }
    }

    $Package.Workbook.Worksheets['README'].Select()
}
