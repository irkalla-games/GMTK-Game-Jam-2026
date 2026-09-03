<#
.SYNOPSIS
    Diffs Docs/EquipmentDesign.xlsx against the authored EquipmentData assets and writes
    Tools/EquipmentSheet/equipment.json.

.DESCRIPTION
    Does NOT touch the Unity project. Assets/Editor/EquipmentSheetImporter.cs reads the work order this
    writes and applies it through AssetDatabase/SerializedObject, which is what keeps GUID minting, local
    fileID assignment and sub-asset surgery Unity's business - the same split every other sheet tool uses.

    What lands in equipment.json, per item:
      * a blank-GUID Equipment row                                -> action "create"
      * an Equipment row whose scalar columns differ from disk    -> action "update" (or "move", if
        Key/Folder changed)
      * a modifiers payload is attached ONLY when the Modifiers merge column itself resolved takeSheet -
        an item whose scalars changed but whose modifier tree still agrees with disk (or where only
        Unity moved) ships with no modifiers array at all, and the importer must not touch the sub-asset
        list in that case.
      * nothing else. An item that matches disk exactly is skipped, which is what makes the round-trip
        identity test (export, import, expect an empty file) meaningful.

    THE IDEAS TAB IS NEVER READ. Same contract as CardSheet's own Ideas tabs.

.PARAMETER WorkbookPath
    Defaults to Docs/EquipmentDesign.xlsx at the repo root.

.PARAMETER OutputPath
    Defaults to Tools/EquipmentSheet/equipment.json.

.PARAMETER FromCsv
    Force reading the Docs/EquipmentDesign/*.csv mirror even though the workbook is readable.

.PARAMETER OnlyKey
    Only consider items whose Key matches one of these wildcard patterns.
#>
[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [string]$OutputPath,
    [switch]$FromCsv,
    [string[]]$OnlyKey
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Resolve-Path (Join-Path $scriptDir '..\..')
Import-Module (Join-Path $scriptDir 'EquipmentSheet.Common.psm1') -Force -DisableNameChecking

if (-not $WorkbookPath) { $WorkbookPath = Join-Path $repoRoot 'Docs\EquipmentDesign.xlsx' }
if (-not $OutputPath)   { $OutputPath   = Join-Path $scriptDir 'equipment.json' }
$csvDir = Join-Path $repoRoot 'Docs\EquipmentDesign'
$equipmentRoot = Join-Path $repoRoot 'Assets\Data\Equipment'

$effectiveFromCsv = [bool]$FromCsv

# Shared-read probe, deliberately not exclusive - see Import-CardSheet.ps1's identical guard for why.
if (-not $effectiveFromCsv -and (Test-Path -LiteralPath $WorkbookPath)) {
    try {
        $probe = [IO.File]::Open($WorkbookPath, 'Open', 'Read', 'ReadWrite')
        $probe.Close()
    }
    catch {
        Write-Host "$WorkbookPath cannot be read ($($_.Exception.Message.Trim())) - falling back to Docs/EquipmentDesign/*.csv for this sync." -ForegroundColor Yellow
        $effectiveFromCsv = $true
    }
}

if (-not $effectiveFromCsv) {
    if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
        throw "The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser"
    }
    Import-Module ImportExcel -DisableNameChecking
}

$schema = Import-ModifierSchema -ToolDir $scriptDir

function Read-Tab {
    param([string]$SheetName)

    if ($effectiveFromCsv) {
        $path = Join-Path $csvDir "$SheetName.csv"
        if (-not (Test-Path -LiteralPath $path)) { return @() }
        return @(Import-Csv -LiteralPath $path)
    }

    try { return @(Import-Excel -Path $WorkbookPath -WorksheetName $SheetName -ErrorAction Stop -WarningAction SilentlyContinue) }
    catch { Write-Verbose "Sheet '$SheetName' missing or empty."; return @() }
}

# ---------------------------------------------------------------------------------------------------
# Read the assets so the diff has something to compare against
# ---------------------------------------------------------------------------------------------------

Write-Host "Reading assets..." -ForegroundColor Cyan
$scriptGuids = Get-ScriptGuidIndex -RepoRoot $repoRoot
$assetIndex  = Build-AssetIndex -RepoRoot $repoRoot -RelativeFolders @('Assets\Data', 'Assets\Prefabs') -ScriptGuidIndex $scriptGuids

$itemsByGuid = @{}
$itemsByKey  = @{}
$schemaProblems = @()

if (Test-Path -LiteralPath $equipmentRoot) {
    foreach ($file in (Get-ChildItem -LiteralPath $equipmentRoot -Recurse -Filter '*.asset' -File)) {
        $item = Read-EquipmentAsset -AssetPath $file.FullName -AssetIndex $assetIndex -Schema $schema -RepoRoot $repoRoot -ScriptGuidIndex $scriptGuids
        if ($item.Guid) { $itemsByGuid[$item.Guid] = $item }
        $itemsByKey[$item.Name] = $item
        foreach ($p in $item.Problems) { $schemaProblems += "$($item.Name): $p" }
    }
}

Write-Host "  $($itemsByKey.Count) equipment items"

if ($schemaProblems.Count -gt 0) {
    throw ("modifier-schema.json is out of date with the assets on disk:`n  " + ($schemaProblems -join "`n  ") +
           "`n`nFocus the Unity Editor once, then run this again.")
}

$baseline = Read-EquipmentBaseline -ToolDir $scriptDir
$mergeColumns = @(Get-EquipmentMergeColumns)

# ---------------------------------------------------------------------------------------------------
# Walk the sheet
# ---------------------------------------------------------------------------------------------------

$equipRows = @(Read-Tab -SheetName 'Equipment')
$modRows = @(Read-Tab -SheetName 'Modifiers')
$tuningRows = @(Read-Tab -SheetName 'Card Tuning')

function Get-Cell {
    param($Row, [string]$Name, $Default = '')
    if ($null -eq $Row) { return $Default }
    if (-not ($Row.PSObject.Properties.Name -contains $Name)) { return $Default }
    $v = $Row.$Name
    if ($null -eq $v) { return $Default }
    return ([string]$v).Trim()
}

function Build-ModifierPayload {
    <# Sheet-tree Modifiers array -> the JSON shape EquipmentSheetImporter.cs expects. Returns
       @{ Modifiers = <payload array>; Problems = <string[]> } #>
    param($SheetModifiers, $Schema)

    $problems = @()
    $out = @()

    foreach ($m in @($SheetModifiers)) {
        $leaves = @()
        foreach ($f in $Schema.ByType[$m.TypeName].Fields) {
            if ($f.Role -eq 'nestedCardModifiers') { continue }
            $text = if ($m.Leaves.Contains($f.Path)) { [string]$m.Leaves[$f.Path] } else { '' }
            $leaf = ConvertTo-ModifierLeafPayload -Text $text -FieldSpec $f
            if ($leaf.problem) { $problems += "$($m.TypeName) ($($f.Path)): $($leaf.problem)" }
            $leaves += $leaf
        }

        $children = @()
        foreach ($s in @($m.CardTuning)) {
            $childLeaves = @()
            if (-not $Schema.ByType.ContainsKey($s.TypeName)) {
                $problems += "unknown Card Modifier Type '$($s.TypeName)'"
                continue
            }
            foreach ($cf in $Schema.ByType[$s.TypeName].Fields) {
                $text = if ($s.Leaves.Contains($cf.Path)) { [string]$s.Leaves[$cf.Path] } else { '' }
                $leaf = ConvertTo-ModifierLeafPayload -Text $text -FieldSpec $cf
                if ($leaf.problem) { $problems += "$($s.TypeName) ($($cf.Path)): $($leaf.problem)" }
                $childLeaves += $leaf
            }
            $children += [ordered]@{ subId = $s.SubId; typeName = $s.TypeName; leaves = @($childLeaves) }
        }

        $out += [ordered]@{ modId = $m.ModId; typeName = $m.TypeName; leaves = @($leaves); cardTuning = @($children) }
    }

    return [pscustomobject]@{ Modifiers = @($out); Problems = $problems }
}

$actions = @()
$seenKeys = @{}
$seenGuids = @{}
$problems = @()
$conflicts = @()
$unresolved = @()
$removedRows = @()
$filtered = 0

foreach ($row in $equipRows) {
    $key = Get-Cell $row 'Key'
    if ($key -eq '') { continue }

    if ($OnlyKey) {
        $match = $false
        foreach ($pattern in $OnlyKey) { if ($key -like $pattern) { $match = $true; break } }
        if (-not $match) { $filtered++; continue }
    }

    if ($seenKeys.ContainsKey($key)) { $problems += "Duplicate Key '$key' on the Equipment tab - skipped."; continue }
    $seenKeys[$key] = $true

    $guid = Get-Cell $row 'GUID'
    $existing = $null
    if ($guid -and $itemsByGuid.ContainsKey($guid)) { $existing = $itemsByGuid[$guid] }
    elseif ($itemsByKey.ContainsKey($key)) { $existing = $itemsByKey[$key] }

    if ($null -ne $existing) {
        $seenGuid = [string]$existing.Guid
        if ($seenGuid) { $seenGuids[$seenGuid] = $true }
    }

    $sheetTree = Get-SheetModifierTree -Key $key -ModifierRows $modRows -TuningRows $tuningRows -Schema $schema
    if ($sheetTree.Problems.Count -gt 0) {
        $problems += @($sheetTree.Problems | ForEach-Object { "'$key': $_" })
        continue
    }

    $sheetRow = [ordered]@{
        Key         = $key
        Folder      = Get-Cell $row 'Folder'
        'Item Name' = Get-Cell $row 'Item Name' $key
        Description = Get-Cell $row 'Description'
        Rarity      = Get-Cell $row 'Rarity' 'Common'
        Class       = Get-Cell $row 'Class' 'Any'
        Slot        = Get-Cell $row 'Slot' 'Ring'
        'No Reward' = (Get-Cell $row 'No Reward').ToUpperInvariant()
        Modifiers   = Format-EquipmentModifiers -Modifiers $sheetTree.Modifiers
    }

    $folder = $sheetRow.Folder
    $path = if ($folder) { "Assets/Data/Equipment/$folder/$key.asset" } else { "Assets/Data/Equipment/$key.asset" }

    $spec = [ordered]@{
        key = $key; assetPath = $path; newAssetPath = ''; guid = $guid; action = ''
        itemName = $sheetRow.'Item Name'
        description = $sheetRow.Description
        rarity = Get-EnumValue -Table @('Common', 'Uncommon', 'Rare', 'Legendary', 'NotOffered') -Name $sheetRow.Rarity
        requiredClass = ConvertFrom-ClassName $sheetRow.Class
        slot = Get-EnumValue -Table @('Ring', 'Weapon', 'Armor', 'Hat', 'Boots') -Name $sheetRow.Slot
        excludeFromRewards = $sheetRow.'No Reward' -eq 'TRUE'
        modifiersProvided = $false
        modifiers = @()
        changed = @()
    }

    if ($null -eq $existing) {
        $payload = Build-ModifierPayload -SheetModifiers $sheetTree.Modifiers -Schema $schema
        if ($payload.Problems.Count -gt 0) {
            $problems += @($payload.Problems | ForEach-Object { "'$key' (new item): $_" })
            continue
        }
        $spec.action = 'create'
        $spec.modifiersProvided = $true
        $spec.modifiers = $payload.Modifiers
        $actions += $spec
        continue
    }

    $assetRow = Get-EquipmentMergeRow -Item $existing
    $hasBase = $existing.Guid -and $baseline.Items.ContainsKey($existing.Guid)

    $changed = @()
    $conflicted = @()
    $unknown = @()

    foreach ($col in $mergeColumns) {
        $sheetValue = $sheetRow[$col]
        $assetValue = $assetRow[$col]
        $baseValue = $null
        if ($hasBase -and $baseline.Items[$existing.Guid].ContainsKey($col)) { $baseValue = $baseline.Items[$existing.Guid][$col] }

        switch (Resolve-ThreeWay -Baseline $baseValue -Asset $assetValue -Sheet $sheetValue -HasBaselineEntry $hasBase) {
            'takeSheet'  { $changed += $col }
            'conflict'   { $conflicted += [ordered]@{ column = $col; unity = [string]$assetValue; sheet = [string]$sheetValue; wasLast = [string]$baseValue } }
            'noBaseline' { $unknown += [ordered]@{ column = $col; unity = [string]$assetValue; sheet = [string]$sheetValue } }
        }
    }

    if ($conflicted.Count -gt 0) { $conflicts += [ordered]@{ key = $key; columns = @($conflicted) }; continue }
    if ($unknown.Count -gt 0) { $unresolved += [ordered]@{ key = $key; columns = @($unknown) }; continue }
    if ($changed.Count -eq 0) { continue }

    $spec.assetPath = $existing.Path.Substring($existing.Path.IndexOf('Assets\Data\Equipment')) -replace '\\', '/'
    $spec.changed = $changed

    if ($changed -contains 'Modifiers') {
        $payload = Build-ModifierPayload -SheetModifiers $sheetTree.Modifiers -Schema $schema
        if ($payload.Problems.Count -gt 0) {
            $problems += @($payload.Problems | ForEach-Object { "'$key': $_" })
            continue
        }
        $spec.modifiersProvided = $true
        $spec.modifiers = $payload.Modifiers
    }

    $keyMoved = $changed -contains 'Key'
    $folderMoved = $changed -contains 'Folder'
    if ($keyMoved -or $folderMoved) {
        $spec.action = 'move'
        $spec.newAssetPath = $path
    }
    else {
        $spec.action = 'update'
    }

    $actions += $spec
}

# Assets with no row in the sheet - reported, never removed. Same split as CardSheet: a key never in
# the baseline was authored in Unity and the export pass will add it; a key that WAS in the baseline
# means the row was deleted from the sheet.
$orphans = @()
if (-not $OnlyKey) {
    foreach ($key in $itemsByKey.Keys) {
        if ($seenKeys.ContainsKey($key)) { continue }
        $item = $itemsByKey[$key]
        $guid = [string]$item.Guid
        if ($guid -and $seenGuids.ContainsKey($guid)) { continue }
        if ($guid -and $baseline.Items.ContainsKey($guid)) { $removedRows += $key } else { $orphans += $key }
    }
}

$payload = [ordered]@{
    generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    source       = if ($effectiveFromCsv) { 'csv' } else { 'xlsx' }
    items        = @($actions)
    conflicts    = @($conflicts)
    unresolved   = @($unresolved)
    removedRows  = @($removedRows | Sort-Object)
    newInUnity   = @($orphans | Sort-Object)
    problems     = @($problems)
}

$json = $payload | ConvertTo-Json -Depth 14
Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8

# ---------------------------------------------------------------------------------------------------

$creates = @($actions | Where-Object { $_.action -eq 'create' })
$updates = @($actions | Where-Object { $_.action -eq 'update' })
$moves   = @($actions | Where-Object { $_.action -eq 'move' })

Write-Host ""
Write-Host "Create : $($creates.Count)" -ForegroundColor $(if ($creates.Count) { 'Green' } else { 'DarkGray' })
foreach ($c in $creates) { Write-Host "    $($c.key)  ->  $($c.assetPath)" -ForegroundColor DarkGray }

Write-Host "Update : $($updates.Count)" -ForegroundColor $(if ($updates.Count) { 'Yellow' } else { 'DarkGray' })
foreach ($u in $updates) { Write-Host "    $($u.key)  :  $($u.changed -join ', ')" -ForegroundColor DarkGray }

Write-Host "Move   : $($moves.Count)" -ForegroundColor $(if ($moves.Count) { 'Yellow' } else { 'DarkGray' })
foreach ($m in $moves) { Write-Host "    $($m.assetPath)  ->  $($m.newAssetPath)" -ForegroundColor DarkGray }

if ($conflicts.Count -gt 0) {
    Write-Host "CONFLICTS : $($conflicts.Count) - changed in BOTH Unity and the sheet, left untouched on both sides" -ForegroundColor Red
    foreach ($c in $conflicts) {
        Write-Host "    $($c.key)" -ForegroundColor Red
        foreach ($col in $c.columns) {
            Write-Host "        $($col.column):" -ForegroundColor DarkGray
            Write-Host "            was    '$($col.wasLast)'" -ForegroundColor DarkGray
            Write-Host "            Unity  '$($col.unity)'" -ForegroundColor DarkGray
            Write-Host "            sheet  '$($col.sheet)'" -ForegroundColor DarkGray
        }
    }
}

if ($unresolved.Count -gt 0) {
    Write-Host "UNRESOLVED : $($unresolved.Count) - differ, but there is no baseline saying which side moved" -ForegroundColor Red
    foreach ($u in $unresolved) { Write-Host "    $($u.key): $((@($u.columns | ForEach-Object { $_.column })) -join ', ')" -ForegroundColor DarkGray }
    Write-Host "    Run Export-EquipmentSheet.ps1 -Force -WriteBaseline to declare Unity correct and start tracking." -ForegroundColor Red
}

if ($filtered -gt 0) { Write-Host "Rows excluded by -OnlyKey filter : $filtered" -ForegroundColor DarkGray }
if ($orphans.Count -gt 0) { Write-Host "New in Unity, will be added : $($orphans -join ', ')" -ForegroundColor Green }
if ($removedRows.Count -gt 0) { Write-Host "Row deleted from the sheet  : $($removedRows -join ', ') (asset kept)" -ForegroundColor Yellow }
if ($problems.Count -gt 0) {
    Write-Host "Problems :" -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "    $p" -ForegroundColor Red }
}

Write-Host ""
if ($actions.Count -eq 0) {
    Write-Host "No sheet edits to apply." -ForegroundColor Green
}
else {
    Write-Host "Wrote $OutputPath" -ForegroundColor Green
    Write-Host "Now focus the Unity Editor and pick  Tools > Sync Equipment With Sheet" -ForegroundColor Cyan
}
