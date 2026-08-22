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
# Board tab tokens
#
# One cell holds zero or more ';'-separated tokens: a bare prefab name (opening roster), "Prefab@N" (a
# wave arriving turn N), or "Pk" (party spawn slot k). Multiple tokens on one cell cover the one case
# the real levels actually need it for - Level1 reuses a couple of cells across several waves.
# ---------------------------------------------------------------------------------------------------

function ConvertTo-BoardToken {
    param([string]$Prefab, [int]$Turn)
    if ($Turn -le 0) { return $Prefab }
    return "$Prefab@$Turn"
}

function ConvertFrom-BoardToken {
    <#
        .SYNOPSIS
            Parses one token out of a board cell.

        .OUTPUTS
            A pscustomobject with Kind = 'Party' (Index set) or 'Enemy' (Prefab/Turn set), or $null for
            a token that parses as neither - the caller reports that as a problem rather than guessing.
    #>
    param([string]$Token)

    $t = $Token.Trim()
    if ($t -eq '') { return $null }

    if ($t -match '^[Pp](\d+)$') {
        return [pscustomobject]@{ Kind = 'Party'; Index = [int]$Matches[1]; Prefab = ''; Turn = 0 }
    }

    if ($t -match '^(.+)@(\d+)$') {
        return [pscustomobject]@{ Kind = 'Enemy'; Index = 0; Prefab = $Matches[1].Trim(); Turn = [int]$Matches[2] }
    }

    return [pscustomobject]@{ Kind = 'Enemy'; Index = 0; Prefab = $t; Turn = 0 }
}

function Read-LevelSheetTab {
    <#
        .SYNOPSIS
            One level tab's synced fields, read from an open EPPlus worksheet by scanning column A for
            labels - same "never by row number" rule Read-BodySheetTab follows, so a spacer row or a
            reordered block in Write-LevelWorkbook.ps1 cannot silently break this reader.

        .DESCRIPTION
            The board itself is the exception: its own extent is read from what is actually drawn (the
            column-number header row under the "Board" label, and rows until the first blank one) rather
            than from the Board Width/Height scalar cells above it. That keeps a board-size edit a plain
            synced-field change instead of something that also has to explain what happens to cells
            outside the new size.
    #>
    param($Worksheet)

    if ($null -eq $Worksheet.Dimension) { return $null }
    $last = $Worksheet.Dimension.End.Row
    $lastCol = $Worksheet.Dimension.End.Column

    $values = @{}
    $boardLabelRow = -1
    $deckLabelRow = -1
    for ($r = 1; $r -le $last; $r++) {
        $label = ([string]$Worksheet.Cells[$r, 1].Text).Trim()
        if ($label -eq '') { continue }
        if ($label -eq 'Board') { $boardLabelRow = $r; continue }
        if ($label -eq 'Deck Overrides') { $deckLabelRow = $r; continue }
        $values[$label] = [string]$Worksheet.Cells[$r, 2].Text
    }

    if (-not $values.ContainsKey('GUID')) { return $null }

    $placements = @()
    $partySpawn = @()
    $problems = @()

    if ($boardLabelRow -gt 0) {
        $headerRow = $boardLabelRow + 1
        $cols = @()
        for ($c = 2; $c -le $lastCol; $c++) {
            $h = ([string]$Worksheet.Cells[$headerRow, $c].Text).Trim()
            if ($h -eq '') { break }
            $cols += [int]$h
        }

        $r = $headerRow + 1
        while ($r -le $last) {
            $rowLabel = ([string]$Worksheet.Cells[$r, 1].Text).Trim()
            if ($rowLabel -eq '') { break }
            $rowNum = [int]$rowLabel

            for ($i = 0; $i -lt $cols.Count; $i++) {
                $cellText = ([string]$Worksheet.Cells[$r, ($i + 2)].Text).Trim()
                if ($cellText -eq '') { continue }

                foreach ($tokenText in ($cellText -split ';')) {
                    $token = ConvertFrom-BoardToken -Token $tokenText
                    if ($null -eq $token) { continue }

                    if ($token.Kind -eq 'Party') {
                        $partySpawn += [pscustomobject]@{ Index = $token.Index; Col = $cols[$i]; Row = $rowNum }
                    }
                    else {
                        $placements += [pscustomobject]@{ Turn = $token.Turn; Col = $cols[$i]; Row = $rowNum; Prefab = $token.Prefab; Deck = @() }
                    }
                }
            }
            $r++
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
