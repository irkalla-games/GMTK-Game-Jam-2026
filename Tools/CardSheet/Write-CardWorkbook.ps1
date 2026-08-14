<#
.SYNOPSIS
    Turns the gathered sheet data into the formatted workbook. Dot-sourced by Export-CardSheet.ps1.

.DESCRIPTION
    Split out from the export purely for size: Export-CardSheet decides *what* the rows are, this
    decides what the workbook looks like.

    The split that matters is inside the Balance sheet. Facts parsed out of the assets (how much damage,
    how many tiles) are written as static values; the opinions (what a point of shield is worth) are
    Excel formulas pointing at named cells on the Model tab. Retuning a weight therefore recalculates
    every card in place, with no need to re-run this script - which is the whole reason the balance
    model lives in the workbook rather than in PowerShell.
#>

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

function Write-CardWorkbook {
    param(
        [string]$WorkbookPath,
        $Sheets,
        [string]$RepoRoot
    )

    $dir = Split-Path -Parent $WorkbookPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    # Rebuilt from scratch every run - everything the human authored was already read back into $Sheets.
    if (Test-Path -LiteralPath $WorkbookPath) { Remove-Item -LiteralPath $WorkbookPath -Force }

    Write-Host "Writing workbook..." -ForegroundColor Cyan

    $pkg = $null

    foreach ($name in $Sheets.Keys) {
        $rows = @($Sheets[$name])

        if ($rows.Count -eq 0) {
            # Export-Excel skips an empty collection, so lay the headers down by hand and keep the tab.
            if ($null -eq $pkg) { $pkg = Open-ExcelPackage -Path $WorkbookPath -Create }
            $ws = $pkg.Workbook.Worksheets.Add($name)
            $cols = if ($name -like '*Ideas') { $script:IdeaColumns } else { $script:CardColumns }
            # Parentheses are load-bearing: PowerShell binds the comma tighter than the plus, so
            # Cells[1, $c + 1] would index with the three-element array (1, $c) + 1.
            for ($c = 0; $c -lt $cols.Count; $c++) { $ws.Cells[1, ($c + 1)].Value = $cols[$c] }
            continue
        }

        $params = @{
            Path          = $WorkbookPath
            WorksheetName = $name
            AutoSize      = $true
            FreezeTopRow  = $true
            PassThru      = $true
        }
        if ($null -ne $pkg) { Close-ExcelPackage $pkg -NoSave:$false }
        $pkg = $rows | Export-Excel @params
    }

    if ($null -eq $pkg) { $pkg = Open-ExcelPackage -Path $WorkbookPath -Create }

    Set-GlossarySheet -Package $pkg
    Add-ModelSheet   -Package $pkg
    Add-EnumsSheet   -Package $pkg -Sheets $Sheets
    Add-ReadmeSheet  -Package $pkg
    Set-BalanceSheet -Package $pkg -RowCount (@($Sheets['Balance']).Count)
    Set-CardSheets   -Package $pkg -Sheets $Sheets
    Set-SheetOrder   -Package $pkg

    # The derived columns ship as formulas with no cached value - EPPlus 4.5.3 as bundled with
    # ImportExcel exposes no Calculate(). Excel evaluates them on open, and nothing in the round trip
    # depends on them: Import-CardSheet reads the authoring columns, which are all static values.
    Close-ExcelPackage $pkg
}

# ---------------------------------------------------------------------------------------------------
# Model
# ---------------------------------------------------------------------------------------------------

function Set-GlossarySheet {
    <#
        Tooltip wording. Body is the column that matters, so it gets the room - and a reminder that the
        {token} placeholders are load-bearing, since deleting one is an easy and silent mistake.
    #>
    param($Package)

    $ws = $Package.Workbook.Worksheets['Glossary']
    if ($null -eq $ws) { return }

    $last = $ws.Dimension.End.Row

    $ws.Cells["A1:G1"].Style.Font.Bold = $true
    $ws.Cells["A1:G$last"].AutoFilter = $true
    $ws.View.FreezePanes(2, 3)

    $ws.Column(1).Width = 10   # Kind
    $ws.Column(2).Width = 20   # Type
    $ws.Column(3).Width = 20   # Title
    $ws.Column(4).Width = 78   # Body
    $ws.Column(4).Style.WrapText = $true
    $ws.Column(5).Width = 34   # Terms
    $ws.Column(5).Style.WrapText = $true
    $ws.Column(6).Width = 15
    $ws.Column(7).Width = 15

    # Flag any body that has lost its token - the tooltip would then show a fixed number that silently
    # disagrees with the stacks the player actually has.
    $fmt = $ws.ConditionalFormatting.AddExpression($ws.Cells["D2:D$last"])
    $fmt.Formula = "AND(LEN(D2)>0,ISERROR(FIND(`"{`",D2)))"
    $fmt.Style.Fill.BackgroundColor.Color = [System.Drawing.Color]::FromArgb(255, 242, 204)

    # Notes go to the right of the table, never underneath it. Import-Excel reads to the last non-empty
    # row, so a note below the data comes back as a row with a blank Kind and looks like a half-authored
    # entry.
    $ws.Cells['I1'].Value = 'Edit Title, Body and Terms here, then run Import-CardSheet.ps1 and sync in Unity.'
    $ws.Cells['I1'].Style.Font.Bold = $true
    $ws.Cells['I2'].Value =
        'Body keeps its {stacks} / {amount} / {magnitude} placeholders - they are filled from the character''s live status when the tooltip is drawn, so one sentence reads correctly for Parry 2 and Parry 7 alike. A shaded Body cell has no placeholder left in it.'
    $ws.Cells['I3'].Value =
        'Terms are the words that become hoverable inside a card description, comma separated. Leave blank to use the Type name itself. Add variants the name does not cover - "Poisoned" for Poison.'
    $ws.Cells['I4'].Value =
        'A row with a blank Title and Body has no tooltip authored yet. Fill both in and sync to create one - the importer ignores rows left blank.'
    $ws.Cells['I1:I4'].Style.WrapText = $true
    $ws.Cells['I2:I4'].Style.Font.Italic = $true
    $ws.Column(9).Width = 70
}

function Add-ModelSheet {
    param($Package)

    if ($Package.Workbook.Worksheets['Model']) { $Package.Workbook.Worksheets.Delete('Model') }
    $ws = $Package.Workbook.Worksheets.Add('Model')

    $ws.Cells['A1'].Value = 'Term'
    $ws.Cells['B1'].Value = 'Value'
    $ws.Cells['C1'].Value = 'Why this number'

    # name, value, note
    $terms = @(
        @('w_Damage',    1.00, 'The numeraire. One point of damage to an enemy = 1 power.'),
        @('w_Heal',      0.80, 'Only useful when already damaged, so worth less than the same damage.'),
        @('w_Shield',    0.70, 'ShieldStatus is wiped on OnTurnStart - it must be spent this round.'),
        @('w_Block',     0.90, 'Charges survive turn start, so a point prevented is worth more than shield.'),
        @('w_Parry',     5.00, 'Per charge. Negates the hit AND reflects it: prevention plus damage.'),
        @('w_Poison',    1.20, 'Per point of lifetime DoT. Unblockable, but arrives slowly.'),
        @('w_Strength',  1.00, 'Per stack per future attack. Multiplied by w_Horizon.'),
        @('w_Weaken',    1.00, 'Per stack per future enemy attack. Symmetric with Strength.'),
        @('w_Draw',      3.00, 'Per card. Standard deckbuilder rate for raw card advantage.'),
        @('w_Move',      3.00, 'Repositioning on a grid where range gates every card - worth more than it looks.'),
        @('w_Combo',     6.00, 'Per stack of Double Next Attack / Double Shield - roughly one extra average payload.'),
        @('w_OccEnemy',  0.50, 'Share of extra AoE tiles expected to actually hold an enemy.'),
        @('w_OccAlly',   0.25, 'Same for allies - a party is smaller and clumps less than a wave.'),
        @('w_MaxEnemies', 4.0, 'Hard cap on how many enemies an area can realistically catch. Without it a 25-tile blast scores 13x and every AoE card reads OVER.'),
        @('w_MaxAllies',  3.0, 'Same cap for friendly areas - the party is small.'),
        @('w_Horizon',   3.00, 'How many future attacks a Strength/Weaken stack is assumed to affect.'),
        @('curve_Base',  2.00, 'Power a 0-cost card is expected to deliver. Fitted to the cards as authored today, not chosen a priori.'),
        @('curve_Slope', 6.00, 'Extra expected power per point of energy. Slash (1 energy, 9 damage) sits on the line by construction.'),
        @('tol_Pct',     0.25, 'How far off the curve a card may sit before it is flagged OVER/UNDER.'),
        @('board_Tiles', 64,   'Tile count used when an area is RangeShape.Anywhere.')
    )

    for ($i = 0; $i -lt $terms.Count; $i++) {
        $r = $i + 2
        $ws.Cells[$r, 1].Value = $terms[$i][0]
        $ws.Cells[$r, 2].Value = [double]$terms[$i][1]
        $ws.Cells[$r, 3].Value = $terms[$i][2]
        Add-NamedCell -Package $Package -Worksheet $ws -Name $terms[$i][0] -Address "B$r"
    }

    $rarityStart = $terms.Count + 4
    $ws.Cells[($rarityStart - 1), 1].Value = 'Rarity multiplier'
    $ws.Cells[($rarityStart - 1), 1].Style.Font.Bold = $true
    $ws.Cells[$rarityStart, 1].Value = 'Rarity'
    $ws.Cells[$rarityStart, 2].Value = 'Multiplier'

    $rarities = @(@('Common', 1.00), @('Uncommon', 1.15), @('Rare', 1.35), @('Legendary', 1.60), @('NotOffered', 1.00))
    for ($i = 0; $i -lt $rarities.Count; $i++) {
        $r = $rarityStart + 1 + $i
        $ws.Cells[$r, 1].Value = $rarities[$i][0]
        $ws.Cells[$r, 2].Value = [double]$rarities[$i][1]

    }
    Add-NamedCell -Package $Package -Worksheet $ws -Name 'rarity_Mult' -Address "A$($rarityStart + 1):B$($rarityStart + $rarities.Count)"

    $ws.Cells['A1:C1'].Style.Font.Bold = $true
    $ws.Cells["A$rarityStart`:B$rarityStart"].Style.Font.Bold = $true
    $ws.Cells['B2:B100'].Style.Numberformat.Format = '0.00'
    $ws.Column(1).Width = 16
    $ws.Column(2).Width = 11
    $ws.Column(3).Width = 82
    $ws.Column(3).Style.WrapText = $true

    $note = $rarityStart + $rarities.Count + 2
    $ws.Cells[$note, 1].Value = 'Edit the Value column and every figure on Balance recalculates. Nothing here is baked into the export script.'
    $ws.Cells[$note, 1].Style.Font.Italic = $true
    $ws.Cells[($note + 1), 1].Value = 'One exception: a summoned totem''s output is pre-multiplied by 3 expected activations in Export-CardSheet.ps1 ($TotemActivations) before it reaches these columns. Change it there, not here.'
    $ws.Cells[($note + 1), 1].Style.Font.Italic = $true
}

# ---------------------------------------------------------------------------------------------------
# Enums - the dropdown sources
# ---------------------------------------------------------------------------------------------------

function Add-EnumsSheet {
    param($Package, $Sheets)

    if ($Package.Workbook.Worksheets['Enums']) { $Package.Workbook.Worksheets.Delete('Enums') }
    $ws = $Package.Workbook.Worksheets.Add('Enums')

    $effectNames = @($Sheets['Effects'] | Where-Object { $_.Class -ne 'EffectPattern' } | ForEach-Object { $_.Name } | Sort-Object -Unique)

    # The canonical folder set, offered whether or not a card currently lives in each one - otherwise a
    # class with no heal cards can never be given one from the sheet. Observed folders are unioned in
    # so nothing already on disk is unpickable.
    $canonicalFolders = @(
        'Melee Attack', 'RangedAttack', 'Buff (Defensive)', 'Buff (Offensive)',
        'Debuff', 'Heal', 'Movement', 'Summon'
    )

    # Legacy spellings are deliberately excluded even while assets still sit in them, so a new row
    # cannot re-introduce the split. Tools > Cards > Normalise Card Folders migrates the stragglers.
    $legacyFolders = @('Defensive Buff', 'Offensive Buffs', 'Defensive Buffs', 'Offensive Buff')

    $folders = @()
    foreach ($tab in @('Knight', 'Mage', 'Rogue', 'Neutral')) {
        $folders += @($Sheets[$tab] | ForEach-Object { $_.Folder })
    }
    $folders = @($canonicalFolders + $folders |
        Where-Object { $_ -and ($legacyFolders -notcontains $_) } |
        Sort-Object -Unique)

    $lists = [ordered]@{
        list_Class     = @('Any', 'Knight', 'Mage', 'Rogue', 'Knight+Mage', 'Knight+Rogue', 'Mage+Rogue', 'Knight+Mage+Rogue')
        list_Rarity    = @('Common', 'Uncommon', 'Rare', 'Legendary', 'NotOffered')
        list_Shape     = @('Anywhere', 'Chebyshev', 'Manhattan', 'SelfTile')
        list_Aim       = @('Tile', 'Self')
        list_Bool      = @('TRUE', 'FALSE')
        list_Buildable = @('Yes', 'Needs Effect', 'Needs Status', 'Needs Hook', 'Needs System')
        list_Priority  = @('High', 'Med', 'Low')
        list_Tag       = @('Attack', 'Defence', 'Movement', 'Poison', 'Fire', 'Summon', 'Healing')
        list_Keyword   = @('Innate', 'Cooldown', 'Dormant')
        list_Effect    = $effectNames
        list_Folder    = $folders
    }

    $col = 1
    foreach ($name in $lists.Keys) {
        $values = @($lists[$name])
        $ws.Cells[1, $col].Value = $name -replace '^list_', ''
        $ws.Cells[1, $col].Style.Font.Bold = $true
        for ($i = 0; $i -lt $values.Count; $i++) {
            $ws.Cells[($i + 2), $col].Value = $values[$i]
        }
        if ($values.Count -gt 0) {
            $letter = Get-ExcelColumnName $col
            Add-NamedCell -Package $Package -Worksheet $ws -Name $name -Address "$letter`2:$letter$($values.Count + 1)"
        }
        $ws.Column($col).Width = 20
        $col++
    }

    $ws.Cells[1, $col].Value = 'These lists drive the dropdowns on the card tabs. Regenerated on every export - edit the C# enum, not this sheet.'
    $ws.Cells[1, $col].Style.Font.Italic = $true
}

# ---------------------------------------------------------------------------------------------------
# Balance - formulas and charts
# ---------------------------------------------------------------------------------------------------

function Set-BalanceSheet {
    param($Package, [int]$RowCount)

    $ws = $Package.Workbook.Worksheets['Balance']
    if ($null -eq $ws -or $RowCount -lt 1) { return }

    $last = $RowCount + 1   # header on row 1

    # Column map, kept here because every formula below depends on it:
    #   D Cost  E Rarity  G Enemy Tiles  H Ally Tiles  I Damage  J Heal  K Shield  L Block  M Parry
    #   N Poison  O Strength  P Weaken  Q Draw  R Move  S Combo  U Control  V Quantifiable
    #   W Power  X Power/Energy  Y Expected  Z Delta  AA Flag  AB Issues  AC..AJ chart helpers
    for ($r = 2; $r -le $last; $r++) {
        # Effective target counts: the aimed tile always counts, extra tiles only at the occupancy
        # rate, and the whole thing capped - area scales with the board, but the number of things
        # standing on it does not.
        $enemyMult = "MIN(1+(G$r-1)*w_OccEnemy,w_MaxEnemies)"
        $allyMult  = "MIN(1+(H$r-1)*w_OccAlly,w_MaxAllies)"

        $hostile = "(I$r*w_Damage + N$r*w_Poison + P$r*w_Weaken*w_Horizon)*$enemyMult"
        $friend  = "(J$r*w_Heal + K$r*w_Shield + L$r*w_Block + M$r*w_Parry + O$r*w_Strength*w_Horizon + S$r*w_Combo)*$allyMult"
        $flat    = "Q$r*w_Draw + R$r*w_Move"

        $ws.Cells[$r, 23].Formula = "IF(`$V$r=`"No`",`"`",ROUND($hostile + $friend + $flat,2))"                      # W Power
        $ws.Cells[$r, 24].Formula = "IF(W$r=`"`",`"`",ROUND(W$r/MAX(D$r,1),2))"                                      # X Power/Energy
        $ws.Cells[$r, 25].Formula = "IF(`$V$r=`"No`",`"`",ROUND((curve_Base+curve_Slope*D$r)*IFERROR(VLOOKUP(E$r,rarity_Mult,2,FALSE),1),2))"  # Y Expected
        $ws.Cells[$r, 26].Formula = "IF(OR(W$r=`"`",Y$r=`"`"),`"`",ROUND(W$r-Y$r,2))"                                # Z Delta
        # Partial cards carry an unscored rider, so their Power is a floor rather than a verdict -
        # flagging them OVER/UNDER would be reading a number that was never the whole card.
        $ws.Cells[$r, 27].Formula = "IF(Z$r=`"`",`"`",IF(`$V$r=`"Partial`",`"RIDER`",IF(ABS(Z$r)<=Y$r*tol_Pct,`"OK`",IF(Z$r>0,`"OVER`",`"UNDER`"))))" # AA Flag

        # Chart helpers. NA() rather than "" so a chart skips the point instead of plotting a zero.
        # Shipped cards and Ideas rows are separated here rather than in the charts, so a proposal
        # never quietly inflates the picture of what the game already has.
        $shipped = "NOT(ISNUMBER(SEARCH(`"Ideas`",`$F$r)))"

        $ws.Cells[$r, 29].Formula = "IF(AND(`$V$r<>`"No`",`$E$r=`"Common`",$shipped),`$W$r,NA())"
        $ws.Cells[$r, 30].Formula = "IF(AND(`$V$r<>`"No`",`$E$r=`"Uncommon`",$shipped),`$W$r,NA())"
        $ws.Cells[$r, 31].Formula = "IF(AND(`$V$r<>`"No`",`$E$r=`"Rare`",$shipped),`$W$r,NA())"
        $ws.Cells[$r, 32].Formula = "IF(AND(`$V$r<>`"No`",`$E$r=`"Legendary`",$shipped),`$W$r,NA())"
        $ws.Cells[$r, 33].Formula = "IF(AND(`$V$r<>`"No`",ISNUMBER(SEARCH(`"Knight`",`$C$r)),$shipped),`$X$r,NA())"
        $ws.Cells[$r, 34].Formula = "IF(AND(`$V$r<>`"No`",ISNUMBER(SEARCH(`"Mage`",`$C$r)),$shipped),`$X$r,NA())"
        $ws.Cells[$r, 35].Formula = "IF(AND(`$V$r<>`"No`",ISNUMBER(SEARCH(`"Rogue`",`$C$r)),$shipped),`$X$r,NA())"
        $ws.Cells[$r, 36].Formula = "IF(`$V$r<>`"No`",`$Y$r,NA())"
        $ws.Cells[$r, 37].Formula = "IF(AND(`$V$r<>`"No`",ISNUMBER(SEARCH(`"Ideas`",`$F$r))),`$W$r,NA())"
    }

    $ws.Cells["W2:Z$last"].Style.Numberformat.Format = '0.00'

    # Colour the verdict rather than making the reader compare two numbers.
    $verdicts = @(
        @('OVER',  @(255, 199, 206), @(156,  0,  6)),
        @('UNDER', @(255, 235, 156), @(156, 101, 0)),
        @('OK',    @(198, 239, 206), @(  0, 97,  0)),
        @('RIDER', @(221, 235, 247), @( 31, 78, 121))
    )
    foreach ($v in $verdicts) {
        $fmt = $ws.ConditionalFormatting.AddEqual($ws.Cells["AA2:AA$last"])
        $fmt.Formula = '"' + $v[0] + '"'
        $fmt.Style.Fill.BackgroundColor.Color = [System.Drawing.Color]::FromArgb($v[1][0], $v[1][1], $v[1][2])
        $fmt.Style.Font.Color.Color = [System.Drawing.Color]::FromArgb($v[2][0], $v[2][1], $v[2][2])
    }

    $fmtIssue = $ws.ConditionalFormatting.AddContainsText($ws.Cells["AB2:AB$last"])
    $fmtIssue.Text = 'blank'
    $fmtIssue.Style.Fill.BackgroundColor.Color = [System.Drawing.Color]::FromArgb(255, 235, 156)

    $fmtBad = $ws.ConditionalFormatting.AddContainsText($ws.Cells["AB2:AB$last"])
    $fmtBad.Text = 'no effects'
    $fmtBad.Style.Fill.BackgroundColor.Color = [System.Drawing.Color]::FromArgb(255, 199, 206)

    $ws.View.FreezePanes(2, 3)
    $ws.Cells["A1:AK1"].Style.Font.Bold = $true
    $ws.Cells["A1:AK$last"].AutoFilter = $true

    # Summary tables and charts live on their own sheet. They used to sit off to the right of this one,
    # which put a second "Cost"/"Knight" header on row 1 and made the sheet unreadable to Import-Excel -
    # the round-trip depends on Balance being a plain rectangular table.
    Add-ChartsSheet -Package $Package -Last $last
}

function Add-ChartsSheet {
    param($Package, [int]$Last)

    if ($Package.Workbook.Worksheets['Charts']) { $Package.Workbook.Worksheets.Delete('Charts') }
    $ws = $Package.Workbook.Worksheets.Add('Charts')

    $B = 'Balance!'

    # These two tables answer "what does the game have TODAY", so they count shipped cards only -
    # Source is the tab a row came from, and anything ending in "Ideas" is a proposal, not content.
    $notIdeas = "$B`$F`$2:`$F`$$Last,`"<>*Ideas*`""

    # --- Cost histogram source, A1:D7. COUNTIFS on a wildcard so "Knight+Rogue" counts for both. ---
    $ws.Cells['A1'].Value = 'Cost'
    $ws.Cells['B1'].Value = 'Knight'
    $ws.Cells['C1'].Value = 'Mage'
    $ws.Cells['D1'].Value = 'Rogue'
    for ($c = 0; $c -le 5; $c++) {
        $r = $c + 2
        $ws.Cells[$r, 1].Value = $c
        $ws.Cells[$r, 2].Formula = "COUNTIFS($B`$D`$2:`$D`$$Last,`$A$r,$B`$C`$2:`$C`$$Last,`"*Knight*`",$notIdeas)"
        $ws.Cells[$r, 3].Formula = "COUNTIFS($B`$D`$2:`$D`$$Last,`$A$r,$B`$C`$2:`$C`$$Last,`"*Mage*`",$notIdeas)"
        $ws.Cells[$r, 4].Formula = "COUNTIFS($B`$D`$2:`$D`$$Last,`$A$r,$B`$C`$2:`$C`$$Last,`"*Rogue*`",$notIdeas)"
    }

    # --- Coverage source, F1:I12. One row per effect kind, keyed to the Balance column that holds it. ---
    $kinds = @(
        @('Damage', 'I'), @('Heal',   'J'), @('Shield', 'K'), @('Block',  'L'),
        @('Parry',  'M'), @('Poison', 'N'), @('Buff',   'O'), @('Weaken', 'P'),
        @('Draw',   'Q'), @('Move',   'R'), @('Combo',  'S')
    )

    $ws.Cells['F1'].Value = 'Effect'
    $ws.Cells['G1'].Value = 'Knight'
    $ws.Cells['H1'].Value = 'Mage'
    $ws.Cells['I1'].Value = 'Rogue'
    for ($i = 0; $i -lt $kinds.Count; $i++) {
        $r = $i + 2
        $col = $kinds[$i][1]
        $ws.Cells[$r, 6].Value = $kinds[$i][0]
        $ws.Cells[$r, 7].Formula = "COUNTIFS($B`$$col`$2:`$$col`$$Last,`">0`",$B`$C`$2:`$C`$$Last,`"*Knight*`",$notIdeas)"
        $ws.Cells[$r, 8].Formula = "COUNTIFS($B`$$col`$2:`$$col`$$Last,`">0`",$B`$C`$2:`$C`$$Last,`"*Mage*`",$notIdeas)"
        $ws.Cells[$r, 9].Formula = "COUNTIFS($B`$$col`$2:`$$col`$$Last,`">0`",$B`$C`$2:`$C`$$Last,`"*Rogue*`",$notIdeas)"
    }
    $r = $kinds.Count + 2
    $ws.Cells[$r, 6].Value = 'Control'
    $ws.Cells[$r, 7].Formula = "COUNTIFS($B`$U`$2:`$U`$$Last,`"?*`",$B`$C`$2:`$C`$$Last,`"*Knight*`",$notIdeas)"
    $ws.Cells[$r, 8].Formula = "COUNTIFS($B`$U`$2:`$U`$$Last,`"?*`",$B`$C`$2:`$C`$$Last,`"*Mage*`",$notIdeas)"
    $ws.Cells[$r, 9].Formula = "COUNTIFS($B`$U`$2:`$U`$$Last,`"?*`",$B`$C`$2:`$C`$$Last,`"*Rogue*`",$notIdeas)"
    $r++
    $ws.Cells[$r, 6].Value = 'Summon'
    $ws.Cells[$r, 7].Formula = "COUNTIFS($B`$T`$2:`$T`$$Last,`"?*`",$B`$C`$2:`$C`$$Last,`"*Knight*`",$notIdeas)"
    $ws.Cells[$r, 8].Formula = "COUNTIFS($B`$T`$2:`$T`$$Last,`"?*`",$B`$C`$2:`$C`$$Last,`"*Mage*`",$notIdeas)"
    $ws.Cells[$r, 9].Formula = "COUNTIFS($B`$T`$2:`$T`$$Last,`"?*`",$B`$C`$2:`$C`$$Last,`"*Rogue*`",$notIdeas)"
    $covLast = $r

    $ws.Cells['A1:D1'].Style.Font.Bold = $true
    $ws.Cells['F1:I1'].Style.Font.Bold = $true

    $q   = "'Balance'!"
    $top = 15

    # 1. The power curve. Series are split by rarity via the NA()-padded helper columns on Balance,
    #    which is what lets one scatter carry four legends without four separate charts.
    $c1 = $ws.Drawings.AddChart('PowerCurve', [OfficeOpenXml.Drawing.Chart.eChartType]::XYScatter)
    $c1.Title.Text = 'Power curve - cost vs delivered power, by rarity (Proposed = Ideas tabs)'
    foreach ($s in @(@('AC', 'Common'), @('AD', 'Uncommon'), @('AE', 'Rare'), @('AF', 'Legendary'),
                     @('AK', 'Proposed'), @('AJ', 'Expected'))) {
        $serie = $c1.Series.Add("$q`$$($s[0])`$2:`$$($s[0])`$$Last", "$q`$D`$2:`$D`$$Last")
        $serie.Header = $s[1]
    }
    $c1.XAxis.Title.Text = 'Energy cost'
    $c1.YAxis.Title.Text = 'Power'
    $c1.SetPosition($top, 0, 0, 0)
    $c1.SetSize(680, 420)

    # 2. Efficiency by class.
    $c2 = $ws.Drawings.AddChart('Efficiency', [OfficeOpenXml.Drawing.Chart.eChartType]::XYScatter)
    $c2.Title.Text = 'Efficiency - power per energy, by class'
    foreach ($s in @(@('AG', 'Knight'), @('AH', 'Mage'), @('AI', 'Rogue'))) {
        $serie = $c2.Series.Add("$q`$$($s[0])`$2:`$$($s[0])`$$Last", "$q`$D`$2:`$D`$$Last")
        $serie.Header = $s[1]
    }
    $c2.XAxis.Title.Text = 'Energy cost'
    $c2.YAxis.Title.Text = 'Power per energy'
    $c2.SetPosition($top, 0, 12, 0)
    $c2.SetSize(680, 420)

    # 3. Cost histogram.
    $c3 = $ws.Drawings.AddChart('CostCurve', [OfficeOpenXml.Drawing.Chart.eChartType]::ColumnClustered)
    $c3.Title.Text = 'Cost distribution - how many cards at each price, per class'
    foreach ($s in @(@('B', 'Knight'), @('C', 'Mage'), @('D', 'Rogue'))) {
        $serie = $c3.Series.Add("'Charts'!`$$($s[0])`$2:`$$($s[0])`$7", "'Charts'!`$A`$2:`$A`$7")
        $serie.Header = $s[1]
    }
    $c3.XAxis.Title.Text = 'Energy cost'
    $c3.YAxis.Title.Text = 'Cards'
    $c3.SetPosition($top + 22, 0, 0, 0)
    $c3.SetSize(680, 420)

    # 4. Coverage - the chart that makes "Rogue has no heal" a picture rather than a hunch.
    $c4 = $ws.Drawings.AddChart('Coverage', [OfficeOpenXml.Drawing.Chart.eChartType]::ColumnClustered)
    $c4.Title.Text = 'Coverage - which effects each class actually has'
    foreach ($s in @(@('G', 'Knight'), @('H', 'Mage'), @('I', 'Rogue'))) {
        $serie = $c4.Series.Add("'Charts'!`$$($s[0])`$2:`$$($s[0])`$$covLast", "'Charts'!`$F`$2:`$F`$$covLast")
        $serie.Header = $s[1]
    }
    $c4.XAxis.Title.Text = 'Effect kind'
    $c4.YAxis.Title.Text = 'Cards'
    $c4.SetPosition($top + 22, 0, 12, 0)
    $c4.SetSize(680, 420)

    $ws.Cells['A9'].Value = 'Source tables for the charts below. Rebuilt on every export - do not hand-edit.'
    $ws.Cells['A9'].Style.Font.Italic = $true
}

# ---------------------------------------------------------------------------------------------------
# Card tabs - dropdowns and the Sync column
# ---------------------------------------------------------------------------------------------------

function Set-CardSheets {
    param($Package, $Sheets)

    $cardTabs = @('Knight', 'Mage', 'Rogue', 'Neutral', 'Knight Ideas', 'Mage Ideas', 'Rogue Ideas')

    foreach ($tab in $cardTabs) {
        $ws = $Package.Workbook.Worksheets[$tab]
        if ($null -eq $ws) { continue }

        $isIdeas = $tab -like '*Ideas'
        $cols    = if ($isIdeas) { $script:IdeaColumns } else { $script:CardColumns }
        $rows    = @($Sheets[$tab]).Count

        # Validate a generous window past the last row so new rows get the dropdowns too.
        $limit = [Math]::Max($rows + 1, 2) + 200

        $validations = @(
            @('Class',       'list_Class'),
            @('Folder',      'list_Folder'),
            @('Rarity',      'list_Rarity'),
            @('Range Shape', 'list_Shape'),
            @('Aim 1',       'list_Aim'),
            @('Aim 2',       'list_Aim'),
            @('Aim 3',       'list_Aim'),
            @('Effect 1',    'list_Effect'),
            @('Effect 2',    'list_Effect'),
            @('Effect 3',    'list_Effect'),
            @('No Reward',   'list_Bool')
        )
        if ($isIdeas) {
            $validations += ,@('Buildable', 'list_Buildable')
            $validations += ,@('Priority',  'list_Priority')
        }

        foreach ($v in $validations) {
            $idx = [array]::IndexOf($cols, $v[0])
            if ($idx -lt 0) { continue }
            $letter = Get-ExcelColumnName ($idx + 1)

            $dv = $ws.DataValidations.AddListValidation("$letter`2:$letter$limit")
            $dv.Formula.ExcelFormula = "=$($v[1])"
            $dv.ShowErrorMessage = $false   # a warning box on every paste would make bulk editing painful
            $dv.AllowBlank = $true
        }

        # Sync: blank GUID means the card does not exist as an asset yet.
        $guidIdx = [array]::IndexOf($cols, 'GUID')
        $syncIdx = [array]::IndexOf($cols, 'Sync')
        if ($guidIdx -ge 0 -and $syncIdx -ge 0 -and $rows -gt 0) {
            $g = Get-ExcelColumnName ($guidIdx + 1)
            $s = Get-ExcelColumnName ($syncIdx + 1)
            for ($r = 2; $r -le $rows + 1; $r++) {
                $ws.Cells[$r, ($syncIdx + 1)].Formula = "IF(`$$g$r=`"`",`"NEW`",`"Live`")"
            }

            $fmtNew = $ws.ConditionalFormatting.AddEqual($ws.Cells["$s`2:$s$($rows + 1)"])
            $fmtNew.Formula = '"NEW"'
            $fmtNew.Style.Fill.BackgroundColor.Color = [System.Drawing.Color]::FromArgb(189, 215, 238)
        }

        $lastCol = Get-ExcelColumnName $cols.Count
        $ws.Cells["A1:$lastCol`1"].Style.Font.Bold = $true
        if ($rows -gt 0) { $ws.Cells["A1:$lastCol$($rows + 1)"].AutoFilter = $true }
        $ws.View.FreezePanes(2, 3)

        # Description is the only column meant to hold a sentence.
        $descIdx = [array]::IndexOf($cols, 'Description')
        if ($descIdx -ge 0) {
            $ws.Column($descIdx + 1).Width = 52
            $ws.Column($descIdx + 1).Style.WrapText = $true
        }
        foreach ($wide in @('Notes', 'Engine Work')) {
            $i = [array]::IndexOf($cols, $wide)
            if ($i -ge 0) { $ws.Column($i + 1).Width = 46; $ws.Column($i + 1).Style.WrapText = $true }
        }
    }
}

# ---------------------------------------------------------------------------------------------------
# README
# ---------------------------------------------------------------------------------------------------

function Add-ReadmeSheet {
    param($Package)

    if ($Package.Workbook.Worksheets['README']) { $Package.Workbook.Worksheets.Delete('README') }
    $ws = $Package.Workbook.Worksheets.Add('README')

    $lines = @(
        @('Card Design Workbook', 'title'),
        @('', ''),
        @('Generated by Tools/CardSheet/Export-CardSheet.ps1. Re-run it any time - your Notes, the Ideas tabs and the Model weights are all preserved.', ''),
        @('', ''),
        @('The tabs', 'head'),
        @('Knight / Mage / Rogue / Neutral', 'THE REAL CARDS. These mirror the .asset files. Everything here syncs to Unity - add a row, change a Description, and it lands in the game.'),
        @('* Ideas', 'DESIGN SPACE ONLY. Never imported, whatever the Buildable column says. To build an idea, copy its row onto the matching class tab - the leading columns are identical, so it pastes straight across.'),
        @('Glossary', 'Tooltip wording for every status and keyword. Edit Title/Body/Terms and sync to reword what the player reads. Also syncs.'),
        @('Effects', 'The shared CardEffect assets under Assets/Data/EffectData. Used By shows how many cards reference each one.'),
        @('Decks', 'DeckData contents, one row per (deck, card) with a count.'),
        @('Model', 'The balance weights. EDIT THESE - every figure on Balance is a live formula reading this tab.'),
        @('Balance', 'One row per card with the derived stats. Never hand-edit; it is rebuilt on every export.'),
        @('Charts', 'The four balance charts and their source tables. Also rebuilt every export.'),
        @('Enums', 'Sources for the dropdowns. Mirrors the C# enums - change the enum, not this sheet.'),
        @('', ''),
        @('Adding a card', 'head'),
        @('1.', 'Add a row on a CLASS tab (Knight/Mage/Rogue/Neutral) and leave GUID blank. Sync will read NEW. Rows on an Ideas tab are never built.'),
        @('2.', 'Key is the asset filename. Folder decides which sub-folder it lands in.'),
        @('3.', 'Effect 1..3 take the NAME of an effect asset - see the Effects tab. "Damage 7" is fine even if it does not exist yet; the importer creates it.'),
        @('4.', 'Aim is Tile (the clicked tile) or Self (the caster). Area is Single, a radius like "Manhattan 0-2", or "Pattern:Cone3".'),
        @('5.', 'Save and close the workbook, then run Tools/CardSheet/Import-CardSheet.ps1.'),
        @('6.', 'Focus the Unity Editor and pick Tools > Cards > Sync From Sheet.'),
        @('', ''),
        @('What the columns mean', 'head'),
        @('Key', 'Asset filename without extension. The join key - stable even when Card Name changes.'),
        @('Card Name', 'The display name shown in game. Often differs from Key.'),
        @('Class', 'Which character may hold it. A mask, so Knight+Rogue is legal. Any means everyone.'),
        @('Cost', 'Energy. The X axis of the power curve.'),
        @('Rarity', 'How good it is as a reward. NotOffered keeps it out of every reward pool.'),
        @('Range Shape/Min/Max', 'Which tiles may be clicked, measured from the caster. Chebyshev is a square, Manhattan a diamond.'),
        @('Area N', 'The footprint each effect covers around its aim tile. Single is one tile.'),
        @('GUID', 'Unity asset id. Blank means the card does not exist yet - this is what the importer diffs on.'),
        @('Buildable', 'Ideas tabs only, and purely informational now: Yes means the current engine could build it, anything else names what is missing. It does NOT gate anything - Ideas rows are never imported at all.'),
        @('Quantifiable', 'Balance tab. Yes = fully scored. Partial = has a numeric core plus an unscored rider (Ice Shard damages AND freezes) - plotted, but Flag reads RIDER because the number is a floor. No = nothing numeric at all, kept off the curve entirely.'),
        @('', ''),
        @('Rewording cards and tooltips', 'head'),
        @('Card text', 'Edit Description on a class tab, then import and sync. The change is written straight to the .asset.'),
        @('Tooltips', 'Edit Title/Body/Terms on the Glossary tab the same way. Keep the {stacks} / {amount} / {magnitude} placeholders - they are filled from the live status, so removing one freezes the number at whatever you typed.'),
        @('', ''),
        @('Rules that are easy to trip over', 'head'),
        @('Never reorder an enum', 'Enum ints are written into .asset files. Appending is safe; reordering silently rewrites every card that used the old value.'),
        @('AllCards.asset is automatic', 'CardLibraryEditor rescans on every CardData import. Never add cards to it by hand.'),
        @('The importer never deletes', 'A card present as an asset but missing from the sheet is reported, not removed.')
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

    $ws.Column(1).Width = 30
    $ws.Column(2).Width = 120
    $ws.Column(2).Style.WrapText = $true
}

function Set-SheetOrder {
    param($Package)

    $order = @('README', 'Knight', 'Mage', 'Rogue', 'Neutral',
               'Knight Ideas', 'Mage Ideas', 'Rogue Ideas',
               'Glossary', 'Effects', 'Decks', 'Model', 'Balance', 'Charts', 'Enums')

    foreach ($name in $order) {
        $ws = $Package.Workbook.Worksheets[$name]
        if ($null -ne $ws) { $Package.Workbook.Worksheets.MoveToEnd($name) }
    }

    $Package.Workbook.Worksheets['README'].Select()
}
