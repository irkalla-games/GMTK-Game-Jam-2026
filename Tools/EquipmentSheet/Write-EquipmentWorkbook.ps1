<#
.SYNOPSIS
    Turns the gathered equipment data into the formatted workbook. Dot-sourced by
    Export-EquipmentSheet.ps1.

.DESCRIPTION
    Follows the Enemy/Level single-package pattern (one Open-ExcelPackage -Create, every sheet built
    against it, one Close-ExcelPackage) rather than CardSheet's open-close-per-sheet - simpler, and
    nothing here needs the per-sheet reopen CardSheet's chart pipeline wanted.

    Enums MUST be added before any tab that references its named ranges in a dropdown - same ordering
    rule Write-EnemyWorkbook.ps1 and Write-CardWorkbook.ps1 both already follow.
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

function Write-EquipmentWorkbook {
    param([string]$WorkbookPath, $Sheets, $Schema, [string]$RepoRoot)

    $dir = Split-Path -Parent $WorkbookPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    if (Test-Path -LiteralPath $WorkbookPath) { Remove-Item -LiteralPath $WorkbookPath -Force }

    Write-Host 'Writing workbook...' -ForegroundColor Cyan

    $pkg = Open-ExcelPackage -Path $WorkbookPath -Create

    Add-EnumsSheet -Package $pkg -Schema $Schema -RepoRoot $RepoRoot
    Add-EquipmentSheet -Package $pkg -Rows $Sheets.Equipment
    Add-ModifiersSheet -Package $pkg -Rows $Sheets.Modifiers -Schema $Schema
    Add-CardTuningSheet -Package $pkg -Rows $Sheets.'Card Tuning' -Schema $Schema
    Add-IdeasSheet -Package $pkg -Rows $Sheets.Ideas
    Add-BalanceSheet -Package $pkg -Rows $Sheets.Balance
    Add-ReadmeSheet -Package $pkg -Schema $Schema

    Set-SheetOrder -Package $pkg

    Close-ExcelPackage $pkg
}

# ---------------------------------------------------------------------------------------------------
# Equipment
# ---------------------------------------------------------------------------------------------------

function Add-EquipmentSheet {
    param($Package, $Rows)

    $cols = @(Get-EquipmentColumns)
    $ws = $Package.Workbook.Worksheets.Add('Equipment')

    for ($c = 0; $c -lt $cols.Count; $c++) { $ws.Cells[1, ($c + 1)].Value = $cols[$c] }
    $ws.Cells[1, 1, 1, $cols.Count].Style.Font.Bold = $true

    $guidCol = $cols.IndexOf('GUID') + 1
    $syncCol = $cols.IndexOf('Sync') + 1

    for ($r = 0; $r -lt @($Rows).Count; $r++) {
        $row = @($Rows)[$r]
        $excelRow = $r + 2
        for ($c = 0; $c -lt $cols.Count; $c++) {
            $val = $row.($cols[$c])
            if ($cols[$c] -eq 'Sync') { continue }
            $ws.Cells[$excelRow, ($c + 1)].Value = $val
        }
        $ws.Cells[$excelRow, $syncCol].Formula = "IF(`$$(Get-ExcelColumnName $guidCol)`$$excelRow=`"`",`"NEW`",`"Live`")"
    }

    $limit = [Math]::Max(@($Rows).Count + 1, 2) + 200
    $validations = @(
        @('Rarity', 'list_Rarity'), @('Class', 'list_Class'), @('Slot', 'list_Slot'),
        @('No Reward', 'list_Bool'), @('Folder', 'list_EquipFolder')
    )
    foreach ($v in $validations) {
        $idx = $cols.IndexOf($v[0])
        if ($idx -lt 0) { continue }
        $letter = Get-ExcelColumnName ($idx + 1)
        $dv = $ws.DataValidations.AddListValidation("$letter`2:$letter$limit")
        $dv.Formula.ExcelFormula = "=$($v[1])"
        $dv.ShowErrorMessage = $false
        $dv.AllowBlank = $true
    }

    $descCol = Get-ExcelColumnName ($cols.IndexOf('Description') + 1)
    $previewCol = Get-ExcelColumnName ($cols.IndexOf('Effect Preview') + 1)
    $ws.Cells["$descCol`2:$descCol$limit"].Style.WrapText = $true
    $ws.Cells["$previewCol`2:$previewCol$limit"].Style.WrapText = $true
    $ws.Cells["$previewCol`2:$previewCol$limit"].Style.Font.Italic = $true

    $ws.Column(1).Width = 22
    $ws.Column($cols.IndexOf('Item Name') + 1).Width = 22
    $ws.Column($cols.IndexOf('Description') + 1).Width = 40
    $ws.Column($cols.IndexOf('Effect Preview') + 1).Width = 40
    $ws.Column($cols.IndexOf('Modifier Summary') + 1).Width = 30
    $ws.View.FreezePanes(2, 2)
    if (@($Rows).Count -gt 0) {
        $lastLetter = Get-ExcelColumnName $cols.Count
        $ws.Cells["A1:$lastLetter$(@($Rows).Count + 1)"].AutoFilter = $true
    }
}

# ---------------------------------------------------------------------------------------------------
# Modifiers / Card Tuning - schema-driven union columns
# ---------------------------------------------------------------------------------------------------

function Write-UnionSheet {
    param($Package, [string]$Name, $Rows, [string[]]$Columns, [string[]]$OutlineFamilies, [string]$TypeDropdownList, [string]$TypeColumnName)

    $ws = $Package.Workbook.Worksheets.Add($Name)

    for ($c = 0; $c -lt $Columns.Count; $c++) { $ws.Cells[1, ($c + 1)].Value = $Columns[$c] }
    $ws.Cells[1, 1, 1, $Columns.Count].Style.Font.Bold = $true

    $guidCol = $Columns.IndexOf('GUID') + 1
    $syncCol = $Columns.IndexOf('Sync') + 1

    for ($r = 0; $r -lt @($Rows).Count; $r++) {
        $row = @($Rows)[$r]
        $excelRow = $r + 2
        for ($c = 0; $c -lt $Columns.Count; $c++) {
            if ($Columns[$c] -eq 'Sync') { continue }
            $ws.Cells[$excelRow, ($c + 1)].Value = $row.($Columns[$c])
        }
        $ws.Cells[$excelRow, $syncCol].Formula = "IF(`$$(Get-ExcelColumnName $guidCol)`$$excelRow=`"`",`"NEW`",`"Live`")"
    }

    $limit = [Math]::Max(@($Rows).Count + 1, 2) + 200

    if ($TypeDropdownList) {
        $idx = $Columns.IndexOf($TypeColumnName)
        if ($idx -ge 0) {
            $letter = Get-ExcelColumnName ($idx + 1)
            $dv = $ws.DataValidations.AddListValidation("$letter`2:$letter$limit")
            $dv.Formula.ExcelFormula = "=$TypeDropdownList"
            $dv.ShowErrorMessage = $false
            $dv.AllowBlank = $true
        }
    }

    # Outline-group each dotted-path family (on.*, area.*, entry.*, filter.*) so the wide leaf column
    # block collapses to a "+" in Excel's margin - see CLAUDE.md's note on the Card Tuning tab's width.
    foreach ($family in $OutlineFamilies) {
        for ($c = 0; $c -lt $Columns.Count; $c++) {
            if ($Columns[$c].StartsWith("$family.", [StringComparison]::Ordinal)) { $ws.Column($c + 1).OutlineLevel = 1 }
        }
    }

    $describeIdx = $Columns.IndexOf('Describe')
    if ($describeIdx -ge 0) {
        $letter = Get-ExcelColumnName ($describeIdx + 1)
        $ws.Cells["$letter`2:$letter$limit"].Style.Font.Italic = $true
    }

    $ws.Column(1).Width = 22
    for ($c = 1; $c -lt $Columns.Count; $c++) { $ws.Column($c + 1).Width = 14 }
    if ($describeIdx -ge 0) { $ws.Column($describeIdx + 1).Width = 34 }

    $freezeCol = [Math]::Min(6, $Columns.Count + 1)
    $ws.View.FreezePanes(2, $freezeCol)
    if (@($Rows).Count -gt 0) {
        $lastLetter = Get-ExcelColumnName $Columns.Count
        $ws.Cells["A1:$lastLetter$(@($Rows).Count + 1)"].AutoFilter = $true
    }
}

function Add-ModifiersSheet {
    param($Package, $Rows, $Schema)

    $cols = @(Get-ModifierColumns -Schema $Schema)
    $families = @($cols | ForEach-Object { ($_ -split '\.')[0] } | Where-Object { $_ -in @('filter') } | Sort-Object -Unique)
    Write-UnionSheet -Package $Package -Name 'Modifiers' -Rows $Rows -Columns $cols -OutlineFamilies $families `
        -TypeDropdownList 'list_EquipModType' -TypeColumnName 'Modifier Type'
}

function Add-CardTuningSheet {
    param($Package, $Rows, $Schema)

    $cols = @(Get-CardTuningColumns -Schema $Schema)
    $families = @('on', 'area', 'entry')
    Write-UnionSheet -Package $Package -Name 'Card Tuning' -Rows $Rows -Columns $cols -OutlineFamilies $families `
        -TypeDropdownList 'list_CardModType' -TypeColumnName 'Card Modifier Type'
}

# ---------------------------------------------------------------------------------------------------
# Ideas
# ---------------------------------------------------------------------------------------------------

function Add-IdeasSheet {
    param($Package, $Rows)

    $cols = @(Get-IdeaColumns)
    $ws = $Package.Workbook.Worksheets.Add('Ideas')

    for ($c = 0; $c -lt $cols.Count; $c++) { $ws.Cells[1, ($c + 1)].Value = $cols[$c] }
    $ws.Cells[1, 1, 1, $cols.Count].Style.Font.Bold = $true

    for ($r = 0; $r -lt @($Rows).Count; $r++) {
        $row = @($Rows)[$r]
        $excelRow = $r + 2
        for ($c = 0; $c -lt $cols.Count; $c++) { $ws.Cells[$excelRow, ($c + 1)].Value = $row.($cols[$c]) }
    }

    $limit = [Math]::Max(@($Rows).Count + 1, 2) + 200
    $validations = @(
        @('Rarity', 'list_Rarity'), @('Class', 'list_Class'), @('Slot', 'list_Slot'),
        @('Buildable', 'list_Buildable'), @('Priority', 'list_Priority')
    )
    foreach ($v in $validations) {
        $idx = $cols.IndexOf($v[0])
        if ($idx -lt 0) { continue }
        $letter = Get-ExcelColumnName ($idx + 1)
        $dv = $ws.DataValidations.AddListValidation("$letter`2:$letter$limit")
        $dv.Formula.ExcelFormula = "=$($v[1])"
        $dv.ShowErrorMessage = $false
        $dv.AllowBlank = $true
    }

    $ws.Column(1).Width = 22
    $ws.Column($cols.IndexOf('Description') + 1).Width = 40
    $ws.Column($cols.IndexOf('Engine Work') + 1).Width = 40
    $ws.View.FreezePanes(2, 2)
}

# ---------------------------------------------------------------------------------------------------
# Balance
# ---------------------------------------------------------------------------------------------------

function Add-BalanceSheet {
    param($Package, $Rows)

    $cols = @('Key', 'Item Name', 'Rarity', 'Class', 'Slot', 'Mod Count', 'Modifier Types',
              'Projects', 'Tunes Cards', 'Cards Named', 'Tags Named', 'No Reward', 'Issues')
    $ws = $Package.Workbook.Worksheets.Add('Balance')

    for ($c = 0; $c -lt $cols.Count; $c++) { $ws.Cells[1, ($c + 1)].Value = $cols[$c] }
    $ws.Cells[1, 1, 1, $cols.Count].Style.Font.Bold = $true

    for ($r = 0; $r -lt @($Rows).Count; $r++) {
        $row = @($Rows)[$r]
        $excelRow = $r + 2
        for ($c = 0; $c -lt $cols.Count; $c++) { $ws.Cells[$excelRow, ($c + 1)].Value = $row.($cols[$c]) }
    }

    if (@($Rows).Count -gt 0) {
        $last = @($Rows).Count + 1
        $issuesCol = Get-ExcelColumnName ($cols.IndexOf('Issues') + 1)
        $fmt = $ws.ConditionalFormatting.AddNotEqual($ws.Cells["$issuesCol`2:$issuesCol$last"])
        $fmt.Formula = '""'
        $fmt.Style.Fill.PatternType = [OfficeOpenXml.Style.ExcelFillStyle]::Solid
        $fmt.Style.Fill.BackgroundColor.Color = [System.Drawing.Color]::FromArgb(255, 235, 156)
        $ws.Cells["A1:M$last"].AutoFilter = $true
    }

    $ws.Column(1).Width = 22
    $ws.Column($cols.IndexOf('Modifier Types') + 1).Width = 30
    $ws.Column($cols.IndexOf('Issues') + 1).Width = 50
    $ws.View.FreezePanes(2, 2)
}

# ---------------------------------------------------------------------------------------------------
# Enums - dropdown sources, entirely schema/asset-index derived, nothing hand-typed
# ---------------------------------------------------------------------------------------------------

function Add-EnumsSheet {
    param($Package, $Schema, [string]$RepoRoot)

    $ws = $Package.Workbook.Worksheets.Add('Enums')

    $statusEnum = $null
    $damageConditionEnum = $null
    foreach ($t in $Schema.ByType.Values) {
        foreach ($f in $t.Fields) {
            if ($f.EnumType -eq 'StatusType' -and -not $statusEnum) { $statusEnum = $f }
            if ($f.EnumType -eq 'DamageCondition' -and -not $damageConditionEnum) { $damageConditionEnum = $f }
        }
    }

    $classCombos = @('Any')
    $classParts = @('Knight', 'Mage', 'Rogue', 'Cleric')
    for ($mask = 1; $mask -lt 16; $mask++) {
        $names = @()
        for ($b = 0; $b -lt 4; $b++) { if (($mask -band (1 -shl $b)) -ne 0) { $names += $classParts[$b] } }
        $classCombos += ($names -join '+')
    }

    $equipFolders = @('') + @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'Assets\Data\Equipment') -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.Name } | Sort-Object -Unique)
    $cardEffectNames = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'Assets\Data\EffectData') -Recurse -Filter '*.asset' -ErrorAction SilentlyContinue | ForEach-Object { $_.BaseName } | Sort-Object -Unique)
    $cardDataNames = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'Assets\Data\CardData') -Recurse -Filter '*.asset' -ErrorAction SilentlyContinue | ForEach-Object { $_.BaseName } | Sort-Object -Unique)
    $patternNames = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'Assets\Data') -Recurse -Filter '*.asset' -ErrorAction SilentlyContinue |
        Where-Object { (Get-Content -LiteralPath $_.FullName -TotalCount 12) -match 'EffectPattern' } | ForEach-Object { $_.BaseName } | Sort-Object -Unique)

    $lists = [ordered]@{
        list_Rarity      = @('Common', 'Uncommon', 'Rare', 'Legendary', 'NotOffered')
        list_Class       = $classCombos
        list_Slot        = @('Ring', 'Weapon', 'Armor', 'Hat', 'Boots')
        list_Bool        = @('TRUE', 'FALSE')
        list_Status      = if ($statusEnum) { $statusEnum.EnumNames } else { @() }
        list_DamageCondition = if ($damageConditionEnum) { $damageConditionEnum.EnumNames } else { @() }
        list_CardTag     = @('None', 'Attack', 'Defence', 'Movement', 'Poison', 'Fire', 'Summon', 'Healing')
        list_AreaKind    = @('Single', 'Radius', 'Pattern')
        list_RangeShape  = @('Anywhere', 'Chebyshev', 'Manhattan', 'SelfTile')
        list_EffectTarget = @('PlayedTile', 'Source')
        list_CardKeyword = @('None', 'Innate', 'Cooldown', 'Dormant', 'Rebound')
        list_EquipModType = $Schema.EquipmentTypeNames
        list_CardModType = $Schema.CardTypeNames
        list_CardEffect  = $cardEffectNames
        list_CardData    = $cardDataNames
        list_EffectPattern = $patternNames
        list_EquipFolder = $equipFolders
        list_Buildable   = @('Yes', 'Needs Modifier', 'Needs Status', 'Needs Hook', 'Needs System')
        list_Priority    = @('High', 'Med', 'Low')
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

    $ws.Cells[1, $col].Value = 'Drives every dropdown on the other tabs. Regenerated on every export - edit the C# type or enum, not this sheet.'
    $ws.Cells[1, $col].Style.Font.Italic = $true
}

# ---------------------------------------------------------------------------------------------------
# README
# ---------------------------------------------------------------------------------------------------

function Add-ReadmeSheet {
    param($Package, $Schema)

    $ws = $Package.Workbook.Worksheets.Add('README')

    $lines = @(
        @('Equipment Design Workbook', 'title'),
        @('', ''),
        @('One row per item on the Equipment tab; its modifiers - what it actually does - live on the Modifiers tab, one row each. A Card Tuning modifier''s own nested rewrite rules get a third tab. This workbook reads AND writes Assets/Data/Equipment.', ''),
        @('', ''),
        @('The tabs', 'head'),
        @('Equipment', 'Key/Folder drive the asset''s path - GUID Assets/Data/Equipment/<Folder>/<Key>.asset. Effect Preview is read-only, generated from the real Describe() code in Unity - compare it against your hand-written Description to catch drift.'),
        @('Modifiers', 'One row per EquipmentModifier sub-asset. Modifier Type is a dropdown covering every type that exists in code today - write a new EquipmentModifier subclass, focus Unity, and it appears here automatically with its own columns. A blank column on a given row means that field does not apply to that row''s type. Order is ROW POSITION - drag rows to reorder, do not just retype Ord (a display aid only).'),
        @('Card Tuning', 'One row per CardModifier nested inside a CardTuningModifier row on the Modifiers tab, linked by Mod Id (or by matching Ord on a brand new pair that has no Mod Id yet). Same union-column, schema-driven shape as Modifiers.'),
        @('Ideas', 'Free-form backlog - never read by the sync. Promote an idea by copying its row onto the Equipment tab.'),
        @('Balance', 'Derived data hygiene view - Issues flags real problems (no modifiers, an unnamed item, a Card Tuning filter matching nothing, and so on). Never hand-edit; rebuilt every export.'),
        @('Enums', 'Every dropdown''s source list, generated from the C# enums, the modifier schema, and the asset folders - nothing here is hand-typed.'),
        @('', ''),
        @('Syncing back to Unity', 'head'),
        @('One button', 'In Unity: Tools > Sync Equipment With Sheet. Applies your edits, then refreshes this workbook so anything authored in the Inspector shows up here too.'),
        @('Mod Id / Sub Id', 'Read-only identity columns - Unity''s own local fileID for that sub-asset. Leave blank on a new row; the sync fills it in.'),
        @('If both sides changed', 'That item is left alone on BOTH sides and named in the Unity console - make them agree, or change only one, then sync again.'),
        @('Regenerating without syncing', 'Tools > Equipment > Refresh Sheet From Unity discards any sheet edit that has not been synced yet - only for when the sheet is known to be wrong.'),
        @('', ''),
        @('Modifier field legend', 'head')
    )

    $r = 1
    foreach ($line in $lines) {
        $ws.Cells[$r, 1].Value = $line[0]
        if ($line[1] -eq 'title') { $ws.Cells[$r, 1].Style.Font.Size = 16; $ws.Cells[$r, 1].Style.Font.Bold = $true }
        elseif ($line[1] -eq 'head') { $ws.Cells[$r, 1].Style.Font.Bold = $true; $ws.Cells[$r, 1].Style.Font.Size = 12 }
        elseif ($line[1] -ne '') { $ws.Cells[$r, 1].Style.Font.Bold = $true; $ws.Cells[$r, 2].Value = $line[1] }
        $r++
    }

    # Every modifier type's fields and tooltips, straight from the schema - this is how a designer
    # learns that (for example) "bonus" means three different things depending on which row's Modifier
    # Type it sits under.
    foreach ($typeName in (@($Schema.EquipmentTypeNames) + @($Schema.CardTypeNames))) {
        $t = $Schema.ByType[$typeName]
        $ws.Cells[$r, 1].Value = $typeName
        $ws.Cells[$r, 1].Style.Font.Bold = $true
        $r++
        foreach ($f in $t.Fields) {
            if ($f.Role -eq 'nestedCardModifiers') { continue }
            $desc = if ($f.Tooltip) { $f.Tooltip } else { '(no description authored)' }
            $ws.Cells[$r, 1].Value = "  $($f.Path)"
            $ws.Cells[$r, 2].Value = $desc
            $r++
        }
    }

    $ws.Column(1).Width = 28
    $ws.Column(2).Width = 110
    $ws.Column(2).Style.WrapText = $true
}

function Set-SheetOrder {
    param($Package)

    $order = @('README', 'Equipment', 'Modifiers', 'Card Tuning', 'Ideas', 'Balance', 'Enums')
    foreach ($name in $order) {
        $ws = $Package.Workbook.Worksheets[$name]
        if ($null -ne $ws) { $Package.Workbook.Worksheets.MoveToEnd($name) }
    }
    $Package.Workbook.Worksheets['README'].Select()
}
