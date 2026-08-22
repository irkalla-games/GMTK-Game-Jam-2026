<#
.SYNOPSIS
    Turns the gathered LevelData/RunData records into the formatted workbook. Dot-sourced by
    Export-LevelSheet.ps1.

.DESCRIPTION
    One tab per level: a labelled scalars block (found by column-A label, never by row number - same
    rule the enemy sheet's body tabs follow), then a board painted at the level's actual size with 1
    at the bottom-left so it reads the way the board reads on screen. A Deck Overrides table underneath
    covers the one thing a grid cell cannot hold - a per-placement card list.
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

    foreach ($level in $Levels) {
        Add-LevelSheet -Package $pkg -Level $level -Notes $(if ($PreservedNotes.ContainsKey($level.Name)) { $PreservedNotes[$level.Name] } else { '' })
    }

    Add-EnumsSheet -Package $pkg -PrefabNames $PrefabNames -LootNames $LootNames -Levels $Levels
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

    # --- Board ---
    $boardLabelRow = 13
    $ws.Cells[$boardLabelRow, 1].Value = 'Board'
    $ws.Cells[$boardLabelRow, 1].Style.Font.Bold = $true

    # Drawn to fit every actual placement, not just the nominal Board Width/Height - Tutorial.asset
    # authors a placement one row above its own boardSize (row 7 of a 6-tall board), which GridManager
    # evidently tolerates. Silently clipping the grid to the nominal size would drop that placement from
    # the sheet entirely rather than surface the discrepancy.
    $maxPlacedCol = 0; $maxPlacedRow = 0
    foreach ($p in @($Level.Placements)) { if ($p.Col -gt $maxPlacedCol) { $maxPlacedCol = $p.Col }; if ($p.Row -gt $maxPlacedRow) { $maxPlacedRow = $p.Row } }
    foreach ($s in @($Level.PartySpawn)) { if ($s.Col -gt $maxPlacedCol) { $maxPlacedCol = $s.Col }; if ($s.Row -gt $maxPlacedRow) { $maxPlacedRow = $s.Row } }

    $width = [Math]::Max([Math]::Max(1, $Level.BoardWidth), $maxPlacedCol)
    $height = [Math]::Max([Math]::Max(1, $Level.BoardHeight), $maxPlacedRow)
    $headerRow = $boardLabelRow + 1

    for ($c = 1; $c -le $width; $c++) {
        $ws.Cells[$headerRow, ($c + 1)].Value = $c
        $ws.Cells[$headerRow, ($c + 1)].Style.Font.Bold = $true
    }

    # By-coordinate lookup, so filling a cell is O(1) instead of scanning every placement per cell.
    $byCell = @{}
    foreach ($p in @($Level.Placements)) {
        $key = "$($p.Col),$($p.Row)"
        $token = ConvertTo-BoardToken -Prefab $p.Prefab -Turn $p.Turn
        if ($byCell.ContainsKey($key)) { $byCell[$key] += $token } else { $byCell[$key] = @($token) }
    }
    foreach ($s in @($Level.PartySpawn)) {
        $key = "$($s.Col),$($s.Row)"
        $token = "P$($s.Index)"
        if ($byCell.ContainsKey($key)) { $byCell[$key] += $token } else { $byCell[$key] = @($token) }
    }

    for ($row = $height; $row -ge 1; $row--) {
        $printedRow = $headerRow + ($height - $row) + 1
        $ws.Cells[$printedRow, 1].Value = $row
        $ws.Cells[$printedRow, 1].Style.Font.Bold = $true

        for ($col = 1; $col -le $width; $col++) {
            $key = "$col,$row"
            if ($byCell.ContainsKey($key)) { $ws.Cells[$printedRow, ($col + 1)].Value = ($byCell[$key] -join '; ') }
        }
    }

    $boardLastRow = $headerRow + $height
    $ws.Cells["B$($headerRow):$( Get-ExcelColumnName ($width + 1))$boardLastRow"].Style.Border.BorderAround('Thin')
    for ($col = 1; $col -le $width; $col++) {
        $letter = Get-ExcelColumnName ($col + 1)
        $ws.Cells["$letter$($headerRow):$letter$boardLastRow"].Style.Border.Left.Style = 'Thin'
        $ws.Cells["$letter$($headerRow):$letter$boardLastRow"].Style.Border.Right.Style = 'Thin'
    }
    for ($row = $headerRow; $row -le $boardLastRow; $row++) {
        $lastLetter = Get-ExcelColumnName ($width + 1)
        $ws.Cells["A$row`:$lastLetter$row"].Style.Border.Top.Style = 'Thin'
        $ws.Cells["A$row`:$lastLetter$row"].Style.Border.Bottom.Style = 'Thin'
    }

    # --- Deck Overrides ---
    $deckLabelRow = $boardLastRow + 2
    $ws.Cells[$deckLabelRow, 1].Value = 'Deck Overrides'
    $ws.Cells[$deckLabelRow, 1].Style.Font.Bold = $true
    $deckHeaderRow = $deckLabelRow + 1
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
    $deckLastRow = [Math]::Max($deckHeaderRow, $r - 1)

    # --- Legend ---
    $legendRow = $deckLastRow + 2
    $ws.Cells[$legendRow, 1].Value = 'Legend'
    $ws.Cells[$legendRow, 1].Style.Font.Bold = $true
    $legendLines = @(
        'Goblin           - placed at battle start',
        'Goblin@4         - reinforcement, arrives on turn 4',
        'P1, P2, ...      - party spawn cell, in party order',
        'Goblin; Bat@3    - one cell can hold several tokens, separated by ; '
    )
    for ($i = 0; $i -lt $legendLines.Count; $i++) {
        $legendLineRow = $legendRow + 1 + $i
        $ws.Cells[$legendLineRow, 1].Value = $legendLines[$i]
        $ws.Cells[$legendLineRow, 1].Style.Font.Name = 'Consolas'
    }

    # --- Notes ---
    $notesRow = $legendRow + $legendLines.Count + 2
    $ws.Cells[$notesRow, 1].Value = 'Notes'
    $ws.Cells[$notesRow, 1].Style.Font.Bold = $true
    if ($Notes) { $ws.Cells[$notesRow, 2].Value = $Notes }
    $ws.Cells[$notesRow, 2].Style.WrapText = $true

    $ws.Column(1).Width = 16
    for ($c = 2; $c -le ($width + 1); $c++) { $ws.Column($c).Width = 14 }
    $ws.View.FreezePanes(($headerRow + 1), 2)
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

    $ws.Cells[1, $col].Value = 'list_Prefab is a reference list to copy names from into a board cell - it does not drive a dropdown there, since a cell can hold several tokens at once.'
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
        @('One tab per level, painted as the actual board - built from Assets/Data/LevelData and Assets/Data/RunData.', ''),
        @('', ''),
        @('The tabs', 'head'),
        @('Runs', 'One row per RunData: which levels it plays, in order, and whether damage carries between them.'),
        @('One tab per level', 'Board Width/Height/Turns To Survive/Hand Size/Loot Table/Clear Reward Table/Tile Sets are synced. The board below them is the enemy placements, waves and party spawn cells - also synced.'),
        @('Enums', 'Reference lists: valid Loot Table names (a real dropdown on the scalar cells), valid prefab/level names (copy-paste reference for the board, since a board cell is free text).'),
        @('', ''),
        @('Reading a board', 'head'),
        @('Coordinates', 'One-based, same numbers the Inspector shows via [OneBasedCell]. Row 1 is nearest the bottom of the printed grid, matching how +Y reads as "up" on the actual board.'),
        @('A bare name', '"Goblin" - placed the moment the battle starts (LevelData.enemies).'),
        @('name@turn', '"Goblin@4" - a reinforcement, arriving on the turn shown (LevelData.waves). The turn is the same count BattleManager.TurnsElapsed reaches.'),
        @('Pk', '"P1", "P2", ... - a party spawn cell, in party order. Renumbering these is safe - what matters is the relative order the P-numbers sort in, not the exact digits.'),
        @('Several tokens', 'Separate with "; " - "Goblin; Bat@3" means Goblin starts there and Bat arrives on turn 3 at the same cell.'),
        @('Deck Overrides', 'A card list for one specific placement, keyed by (Col, Row, Turn) - Turn 0 means the opening roster, matching the board. Leave a placement out of this table to use its prefab''s own deck.'),
        @('', ''),
        @('Syncing back to Unity', 'head'),
        @('One button', 'In Unity: Tools > Sync Levels With Sheet.'),
        @('What counts as one change', 'The WHOLE board (every placement, wave, party spawn cell and deck override together) is one merge column, "Layout" - editing anything on the board marks the whole level changed. Changing it in Unity AND the sheet since the last sync is a conflict for the whole level, same as any other column - see Docs/CardDesign.xlsx''s own rule.'),
        @('Unknown prefab names', 'Reported as a problem and that token is skipped, rather than guessed at - check the Enums tab''s Prefab list for the exact spelling.'),
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
