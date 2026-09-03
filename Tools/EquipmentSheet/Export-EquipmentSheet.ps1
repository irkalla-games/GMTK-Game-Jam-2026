<#
.SYNOPSIS
    Builds Docs/EquipmentDesign.xlsx from the authored EquipmentData assets under Assets/Data/Equipment.

.DESCRIPTION
    Read-only against the Unity project: it parses Assets/Data/**/*.asset (for CardData/CardEffect name
    resolution too) and Assets/Data/Equipment/**/*.asset, and writes only into Docs/ and this tool's own
    baseline.json. Safe to run at any time, including with the Editor open.

    The Modifiers and Card Tuning tabs' columns are not hand-listed here - they come from
    Tools/EquipmentSheet/modifier-schema.json, which Unity regenerates by reflecting over every
    EquipmentModifier/CardModifier subclass (Assets/Editor/EquipmentModifierSchema.cs). A missing or
    stale schema is a hard failure below, never a guessed column set - see the schema gate.

.PARAMETER WorkbookPath
    Defaults to Docs/EquipmentDesign.xlsx at the repo root.

.PARAMETER NoCsvMirror
    Skip writing Docs/EquipmentDesign/*.csv.

.PARAMETER WriteBaseline
    Record the state both sides now agree on into baseline.json. Passed by the sync, never by a bare
    export - see Read-Baseline's own doc comment in CardSheet.Common.psm1 for why.

.PARAMETER Force
    Rebuild the sheet even though it holds edits that have not reached the assets, discarding them.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/EquipmentSheet/Export-EquipmentSheet.ps1
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
Import-Module (Join-Path $scriptDir 'EquipmentSheet.Common.psm1') -Force -DisableNameChecking

if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    throw "The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser"
}
Import-Module ImportExcel -DisableNameChecking

if (-not $WorkbookPath) { $WorkbookPath = Join-Path $repoRoot 'Docs\EquipmentDesign.xlsx' }
$csvDir = Join-Path $repoRoot 'Docs\EquipmentDesign'
$equipmentRoot = Join-Path $repoRoot 'Assets\Data\Equipment'

# Fail fast and in plain language if the workbook is open - see Export-CardSheet.ps1's identical guard.
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

# ---------------------------------------------------------------------------------------------------
# Schema gate - never fall back to a guessed column set. See EquipmentSheet.Common's Import-ModifierSchema
# for the missing case; the stale case (a type or field on disk the schema does not know) is checked
# below, once the assets have been read.
# ---------------------------------------------------------------------------------------------------

$schema = Import-ModifierSchema -ToolDir $scriptDir

# ---------------------------------------------------------------------------------------------------
# Read back what the human authored
# ---------------------------------------------------------------------------------------------------

function Get-ExistingSheet {
    param([string]$SheetName)

    if (Test-Path -LiteralPath $WorkbookPath) {
        try {
            $rows = @(Import-Excel -Path $WorkbookPath -WorksheetName $SheetName -ErrorAction Stop -WarningAction SilentlyContinue)
            if ($rows.Count -gt 0) { return $rows }
        }
        catch { Write-Verbose "No '$SheetName' sheet in the existing workbook." }
    }

    $csv = Join-Path $csvDir "$SheetName.csv"
    if (Test-Path -LiteralPath $csv) { return @(Import-Csv -LiteralPath $csv) }

    return @()
}

function Get-PreservedNotes {
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
# Gather
# ---------------------------------------------------------------------------------------------------

Write-Host "Reading assets..." -ForegroundColor Cyan

$scriptGuids = Get-ScriptGuidIndex -RepoRoot $repoRoot
$assetIndex  = Build-AssetIndex -RepoRoot $repoRoot -RelativeFolders @('Assets\Data', 'Assets\Prefabs') -ScriptGuidIndex $scriptGuids
$describeCache = Import-DescribeCache -ToolDir $scriptDir

if (-not (Test-Path -LiteralPath $equipmentRoot)) { throw "$equipmentRoot not found." }

$assetFiles = @(Get-ChildItem -LiteralPath $equipmentRoot -Recurse -Filter '*.asset' -File)
$items = @()
$schemaProblems = @()

foreach ($file in $assetFiles) {
    $item = Read-EquipmentAsset -AssetPath $file.FullName -AssetIndex $assetIndex -Schema $schema -RepoRoot $repoRoot -ScriptGuidIndex $scriptGuids
    $items += $item
    foreach ($p in $item.Problems) { $schemaProblems += "$($item.Name): $p" }
}

Write-Host "  $($items.Count) equipment item(s), $($assetIndex.All.Count) other assets indexed"

if ($schemaProblems.Count -gt 0) {
    throw ("modifier-schema.json is out of date with the assets on disk:`n  " + ($schemaProblems -join "`n  ") +
           "`n`nFocus the Unity Editor once (or run Tools > Equipment > Write Modifier Schema) to " +
           "regenerate it, then run this again.")
}

# ---------------------------------------------------------------------------------------------------
# Refuse to discard unsynced sheet edits - same rule as every other sheet tool.
# ---------------------------------------------------------------------------------------------------

if (-not $Force -and (Test-Path -LiteralPath $WorkbookPath)) {
    $baseline = Read-EquipmentBaseline -ToolDir $scriptDir
    $byGuid = @{}
    foreach ($item in $items) { if ($item.Guid) { $byGuid[$item.Guid] = $item } }

    $pending = @()

    foreach ($row in (Get-ExistingSheet -SheetName 'Equipment')) {
        $guid = ''
        if ($row.PSObject.Properties.Name -contains 'GUID') { $guid = ([string]$row.GUID).Trim() }
        $key = ''
        if ($row.PSObject.Properties.Name -contains 'Key') { $key = ([string]$row.Key).Trim() }

        if (-not $guid) {
            if ($key) { $pending += "$key (new row, not yet created)" }
            continue
        }

        if (-not $byGuid.ContainsKey($guid)) { continue }
        $item = $byGuid[$guid]
        $assetRow = Get-EquipmentMergeRow -Item $item
        $hasBase = $baseline.Items.ContainsKey($guid)

        foreach ($col in (Get-EquipmentMergeColumns)) {
            $sheetValue = ''
            if ($col -eq 'Modifiers') {
                # Reconstructed the same way Import-EquipmentSheet.ps1 will - see that script's
                # Get-SheetModifierTree. Cheap here: this only runs to detect "did the sheet move".
                continue   # the Modifiers tab's own guard covers this column; see below.
            }
            if ($row.PSObject.Properties.Name -contains $col) { $sheetValue = $row.$col }

            $baseValue = $null
            if ($hasBase -and $baseline.Items[$guid].ContainsKey($col)) { $baseValue = $baseline.Items[$guid][$col] }

            $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $assetRow[$col] -Sheet $sheetValue -HasBaselineEntry $hasBase
            if ($verdict -in @('takeSheet', 'conflict', 'noBaseline')) { $pending += "$key -> $col" }
        }
    }

    # The Modifiers tab: any row whose GUID resolves to a known item and whose canonical column
    # (rebuilt straight from the sheet's own Modifiers+Card Tuning rows, the same way the importer
    # does) disagrees with what is on disk counts as a pending edit too.
    $existingModRows = @(Get-ExistingSheet -SheetName 'Modifiers')
    $existingTuningRows = @(Get-ExistingSheet -SheetName 'Card Tuning')
    $seenItemsWithModRows = @{}
    foreach ($row in $existingModRows) {
        if ($row.PSObject.Properties.Name -contains 'Item') { $seenItemsWithModRows[[string]$row.Item] = $true }
    }
    foreach ($key in $seenItemsWithModRows.Keys) {
        $item = $items | Where-Object { $_.Name -eq $key } | Select-Object -First 1
        if ($null -eq $item -or -not $item.Guid) { continue }

        $sheetTree = Get-SheetModifierTree -Key $key -ModifierRows $existingModRows -TuningRows $existingTuningRows -Schema $schema
        $sheetCanonical = Format-EquipmentModifiers -Modifiers $sheetTree.Modifiers
        $assetCanonical = Format-EquipmentModifiers -Modifiers $item.Modifiers

        $hasBase = $baseline.Items.ContainsKey($item.Guid)
        $baseValue = $null
        if ($hasBase -and $baseline.Items[$item.Guid].ContainsKey('Modifiers')) { $baseValue = $baseline.Items[$item.Guid]['Modifiers'] }

        $verdict = Resolve-ThreeWay -Baseline $baseValue -Asset $assetCanonical -Sheet $sheetCanonical -HasBaselineEntry $hasBase
        if ($verdict -in @('takeSheet', 'conflict', 'noBaseline')) { $pending += "$key -> Modifiers" }
    }

    if ($pending.Count -gt 0) {
        $shown = @($pending | Select-Object -First 12)
        $more = if ($pending.Count -gt 12) { "`n  ... and $($pending.Count - 12) more" } else { '' }

        throw ("The sheet holds $($pending.Count) edit(s) that have not reached the assets. Rebuilding it " +
               "now would discard them:`n  " + ($shown -join "`n  ") + $more +
               "`n`nRun the sync instead - Tools > Sync Equipment With Sheet in Unity. Pass -Force to " +
               "overwrite them anyway.")
    }
}

# ---------------------------------------------------------------------------------------------------
# Build sheet data
# ---------------------------------------------------------------------------------------------------

$equipColumns = @(Get-EquipmentColumns)
$modColumns = @(Get-ModifierColumns -Schema $schema)
$tuningColumns = @(Get-CardTuningColumns -Schema $schema)
$ideaColumns = @(Get-IdeaColumns)

$notes = Get-PreservedNotes -SheetName 'Equipment'

$equipRows = @()
$modRows = @()
$tuningRows = @()
$balanceRows = @()

foreach ($item in ($items | Sort-Object Folder, Name)) {
    $preview = Get-DescribePreview -DescribeCache $describeCache -Item $item

    $r = [ordered]@{
        Key                = $item.Name
        Folder             = $item.Folder
        'Item Name'        = $item.EquipmentName
        Description        = $item.Description
        'Effect Preview'   = $preview
        Rarity             = $item.Rarity
        Class              = $item.Class
        Slot               = $item.Slot
        'No Reward'        = $item.NoReward
        'Modifier Summary' = ($item.Modifiers | ForEach-Object { $_.TypeName }) -join ', '
        'Mod Count'        = @($item.Modifiers).Count
        GUID               = $item.Guid
        Sync               = 'Live'
        Notes              = ''
    }
    if ($notes.ContainsKey($item.Name)) { $r['Notes'] = $notes[$item.Name] }
    $equipRows += [pscustomobject]$r

    $ord = 0
    foreach ($m in $item.Modifiers) {
        $row = [ordered]@{}
        foreach ($c in $modColumns) { $row[$c] = '' }
        $row['Item'] = $item.Name
        $row['Ord'] = $ord
        $row['Modifier Type'] = $m.TypeName
        $spec = $schema.ByType[$m.TypeName]
        $row['Describe'] = if ($describeCache.ContainsKey($item.Guid)) {
            ($describeCache[$item.Guid].Preview)
        } else { '' }
        foreach ($k in $m.Leaves.Keys) { $row[$k] = $m.Leaves[$k] }
        $row['Mod Id'] = $m.ModId
        $row['GUID'] = $item.Guid
        $row['Sync'] = 'Live'
        $modRows += [pscustomobject]$row

        $subOrd = 0
        foreach ($s in $m.CardTuning) {
            $trow = [ordered]@{}
            foreach ($c in $tuningColumns) { $trow[$c] = '' }
            $trow['Item'] = $item.Name
            $trow['Ord'] = $ord
            $trow['Sub'] = $subOrd
            $trow['Card Modifier Type'] = $s.TypeName
            foreach ($k in $s.Leaves.Keys) { $trow[$k] = $s.Leaves[$k] }
            $trow['Sub Id'] = $s.SubId
            $trow['Mod Id'] = $m.ModId
            $trow['GUID'] = $item.Guid
            $trow['Sync'] = 'Live'
            $tuningRows += [pscustomobject]$trow
            $subOrd++
        }
        $ord++
    }

    $balanceRows += [pscustomobject](Get-EquipmentBalanceRecord -Item $item -Schema $schema)
}

Write-Host "  Equipment : $($equipRows.Count)   Modifiers : $($modRows.Count)   Card Tuning : $($tuningRows.Count)"

# Ideas tab - hand-authored, never derived. Read back and passed through verbatim, same contract as
# CardSheet's own Ideas tabs.
$ideaRows = @()
foreach ($row in (Get-ExistingSheet -SheetName 'Ideas')) {
    $r = [ordered]@{}
    foreach ($col in $ideaColumns) {
        $value = ''
        if ($row.PSObject.Properties.Name -contains $col) { $value = $row.$col }
        $r[$col] = $value
    }
    $ideaRows += [pscustomobject]$r
}
Write-Host "  Ideas : $($ideaRows.Count)"

$sheets = [ordered]@{
    Equipment    = $equipRows
    Modifiers    = $modRows
    'Card Tuning' = $tuningRows
    Ideas        = $ideaRows
    Balance      = $balanceRows
}

. (Join-Path $scriptDir 'Write-EquipmentWorkbook.ps1')

Write-EquipmentWorkbook -WorkbookPath $WorkbookPath -Sheets $sheets -Schema $schema -RepoRoot $repoRoot

if (-not $NoCsvMirror) {
    if (-not (Test-Path -LiteralPath $csvDir)) { New-Item -ItemType Directory -Path $csvDir -Force | Out-Null }
    foreach ($name in $sheets.Keys) {
        $path = Join-Path $csvDir "$name.csv"
        if (@($sheets[$name]).Count -eq 0) {
            $cols = switch ($name) {
                'Equipment' { $equipColumns }
                'Modifiers' { $modColumns }
                'Card Tuning' { $tuningColumns }
                'Ideas' { $ideaColumns }
                default { @('Key') }
            }
            ($cols -join ',') | Set-Content -LiteralPath $path -Encoding utf8
        }
        else {
            $sheets[$name] | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding utf8
        }
    }
    Write-Host "CSV mirror written to Docs/EquipmentDesign/" -ForegroundColor DarkGray
}

if ($WriteBaseline) {
    Write-EquipmentBaseline -ToolDir $scriptDir -Items $items
    Write-Host "Baseline recorded ($($items.Count) items)" -ForegroundColor DarkGray
}

Write-Host "`nWrote $WorkbookPath" -ForegroundColor Green
