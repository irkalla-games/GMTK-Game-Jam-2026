<#
.SYNOPSIS
    Copies one (or every) design workbook's current cells to its Google Sheets mirror.

.DESCRIPTION
    Read-only against the .xlsx - it only opens the workbook to read cells, never to write. Run after
    Export-*.ps1 (or as the last step of Sync ... With Sheet) so the phone always sees what Unity's
    assets currently say. Records, per tab, a hash of the cells the phone is allowed to edit -
    Pull-GoogleSheet.ps1 refuses to apply anything for a tab whose hash has drifted, which is how it
    tells "the phone edited this" from "Excel changed under us since the last push".

    Creates the spreadsheet (and any tab that does not exist yet - a new card class is never added this
    way, but a newly-generated enemy prefab or level needs no extra step here) on first use; a spreadsheet
    that already exists is never deleted or renamed, only added to.

    Card, Equipment and Level "Runs"/"Placements"/"Party" tabs are genuinely flat header/row tables and
    are read with Import-Excel, same as the rest of Tools/. Enemy/Boss body tabs and each level's scalar
    fields are label-scanned (column A holds the label, column B its value) - read the same "find by
    label, never by row number" way EnemySheet.Common.psm1's Read-BodySheetTab and
    LevelSheet.Common.psm1's Read-LevelSheetTab already do, reusing those functions directly rather than
    re-implementing the scan.

.PARAMETER Workbook
    One of Cards, Enemies, Bosses, Levels, Equipment.

.PARAMETER All
    Push every workbook in Tools/GoogleBridge/workbooks.psd1.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/GoogleBridge/Push-GoogleSheet.ps1 -Workbook Cards
#>
[CmdletBinding(DefaultParameterSetName = 'One')]
param(
    [Parameter(ParameterSetName = 'One', Mandatory)]
    [ValidateSet('Cards', 'Enemies', 'Bosses', 'Levels', 'Equipment')]
    [string]$Workbook,

    [Parameter(ParameterSetName = 'All', Mandatory)]
    [switch]$All
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'GoogleBridge.Common.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\LevelSheet\LevelSheet.Common.psm1') -Force -DisableNameChecking      # pulls in CardSheet.Common + EnemySheet.Common too
Import-Module (Join-Path $PSScriptRoot '..\EquipmentSheet\EquipmentSheet.Common.psm1') -Force -DisableNameChecking
$repoRoot = Get-BridgeRoot

if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    throw "The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser"
}
Import-Module ImportExcel -DisableNameChecking

$manifest = Get-WorkbookManifest
$keys = if ($All) { @($manifest.Keys) } else { @($Workbook) }

# -----------------------------------------------------------------------------------------------
# Flat header/row tabs - Cards, Equipment, and Level's Runs/Placements/Party. Columns are resolved by
# NAME (Import-Excel is header-keyed), so a tab's on-disk column order is purely cosmetic here, same as
# everywhere else in Tools/.
# -----------------------------------------------------------------------------------------------

function Resolve-TabSpec {
    <# Fills in Pullable/AnchorColumns for a tab whose columns come from modifier-schema.json instead of
       being listed in workbooks.psd1 - Equipment's Modifiers and Card Tuning tabs. #>
    param($Spec, [string]$RepoRoot)

    if (-not $Spec.ContainsKey('DynamicSchema')) { return $Spec }

    # Import-ModifierSchema wants the FOLDER modifier-schema.json lives in (Tools/EquipmentSheet), not
    # the repo root - easy to get backwards since every other helper in this script takes RepoRoot.
    $schema = Import-ModifierSchema -ToolDir (Join-Path $RepoRoot 'Tools\EquipmentSheet')
    $resolved = $Spec.Clone()
    if ($Spec.DynamicSchema -eq 'Equipment') {
        $all = Get-ModifierColumns -Schema $schema
        $resolved.Pullable = @($all | Where-Object { $_ -notin @('Mod Id', 'GUID', 'Sync') })
    }
    else {
        $all = Get-CardTuningColumns -Schema $schema
        $resolved.Pullable = @($all | Where-Object { $_ -notin @('Sub Id', 'Mod Id', 'GUID', 'Sync') })
    }
    return $resolved
}

function Get-FlatTabValues {
    <# Header row (AnchorColumns + Pullable) + one data row per sheet row, values fetched by name so a
       missing column on an odd tab just comes back blank rather than throwing. #>
    param([string]$WorkbookPath, $Spec)

    $header = @($Spec.AnchorColumns) + @($Spec.Pullable)
    $rows = @()
    try {
        $rows = @(Import-Excel -Path $WorkbookPath -WorksheetName $Spec.Sheet -ErrorAction Stop -WarningAction SilentlyContinue)
    }
    catch {
        Write-Verbose "No '$($Spec.Sheet)' tab yet."
    }

    $values = @(, $header)
    foreach ($row in $rows) {
        $names = $row.PSObject.Properties.Name
        $values += , @($header | ForEach-Object { if ($names -contains $_) { "$($row.$_)" } else { '' } })
    }

    # Import-Excel's own idea of "how many rows" is not consistent across tabs - Model comes back with
    # ~70 entirely-blank trailing rows appended after its real ~30, while other tabs stop exactly at
    # their last real row. Truncating at the FIRST all-blank row (not trimming backward from the last
    # row) is what Model specifically needs: Write-CardWorkbook.ps1 puts footer/instructional text below
    # the real Term/Value rows, separated by exactly one blank row - the true data's own tail is never
    # blank, only that separator is, so a from-the-end trim never reaches it and the footer text (which
    # DOES have non-blank cells) survives into the middle of what should be pure data. Truncating at the
    # first blank row draws the same line Pull-GoogleSheet.ps1's Read-CurrentFlatTab draws, regardless of
    # what junk either side's raw scan happens to find further down.
    $lastReal = $values.Count - 1
    for ($i = 1; $i -lt $values.Count; $i++) {
        $isBlank = $true
        foreach ($v in $values[$i]) { if ($v -ne '') { $isBlank = $false; break } }
        if ($isBlank) { $lastReal = $i - 1; break }
    }
    if ($lastReal -lt $values.Count - 1) { $values = $values[0..$lastReal] }

    # The leading comma matters whenever a tab has zero data rows (a brand-new Ideas row, say): with
    # just the header row, $values has exactly ONE element. A plain "return $values" lets PowerShell's
    # pipeline enumerate that single-element array and hand the CALLER its one element (the header row
    # itself) instead of the wrapping array - silently turning a 1-row table into a bare list of column
    # names. That is exactly what produced Google's "Invalid value ... ListValue" 400 the first time
    # this shipped: a single flat row of strings where Sheets expected one row CONTAINING those strings.
    return ,$values
}

function Get-PullableHash {
    <# Hash of ONLY the columns the phone may edit, in row order - what Pull-GoogleSheet.ps1 compares
       against to tell whether Excel moved these cells since this push. #>
    param($Values, $Spec)

    $header = $Values[0]
    $pullIdx = @($Spec.Pullable | ForEach-Object { [array]::IndexOf($header, $_) })
    $rows = @()
    for ($i = 1; $i -lt $Values.Count; $i++) {
        $rows += , @($pullIdx | ForEach-Object { $Values[$i][$_] })
    }
    return Get-CellsHash -Rows $rows
}

# -----------------------------------------------------------------------------------------------
# Label-scanned tabs - Enemy/Boss bodies and a level's scalar fields.
# -----------------------------------------------------------------------------------------------

function Get-BodyReferenceBlock {
    <#
        .SYNOPSIS
            The read-only balance block for one body tab: which labels it carries, their values, and -
            for the cells Excel computes with a formula - the formula itself, rewritten for the Google
            layout.

        .DESCRIPTION
            Every row is found by label (never by row number) and is one of the rows
            Write-EnemyWorkbook.ps1 already owns, so nothing here re-derives a number the workbook
            already defines.

            A cell that carries a formula in Excel is emitted AS a formula rather than as its cached
            value. That is what makes Card Power / Status Power / Range / Estimated Power Level actually
            compute in Google instead of showing blank: EPPlus never evaluates them, so their cached
            value is empty, but Google will happily do the arithmetic itself once the pw_* named ranges
            exist. The formula's row references are remapped (see Convert-FormulaRowRefs) because the two
            layouts put the same labels on different rows.

            Returns @{ Rows; FormulaRowIndexes } - Rows is label-plus-cells, and FormulaRowIndexes marks
            which of them must be written with USER_ENTERED rather than RAW.
    #>
    param($Worksheet, $Entry, [int]$DeckLength, [hashtable]$ExcelRowToGoogleRow)

    $result = @{ Rows = @(); FormulaRowIndexes = @() }
    if (-not $Entry.Contains('ReferenceFields') -or $DeckLength -le 0) { return $result }

    $labelRows = Get-LabelRowMap -Worksheet $Worksheet
    $lastCol = $Worksheet.Dimension.End.Column

    foreach ($label in $Entry.ReferenceFields) {
        if (-not $labelRows.Contains($label)) { continue }
        $r = $labelRows[$label]

        $cells = @()
        $any = $false
        $hasFormula = $false
        for ($i = 0; $i -lt $DeckLength; $i++) {
            $c = 2 + $i
            $text = ''
            if ($c -le $lastCol) {
                $formula = $Worksheet.Cells[$r, $c].Formula
                if (-not [string]::IsNullOrWhiteSpace($formula)) {
                    $text = '=' + (Convert-FormulaRowRefs -Formula $formula -RowMap $ExcelRowToGoogleRow)
                    $hasFormula = $true
                }
                else {
                    $text = "$($Worksheet.Cells[$r, $c].Value)"
                }
            }
            if ($text -ne '') { $any = $true }
            $cells += $text
        }

        if (-not $any) { continue }
        $result.Rows += , (@($label) + $cells)
        if ($hasFormula) { $result.FormulaRowIndexes += ($result.Rows.Count - 1) }
    }
    return $result
}

function Get-BodyLayoutRowMap {
    <#
        Excel row -> Google row (both 1-based), for every label that appears on both sides. Built before
        any formula is rewritten, because a formula may reference a row anywhere in the layout - an
        editable field near the top (Health, Actions Per Turn) just as easily as another reference row.

        Mirrors Get-BodyTabValues's layout exactly: header, PullableFields in order, Deck, GUID,
        separator, then the reference rows that actually survive the "has content" test.
    #>
    param($Worksheet, $Entry, [string[]]$EmittedReferenceLabels)

    $labelRows = Get-LabelRowMap -Worksheet $Worksheet
    $map = @{}

    $googleRow = 1                              # row 1 is the Field/Value header
    foreach ($field in $Entry.PullableFields) {
        $googleRow++
        if ($labelRows.Contains($field)) { $map[$labelRows[$field]] = $googleRow }
    }
    if ($Entry.DeckField) {
        $googleRow++
        if ($labelRows.Contains($Entry.DeckField)) { $map[$labelRows[$Entry.DeckField]] = $googleRow }
    }
    $googleRow++                                # GUID row
    $googleRow++                                # separator row

    foreach ($label in $EmittedReferenceLabels) {
        $googleRow++
        if ($labelRows.Contains($label)) { $map[$labelRows[$label]] = $googleRow }
    }
    return $map
}

function Get-BodyReferenceLabels {
    <# Which reference labels a tab will actually emit - the same "has any content" test
       Get-BodyReferenceBlock applies, run first so row positions can be known before formulas are
       rewritten to point at them. #>
    param($Worksheet, $Entry, [int]$DeckLength)

    $labels = @()
    if (-not $Entry.Contains('ReferenceFields') -or $DeckLength -le 0) { return ,$labels }

    $labelRows = Get-LabelRowMap -Worksheet $Worksheet
    $lastCol = $Worksheet.Dimension.End.Column

    foreach ($label in $Entry.ReferenceFields) {
        if (-not $labelRows.Contains($label)) { continue }
        $r = $labelRows[$label]
        $any = $false
        for ($i = 0; $i -lt $DeckLength; $i++) {
            $c = 2 + $i
            if ($c -gt $lastCol) { continue }
            # Pulled into a variable first: "Cells[$r, $c]" written directly inside a method-call
            # argument list does not parse, because the comma reads as an argument separator there.
            $cell = $Worksheet.Cells[$r, $c]
            if (-not [string]::IsNullOrWhiteSpace($cell.Formula)) { $any = $true; break }
            if ("$($cell.Value)" -ne '') { $any = $true; break }
        }
        if ($any) { $labels += $label }
    }
    return ,$labels
}

function Get-BodyTabValues {
    param($Body, $Entry, $Reference)

    $rows = @(, @('Field', 'Value'))
    foreach ($field in $Entry.PullableFields) {
        $prop = Get-BodyFieldPropertyName -Field $field
        $value = $Body.PSObject.Properties[$prop]
        $rows += , @($field, "$(if ($value) { $value.Value } else { '' })")
    }
    if ($Entry.DeckField) {
        # One card per CELL across the row rather than one pipe-joined string, so each slot can carry a
        # dropdown. Pull-GoogleSheet.ps1's Sync-BodyTab reads this row back the same way.
        $rows += , (@($Entry.DeckField) + @($Body.Deck))
    }
    $rows += , @('GUID (reference only)', $Body.Guid)

    # Everything past here is reference only - Sync-BodyTab never writes any of it back, because none of
    # these labels is one of PullableFields.
    $refRows = @($Reference)
    if ($refRows.Count -gt 0) {
        $rows += , @('--- below is read-only reference, edits here are ignored ---')
        foreach ($refRow in $refRows) { $rows += , $refRow }
    }

    return ,$rows   # see the comment in Get-FlatTabValues on why the leading comma is load-bearing
}

function Get-VerbatimTabValues {
    <# A whole worksheet copied cell for cell, so named ranges pointing into it keep their exact Excel
       addresses. Formula cells fall back to their cached value (these tabs are plain data - the weights
       table and the Summoned mirror - so in practice there are none). #>
    param($Worksheet)

    $rows = @()
    if ($null -eq $Worksheet -or $null -eq $Worksheet.Dimension) { return ,$rows }

    $lastRow = $Worksheet.Dimension.End.Row
    $lastCol = $Worksheet.Dimension.End.Column
    for ($r = 1; $r -le $lastRow; $r++) {
        $cells = @()
        for ($c = 1; $c -le $lastCol; $c++) { $cells += "$($Worksheet.Cells[$r, $c].Value)" }
        $rows += , $cells
    }
    return ,$rows
}

function Get-LevelScalarValues {
    param($Level, $Entry)

    $rows = @(, @('Field', 'Value'))
    foreach ($field in $Entry.PullableFields) {
        $prop = ($field -replace '[^A-Za-z0-9]', '')
        $value = $Level.PSObject.Properties[$prop]
        $text = if ($value) {
            if ($value.Value -is [System.Collections.IEnumerable] -and $value.Value -isnot [string]) { ($value.Value -join ';') }
            else { "$($value.Value)" }
        } else { '' }
        $rows += , @($field, $text)
    }
    $rows += , @('GUID (reference only)', $Level.Guid)
    return ,$rows   # see the comment in Get-FlatTabValues on why the leading comma is load-bearing
}

function Get-PlacementsValues {
    <# Turn/Row/Col/Prefab order matches Format-LevelLayout's own sort, so the sheet is stable across
       pushes even though Placements comes back in board-scan order. #>
    param($Level)

    $sorted = @($Level.Placements | Sort-Object Turn, Row, Col, Prefab)
    $rows = @(, @('Turn', 'Col', 'Row', 'Prefab', 'Deck'))
    foreach ($p in $sorted) {
        $turnText = if ($p.Turn -eq 0) { 'Start' } else { "$($p.Turn)" }
        $rows += , @($turnText, "$($p.Col)", "$($p.Row)", $p.Prefab, ((@($p.Deck)) -join ';'))
    }
    # Load-bearing when a level has zero placements - see the comment in Get-FlatTabValues.
    return ,$rows
}

function Get-PartyValues {
    param($Level)

    $sorted = @($Level.PartySpawn | Sort-Object Index)
    $rows = @(, @('Slot', 'Col', 'Row'))
    foreach ($p in $sorted) { $rows += , @("$($p.Index)", "$($p.Col)", "$($p.Row)") }
    # Load-bearing when a level has zero (or one) party member - see the comment in Get-FlatTabValues.
    return ,$rows
}

# -----------------------------------------------------------------------------------------------
# Spreadsheet plumbing - create on first use, add any tab that does not exist yet, never delete one.
# -----------------------------------------------------------------------------------------------

function Get-OrCreateSpreadsheet {
    param([string]$Key, [string]$Title, [string[]]$TabNames, $State)

    if ($State.spreadsheets.Contains($Key) -and $State.spreadsheets[$Key]) {
        $id = $State.spreadsheets[$Key]
        try {
            $meta = Get-GoogleSpreadsheetMeta -SpreadsheetId $id
        }
        catch {
            throw "Google spreadsheet '$Title' (id $id, recorded in Tools/GoogleBridge/state.json) could not be opened - " +
                  "$($_.Exception.Message). If it was deleted, remove its entry from state.json and push again to recreate it."
        }
        $existingNames = @($meta.sheets | ForEach-Object { $_.properties.title })
        $missing = @($TabNames | Where-Object { $_ -notin $existingNames })
        if ($missing.Count -gt 0) {
            $requests = @($missing | ForEach-Object { @{ addSheet = @{ properties = @{ title = $_ } } } })
            Invoke-GoogleBatchUpdate -SpreadsheetId $id -Requests $requests
        }
        return $id
    }

    Write-Host "Creating Google Sheet '$Title'..." -ForegroundColor Cyan
    $id = New-GoogleSpreadsheet -Title $Title -SheetTitles $TabNames
    $State.spreadsheets[$Key] = $id

    # Sheets API always creates a default "Sheet1" alongside the ones we asked for.
    $meta = Get-GoogleSpreadsheetMeta -SpreadsheetId $id
    $stray = @($meta.sheets | Where-Object { $_.properties.title -eq 'Sheet1' -and $_.properties.title -notin $TabNames })
    if ($stray.Count -gt 0) {
        Invoke-GoogleBatchUpdate -SpreadsheetId $id -Requests @(@{ deleteSheet = @{ sheetId = $stray[0].properties.sheetId } })
    }

    Write-Host "Created. Open it at https://docs.google.com/spreadsheets/d/$id/edit" -ForegroundColor Green
    return $id
}

function Format-NewTabs {
    <# Freeze + bold the header row on every tab, every push. Unconditional rather than "only if new" -
       cheap to repeat next to the values calls, and simpler than tracking which tabs already have it. #>
    param([string]$SpreadsheetId, $Meta)

    $requests = @()
    foreach ($sheet in $Meta.sheets) {
        $sheetId = $sheet.properties.sheetId
        $requests += @{ updateSheetProperties = @{
            properties = @{ sheetId = $sheetId; gridProperties = @{ frozenRowCount = 1 } }
            fields     = 'gridProperties.frozenRowCount'
        } }
        $requests += @{ repeatCell = @{
            range      = @{ sheetId = $sheetId; startRowIndex = 0; endRowIndex = 1 }
            cell       = @{ userEnteredFormat = @{ textFormat = @{ bold = $true } } }
            fields     = 'userEnteredFormat.textFormat.bold'
        } }
    }
    if ($requests.Count -gt 0) { Invoke-GoogleBatchUpdate -SpreadsheetId $SpreadsheetId -Requests $requests }
}

# -----------------------------------------------------------------------------------------------
# Per-workbook drivers
# -----------------------------------------------------------------------------------------------

function Get-WorkbookEnumLists {
    <# The Enums tab of one workbook, read through its own EPPlus handle. #>
    param([string]$WorkbookPath)

    $pkg = Open-ExcelPackage -Path $WorkbookPath
    try {
        return Get-EnumLists -Worksheet $pkg.Workbook.Worksheets['Enums']
    }
    finally {
        Close-ExcelPackage -ExcelPackage $pkg -NoSave
    }
}

function Get-ColumnDropdownSpecs {
    <# Dropdown specs for a flat tab's columns. Rows run from the first data row to well past the last
       one so a row added from the phone still gets the dropdown. #>
    param($Spec, $Values, $Lists)

    $specs = @()
    if (-not $Spec.Contains('Dropdowns')) { return $specs }

    $header = @($Spec.AnchorColumns) + @($Spec.Pullable)
    foreach ($colName in $Spec.Dropdowns.Keys) {
        $listName = $Spec.Dropdowns[$colName]
        if (-not $Lists.ContainsKey($listName)) { continue }
        $idx = [array]::IndexOf($header, $colName)
        if ($idx -lt 0) { continue }
        $specs += @{
            Sheet    = $Spec.Sheet
            StartRow = 1
            EndRow   = ($Values.Count + 200)
            StartCol = $idx
            EndCol   = ($idx + 1)
            Values   = $Lists[$listName]
        }
    }
    return $specs
}

function Push-FlatWorkbook {
    param([string]$Key, $Entry, [string]$RepoRoot, $State)

    $xlsxPath = Join-Path $RepoRoot $Entry.Xlsx
    Assert-WorkbookReadable -WorkbookPath $xlsxPath

    $specs = @($Entry.Tabs | ForEach-Object { Resolve-TabSpec -Spec $_ -RepoRoot $RepoRoot })
    $tabNames = @($specs | ForEach-Object { $_.Sheet })
    $lists = Get-WorkbookEnumLists -WorkbookPath $xlsxPath
    $id = Get-OrCreateSpreadsheet -Key $Key -Title $Entry.Title -TabNames $tabNames -State $State
    Write-BridgeState -State $State   # persist the id immediately - a failure below must not recreate it next run

    $rangeValues = @{}
    $dropdowns = @()
    foreach ($spec in $specs) {
        $values = Get-FlatTabValues -WorkbookPath $xlsxPath -Spec $spec
        $rangeValues["'$($spec.Sheet)'!A1"] = $values
        $State.tabHashes[(Get-TabHashKey $Key $spec.Sheet)] = Get-PullableHash -Values $values -Spec $spec
        $dropdowns += @(Get-ColumnDropdownSpecs -Spec $spec -Values $values -Lists $lists)
    }

    Clear-GoogleTabs -SpreadsheetId $id -SheetNames $tabNames
    Set-GoogleValues -SpreadsheetId $id -RangeValues $rangeValues
    $meta = Get-GoogleSpreadsheetMeta -SpreadsheetId $id
    Format-NewTabs -SpreadsheetId $id -Meta $meta
    Set-GoogleDropdowns -SpreadsheetId $id -Meta $meta -Specs $dropdowns
    Write-Host "Pushed $($specs.Count) tab(s) to '$($Entry.Title)'." -ForegroundColor Green
}

function Get-NamedRangeSpecs {
    <#
        .SYNOPSIS
            Which named ranges to recreate in Google, derived from the workbook's own.

        .DESCRIPTION
            Three cases, and only the third needs any thought:

              pw_* / range_Power  point into a verbatim-pushed tab (PowerLevel), so their Excel address
                                  carries over unchanged - that is the whole reason those tabs are pushed
                                  cell for cell rather than reshaped.
              pow_<Prefab>        pointing at a verbatim tab (the boss workbook's Summoned mirror) is
                                  likewise unchanged.
              pow_<Prefab>        pointing at a BODY tab's $B$16 is the one that moves: the Google layout
                                  puts Estimated Power Level somewhere else, so it is remapped to
                                  wherever that tab actually emitted it.

            list_* are skipped - the Enums tab is not pushed, and the dropdowns carry their values
            inline rather than referencing a range.
    #>
    param($ExcelNames, [string[]]$VerbatimTabs, [hashtable]$EstimatedRow)

    $specs = @()
    foreach ($entry in @($ExcelNames)) {
        if ($entry.Name -like 'list_*') { continue }

        $parsed = ConvertFrom-ExcelAddress -Address $entry.Address
        if ($null -eq $parsed) { continue }

        if ($VerbatimTabs -contains $parsed.Sheet) {
            $specs += @{ Name = $entry.Name; Sheet = $parsed.Sheet
                         StartRow = $parsed.StartRow; EndRow = $parsed.EndRow
                         StartCol = $parsed.StartCol; EndCol = $parsed.EndCol }
            continue
        }

        if ($EstimatedRow.ContainsKey($parsed.Sheet)) {
            $row = $EstimatedRow[$parsed.Sheet]
            $specs += @{ Name = $entry.Name; Sheet = $parsed.Sheet
                         StartRow = ($row - 1); EndRow = $row
                         StartCol = 1; EndCol = 2 }      # column B
        }
    }
    return $specs
}

function Push-BodyWorkbook {
    param([string]$Key, $Entry, [string]$RepoRoot, $State)

    $xlsxPath = Join-Path $RepoRoot $Entry.Xlsx
    Assert-WorkbookReadable -WorkbookPath $xlsxPath

    $verbatimNames = @($Entry.VerbatimTabs)

    $pkg = Open-ExcelPackage -Path $xlsxPath
    $bodies = @()
    $references = @{}
    $verbatim = @{}
    $excelNames = @()
    $estimatedGoogleRow = @{}
    try {
        foreach ($ws in $pkg.Workbook.Worksheets) {
            $body = Read-BodySheetTab -Worksheet $ws
            if ($null -eq $body) { continue }
            $bodies += $body

            # Captured now, while the package is still open - the push itself happens after it closes.
            # Two passes: which reference rows survive, so their Google row positions are known, and only
            # then the rows themselves, whose formulas are rewritten to point at those positions.
            $deckLength = @($body.Deck).Count
            $emitted = Get-BodyReferenceLabels -Worksheet $ws -Entry $Entry -DeckLength $deckLength
            $rowMap = Get-BodyLayoutRowMap -Worksheet $ws -Entry $Entry -EmittedReferenceLabels $emitted
            $references[$body.Prefab] = Get-BodyReferenceBlock -Worksheet $ws -Entry $Entry `
                                            -DeckLength $deckLength -ExcelRowToGoogleRow $rowMap

            # Where this tab's Estimated Power Level ends up, for the pow_<Prefab> named ranges that
            # other tabs' Summon Power formulas reference.
            $labelRows = Get-LabelRowMap -Worksheet $ws
            if ($labelRows.Contains('Estimated Power Level') -and $rowMap.ContainsKey($labelRows['Estimated Power Level'])) {
                $estimatedGoogleRow[$body.Prefab] = $rowMap[$labelRows['Estimated Power Level']]
            }
        }

        foreach ($name in $verbatimNames) {
            $vws = $pkg.Workbook.Worksheets[$name]
            if ($null -ne $vws) { $verbatim[$name] = Get-VerbatimTabValues -Worksheet $vws }
        }

        # The workbook's own named ranges are the source of truth for what pw_Damage et al point at -
        # read rather than restated, so a weight moving a row in Excel needs no change here.
        foreach ($n in $pkg.Workbook.Names) { $excelNames += @{ Name = $n.Name; Address = "$($n.FullAddress)" } }
    }
    finally {
        Close-ExcelPackage -ExcelPackage $pkg -NoSave
    }

    if ($bodies.Count -eq 0) {
        Write-Host "No recognisable body tabs in $($Entry.Xlsx) yet - nothing to push." -ForegroundColor Yellow
        return
    }

    $tabNames = @($bodies | ForEach-Object { $_.Prefab }) + @($verbatim.Keys)
    $lists = Get-WorkbookEnumLists -WorkbookPath $xlsxPath
    $id = Get-OrCreateSpreadsheet -Key $Key -Title $Entry.Title -TabNames $tabNames -State $State
    Write-BridgeState -State $State

    # A body tab's layout is fixed by Get-BodyTabValues: row 0 is the header, then one row per
    # PullableField in order, then the Deck row, then GUID. That makes each field's row index
    # computable rather than something to search for.
    $fieldRow = @{}
    for ($i = 0; $i -lt $Entry.PullableFields.Count; $i++) { $fieldRow[$Entry.PullableFields[$i]] = $i + 1 }
    $deckRowIndex = $Entry.PullableFields.Count + 1

    $rangeValues = @{}
    $formulaValues = @{}
    $dropdowns = @()
    $shading = @()
    foreach ($body in $bodies) {
        $block = $references[$body.Prefab]
        $values = Get-BodyTabValues -Body $body -Entry $Entry -Reference $block.Rows
        $rangeValues["'$($body.Prefab)'!A1"] = $values

        # Formula rows are pulled OUT of the RAW payload and re-sent as USER_ENTERED, otherwise a leading
        # "=" is stored as literal text. Only their cells (column B onward) move across; the label in
        # column A still goes with the RAW push.
        # 1-based sheet row of the first reference row: header(1) + fields(N) + deck(1) + GUID(1) +
        # separator(1) + 1. Must agree with Get-BodyLayoutRowMap, which the formulas were rewritten
        # against - if these two ever disagree the formulas point at the wrong rows.
        $refStart = $Entry.PullableFields.Count + 5
        foreach ($refIndex in @($block.FormulaRowIndexes)) {
            $sheetRow = $refStart + $refIndex
            $cells = @($block.Rows[$refIndex])
            $formulaValues["'$($body.Prefab)'!B$sheetRow"] = @(, @($cells[1..($cells.Count - 1)]))
            for ($ci = 1; $ci -lt $cells.Count; $ci++) { $values[$sheetRow - 1][$ci] = '' }
        }

        # Built from the pullable fields directly rather than by filtering rows back out of $values -
        # Sync-BodyTab builds its comparison hash exactly this way, and doing it independently here means
        # adding reference rows to the pushed layout can never shift the hash and refuse every tab.
        $pullableRows = @()
        foreach ($field in $Entry.PullableFields) {
            $prop = Get-BodyFieldPropertyName -Field $field
            $v = $body.PSObject.Properties[$prop]
            $pullableRows += , @($field, "$(if ($v) { $v.Value } else { '' })")
        }
        if ($Entry.DeckField) { $pullableRows += , (@($Entry.DeckField) + @($body.Deck)) }
        $State.tabHashes[(Get-TabHashKey $Key $body.Prefab)] = Get-CellsHash -Rows $pullableRows

        # Grey out the reference block so it reads as "look, don't touch" on a phone.
        $refCount = @($block.Rows).Count
        if ($refCount -gt 0) {
            $separatorIndex = $Entry.PullableFields.Count + 2   # header + fields + deck + GUID
            $shading += @{ Sheet = $body.Prefab; StartRow = $separatorIndex; EndRow = ($separatorIndex + $refCount + 1) }
        }

        if ($Entry.Contains('FieldDropdowns')) {
            foreach ($field in $Entry.FieldDropdowns.Keys) {
                $listName = $Entry.FieldDropdowns[$field]
                if (-not $lists.ContainsKey($listName) -or -not $fieldRow.ContainsKey($field)) { continue }
                $dropdowns += @{
                    Sheet = $body.Prefab; StartRow = $fieldRow[$field]; EndRow = ($fieldRow[$field] + 1)
                    StartCol = 1; EndCol = 2; Values = $lists[$listName]
                }
            }
        }
        if ($Entry.Contains('DeckDropdown') -and $lists.ContainsKey($Entry.DeckDropdown)) {
            # Well past the current deck length so slots can be added from the phone.
            $dropdowns += @{
                Sheet = $body.Prefab; StartRow = $deckRowIndex; EndRow = ($deckRowIndex + 1)
                StartCol = 1; EndCol = 31; Values = $lists[$Entry.DeckDropdown]
            }
        }
    }

    foreach ($name in $verbatim.Keys) {
        $rangeValues["'$name'!A1"] = $verbatim[$name]
        $shading += @{ Sheet = $name; StartRow = 0; EndRow = (@($verbatim[$name]).Count) }
    }

    Clear-GoogleTabs -SpreadsheetId $id -SheetNames $tabNames
    Set-GoogleValues -SpreadsheetId $id -RangeValues $rangeValues

    $meta = Get-GoogleSpreadsheetMeta -SpreadsheetId $id

    # Named ranges BEFORE the formulas that use them, so nothing is ever entered against a name Google
    # does not know yet.
    Set-GoogleNamedRanges -SpreadsheetId $id -Meta $meta `
        -Specs (Get-NamedRangeSpecs -ExcelNames $excelNames -VerbatimTabs $verbatimNames -EstimatedRow $estimatedGoogleRow)

    Set-GoogleFormulas -SpreadsheetId $id -RangeValues $formulaValues
    Format-NewTabs -SpreadsheetId $id -Meta $meta
    Set-GoogleDropdowns -SpreadsheetId $id -Meta $meta -Specs $dropdowns
    Set-GoogleRowShading -SpreadsheetId $id -Meta $meta -Specs $shading
    Write-Host "Pushed $($bodies.Count) prefab(s) to '$($Entry.Title)'." -ForegroundColor Green
}

function Push-LevelWorkbook {
    param([string]$Key, $Entry, [string]$RepoRoot, $State)

    $xlsxPath = Join-Path $RepoRoot $Entry.Xlsx
    Assert-WorkbookReadable -WorkbookPath $xlsxPath

    $pkg = Open-ExcelPackage -Path $xlsxPath
    $levels = @()
    try {
        foreach ($ws in $pkg.Workbook.Worksheets) {
            $level = Read-LevelSheetTab -Worksheet $ws
            if ($null -ne $level) { $levels += $level }
        }
    }
    finally {
        Close-ExcelPackage -ExcelPackage $pkg -NoSave
    }

    $extraSpecs = @($Entry.ExtraTables | ForEach-Object { Resolve-TabSpec -Spec $_ -RepoRoot $RepoRoot })

    $tabNames = @()
    foreach ($level in $levels) { $tabNames += $level.Name, "$($level.Name) Placements", "$($level.Name) Party" }
    $tabNames += @($extraSpecs | ForEach-Object { $_.Sheet })

    $lists = Get-WorkbookEnumLists -WorkbookPath $xlsxPath
    $id = Get-OrCreateSpreadsheet -Key $Key -Title $Entry.Title -TabNames $tabNames -State $State
    Write-BridgeState -State $State

    # Scalar tabs share Get-LevelScalarValues's fixed layout: header, then PullableFields in order.
    $fieldRow = @{}
    for ($i = 0; $i -lt $Entry.PullableFields.Count; $i++) { $fieldRow[$Entry.PullableFields[$i]] = $i + 1 }

    $rangeValues = @{}
    $dropdowns = @()
    foreach ($level in $levels) {
        $scalar = Get-LevelScalarValues -Level $level -Entry $Entry
        $rangeValues["'$($level.Name)'!A1"] = $scalar
        $pullableRows = @($scalar | Select-Object -Skip 1 | Where-Object { $_[0] -ne 'GUID (reference only)' })
        $State.tabHashes[(Get-TabHashKey $Key $level.Name)] = Get-CellsHash -Rows $pullableRows

        if ($Entry.Contains('FieldDropdowns')) {
            foreach ($field in $Entry.FieldDropdowns.Keys) {
                $listName = $Entry.FieldDropdowns[$field]
                if (-not $lists.ContainsKey($listName) -or -not $fieldRow.ContainsKey($field)) { continue }
                $dropdowns += @{
                    Sheet = $level.Name; StartRow = $fieldRow[$field]; EndRow = ($fieldRow[$field] + 1)
                    StartCol = 1; EndCol = 2; Values = $lists[$listName]
                }
            }
        }

        $placements = Get-PlacementsValues -Level $level
        $rangeValues["'$($level.Name) Placements'!A1"] = $placements
        $State.tabHashes[(Get-TabHashKey $Key "$($level.Name) Placements")] = Get-CellsHash -Rows (@($placements | Select-Object -Skip 1))

        if ($Entry.Contains('PlacementDropdowns')) {
            $placementHeader = @('Turn', 'Col', 'Row', 'Prefab', 'Deck')
            foreach ($colName in $Entry.PlacementDropdowns.Keys) {
                $listName = $Entry.PlacementDropdowns[$colName]
                if (-not $lists.ContainsKey($listName)) { continue }
                $idx = [array]::IndexOf($placementHeader, $colName)
                if ($idx -lt 0) { continue }
                $dropdowns += @{
                    Sheet = "$($level.Name) Placements"; StartRow = 1; EndRow = ($placements.Count + 50)
                    StartCol = $idx; EndCol = ($idx + 1); Values = $lists[$listName]
                }
            }
        }

        $party = Get-PartyValues -Level $level
        $rangeValues["'$($level.Name) Party'!A1"] = $party
        $State.tabHashes[(Get-TabHashKey $Key "$($level.Name) Party")] = Get-CellsHash -Rows (@($party | Select-Object -Skip 1))
    }
    foreach ($spec in $extraSpecs) {
        $values = Get-FlatTabValues -WorkbookPath $xlsxPath -Spec $spec
        $rangeValues["'$($spec.Sheet)'!A1"] = $values
        $State.tabHashes[(Get-TabHashKey $Key $spec.Sheet)] = Get-PullableHash -Values $values -Spec $spec
        $dropdowns += @(Get-ColumnDropdownSpecs -Spec $spec -Values $values -Lists $lists)
    }

    Clear-GoogleTabs -SpreadsheetId $id -SheetNames $tabNames
    Set-GoogleValues -SpreadsheetId $id -RangeValues $rangeValues
    $meta = Get-GoogleSpreadsheetMeta -SpreadsheetId $id
    Format-NewTabs -SpreadsheetId $id -Meta $meta
    Set-GoogleDropdowns -SpreadsheetId $id -Meta $meta -Specs $dropdowns
    Write-Host "Pushed $($levels.Count) level(s) to '$($Entry.Title)'." -ForegroundColor Green
}

# -----------------------------------------------------------------------------------------------

foreach ($key in $keys) {
    $entry = $manifest[$key]
    if (-not $entry) { throw "Unknown workbook '$key'." }

    $state = Read-BridgeState
    switch ($true) {
        ($entry.ContainsKey('Kind') -and $entry.Kind -eq 'BodyTabs')  { Push-BodyWorkbook  -Key $key -Entry $entry -RepoRoot $repoRoot -State $state }
        ($entry.ContainsKey('Kind') -and $entry.Kind -eq 'LevelTabs') { Push-LevelWorkbook -Key $key -Entry $entry -RepoRoot $repoRoot -State $state }
        default                                                       { Push-FlatWorkbook  -Key $key -Entry $entry -RepoRoot $repoRoot -State $state }
    }
    Write-BridgeState -State $state
}
