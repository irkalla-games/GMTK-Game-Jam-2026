<#
.SYNOPSIS
    Copies phone edits from one (or every) Google Sheets mirror back into its .xlsx.

.DESCRIPTION
    Run before Import-*.ps1 (or as the first step of Sync ... With Sheet) so phone edits are sitting in
    the workbook by the time the existing importer reads it - nothing downstream of this script is new,
    it only gets cells into the same file Import-*.ps1 already reads.

    Two guards run before anything is written, per tab:

      1. Hash guard - the cells the phone may edit are hashed at every successful push (see
         Push-GoogleSheet.ps1) and compared here against the SAME cells' current content in the .xlsx.
         A mismatch means Excel changed them since the last push, and this tab is refused rather than
         silently overwritten with what may now be stale phone data. -Force overrides it.

      2. Row-identity guard - Google's copy of each row's identity column(s) (GUID, or Kind+Type for
         Glossary) must still line up, row for row, with the .xlsx's. A mismatch means a row was
         inserted, deleted or reordered in Sheets, which this bridge does not attempt to reconcile - it
         refuses and NAMES the row rather than writing cells to the wrong card. A trailing row whose
         identity is blank is the one case that is not a mismatch: that is exactly what Import-CardSheet
         /Import-EquipmentSheet already read as "create this", so it is appended - unless the tab's
         AllowCreate is false, or it has no identity column at all (Equipment's Ideas tab - see
         workbooks.psd1), in which case alignment falls back to row count alone.

    Refusal is per TAB, not per workbook - one bad tab does not block every other tab's edits.

    Enemy/Boss body tabs and each level's scalar fields are written back by the same label-scan
    Read-BodySheetTab/Read-LevelSheetTab already use to read them - see Sync-BodyTab/Sync-LevelScalars.
    A level's Waves block (turn/Coords row pairs) and Deck Overrides table only support editing an
    EXISTING placement in place, plus adding a brand new one into a spare pair Write-LevelWorkbook.ps1
    always leaves - changing how many placements or party members there are, or a Deck Override with no
    row for it yet, must be done at the desk first. See Sync-LevelPlacements.

.PARAMETER Workbook
.PARAMETER All
    Same as Push-GoogleSheet.ps1.

.PARAMETER Force
    Apply a tab even though its hash guard failed, discarding whatever changed in Excel since the last
    push. Never overrides the row-identity guard - that one has no safe "discard" side.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/GoogleBridge/Pull-GoogleSheet.ps1 -Workbook Cards
#>
[CmdletBinding(DefaultParameterSetName = 'One')]
param(
    [Parameter(ParameterSetName = 'One', Mandatory)]
    [ValidateSet('Cards', 'Enemies', 'Bosses', 'Levels', 'Equipment')]
    [string]$Workbook,

    [Parameter(ParameterSetName = 'All', Mandatory)]
    [switch]$All,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'GoogleBridge.Common.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\LevelSheet\LevelSheet.Common.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\EquipmentSheet\EquipmentSheet.Common.psm1') -Force -DisableNameChecking
$repoRoot = Get-BridgeRoot

if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    throw "The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser"
}
Import-Module ImportExcel -DisableNameChecking

$manifest = Get-WorkbookManifest
$keys = if ($All) { @($manifest.Keys) } else { @($Workbook) }
$anyApplied = $false

function Resolve-TabSpec {
    param($Spec, [string]$RepoRoot)
    if (-not $Spec.ContainsKey('DynamicSchema')) { return $Spec }
    # Import-ModifierSchema wants the FOLDER modifier-schema.json lives in (Tools/EquipmentSheet), not
    # the repo root - see the matching comment in Push-GoogleSheet.ps1.
    $schema = Import-ModifierSchema -ToolDir (Join-Path $RepoRoot 'Tools\EquipmentSheet')
    $resolved = $Spec.Clone()
    if ($Spec.DynamicSchema -eq 'Equipment') {
        $resolved.Pullable = @((Get-ModifierColumns -Schema $schema) | Where-Object { $_ -notin @('Mod Id', 'GUID', 'Sync') })
    }
    else {
        $resolved.Pullable = @((Get-CardTuningColumns -Schema $schema) | Where-Object { $_ -notin @('Sub Id', 'Mod Id', 'GUID', 'Sync') })
    }
    return $resolved
}

# -----------------------------------------------------------------------------------------------
# Flat header/row tabs
# -----------------------------------------------------------------------------------------------

function Read-CurrentFlatTab {
    <#
        Raw-EPPlus read keyed by header name, in xlsx row order - Import-Excel does not expose column
        INDEX, which the write-back needs.

        Truncates at the FIRST row whose header columns are ALL blank, rather than reading all the way
        to Worksheet.Dimension.End.Row. Two distinct reasons this matters, both confirmed by hitting
        them directly:

          - EPPlus's Dimension follows formatting, not content - Equipment.xlsx's Modifiers/Card Tuning
            tabs report a Dimension.End.Row about 200 rows past their last real one (border/fill applied
            that far down), which without truncation shows up as a "Google has 33 rows, the workbook has
            233" guard failure on every single pull.
          - Model specifically has real Term/Value rows, then exactly one blank separator row, then
            footer/instructional text Write-CardWorkbook.ps1 puts below the table - text that is NOT
            blank. Trimming backward from the last row never reaches that separator (the true tail is
            the non-blank footer text), so the footer rows would otherwise survive into what should be
            pure data. Truncating at the FIRST blank row draws the line in the same place regardless.

        Push-GoogleSheet.ps1's Get-FlatTabValues truncates its Import-Excel-based read the identical way,
        which is what keeps the two sides agreeing on where a tab's data ends.
    #>
    param($Worksheet, [string[]]$Header)

    # Reads .Value, not .Text - Push-GoogleSheet.ps1's Get-FlatTabValues reads through Import-Excel,
    # which hands back the cell's underlying VALUE (so a weight cell showing "2.00" under a 0.00 number
    # format comes back as the double 2, stringifying to "2"). EPPlus's own .Text instead renders the
    # DISPLAY string, honouring that number format - "2.00" for the exact same cell. The two read
    # different things for any numerically-formatted cell, so a guard comparing one against the other
    # refuses forever regardless of whether anything actually changed. Confirmed on Model's Value column.
    $colMap = Get-HeaderColumnMap -Worksheet $Worksheet
    $rows = @()
    if ($null -ne $Worksheet.Dimension) {
        $lastRow = $Worksheet.Dimension.End.Row
        for ($r = 2; $r -le $lastRow; $r++) {
            $rowVals = @($Header | ForEach-Object { if ($colMap.Contains($_)) { "$($Worksheet.Cells[$r, $colMap[$_]].Value)" } else { '' } })
            $isBlank = $true
            foreach ($v in $rowVals) { if ($v -ne '') { $isBlank = $false; break } }
            if ($isBlank) { break }
            $rows += , @{ Row = $r; Values = $rowVals }
        }
    }

    $lastRow = if ($rows.Count -gt 0) { $rows[$rows.Count - 1].Row } else { 1 }
    return @{ ColMap = $colMap; Rows = $rows; LastRow = $lastRow }
}

function Get-AnchorKey {
    param([string[]]$Header, [string[]]$AnchorColumns, [string[]]$RowValues)
    if ($AnchorColumns.Count -eq 0) { return $null }
    $idx = @($AnchorColumns | ForEach-Object { [array]::IndexOf($Header, $_) })
    return ($idx | ForEach-Object { $RowValues[$_] }) -join "`u{1}"
}

function Sync-FlatTab {
    <# Returns $true if the tab was applied (including "nothing to do"), $false if refused. #>
    param($Worksheet, $Spec, [string]$Key, $State, [switch]$GoogleRowsOnly, $GoogleValues)

    $header = @($Spec.AnchorColumns) + @($Spec.Pullable)
    $tabName = $Spec.Sheet

    # Google omits trailing blank cells from a row entirely, so every fetched row is padded back out to
    # the full header width before anything indexes into it by position.
    $googleRows = @($GoogleValues | Select-Object -Skip 1 | ForEach-Object { Get-PaddedRow -Row $_ -MinLength $header.Count })

    $current = Read-CurrentFlatTab -Worksheet $Worksheet -Header $header

    # Guard 1: did Excel move the pullable cells since the last push?
    $pullIdx = @($Spec.Pullable | ForEach-Object { [array]::IndexOf($header, $_) })
    $currentPullOnly = @()
    foreach ($row in $current.Rows) { $currentPullOnly += , @($pullIdx | ForEach-Object { $row.Values[$_] }) }
    $currentHash = Get-CellsHash -Rows $currentPullOnly
    $hashKey = Get-TabHashKey $Key $tabName
    $storedHash = if ($State.tabHashes.Contains($hashKey)) { $State.tabHashes[$hashKey] } else { $null }

    if (-not $Force -and $storedHash -and $storedHash -ne $currentHash) {
        Write-Warning "'$tabName': the workbook changed since the last push - refused (use -Force to discard the Excel edit, or push then pull again)."
        return $false
    }
    if (-not $storedHash) {
        Write-Warning "'$tabName': never pushed yet - nothing to compare against, refused. Push it first."
        return $false
    }

    # Guard 2: row identity.
    if ($googleRows.Count -lt $current.Rows.Count) {
        Write-Warning "'$tabName': Google has fewer rows ($($googleRows.Count)) than the workbook ($($current.Rows.Count)) - a deleted row is not supported. Restore it in Google, or refresh the sheet from Unity."
        return $false
    }

    $anchorCols = @($Spec.AnchorColumns)
    for ($i = 0; $i -lt $current.Rows.Count; $i++) {
        if ($anchorCols.Count -eq 0) { continue }   # no identity column - position is the only check, and length already matched above
        $curKey = Get-AnchorKey -Header $header -AnchorColumns $anchorCols -RowValues $current.Rows[$i].Values
        $gKey = Get-AnchorKey -Header $header -AnchorColumns $anchorCols -RowValues $googleRows[$i]
        if ($curKey -ne $gKey) {
            Write-Warning "'$tabName' row $($current.Rows[$i].Row): identity changed ('$curKey' -> '$gKey') - a mid-sheet insert, delete or reorder is not supported. Refused."
            return $false
        }
    }

    for ($i = $current.Rows.Count; $i -lt $googleRows.Count; $i++) {
        if (-not $Spec.AllowCreate) {
            Write-Warning "'$tabName': Google has $($googleRows.Count - $current.Rows.Count) extra row(s) but this tab does not allow creating new ones. Refused."
            return $false
        }
        if ($anchorCols.Count -gt 0) {
            $gKey = Get-AnchorKey -Header $header -AnchorColumns $anchorCols -RowValues $googleRows[$i]
            if ($gKey -and ($gKey -replace "`u{1}", '')) {
                Write-Warning "'$tabName' new row $($i + 2): its identity column is not blank ('$gKey') - a genuinely new row must leave that column empty. Refused."
                return $false
            }
        }
    }

    # Everything checked out - write it.
    foreach ($col in $Spec.Pullable) {
        if (-not $current.ColMap.Contains($col)) { Write-Warning "'$tabName': column '$col' not found - skipped."; continue }
        $colIdx = $current.ColMap[$col]
        $hIdx = [array]::IndexOf($header, $col)

        for ($i = 0; $i -lt $current.Rows.Count; $i++) {
            $newVal = "$($googleRows[$i][$hIdx])"
            if ($current.Rows[$i].Values[$hIdx] -ne $newVal) { $Worksheet.Cells[$current.Rows[$i].Row, $colIdx].Value = $newVal }
        }
        for ($i = $current.Rows.Count; $i -lt $googleRows.Count; $i++) {
            $physRow = $current.LastRow + 1 + ($i - $current.Rows.Count)
            $Worksheet.Cells[$physRow, $colIdx].Value = "$($googleRows[$i][$hIdx])"
        }
    }

    return $true
}

function Pull-FlatWorkbook {
    param([string]$Key, $Entry, [string]$RepoRoot, $State)

    if (-not $State.spreadsheets.Contains($Key) -or -not $State.spreadsheets[$Key]) {
        Write-Warning "'$($Entry.Title)' has never been pushed - nothing to pull. Run Push-GoogleSheet.ps1 -Workbook $Key first."
        return
    }
    $id = $State.spreadsheets[$Key]
    $xlsxPath = Join-Path $RepoRoot $Entry.Xlsx
    $specs = @($Entry.Tabs | ForEach-Object { Resolve-TabSpec -Spec $_ -RepoRoot $RepoRoot })

    $ranges = @($specs | ForEach-Object { "'$($_.Sheet)'!A1:ZZ" })
    $googleValues = Get-GoogleValues -SpreadsheetId $id -Ranges $ranges

    Assert-WorkbookWritable -WorkbookPath $xlsxPath
    $pkg = Open-ExcelPackage -Path $xlsxPath
    $applied = 0
    try {
        foreach ($spec in $specs) {
            $ws = $pkg.Workbook.Worksheets[$spec.Sheet]
            if ($null -eq $ws) { Write-Warning "'$($spec.Sheet)' does not exist in $($Entry.Xlsx) - skipped."; continue }
            $values = $googleValues["'$($spec.Sheet)'!A1:ZZ"]
            if ($values.Count -eq 0) { continue }
            if (Sync-FlatTab -Worksheet $ws -Spec $spec -Key $Key -State $State -GoogleValues $values) { $applied++ }
        }
    }
    finally {
        if ($applied -gt 0) { Close-ExcelPackage -ExcelPackage $pkg }   # no -Save switch exists on this cmdlet - omitting -NoSave IS the save
        else { Close-ExcelPackage -ExcelPackage $pkg -NoSave }
    }
    Write-Host "Pulled $applied/$($specs.Count) tab(s) into $($Entry.Xlsx)." -ForegroundColor Green
    if ($applied -gt 0) { $script:anyApplied = $true }
}

# -----------------------------------------------------------------------------------------------
# Body tabs (Enemies/Bosses) - one Google tab per prefab, matched by NAME, so there is no row-identity
# question at all: either the tab is that prefab's or it isn't.
# -----------------------------------------------------------------------------------------------

function Sync-BodyTab {
    param($Worksheet, $Entry, [string]$Key, $State, $GoogleValues)

    $prefab = $Worksheet.Name
    $current = Read-BodySheetTab -Worksheet $Worksheet
    if ($null -eq $current) { return $false }   # not a recognised body tab (shouldn't happen - Push only creates these)

    $hashKey = Get-TabHashKey $Key $prefab
    $storedHash = if ($State.tabHashes.Contains($hashKey)) { $State.tabHashes[$hashKey] } else { $null }

    $currentRows = @()
    foreach ($field in $Entry.PullableFields) {
        $prop = Get-BodyFieldPropertyName -Field $field
        $v = $current.PSObject.Properties[$prop]
        $currentRows += , @($field, "$(if ($v) { $v.Value } else { '' })")
    }
    # Must mirror Push's Get-BodyTabValues exactly, deck included (one card per cell across the row,
    # not a pipe-joined string) - the two hashes are compared against each other.
    if ($Entry.DeckField) { $currentRows += , (@($Entry.DeckField) + @($current.Deck)) }
    $currentHash = Get-CellsHash -Rows $currentRows

    if (-not $Force -and $storedHash -and $storedHash -ne $currentHash) {
        Write-Warning "'$prefab': the workbook changed since the last push - refused (use -Force to discard the Excel edit)."
        return $false
    }
    if (-not $storedHash) { Write-Warning "'$prefab': never pushed yet - refused."; return $false }

    $googleFields = @{}
    $googleDeck = $null
    foreach ($row in ($GoogleValues | Select-Object -Skip 1)) {
        $padded = Get-PaddedRow -Row $row -MinLength 2
        $name = "$($padded[0])"
        if ($name -eq '') { continue }

        if ($Entry.DeckField -and $name -eq $Entry.DeckField) {
            # The whole row past the label is the deck, one card per cell. Stops at the first blank, the
            # same way Read-BodySheetTab reads the workbook's own deck row.
            $cards = @()
            for ($c = 1; $c -lt $padded.Count; $c++) {
                $card = "$($padded[$c])".Trim()
                if ($card -eq '') { break }
                $cards += $card
            }
            $googleDeck = $cards
            continue
        }

        $googleFields[$name] = "$($padded[1])"
    }

    $labelRows = Get-LabelRowMap -Worksheet $Worksheet
    foreach ($field in $Entry.PullableFields) {
        if (-not $labelRows.Contains($field)) { Write-Warning "'$prefab': no '$field' row - skipped."; continue }
        if (-not $googleFields.ContainsKey($field)) { continue }
        $Worksheet.Cells[$labelRows[$field], 2].Value = $googleFields[$field]
    }

    if ($Entry.DeckField -and $null -ne $googleDeck) {
        $deckRow = $labelRows['Deck']
        if ($deckRow) {
            $newDeck = @($googleDeck)
            $lastCol = $Worksheet.Dimension.End.Column
            $oldLast = 1
            for ($c = 2; $c -le $lastCol; $c++) { if (([string]$Worksheet.Cells[$deckRow, $c].Text).Trim() -ne '') { $oldLast = $c } }
            # The column is precomputed into $col rather than written as "2 + $i" inline in the indexer -
            # PowerShell's parser mishandles an un-parenthesized arithmetic expression as an indexer
            # argument specifically when the whole thing is an ASSIGNMENT target, throwing "The property
            # 'Value' cannot be found on this object" even though $Worksheet.Cells[$deckRow, 2 + $i] reads
            # back just fine anywhere else. Confirmed by direct reproduction - not a guess.
            for ($i = 0; $i -lt $newDeck.Count; $i++) { $col = 2 + $i; $Worksheet.Cells[$deckRow, $col].Value = $newDeck[$i] }
            for ($c = 2 + $newDeck.Count; $c -le $oldLast; $c++) { $Worksheet.Cells[$deckRow, $c].Value = $null }
        }
    }

    return $true
}

function Pull-BodyWorkbook {
    param([string]$Key, $Entry, [string]$RepoRoot, $State)

    if (-not $State.spreadsheets.Contains($Key) -or -not $State.spreadsheets[$Key]) {
        Write-Warning "'$($Entry.Title)' has never been pushed - nothing to pull."
        return
    }
    $id = $State.spreadsheets[$Key]
    $xlsxPath = Join-Path $RepoRoot $Entry.Xlsx

    $meta = Get-GoogleSpreadsheetMeta -SpreadsheetId $id
    # Verbatim tabs (PowerLevel, and the boss workbook's Summoned mirror) are pushed for reference and
    # for the named ranges the formulas need. They are not bodies, so they are excluded here rather than
    # fetched and then counted as tabs that failed to sync.
    $verbatim = @($Entry.VerbatimTabs)
    $tabNames = @($meta.sheets | ForEach-Object { $_.properties.title } | Where-Object { $_ -notin $verbatim })
    # Wider than A:B - a body tab's Deck row runs one card per cell across the row.
    $googleValues = Get-GoogleValues -SpreadsheetId $id -Ranges (@($tabNames | ForEach-Object { "'$_'!A1:AZ90" }))

    Assert-WorkbookWritable -WorkbookPath $xlsxPath
    $pkg = Open-ExcelPackage -Path $xlsxPath
    $applied = 0
    $known = 0
    try {
        foreach ($tabName in $tabNames) {
            $ws = $pkg.Workbook.Worksheets[$tabName]
            if ($null -eq $ws) { Write-Warning "Google tab '$tabName' has no matching prefab in $($Entry.Xlsx) - skipped (renamed or removed prefab?)."; continue }
            $values = $googleValues["'$tabName'!A1:AZ90"]
            if ($values.Count -eq 0) { continue }
            $known++
            if (Sync-BodyTab -Worksheet $ws -Entry $Entry -Key $Key -State $State -GoogleValues $values) { $applied++ }
        }
    }
    finally {
        if ($applied -gt 0) { Close-ExcelPackage -ExcelPackage $pkg }   # no -Save switch exists on this cmdlet - omitting -NoSave IS the save
        else { Close-ExcelPackage -ExcelPackage $pkg -NoSave }
    }
    Write-Host "Pulled $applied/$known prefab tab(s) into $($Entry.Xlsx)." -ForegroundColor Green
    if ($applied -gt 0) { $script:anyApplied = $true }
}

# -----------------------------------------------------------------------------------------------
# Levels - scalar fields the same way as a body tab, plus the Waves/Party blocks, which only support
# editing an existing placement/slot in place or filling a spare pair - see the module header comment.
# -----------------------------------------------------------------------------------------------

function Get-WavesCells {
    <# One entry per (row-pair, column) slot in the Waves block - occupied (Prefab set) or spare (both
       blank). Mirrors Read-LevelSheetTab's scan exactly so this can never disagree with it about which
       rows are the Waves table. #>
    param($Worksheet, [hashtable]$LabelRows)

    if (-not $LabelRows.Contains('Waves')) { return @() }
    $last = $Worksheet.Dimension.End.Row
    $lastCol = $Worksheet.Dimension.End.Column
    $r = $LabelRows['Waves'] + 2
    $cells = @()
    while ($r -le $last) {
        $turnLabel = ([string]$Worksheet.Cells[$r, 1].Text).Trim()
        if ($turnLabel -eq 'Party' -or $turnLabel -eq 'Deck Overrides') { break }
        $coordLabel = ([string]$Worksheet.Cells[($r + 1), 1].Text).Trim()
        if ($coordLabel -ne 'Coords') { break }
        for ($c = 2; $c -le $lastCol; $c++) {
            $cells += [pscustomobject]@{
                TurnRow = $r; CoordRow = ($r + 1); Col = $c
                Turn    = if ($turnLabel -eq 'Start') { 0 } elseif ($turnLabel -match '^\d+$') { [int]$turnLabel } else { -1 }
                Prefab  = ([string]$Worksheet.Cells[$r, $c].Text).Trim()
                Coord   = ConvertFrom-Coord -Text ([string]$Worksheet.Cells[($r + 1), $c].Text).Trim()
            }
        }
        $r += 2
    }
    return $cells
}

function Get-DeckOverrideRows {
    param($Worksheet, [hashtable]$LabelRows)
    if (-not $LabelRows.Contains('Deck Overrides')) { return @() }
    $last = $Worksheet.Dimension.End.Row
    $r = $LabelRows['Deck Overrides'] + 2
    $rows = @()
    while ($r -le $last) {
        $colText = ([string]$Worksheet.Cells[$r, 1].Text).Trim()
        if ($colText -eq '') { break }
        $rows += [pscustomobject]@{ Row = $r; Col = [int]$colText; RowCoord = [int]([string]$Worksheet.Cells[$r, 2].Text).Trim(); Turn = [int]([string]$Worksheet.Cells[$r, 3].Text).Trim() }
        $r++
    }
    return $rows
}

function Format-PlacementKey {
    param($Turn, $Col, $Row)
    return "$Turn|$Col|$Row"
}

function Sync-LevelScalars {
    param($Worksheet, $Entry, [string]$Key, $State, $GoogleValues)

    $levelName = $Worksheet.Name
    $current = Read-LevelSheetTab -Worksheet $Worksheet
    if ($null -eq $current) { return $false }

    $currentRows = @()
    foreach ($field in $Entry.PullableFields) {
        $prop = ($field -replace '[^A-Za-z0-9]', '')
        $v = $current.PSObject.Properties[$prop]
        $text = if ($v) { if ($v.Value -is [System.Collections.IEnumerable] -and $v.Value -isnot [string]) { ($v.Value -join ';') } else { "$($v.Value)" } } else { '' }
        $currentRows += , @($field, $text)
    }
    $currentHash = Get-CellsHash -Rows $currentRows

    $hashKey = Get-TabHashKey $Key $levelName
    $storedHash = if ($State.tabHashes.Contains($hashKey)) { $State.tabHashes[$hashKey] } else { $null }
    if (-not $Force -and $storedHash -and $storedHash -ne $currentHash) {
        Write-Warning "'$levelName': the workbook changed since the last push - refused."
        return $false
    }
    if (-not $storedHash) { Write-Warning "'$levelName': never pushed yet - refused."; return $false }

    $googleFields = @{}
    foreach ($row in ($GoogleValues | Select-Object -Skip 1)) {
        $padded = Get-PaddedRow -Row $row -MinLength 2
        if ("$($padded[0])" -ne '') { $googleFields["$($padded[0])"] = "$($padded[1])" }
    }

    $labelRows = Get-LabelRowMap -Worksheet $Worksheet
    foreach ($field in $Entry.PullableFields) {
        if (-not $labelRows.Contains($field) -or -not $googleFields.ContainsKey($field)) { continue }
        $Worksheet.Cells[$labelRows[$field], 2].Value = $googleFields[$field]
    }
    return $true
}

function Sync-LevelPlacements {
    <# Existing placements may have their Prefab/Deck retyped in place; a genuinely new placement fills
       one of Write-LevelWorkbook's spare pairs. Anything else (moving a placement, removing one, or
       adding more than there are spare pairs) is refused - see the module header. #>
    param($Worksheet, [string]$Key, $State, $PlacementValues, $DeckValues)

    $levelName = $Worksheet.Name
    $current = Read-LevelSheetTab -Worksheet $Worksheet
    if ($null -eq $current) { return $false }

    $currentSorted = @($current.Placements | Sort-Object Turn, Row, Col, Prefab)
    $currentRows = @($currentSorted | ForEach-Object {
        $turnText = if ($_.Turn -eq 0) { 'Start' } else { "$($_.Turn)" }
        , @($turnText, "$($_.Col)", "$($_.Row)", $_.Prefab, ((@($_.Deck)) -join ';'))
    })
    $currentHash = Get-CellsHash -Rows $currentRows

    $hashKey = Get-TabHashKey $Key "$levelName Placements"
    $storedHash = if ($State.tabHashes.Contains($hashKey)) { $State.tabHashes[$hashKey] } else { $null }
    if (-not $Force -and $storedHash -and $storedHash -ne $currentHash) {
        Write-Warning "'$levelName Placements': the workbook changed since the last push - refused."
        return $false
    }
    if (-not $storedHash) { Write-Warning "'$levelName Placements': never pushed yet - refused."; return $false }

    $googleRows = @($PlacementValues | Select-Object -Skip 1 | ForEach-Object { Get-PaddedRow -Row $_ -MinLength 5 } | Where-Object { $_[0] -ne '' -or $_[3] -ne '' })
    $googlePlacements = @()
    foreach ($row in $googleRows) {
        $turnText = "$($row[0])".Trim()
        $turn = if ($turnText -eq 'Start') { 0 } elseif ($turnText -match '^\d+$') { [int]$turnText } else { $null }
        if ($null -eq $turn) { Write-Warning "'$levelName Placements': '$turnText' is not 'Start' or a whole number - row skipped."; continue }
        $googlePlacements += [pscustomobject]@{ Turn = $turn; Col = [int]"$($row[1])"; Row = [int]"$($row[2])"; Prefab = "$($row[3])"; Deck = @("$($row[4])" -split ';' | Where-Object { $_ }) }
    }

    $currentByKey = @{}
    foreach ($p in $current.Placements) { $currentByKey[(Format-PlacementKey $p.Turn $p.Col $p.Row)] = $p }
    $googleByKey = @{}
    foreach ($p in $googlePlacements) {
        $k = Format-PlacementKey $p.Turn $p.Col $p.Row
        if ($googleByKey.ContainsKey($k)) { Write-Warning "'$levelName Placements': two rows both claim Turn $($p.Turn), Col $($p.Col), Row $($p.Row) - refused."; return $false }
        $googleByKey[$k] = $p
    }

    $missing = @($currentByKey.Keys | Where-Object { -not $googleByKey.ContainsKey($_) })
    if ($missing.Count -gt 0) {
        Write-Warning "'$levelName Placements': $($missing.Count) placement(s) missing from Google ($($missing -join ', ')) - removing a placement from the phone is not supported. Refused."
        return $false
    }

    $labelRows = Get-LabelRowMap -Worksheet $Worksheet
    $wavesCells = Get-WavesCells -Worksheet $Worksheet -LabelRows $labelRows
    $occupied = @($wavesCells | Where-Object { $_.Prefab -ne '' })
    $spare = @($wavesCells | Where-Object { $_.Prefab -eq '' -and $null -eq $_.Coord })

    $newKeys = @($googleByKey.Keys | Where-Object { -not $currentByKey.ContainsKey($_) })
    if ($newKeys.Count -gt $spare.Count) {
        Write-Warning "'$levelName Placements': $($newKeys.Count) new placement(s) but only $($spare.Count) spare wave slot(s) in the workbook - add more at the desk (a fresh export always leaves 10) and sync first. Refused."
        return $false
    }

    # Existing placements: retype Prefab in place if it changed. Deck changes go through Deck Overrides.
    # $cell.Col is which SHEET column the slot lives in - the placement's own grid coordinate is
    # $cell.Coord.Col/.Row, parsed from the "Col,Row" text underneath it; the two are unrelated numbers.
    foreach ($cell in $occupied) {
        if ($null -eq $cell.Coord) { continue }   # malformed coordinate - Read-LevelSheetTab already reports this as a problem
        $coordKey = Format-PlacementKey $cell.Turn $cell.Coord.Col $cell.Coord.Row
        if ($googleByKey.ContainsKey($coordKey)) {
            $g = $googleByKey[$coordKey]
            if ($g.Prefab -ne $cell.Prefab) { $Worksheet.Cells[$cell.TurnRow, $cell.Col].Value = $g.Prefab }
        }
    }

    # New placements: one spare pair each, in no particular order.
    $spareQueue = [System.Collections.Generic.Queue[object]]::new()
    foreach ($s in $spare) { $spareQueue.Enqueue($s) }
    foreach ($key in $newKeys) {
        $g = $googleByKey[$key]
        $slot = $spareQueue.Dequeue()
        $turnText = if ($g.Turn -eq 0) { 'Start' } else { "$($g.Turn)" }
        $Worksheet.Cells[$slot.TurnRow, $slot.Col].Value = $turnText
        $Worksheet.Cells[$slot.CoordRow, $slot.Col].Value = (Format-Coord -Col $g.Col -Row $g.Row)
    }

    # Deck Overrides - update only. A placement whose deck changed but has no override row yet is refused
    # individually rather than aborting the whole tab, since it is common for most placements to use the
    # default deck and never need one.
    $overrideRows = @(Get-DeckOverrideRows -Worksheet $Worksheet -LabelRows $labelRows)
    foreach ($key in $googleByKey.Keys) {
        $g = $googleByKey[$key]
        $before = if ($currentByKey.ContainsKey($key)) { @($currentByKey[$key].Deck) } else { @() }
        $after = @($g.Deck)
        if (($before -join '|') -eq ($after -join '|')) { continue }

        $existing = $overrideRows | Where-Object { $_.Col -eq $g.Col -and $_.RowCoord -eq $g.Row -and $_.Turn -eq $g.Turn } | Select-Object -First 1
        if ($null -eq $existing) {
            Write-Warning "'$levelName Placements': deck for (Turn $($g.Turn), Col $($g.Col), Row $($g.Row)) changed but it has no Deck Overrides row yet - add a blank one at the desk first. Skipped just this placement."
            continue
        }
        $Worksheet.Cells[$existing.Row, 4].Value = ($after -join ';')
    }

    return $true
}

function Sync-LevelParty {
    param($Worksheet, [string]$Key, $State, $PartyValues)

    $levelName = $Worksheet.Name
    $current = Read-LevelSheetTab -Worksheet $Worksheet
    if ($null -eq $current) { return $false }

    $currentSorted = @($current.PartySpawn | Sort-Object Index)
    $currentRows = @($currentSorted | ForEach-Object { , @("$($_.Index)", "$($_.Col)", "$($_.Row)") })
    $currentHash = Get-CellsHash -Rows $currentRows

    $hashKey = Get-TabHashKey $Key "$levelName Party"
    $storedHash = if ($State.tabHashes.Contains($hashKey)) { $State.tabHashes[$hashKey] } else { $null }
    if (-not $Force -and $storedHash -and $storedHash -ne $currentHash) { Write-Warning "'$levelName Party': the workbook changed since the last push - refused."; return $false }
    if (-not $storedHash) { Write-Warning "'$levelName Party': never pushed yet - refused."; return $false }

    $googleRows = @($PartyValues | Select-Object -Skip 1 | ForEach-Object { Get-PaddedRow -Row $_ -MinLength 3 } | Where-Object { "$($_[0])" -ne '' })
    if ($googleRows.Count -lt $currentSorted.Count) {
        Write-Warning "'$levelName Party': Google has fewer party slots than the workbook - removing one from the phone is not supported. Refused."
        return $false
    }
    # PartySpawn.Index is 1-based (Read-LevelSheetTab sets it from $c - 1 where $c starts at column 2,
    # the first party member's column), so Get-PartyValues's Slot column is 1-based too - comparing
    # against the 0-based loop counter $i directly refused every legitimately-numbered party.
    for ($i = 0; $i -lt $googleRows.Count; $i++) {
        if ("$($googleRows[$i][0])" -ne "$($i + 1)") {
            Write-Warning "'$levelName Party': slots must stay numbered 1..N with no gaps ('$($googleRows[$i][0])' at position $($i + 1)) - refused."
            return $false
        }
    }

    $labelRows = Get-LabelRowMap -Worksheet $Worksheet
    if (-not $labelRows.Contains('Party')) { Write-Warning "'$levelName Party': no Party row in the workbook - refused."; return $false }
    $partyRow = $labelRows['Party']
    for ($i = 0; $i -lt $googleRows.Count; $i++) {
        # Column precomputed into $col - see the matching comment in Sync-BodyTab on why "2 + $i" inline
        # in the indexer breaks specifically here, as an assignment target.
        $col = 2 + $i
        $Worksheet.Cells[$partyRow, $col].Value = "$($googleRows[$i][1]),$($googleRows[$i][2])"
    }
    return $true
}

function Pull-LevelWorkbook {
    param([string]$Key, $Entry, [string]$RepoRoot, $State)

    if (-not $State.spreadsheets.Contains($Key) -or -not $State.spreadsheets[$Key]) {
        Write-Warning "'$($Entry.Title)' has never been pushed - nothing to pull."
        return
    }
    $id = $State.spreadsheets[$Key]
    $xlsxPath = Join-Path $RepoRoot $Entry.Xlsx

    $meta = Get-GoogleSpreadsheetMeta -SpreadsheetId $id
    $allTabNames = @($meta.sheets | ForEach-Object { $_.properties.title })
    $levelNames = @($allTabNames | Where-Object { $_ -notlike '* Placements' -and $_ -notlike '* Party' -and $_ -notin @($Entry.ExtraTables | ForEach-Object { $_.Sheet }) })

    $ranges = @()
    foreach ($n in $levelNames) { $ranges += "'$n'!A1:B60", "'$n Placements'!A1:ZZ", "'$n Party'!A1:ZZ" }
    $extraSpecs = @($Entry.ExtraTables | ForEach-Object { Resolve-TabSpec -Spec $_ -RepoRoot $RepoRoot })
    $ranges += @($extraSpecs | ForEach-Object { "'$($_.Sheet)'!A1:ZZ" })
    $googleValues = Get-GoogleValues -SpreadsheetId $id -Ranges $ranges

    Assert-WorkbookWritable -WorkbookPath $xlsxPath
    $pkg = Open-ExcelPackage -Path $xlsxPath
    $applied = 0
    $total = 0
    try {
        foreach ($n in $levelNames) {
            $ws = $pkg.Workbook.Worksheets[$n]
            if ($null -eq $ws) { Write-Warning "Google tab '$n' has no matching level in $($Entry.Xlsx) - skipped."; continue }

            $total += 3
            if (Sync-LevelScalars -Worksheet $ws -Entry $Entry -Key $Key -State $State -GoogleValues $googleValues["'$n'!A1:B60"]) { $applied++ }
            if (Sync-LevelPlacements -Worksheet $ws -Key $Key -State $State -PlacementValues $googleValues["'$n Placements'!A1:ZZ"] -DeckValues $null) { $applied++ }
            if (Sync-LevelParty -Worksheet $ws -Key $Key -State $State -PartyValues $googleValues["'$n Party'!A1:ZZ"]) { $applied++ }
        }
        foreach ($spec in $extraSpecs) {
            $ws = $pkg.Workbook.Worksheets[$spec.Sheet]
            if ($null -eq $ws) { continue }
            $total++
            $values = $googleValues["'$($spec.Sheet)'!A1:ZZ"]
            if ($values.Count -gt 0 -and (Sync-FlatTab -Worksheet $ws -Spec $spec -Key $Key -State $State -GoogleValues $values)) { $applied++ }
        }
    }
    finally {
        if ($applied -gt 0) { Close-ExcelPackage -ExcelPackage $pkg }   # no -Save switch exists on this cmdlet - omitting -NoSave IS the save
        else { Close-ExcelPackage -ExcelPackage $pkg -NoSave }
    }
    Write-Host "Pulled $applied/$total section(s) into $($Entry.Xlsx)." -ForegroundColor Green
    if ($applied -gt 0) { $script:anyApplied = $true }
}

# -----------------------------------------------------------------------------------------------

foreach ($key in $keys) {
    $entry = $manifest[$key]
    if (-not $entry) { throw "Unknown workbook '$key'." }
    $state = Read-BridgeState

    switch ($true) {
        ($entry.ContainsKey('Kind') -and $entry.Kind -eq 'BodyTabs')  { Pull-BodyWorkbook  -Key $key -Entry $entry -RepoRoot $repoRoot -State $state }
        ($entry.ContainsKey('Kind') -and $entry.Kind -eq 'LevelTabs') { Pull-LevelWorkbook -Key $key -Entry $entry -RepoRoot $repoRoot -State $state }
        default                                                       { Pull-FlatWorkbook  -Key $key -Entry $entry -RepoRoot $repoRoot -State $state }
    }
}

if (-not $anyApplied) {
    Write-Host "Nothing new from Google." -ForegroundColor Yellow
    exit 0
}
