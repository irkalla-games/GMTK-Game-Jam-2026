<#
.SYNOPSIS
    Shared plumbing for the level design workbook: reading LevelData/RunData YAML into flat records, and
    the canonical "Layout" string that lets a whole board (placements, waves, party spawns, deck
    overrides) be compared as one merge column.

.DESCRIPTION
    Imports Tools/CardSheet/CardSheet.Common.psm1 for the YAML parser and asset index (unchanged - a
    LevelData's enemies/waves lists are List<struct>, the same shape CardData.effectEntries already is,
    so nothing new is needed there) and Tools/EnemySheet/EnemySheet.Common.psm1 for Get-DiscoveredRoster,
    which is what tells a level tab's board grid which prefab names are legal to type into a cell - the
    two sheets must never disagree about what counts as a real body.

    Read-only with respect to the Unity project, same guarantee both of those make.
#>

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot '..\CardSheet\CardSheet.Common.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\EnemySheet\EnemySheet.Common.psm1') -Force -DisableNameChecking

# ---------------------------------------------------------------------------------------------------
# Reading one placement list - the shape List<EnemyPlacement> takes both as LevelData.enemies and as
# one EnemyWave.enemies. A plain [Serializable] struct list, so CardSheet.Common's existing YAML engine
# already parses it as nested mappings with no changes needed there.
# ---------------------------------------------------------------------------------------------------

function Get-PlacementList {
    param($Node, $AssetIndex)

    $out = @()
    if ($Node -isnot [System.Collections.IEnumerable] -or $Node -is [string]) { return $out }

    foreach ($item in $Node) {
        if (-not ($item -is [System.Collections.IDictionary])) { continue }

        $prefabName = Resolve-AssetName -Reference (Get-NodeField $item 'prefab') -AssetIndex $AssetIndex
        $cellNode = Get-NodeField $item 'cell'
        $x = ConvertTo-IntOrDefault (Get-NodeField $cellNode 'x')
        $y = ConvertTo-IntOrDefault (Get-NodeField $cellNode 'y')

        $deckNames = @()
        $deckNode = Get-NodeField $item 'deckOverride'
        if ($deckNode -is [System.Collections.IEnumerable] -and -not ($deckNode -is [string])) {
            foreach ($ref in $deckNode) {
                $n = Resolve-AssetName -Reference $ref -AssetIndex $AssetIndex
                if ($n) { $deckNames += $n }
            }
        }

        $out += [pscustomobject]@{ Prefab = $prefabName; X = $x; Y = $y; Deck = $deckNames }
    }
    return $out
}

function Read-LevelAsset {
    <#
        .SYNOPSIS
            One LevelData asset, flattened: every enemies/waves entry becomes one row in Placements
            (Turn 0 = the opening roster), coordinates already converted to the one-based form the
            sheet and the Inspector both show.
    #>
    param($Asset, $AssetIndex)

    $n = $Asset.Node

    $boardNode = Get-NodeField $n 'boardSize'
    $width = ConvertTo-IntOrDefault (Get-NodeField $boardNode 'x')
    $height = ConvertTo-IntOrDefault (Get-NodeField $boardNode 'y')

    $tileSets = @()
    $tsNode = Get-NodeField $n 'tileSets'
    if ($tsNode -is [System.Collections.IEnumerable] -and -not ($tsNode -is [string])) {
        foreach ($ref in $tsNode) {
            $name = Resolve-AssetName -Reference $ref -AssetIndex $AssetIndex
            if ($name) { $tileSets += $name }
        }
    }

    $placements = @()
    foreach ($p in @(Get-PlacementList -Node (Get-NodeField $n 'enemies') -AssetIndex $AssetIndex)) {
        $placements += [pscustomobject]@{ Turn = 0; Col = $p.X + 1; Row = $p.Y + 1; Prefab = $p.Prefab; Deck = $p.Deck }
    }

    $wavesNode = Get-NodeField $n 'waves'
    if ($wavesNode -is [System.Collections.IEnumerable] -and -not ($wavesNode -is [string])) {
        foreach ($w in $wavesNode) {
            if (-not ($w -is [System.Collections.IDictionary])) { continue }
            $turn = ConvertTo-IntOrDefault (Get-NodeField $w 'turn')
            foreach ($p in @(Get-PlacementList -Node (Get-NodeField $w 'enemies') -AssetIndex $AssetIndex)) {
                $placements += [pscustomobject]@{ Turn = $turn; Col = $p.X + 1; Row = $p.Y + 1; Prefab = $p.Prefab; Deck = $p.Deck }
            }
        }
    }

    $partySpawn = @()
    $psNode = Get-NodeField $n 'partySpawnCells'
    if ($psNode -is [System.Collections.IEnumerable] -and -not ($psNode -is [string])) {
        $i = 1
        foreach ($cell in $psNode) {
            $x = ConvertTo-IntOrDefault (Get-NodeField $cell 'x')
            $y = ConvertTo-IntOrDefault (Get-NodeField $cell 'y')
            $partySpawn += [pscustomobject]@{ Index = $i; Col = $x + 1; Row = $y + 1 }
            $i++
        }
    }

    return [pscustomobject]@{
        Name = $Asset.Name; Guid = $Asset.Guid; Path = $Asset.Path
        BoardWidth = $width; BoardHeight = $height
        TurnsToSurvive = ConvertTo-IntOrDefault (Get-NodeField $n 'turnsToSurvive') -Default 10
        HandSize = ConvertTo-IntOrDefault (Get-NodeField $n 'handSize') -Default 5
        LootTable = Resolve-AssetName -Reference (Get-NodeField $n 'lootTable') -AssetIndex $AssetIndex
        ClearRewardTable = Resolve-AssetName -Reference (Get-NodeField $n 'clearRewardTable') -AssetIndex $AssetIndex
        TileSets = $tileSets
        Placements = $placements
        PartySpawn = $partySpawn
    }
}

function Format-LevelLayout {
    <#
        .SYNOPSIS
            Every placement, wave and party spawn cell folded into one comparable string - the "Layout"
            merge column.

        .DESCRIPTION
            A full per-placement three-way merge (matching an edited cell to the row it used to be,
            detecting an insert vs a move) is real complexity for what a jam-scale level count needs.
            Treating the whole board as one column means a changed cell marks the WHOLE level as
            changed - which is exactly the existing "one conflicting column disqualifies the whole
            [thing]" rule the enemy and card sheets already apply, just drawn at the level of "the board"
            instead of "one field". Sorted so two reads of an unchanged level always produce identical
            text regardless of authoring order - Placements is a Sort-Object away from being a set, and
            PartySpawn is sorted by its authored Index (not position), since spawn order is meaningful
            and this must not silently renumber it.
    #>
    param($Placements, $PartySpawn)

    $enemyParts = @()
    foreach ($p in ($Placements | Sort-Object Turn, Row, Col, Prefab)) {
        $deck = ($p.Deck -join '|')
        $enemyParts += "$($p.Turn),$($p.Col),$($p.Row),$($p.Prefab),$deck"
    }

    $spawnParts = @()
    foreach ($s in ($PartySpawn | Sort-Object Index)) {
        $spawnParts += "$($s.Col),$($s.Row)"
    }

    return "E:" + ($enemyParts -join ';') + "|S:" + ($spawnParts -join ';')
}

function Get-LevelMergeColumns {
    return @('BoardWidth', 'BoardHeight', 'TurnsToSurvive', 'HandSize', 'LootTable', 'ClearRewardTable', 'TileSets', 'Layout')
}

function Get-LevelMergeRow {
    <# The current-ASSET side of the merge, from a Read-LevelAsset result. #>
    param($Level)

    return [ordered]@{
        BoardWidth       = $Level.BoardWidth
        BoardHeight      = $Level.BoardHeight
        TurnsToSurvive   = $Level.TurnsToSurvive
        HandSize         = $Level.HandSize
        LootTable        = $Level.LootTable
        ClearRewardTable = $Level.ClearRewardTable
        TileSets         = ($Level.TileSets -join '|')
        Layout           = Format-LevelLayout -Placements $Level.Placements -PartySpawn $Level.PartySpawn
    }
}

# ---------------------------------------------------------------------------------------------------
# RunData - a flat one-row-per-run table, unlike the board tabs.
# ---------------------------------------------------------------------------------------------------

function Read-RunAsset {
    param($Asset, $AssetIndex)

    $n = $Asset.Node
    $levels = @()
    $levelsNode = Get-NodeField $n 'levels'
    if ($levelsNode -is [System.Collections.IEnumerable] -and -not ($levelsNode -is [string])) {
        foreach ($ref in $levelsNode) {
            $name = Resolve-AssetName -Reference $ref -AssetIndex $AssetIndex
            if ($name) { $levels += $name }
        }
    }

    return [pscustomobject]@{
        Name   = $Asset.Name; Guid = $Asset.Guid; Path = $Asset.Path
        Levels = $levels
        CarryDamageBetweenLevels = (ConvertTo-IntOrDefault (Get-NodeField $n 'carryDamageBetweenLevels')) -ne 0
    }
}

function Get-RunMergeColumns {
    return @('Levels', 'CarryDamageBetweenLevels')
}

function Get-RunMergeRow {
    param($Run)
    return [ordered]@{
        Levels = ($Run.Levels -join '|')
        CarryDamageBetweenLevels = if ($Run.CarryDamageBetweenLevels) { 'TRUE' } else { 'FALSE' }
    }
}

function Get-RunColumns {
    <# Flat-table header order for the Runs tab - read with Import-Excel/Import-Csv like the card
       sheet's own tabs, not the label-scanning a board tab needs. #>
    return @('Run', 'Levels', 'Carry Damage', 'GUID', 'Sync')
}

# ---------------------------------------------------------------------------------------------------
# Waves table coordinates
#
# A placement's cell is written as "Col,Row" text in the row directly under its enemy name - one column
# per enemy in that wave, so a multi-enemy wave is just a wider pair of rows rather than several tokens
# jammed into one cell.
# ---------------------------------------------------------------------------------------------------

function Format-Coord {
    param([int]$Col, [int]$Row)
    return "$Col,$Row"
}

function ConvertFrom-Coord {
    <# Parses "3,5" (also tolerating "(3,5)" or "3, 5") into {Col;Row}, or $null if it does not parse -
       the caller reports that as a problem rather than guessing at a placement's location. #>
    param([string]$Text)

    $t = $Text.Trim().Trim('(', ')').Trim()
    if ($t -match '^(\d+)\s*,\s*(\d+)$') {
        return [pscustomobject]@{ Col = [int]$Matches[1]; Row = [int]$Matches[2] }
    }
    return $null
}

function Read-LevelSheetTab {
    <#
        .SYNOPSIS
            One level tab's synced fields, read from an open EPPlus worksheet by scanning column A for
            labels - same "never by row number" rule Read-BodySheetTab follows, so a spacer row or a
            reordered block in Write-LevelWorkbook.ps1 cannot silently break this reader.

        .DESCRIPTION
            Enemies and waves are authored as row PAIRS under the "Waves" label: a name row (column A
            holds "Start" or a turn number, one prefab per column from B onward) immediately followed by
            a "Coords" row (the same columns hold "Col,Row" text). Party spawn cells are one row headed
            "Party" the same way. The Board section underneath is pure output (formulas reading the rows
            above) and is never read here - editing happens in the rows, not the grid.
    #>
    param($Worksheet)

    if ($null -eq $Worksheet.Dimension) { return $null }
    $last = $Worksheet.Dimension.End.Row
    $lastCol = $Worksheet.Dimension.End.Column

    $values = @{}
    $wavesLabelRow = -1
    $partyRow = -1
    $deckLabelRow = -1
    for ($r = 1; $r -le $last; $r++) {
        $label = ([string]$Worksheet.Cells[$r, 1].Text).Trim()
        if ($label -eq '') { continue }
        switch ($label) {
            'Waves'          { $wavesLabelRow = $r; continue }
            'Party'          { $partyRow = $r; continue }
            'Deck Overrides' { $deckLabelRow = $r; continue }
        }
        $values[$label] = [string]$Worksheet.Cells[$r, 2].Text
    }

    if (-not $values.ContainsKey('GUID')) { return $null }

    $placements = @()
    $partySpawn = @()
    $problems = @()

    if ($wavesLabelRow -gt 0) {
        $r = $wavesLabelRow + 2   # +1 is the "Turn / Enemy 1 / Enemy 2 ..." column-header row

        while ($r -le $last) {
            $turnLabel = ([string]$Worksheet.Cells[$r, 1].Text).Trim()
            if ($turnLabel -eq 'Party' -or $turnLabel -eq 'Deck Overrides') { break }

            # A structural mismatch - the row after this one is not labelled "Coords" - means we have
            # left the Waves table (its blank separator row before Party/Deck Overrides included, since
            # a spare pair's OWN "Coords" label lives one row lower than a blank separator's would).
            $coordRowLabel = ([string]$Worksheet.Cells[($r + 1), 1].Text).Trim()
            if ($coordRowLabel -ne 'Coords') { break }

            if ($turnLabel -ne '') {
                $turn = 0
                $validTurn = $true
                if ($turnLabel -eq 'Start') { $turn = 0 }
                elseif ($turnLabel -match '^\d+$') { $turn = [int]$turnLabel }
                else {
                    $validTurn = $false
                    $problems += "'$($Worksheet.Name)' row $r`: '$turnLabel' is not 'Start' or a whole number - that wave was skipped."
                }

                if ($validTurn) {
                    for ($c = 2; $c -le $lastCol; $c++) {
                        $name = ([string]$Worksheet.Cells[$r, $c].Text).Trim()
                        $coordText = ([string]$Worksheet.Cells[($r + 1), $c].Text).Trim()
                        if ($name -eq '' -and $coordText -eq '') { continue }

                        if ($name -eq '') {
                            $problems += "'$($Worksheet.Name)' row $($r + 1) has a coordinate with no enemy chosen above it (column $c) - skipped."
                            continue
                        }

                        $coord = ConvertFrom-Coord -Text $coordText
                        if ($null -eq $coord) {
                            $problems += "'$($Worksheet.Name)' row $($r + 1), column $c`: '$coordText' is not a valid coordinate (expected 'Col,Row') - '$name' skipped."
                            continue
                        }

                        $placements += [pscustomobject]@{ Turn = $turn; Col = $coord.Col; Row = $coord.Row; Prefab = $name; Deck = @() }
                    }
                }
            }
            # A blank Turn label with a valid "Coords" row below it is an unused spare pair - nothing to
            # read, just move on to the next one.

            $r += 2
        }
    }

    if ($partyRow -gt 0) {
        for ($c = 2; $c -le $lastCol; $c++) {
            $coordText = ([string]$Worksheet.Cells[$partyRow, $c].Text).Trim()
            if ($coordText -eq '') { continue }

            $coord = ConvertFrom-Coord -Text $coordText
            if ($null -eq $coord) {
                $problems += "'$($Worksheet.Name)' Party row, column $c`: '$coordText' is not a valid coordinate (expected 'Col,Row') - skipped."
                continue
            }

            # Index is authoring ORDER (left to right), not tied to the "Pk" header label above it - a
            # renumbered header would be purely cosmetic.
            $partySpawn += [pscustomobject]@{ Index = ($c - 1); Col = $coord.Col; Row = $coord.Row }
        }
    }

    if ($deckLabelRow -gt 0) {
        $headerRow = $deckLabelRow + 1   # Col, Row, Turn, Deck
        $r = $headerRow + 1
        while ($r -le $last) {
            $colText = ([string]$Worksheet.Cells[$r, 1].Text).Trim()
            if ($colText -eq '') { break }

            $col = [int]$colText
            $row = [int]([string]$Worksheet.Cells[$r, 2].Text).Trim()
            $turn = [int]([string]$Worksheet.Cells[$r, 3].Text).Trim()
            $deckText = ([string]$Worksheet.Cells[$r, 4].Text).Trim()
            $deckNames = @()
            if ($deckText) { $deckNames = @($deckText -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

            $match = $placements | Where-Object { $_.Col -eq $col -and $_.Row -eq $row -and $_.Turn -eq $turn } | Select-Object -First 1
            if ($null -eq $match) {
                $problems += "Deck Overrides row (Col $col, Row $row, Turn $turn) does not match any placement on the board - ignored."
            }
            else {
                $match.Deck = $deckNames
            }
            $r++
        }
    }

    $get = { param($key) if ($values.ContainsKey($key)) { $values[$key] } else { '' } }

    return [pscustomobject]@{
        Name             = $Worksheet.Name
        Guid             = & $get 'GUID'
        BoardWidth       = & $get 'Board Width'
        BoardHeight      = & $get 'Board Height'
        TurnsToSurvive   = & $get 'Turns To Survive'
        HandSize         = & $get 'Hand Size'
        LootTable        = & $get 'Loot Table'
        ClearRewardTable = & $get 'Clear Reward Table'
        TileSets         = @(((& $get 'Tile Sets')) -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        Notes            = & $get 'Notes'
        Placements       = $placements
        PartySpawn       = $partySpawn
        Problems         = $problems
    }
}

function Get-LevelBaselinePath {
    param([string]$ToolDir)
    return (Join-Path $ToolDir 'baseline.json')
}

function Read-LevelBaseline {
    param([string]$ToolDir)

    $path = Get-LevelBaselinePath -ToolDir $ToolDir
    $result = @{ Exists = $false; Levels = @{}; Runs = @{}; WrittenUtc = '' }

    if (-not (Test-Path -LiteralPath $path)) { return $result }

    try { $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    catch {
        Write-Warning "Level baseline.json could not be read ($($_.Exception.Message)) - treating this as a first sync."
        return $result
    }

    $result.Exists = $true
    if ($json.PSObject.Properties.Name -contains 'writtenUtc') { $result.WrittenUtc = [string]$json.writtenUtc }

    foreach ($section in @(@{ Key = 'levels'; Target = 'Levels' }, @{ Key = 'runs'; Target = 'Runs' })) {
        if ($json.PSObject.Properties.Name -notcontains $section.Key) { continue }
        foreach ($entry in @($json.($section.Key))) {
            if (-not $entry.guid) { continue }
            $cols = @{}
            foreach ($p in $entry.columns.PSObject.Properties) { $cols[$p.Name] = ConvertTo-Comparable $p.Value }
            $result.($section.Target)[[string]$entry.guid] = $cols
        }
    }

    return $result
}

function Write-LevelBaseline {
    param([string]$ToolDir, $Levels, $Runs)

    $levelRows = @()
    foreach ($l in @($Levels)) {
        if (-not $l.Guid) { continue }
        $cols = Get-LevelMergeRow -Level $l
        $comparable = [ordered]@{}
        foreach ($k in $cols.Keys) { $comparable[$k] = ConvertTo-Comparable $cols[$k] }
        $levelRows += [ordered]@{ guid = $l.Guid; columns = $comparable }
    }

    $runRows = @()
    foreach ($r in @($Runs)) {
        if (-not $r.Guid) { continue }
        $cols = Get-RunMergeRow -Run $r
        $comparable = [ordered]@{}
        foreach ($k in $cols.Keys) { $comparable[$k] = ConvertTo-Comparable $cols[$k] }
        $runRows += [ordered]@{ guid = $r.Guid; columns = $comparable }
    }

    $payload = [ordered]@{
        writtenUtc = (Get-Date).ToUniversalTime().ToString('o')
        levels     = @($levelRows)
        runs       = @($runRows)
    }

    Set-Content -LiteralPath (Get-LevelBaselinePath -ToolDir $ToolDir) -Value ($payload | ConvertTo-Json -Depth 10) -Encoding utf8
}

Export-ModuleMember -Function *
