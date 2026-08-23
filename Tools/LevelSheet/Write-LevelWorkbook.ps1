<#
.SYNOPSIS
    Turns the gathered LevelData/RunData records into the formatted workbook. Dot-sourced by
    Export-LevelSheet.ps1.

.DESCRIPTION
    One tab per level: a labelled scalars block (found by column-A label, never by row number - same
    rule the enemy sheet's body tabs follow), then a Waves table where the opening roster ("Start") and
    every wave turn are a row PAIR - a dropdown-picked enemy name per column, with its "Col,Row" cell
    directly below it - then a single Party row in the same format, then a Deck Overrides table for the
    one thing a row cannot hold (a per-placement card list).

    The Board beneath all of that is pure OUTPUT: a fixed 10x10 grid whose cells are formulas, driven by
    a "Show:" dropdown that picks which row pair to re-read (INDEX/MATCH/OFFSET against a small static
    helper table at the bottom of the tab). Editing happens in the rows; the grid exists to see the
    result, not to type into.
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

function Write-LevelWorkbook {
    param(
        [string]$WorkbookPath,
        $Levels,           # array of Read-LevelAsset results
        $Runs,             # array of Read-RunAsset results
        [string[]]$PrefabNames,
        [string[]]$LootNames,
        $PreservedNotes,   # hashtable: level name -> Notes text
        [string]$RepoRoot
    )

    $dir = Split-Path -Parent $WorkbookPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    if (Test-Path -LiteralPath $WorkbookPath) { Remove-Item -LiteralPath $WorkbookPath -Force }

    Write-Host 'Writing workbook...' -ForegroundColor Cyan

    $pkg = Open-ExcelPackage -Path $WorkbookPath -Create

    Add-RunsSheet -Package $pkg -Runs $Runs
    Add-EnumsSheet -Package $pkg -PrefabNames $PrefabNames -LootNames $LootNames -Levels $Levels

    foreach ($level in $Levels) {
        Add-LevelSheet -Package $pkg -Level $level -Notes $(if ($PreservedNotes.ContainsKey($level.Name)) { $PreservedNotes[$level.Name] } else { '' })
    }

    Add-ReadmeSheet -Package $pkg

    Set-SheetOrder -Package $pkg -Levels $Levels

    Close-ExcelPackage $pkg
}

# ---------------------------------------------------------------------------------------------------
# Runs - a flat table, one row per RunData
# ---------------------------------------------------------------------------------------------------

function Add-RunsSheet {
    param($Package, $Runs)

    $ws = $Package.Workbook.Worksheets.Add('Runs')
    $cols = @(Get-RunColumns)

    for ($c = 0; $c -lt $cols.Count; $c++) { $ws.Cells[1, ($c + 1)].Value = $cols[$c] }
    $lastLetter = Get-ExcelColumnName $cols.Count
    $ws.Cells["A1:$lastLetter`1"].Style.Font.Bold = $true

    for ($i = 0; $i -lt $Runs.Count; $i++) {
        $r = $i + 2
        $run = $Runs[$i]
        $ws.Cells[$r, 1].Value = $run.Name
        $ws.Cells[$r, 2].Value = ($run.Levels -join '; ')
        $ws.Cells[$r, 3].Value = if ($run.CarryDamageBetweenLevels) { 'TRUE' } else { 'FALSE' }
        $ws.Cells[$r, 4].Value = $run.Guid
        $ws.Cells[$r, 5].Formula = "IF(`$D$r=`"`",`"NEW`",`"Live`")"
    }

    if ($Runs.Count -gt 0) {
        $last = $Runs.Count + 1
        $ws.Cells["A1:$lastLetter$last"].AutoFilter = $true
    }

    $ws.Column(1).Width = 18
    $ws.Column(2).Width = 40
    $ws.Column(2).Style.WrapText = $true
    $ws.View.FreezePanes(2, 1)
}

# ---------------------------------------------------------------------------------------------------
# One tab per level
# ---------------------------------------------------------------------------------------------------

function Format-DeckList {
    param([string[]]$Names)
    if ($Names.Count -eq 0) { return '' }
    return ($Names -join '; ')
}

function Add-LevelSheet {
    <#
        Row-based editing: every wave (and the opening roster, as turn "Start") is a pair of rows - one
        enemy name per column, dropdown-picked, with its "Col,Row" coordinate directly below it. The
        board further down is pure OUTPUT, a 10x10 reference grid with a "Show:" dropdown that re-reads
        whichever wave-pair is selected via formulas against a small static helper table at the bottom of
        the tab - editing happens in the rows, never in the grid.
    #>
    param($Package, $Level, [string]$Notes)

    $ws = $Package.Workbook.Worksheets.Add($Level.Name)

    $ws.Cells[1, 1].Value = 'Level Name'
    $ws.Cells[1, 2].Value = $Level.Name
    $ws.Cells[2, 1].Value = 'GUID'
    $ws.Cells[2, 2].Value = $Level.Guid
    $ws.Cells[3, 1].Value = 'Sync'
    $ws.Cells[3, 2].Formula = 'IF($B$2="","NEW","Live")'
    $ws.Cells['A1:A3'].Style.Font.Bold = $true

    $ws.Cells[4, 1].Value = 'Board Settings (synced to Unity)'
    $ws.Cells[4, 1].Style.Font.Bold = $true

    $ws.Cells[5, 1].Value = 'Board Width'
    $ws.Cells[5, 2].Value = $Level.BoardWidth
    $ws.Cells[6, 1].Value = 'Board Height'
    $ws.Cells[6, 2].Value = $Level.BoardHeight
    $ws.Cells[7, 1].Value = 'Turns To Survive'
    $ws.Cells[7, 2].Value = $Level.TurnsToSurvive
    $ws.Cells[8, 1].Value = 'Hand Size'
    $ws.Cells[8, 2].Value = $Level.HandSize
    $ws.Cells[9, 1].Value = 'Loot Table'
    $ws.Cells[9, 2].Value = $Level.LootTable
    $ws.Cells[10, 1].Value = 'Clear Reward Table'
    $ws.Cells[10, 2].Value = $Level.ClearRewardTable
    $ws.Cells[11, 1].Value = 'Tile Sets'
    $ws.Cells[11, 2].Value = ($Level.TileSets -join '; ')
    foreach ($r in 5..11) { $ws.Cells[$r, 1].Style.Font.Italic = $true }

    foreach ($v in @(@(9, 'list_Loot'), @(10, 'list_Loot'))) {
        $dv = $ws.DataValidations.AddListValidation("B$($v[0])")
        $dv.Formula.ExcelFormula = "=$($v[1])"
        $dv.ShowErrorMessage = $false
        $dv.AllowBlank = $true
    }

    # ---------------------------------------------------------------------------------------------
    # Plan every section's row range up front. The board's formulas reference the helper table by
    # address, and the helper table is physically written last - both need the same numbers, so they
    # are computed once here rather than threaded through as the function writes top to bottom.
    # ---------------------------------------------------------------------------------------------

    $waveGroups = @($Level.Placements | Group-Object Turn | Sort-Object { [int]$_.Name })
    if (@($waveGroups | Where-Object { $_.Name -eq '0' }).Count -eq 0) {
        # Always have a Start pair, even on a level with no turn-0 placements - row 15 is the fixed
        # anchor every board formula's OFFSET counts from.
        $waveGroups = @([pscustomobject]@{ Name = '0'; Group = @() }) + $waveGroups
    }

    # Always at least 10 wave slots beyond Start, blank ones ready to fill in later - a level with 6
    # real waves gets 4 spares, a level with 0 gets 10. Never fewer spares than that; never trims a
    # level that already has more than 10 waves authored.
    $realWaveCount = $waveGroups.Count - 1
    $sparePairs = [Math]::Max(0, 10 - $realWaveCount)
    $tableWidth = [Math]::Max(1, (@($waveGroups | ForEach-Object { $_.Group.Count }) | Measure-Object -Maximum).Maximum)

    $wavesLabelRow = 13
    $waveHeaderRow = $wavesLabelRow + 1
    $startRow = $waveHeaderRow + 1   # row 15 - the fixed OFFSET anchor
    $totalPairs = $waveGroups.Count + $sparePairs
    $lastWaveRow = $startRow + (2 * $totalPairs) - 1

    $partyWidth = [Math]::Max(4, @($Level.PartySpawn).Count + 2)
    $partySectionRow = $lastWaveRow + 2
    $partyHeaderRow = $partySectionRow + 1
    $partyDataRow = $partyHeaderRow + 1

    $deckLabelRow = $partyDataRow + 2
    $deckHeaderRow = $deckLabelRow + 1
    $deckDataCount = @($Level.Placements | Where-Object { $_.Deck.Count -gt 0 }).Count
    $deckLastRow = [Math]::Max($deckHeaderRow, $deckHeaderRow + $deckDataCount)

    $boardLabelRow = $deckLastRow + 2
    $showRow = $boardLabelRow + 1
    $boardHeaderRow = $showRow + 2
    $boardLastRow = $boardHeaderRow + 10

    $legendRow = $boardLastRow + 2
    $legendLines = @(
        'Pick an enemy from the dropdown on a "Turn"/"Start" row, then type its cell as Col,Row directly below - e.g. 3,5.',
        'Blank Turn/Coords pairs are spares - type a turn number into one to add a new wave; re-sync to pick it up in the Show: dropdown.',
        'Party spawn cells go on the single "Party" row below, in the same Col,Row format, in party order.',
        'The board below is read-only - it shows whichever row the Show: dropdown selects. Cells past the level''s own Board Width/Height are marked X.'
    )
    $notesRow = $legendRow + $legendLines.Count + 2

    $helperNoteRow = $notesRow + 2
    $helperHeaderRow = $helperNoteRow + 1
    $helperFirstRow = $helperHeaderRow + 1
    $helperLastRow = $helperFirstRow + $waveGroups.Count - 1

    # ---------------------------------------------------------------------------------------------
    # Waves
    # ---------------------------------------------------------------------------------------------

    $ws.Cells[$wavesLabelRow, 1].Value = 'Waves'
    $ws.Cells[$wavesLabelRow, 1].Style.Font.Bold = $true

    $ws.Cells[$waveHeaderRow, 1].Value = 'Turn'
    $ws.Cells[$waveHeaderRow, 1].Style.Font.Italic = $true
    for ($c = 1; $c -le $tableWidth; $c++) {
        $ws.Cells[$waveHeaderRow, ($c + 1)].Value = "Enemy $c"
        $ws.Cells[$waveHeaderRow, ($c + 1)].Style.Font.Italic = $true
    }

    for ($i = 0; $i -lt $waveGroups.Count; $i++) {
        $nameRow = $startRow + (2 * $i)
        $coordRow = $nameRow + 1
        $turnValue = [int]$waveGroups[$i].Name
        $ws.Cells[$nameRow, 1].Value = if ($turnValue -eq 0) { 'Start' } else { $turnValue }
        $ws.Cells[$nameRow, 1].Style.Font.Bold = $true
        $ws.Cells[$coordRow, 1].Value = 'Coords'
        $ws.Cells[$coordRow, 1].Style.Font.Italic = $true

        $placementsThisTurn = @($waveGroups[$i].Group | Sort-Object Row, Col)
        for ($c = 0; $c -lt $placementsThisTurn.Count; $c++) {
            $ws.Cells[$nameRow, ($c + 2)].Value = $placementsThisTurn[$c].Prefab
            $ws.Cells[$coordRow, ($c + 2)].Value = Format-Coord -Col $placementsThisTurn[$c].Col -Row $placementsThisTurn[$c].Row
        }
    }

    # Spare pairs - blank Turn cell, "Coords" already labelled, ready to fill in.
    for ($i = $waveGroups.Count; $i -lt $totalPairs; $i++) {
        $nameRow = $startRow + (2 * $i)
        $coordRow = $nameRow + 1
        $ws.Cells[$coordRow, 1].Value = 'Coords'
        $ws.Cells[$coordRow, 1].Style.Font.Italic = $true
        $ws.Cells["A$($nameRow):A$coordRow"].Style.Font.Color.SetColor([System.Drawing.Color]::Gray)
    }

    # Enemy-name dropdown across every name row, real and spare alike.
    for ($i = 0; $i -lt $totalPairs; $i++) {
        $nameRow = $startRow + (2 * $i)
        $lastLetter = Get-ExcelColumnName ($tableWidth + 1)
        $dv = $ws.DataValidations.AddListValidation("B$($nameRow):$lastLetter$nameRow")
        $dv.Formula.ExcelFormula = '=list_Prefab'
        $dv.ShowErrorMessage = $false
        $dv.AllowBlank = $true
    }

    # ---------------------------------------------------------------------------------------------
    # Party
    # ---------------------------------------------------------------------------------------------

    $ws.Cells[$partySectionRow, 1].Value = 'Party Spawn (synced to Unity)'
    $ws.Cells[$partySectionRow, 1].Style.Font.Bold = $true
    for ($c = 1; $c -le $partyWidth; $c++) {
        $ws.Cells[$partyHeaderRow, ($c + 1)].Value = "P$c"
        $ws.Cells[$partyHeaderRow, ($c + 1)].Style.Font.Italic = $true
    }
    $ws.Cells[$partyDataRow, 1].Value = 'Party'
    $ws.Cells[$partyDataRow, 1].Style.Font.Bold = $true

    $spawnOrdered = @($Level.PartySpawn | Sort-Object Index)
    for ($c = 0; $c -lt $spawnOrdered.Count; $c++) {
        $ws.Cells[$partyDataRow, ($c + 2)].Value = Format-Coord -Col $spawnOrdered[$c].Col -Row $spawnOrdered[$c].Row
    }

    # ---------------------------------------------------------------------------------------------
    # Deck Overrides - unchanged shape, just repositioned under Party instead of under the old grid.
    # ---------------------------------------------------------------------------------------------

    $ws.Cells[$deckLabelRow, 1].Value = 'Deck Overrides'
    $ws.Cells[$deckLabelRow, 1].Style.Font.Bold = $true
    foreach ($h in @('Col', 'Row', 'Turn', 'Deck')) {
        $idx = ([array]::IndexOf(@('Col', 'Row', 'Turn', 'Deck'), $h)) + 1
        $ws.Cells[$deckHeaderRow, $idx].Value = $h
        $ws.Cells[$deckHeaderRow, $idx].Style.Font.Italic = $true
    }

    $r = $deckHeaderRow + 1
    foreach ($p in @($Level.Placements | Where-Object { $_.Deck.Count -gt 0 } | Sort-Object Turn, Row, Col)) {
        $ws.Cells[$r, 1].Value = $p.Col
        $ws.Cells[$r, 2].Value = $p.Row
        $ws.Cells[$r, 3].Value = $p.Turn
        $ws.Cells[$r, 4].Value = Format-DeckList -Names $p.Deck
        $r++
    }

    # ---------------------------------------------------------------------------------------------
    # Board - read-only, formulas only
    # ---------------------------------------------------------------------------------------------

    $ws.Cells[$boardLabelRow, 1].Value = 'Board (read-only - shows the wave picked below)'
    $ws.Cells[$boardLabelRow, 1].Style.Font.Bold = $true

    $ws.Cells[$showRow, 1].Value = 'Show:'
    $ws.Cells[$showRow, 1].Style.Font.Bold = $true
    $ws.Cells[$showRow, 2].Value = 'Start'
    $dvShow = $ws.DataValidations.AddListValidation("B$showRow")
    $dvShow.Formula.ExcelFormula = "=`$A`$$helperFirstRow`:`$A`$$helperLastRow"
    $dvShow.ShowErrorMessage = $false

    $ws.Cells[$showRow, 3].Value = '(name row)'
    $ws.Cells[$showRow, 3].Style.Font.Italic = $true
    $ws.Cells[$showRow, 4].Formula = ('INDEX($B${0}:$B${1},MATCH($B${2},$A${0}:$A${1},0))' -f $helperFirstRow, $helperLastRow, $showRow)
    $ws.Cells[$showRow, 5].Value = '(coord row)'
    $ws.Cells[$showRow, 5].Style.Font.Italic = $true
    $ws.Cells[$showRow, 6].Formula = ('INDEX($C${0}:$C${1},MATCH($B${2},$A${0}:$A${1},0))' -f $helperFirstRow, $helperLastRow, $showRow)
    $ws.Cells["C$showRow`:F$showRow"].Style.Font.Size = 8

    for ($c = 1; $c -le 10; $c++) {
        $ws.Cells[$boardHeaderRow, ($c + 1)].Value = $c
        $ws.Cells[$boardHeaderRow, ($c + 1)].Style.Font.Bold = $true
    }

    $partyHeaderRange = "`$B`${0}:`${1}`${0}" -f $partyHeaderRow, (Get-ExcelColumnName ($partyWidth + 1))
    $partyCoordRange  = "`$B`${0}:`${1}`${0}" -f $partyDataRow, (Get-ExcelColumnName ($partyWidth + 1))
    $formulaTemplate = 'IF(OR({0}>$B$5,{1}>$B$6),"X",IF(IFERROR(INDEX(OFFSET($B$15,$D${2}-15,0,1,{3}),MATCH({0}&","&{1},OFFSET($B$15,$F${2}-15,0,1,{3}),0)),"")<>"",IFERROR(INDEX(OFFSET($B$15,$D${2}-15,0,1,{3}),MATCH({0}&","&{1},OFFSET($B$15,$F${2}-15,0,1,{3}),0)),""),IF($B${2}="Start",IFERROR(INDEX({4},MATCH({0}&","&{1},{5},0)),""),"")))'

    for ($row = 10; $row -ge 1; $row--) {
        $printedRow = $boardHeaderRow + (10 - $row) + 1
        $ws.Cells[$printedRow, 1].Value = $row
        $ws.Cells[$printedRow, 1].Style.Font.Bold = $true

        for ($col = 1; $col -le 10; $col++) {
            $formula = $formulaTemplate -f $col, $row, $showRow, $tableWidth, $partyHeaderRange, $partyCoordRange
            $ws.Cells[$printedRow, ($col + 1)].Formula = $formula
        }
    }

    $ws.Cells["B$($boardHeaderRow):K$boardLastRow"].Style.Border.BorderAround('Thin')
    for ($col = 1; $col -le 10; $col++) {
        $letter = Get-ExcelColumnName ($col + 1)
        $ws.Cells["$letter$($boardHeaderRow):$letter$boardLastRow"].Style.Border.Left.Style = 'Thin'
        $ws.Cells["$letter$($boardHeaderRow):$letter$boardLastRow"].Style.Border.Right.Style = 'Thin'
    }
    for ($row = $boardHeaderRow; $row -le $boardLastRow; $row++) {
        $ws.Cells["A$row`:K$row"].Style.Border.Top.Style = 'Thin'
        $ws.Cells["A$row`:K$row"].Style.Border.Bottom.Style = 'Thin'
    }
    $fmtX = $ws.ConditionalFormatting.AddEqual($ws.Cells["B$($boardHeaderRow):K$boardLastRow"])
    $fmtX.Formula = '"X"'
    $fmtX.Style.Font.Color.Color = [System.Drawing.Color]::LightGray

    # ---------------------------------------------------------------------------------------------
    # Legend / Notes
    # ---------------------------------------------------------------------------------------------

    $ws.Cells[$legendRow, 1].Value = 'Legend'
    $ws.Cells[$legendRow, 1].Style.Font.Bold = $true
    for ($i = 0; $i -lt $legendLines.Count; $i++) {
        $ws.Cells[($legendRow + 1 + $i), 1].Value = $legendLines[$i]
    }

    $ws.Cells[$notesRow, 1].Value = 'Notes'
    $ws.Cells[$notesRow, 1].Style.Font.Bold = $true
    if ($Notes) { $ws.Cells[$notesRow, 2].Value = $Notes }
    $ws.Cells[$notesRow, 2].Style.WrapText = $true

    # ---------------------------------------------------------------------------------------------
    # Helper table - the dropdown's source and the board's OFFSET anchors. Regenerated every export;
    # never hand-edit, same rule Docs/CardDesign.xlsx's Balance tab already follows.
    # ---------------------------------------------------------------------------------------------

    $ws.Cells[$helperNoteRow, 1].Value = '(internal - dropdown source, regenerated on every export, do not edit)'
    $ws.Cells[$helperNoteRow, 1].Style.Font.Italic = $true
    $ws.Cells[$helperHeaderRow, 1].Value = 'Label'
    $ws.Cells[$helperHeaderRow, 2].Value = 'Name Row'
    $ws.Cells[$helperHeaderRow, 3].Value = 'Coord Row'
    $ws.Cells["A$helperHeaderRow`:C$helperHeaderRow"].Style.Font.Italic = $true

    for ($i = 0; $i -lt $waveGroups.Count; $i++) {
        $r = $helperFirstRow + $i
        $turnValue = [int]$waveGroups[$i].Name
        $ws.Cells[$r, 1].Value = if ($turnValue -eq 0) { 'Start' } else { [string]$turnValue }
        $ws.Cells[$r, 2].Value = $startRow + (2 * $i)
        $ws.Cells[$r, 3].Value = $startRow + (2 * $i) + 1
    }

    $ws.Column(1).Width = 16
    for ($c = 2; $c -le 11; $c++) { $ws.Column($c).Width = 14 }
    $ws.View.FreezePanes(($waveHeaderRow + 1), 2)
}

# ---------------------------------------------------------------------------------------------------
# Enums
# ---------------------------------------------------------------------------------------------------

function Add-EnumsSheet {
    param($Package, [string[]]$PrefabNames, [string[]]$LootNames, $Levels)

    $ws = $Package.Workbook.Worksheets.Add('Enums')

    $lists = [ordered]@{
        list_Loot   = @('') + @($LootNames | Sort-Object -Unique)
        list_Prefab = @($PrefabNames | Sort-Object -Unique)
        list_Level  = @($Levels | ForEach-Object { $_.Name } | Sort-Object -Unique)
        list_Bool   = @('TRUE', 'FALSE')
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
        $ws.Column($col).Width = 24
        $col++
    }

    $ws.Cells[1, $col].Value = 'list_Prefab drives the dropdown on every enemy-name cell in a level''s Waves table.'
    $ws.Cells[1, $col].Style.Font.Italic = $true
    $ws.Column($col).Width = 70
}

# ---------------------------------------------------------------------------------------------------
# README
# ---------------------------------------------------------------------------------------------------

function Add-ReadmeSheet {
    param($Package)

    $ws = $Package.Workbook.Worksheets.Add('README')

    $lines = @(
        @('Level Design Workbook', 'title'),
        @('', ''),
        @('One tab per level, built from Assets/Data/LevelData and Assets/Data/RunData. Editing happens in the Waves/Party rows; the Board underneath is a read-only picture of whichever one you pick.', ''),
        @('', ''),
        @('The tabs', 'head'),
        @('Runs', 'One row per RunData: which levels it plays, in order, and whether damage carries between them.'),
        @('One tab per level', 'Board Width/Height/Turns To Survive/Hand Size/Loot Table/Clear Reward Table/Tile Sets, the Waves table and the Party row are all synced.'),
        @('Enums', 'list_Loot and list_Prefab drive real dropdowns (Loot Table cells, and every enemy-name cell in a Waves table). list_Level is a reference list for the Runs tab.'),
        @('', ''),
        @('Editing a level''s Waves table', 'head'),
        @('One wave, two rows', 'The "Start" row (the opening roster) and each wave turn are a pair: pick an enemy from the dropdown in the top row, then type where it lands directly below as "Col,Row" (one-based, same numbers the Inspector shows) - e.g. 3,5. A wave with two enemies just uses two columns.'),
        @('Adding a new wave', 'There are always at least 10 wave slots beyond Start, blank ones ready after the real waves - type a turn number into one and fill in its row. Sync, and the next export picks it up in the Show: dropdown.'),
        @('Party', 'One row, same "Col,Row" format, in party order - P1''s cell first, then P2''s, and so on.'),
        @('Deck Overrides', 'A card list for one specific placement, keyed by (Col, Row, Turn) - Turn 0 means the opening roster. Leave a placement out of this table to use its prefab''s own deck.'),
        @('', ''),
        @('The Board', 'head'),
        @('Show:', 'Pick "Start" or a wave turn here and the 10x10 grid below re-reads that row pair - formulas, not something to type into. Cells past the level''s own Board Width/Height show a grey X. Party positions only appear when Show: is "Start".'),
        @('If a cell looks wrong', 'The Show: dropdown and the grid are driven by INDEX/MATCH/OFFSET formulas against a small internal table at the bottom of the tab - regenerated every export, never hand-edit it. Excel evaluates these on open; if one looks off, check the Waves row it should be reading before assuming the data is wrong.'),
        @('', ''),
        @('Syncing back to Unity', 'head'),
        @('One button', 'In Unity: Tools > Sync Levels With Sheet.'),
        @('What counts as one change', 'The WHOLE board (every placement, wave, party spawn cell and deck override together) is one merge column, "Layout" - editing anything in the Waves/Party rows marks the whole level changed. Changing it in Unity AND the sheet since the last sync is a conflict for the whole level, same as any other column - see Docs/CardDesign.xlsx''s own rule.'),
        @('Unknown prefab names', 'Reported as a problem and that row is skipped, rather than guessed at - the dropdown should prevent this, but a typo pasted in directly is still checked.'),
        @('Regenerating without syncing', 'Tools > Levels > Refresh Sheet From Unity discards any sheet edit that has not been synced yet - only for when the sheet is known to be wrong.')
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

    $ws.Column(1).Width = 24
    $ws.Column(2).Width = 110
    $ws.Column(2).Style.WrapText = $true
}

function Set-SheetOrder {
    param($Package, $Levels)

    $order = @('README', 'Runs') + @($Levels | ForEach-Object { $_.Name }) + @('Enums')

    foreach ($name in $order) {
        $ws = $Package.Workbook.Worksheets[$name]
        if ($null -ne $ws) { $Package.Workbook.Worksheets.MoveToEnd($name) }
    }

    $Package.Workbook.Worksheets['README'].Select()
}
