<#
.SYNOPSIS
    Shared plumbing for the equipment design workbook: reads EquipmentData .asset YAML (main object plus
    every embedded modifier sub-asset), resolves it against the schema Unity's reflection walk writes to
    modifier-schema.json, and turns both into the flat records the spreadsheet is built from.

.DESCRIPTION
    Read-only with respect to the Unity project, same guarantee CardSheet.Common gives. Imports it for
    the YAML block reader, the asset index, the enum/backing-field helpers and the three-way merge -
    nothing there needs reimplementing.

    The one thing this file adds that CardSheet.Common cannot give it: an EquipmentData asset is a
    MULTI-DOCUMENT file (the item itself plus one embedded document per modifier sub-asset, and per
    nested CardModifier inside a CardTuningModifier), and CardSheet.Common's own multi-document reader
    (ConvertFrom-UnityYamlDocuments) throws away each document's anchor id - which is exactly the local
    fileID a modifier's identity depends on. See Read-AnchoredDocuments.

    A bug found in the shared reader while building this: ConvertFrom-UnityYaml / Build-AssetIndex reads
    the FIRST "MonoBehaviour:" document in a file, and Unity writes sub-asset documents in fileID order -
    so a modifier with a negative fileID sorts before the main object's &11400000 and Build-AssetIndex
    ends up indexing the equipment item under the MODIFIER's type and fields. Confirmed against
    Tower Shield.asset (indexes as GrantBonusModifier), Fire Mage's Hat.asset (AreaModifier),
    Totem Anchor.asset (SummonHealthModifier) and Weaken Ring.asset (AppliedPotencyModifier). This is
    NOT fixed here - three other tools' baselines were computed against the shared reader's current
    behaviour, and nothing else reads equipment through it today, so changing it is a live risk for zero
    benefit to this tool. Read-AnchoredDocuments below is EquipmentSheet's own reader, keyed off the
    .meta file's mainObjectFileID rather than "whichever document came first".
#>

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot '..\CardSheet\CardSheet.Common.psm1') -Force -DisableNameChecking

# ---------------------------------------------------------------------------------------------------
# Local enum tables not already in CardSheet.Common. Index = declaration order, same append-only rule.
# Sources: Assets/Scripts/Equipment/EquipmentSlot.cs, Assets/Scripts/Loot/Rarity.cs (already in
# CardSheet.Common as RarityNames - reused, not duplicated).
# ---------------------------------------------------------------------------------------------------

$script:SlotNames = @('Ring', 'Weapon', 'Armor', 'Hat', 'Boots')

# CardSheet.Common.psm1's $script:RarityNames is that module's own private script-scope variable, not
# reachable from here (module script scopes do not nest) - so this is a local copy, same source of
# truth (Assets/Scripts/Loot/Rarity.cs), same append-only rule. Duplicated rather than piped through a
# new exported function in CardSheet.Common, on the same "leave working, everyday-used code alone"
# call SheetSyncProcess.cs documents for CardSheetImporter's own process runner.
$script:RarityNames = @('Common', 'Uncommon', 'Rare', 'Legendary', 'NotOffered')

# ---------------------------------------------------------------------------------------------------
# Multi-document YAML - anchor-aware, unlike CardSheet.Common's ConvertFrom-UnityYamlDocuments.
# ---------------------------------------------------------------------------------------------------

function Read-AnchoredDocuments {
    <#
        .SYNOPSIS
            Splits a .asset file on its "--- !u!114 &<fileID>" document markers, keeping the fileID, and
            parses each document body into a mapping node with CardSheet.Common's existing YAML engine.

        .OUTPUTS
            A hashtable keyed by fileID (as the literal string Unity wrote, negative sign included), each
            value @{ FileId; Type; Node }.
    #>
    param([string]$AssetPath, [hashtable]$ScriptGuidIndex)

    $lines = Get-Content -LiteralPath $AssetPath
    $byFileId = [ordered]@{}

    $currentId = $null
    $currentLines = @()

    $flush = {
        if ($null -eq $currentId) { return }

        $body = @()
        $started = $false
        foreach ($line in $currentLines) {
            if (-not $started) {
                if ($line -match '^[A-Za-z_][A-Za-z0-9_]*:\s*$') { $started = $true }
                continue
            }
            $body += $line
        }
        if (-not $started) { return }

        $idx = 0
        $node = Read-YamlMapping -Lines $body -Index ([ref]$idx) -Indent 2
        $type = Get-AssetTypeName -Node $node -ScriptGuidIndex $ScriptGuidIndex
        $byFileId[$currentId] = [pscustomobject]@{ FileId = $currentId; Type = $type; Node = $node }
    }

    foreach ($line in $lines) {
        if ($line -match '^---\s+!u!\d+\s+&(-?\d+)\s*$') {
            & $flush
            $currentId = $Matches[1]
            $currentLines = @()
            continue
        }
        if ($null -ne $currentId) { $currentLines += $line }
    }
    & $flush

    return $byFileId
}

function Get-MainObjectFileId {
    <# Reads mainObjectFileID out of an asset's .meta - which document is the EquipmentData itself,
       independent of fileID sort order. #>
    param([string]$AssetPath)

    $meta = "$AssetPath.meta"
    if (-not (Test-Path -LiteralPath $meta)) { return $null }

    foreach ($line in (Get-Content -LiteralPath $meta)) {
        if ($line -match '^\s*mainObjectFileID:\s*(-?\d+)') { return $Matches[1] }
    }
    return $null
}

# ---------------------------------------------------------------------------------------------------
# Schema
# ---------------------------------------------------------------------------------------------------

function Get-JsonProp {
    <# Safe property read off a ConvertFrom-Json object under Set-StrictMode - a property JsonUtility
       omitted (or a hand-written schema left out) returns $Default instead of throwing. #>
    param($Object, [string]$Name, $Default)

    if ($null -eq $Object) { return $Default }
    if ($Object.PSObject.Properties.Name -notcontains $Name) { return $Default }
    $v = $Object.$Name
    if ($null -eq $v) { return $Default }
    return $v
}

function Import-ModifierSchema {
    <#
        .SYNOPSIS
            Loads Tools/EquipmentSheet/modifier-schema.json and indexes it for fast lookup. Throws if the
            file is missing - see Export-EquipmentSheet.ps1's schema gate for why a guessed column set is
            never an acceptable fallback.
    #>
    param([string]$ToolDir)

    $path = Join-Path $ToolDir 'modifier-schema.json'
    if (-not (Test-Path -LiteralPath $path)) {
        throw "$path is missing. It is generated by Unity (Assets/Editor/EquipmentModifierSchema.cs) and " +
              "describes every EquipmentModifier/CardModifier type's fields. Focus the Unity Editor once, " +
              "or run Tools > Equipment > Write Modifier Schema, then try again."
    }

    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json

    $byType = @{}
    foreach ($t in @($json.types)) {
        $fields = @()
        foreach ($f in @($t.fields)) {
            $enumNames = @(Get-JsonProp $f 'enumNames' @())
            $enumValues = @(Get-JsonProp $f 'enumValues' @())
            $enumMap = @{}
            for ($i = 0; $i -lt $enumNames.Count -and $i -lt $enumValues.Count; $i++) { $enumMap[[int]$enumValues[$i]] = [string]$enumNames[$i] }

            $fields += [pscustomobject]@{
                Path           = [string](Get-JsonProp $f 'path' '')
                SerializedPath = [string](Get-JsonProp $f 'serializedPath' '')
                Kind           = [string](Get-JsonProp $f 'kind' '')
                Role           = [string](Get-JsonProp $f 'role' '')
                IsList         = [bool](Get-JsonProp $f 'isList' $false)
                ListEncoding   = [string](Get-JsonProp $f 'listEncoding' '')
                EnumType       = [string](Get-JsonProp $f 'enumType' '')
                EnumNames      = $enumNames
                EnumValues     = $enumValues
                EnumByValue    = $enumMap
                ObjectType     = [string](Get-JsonProp $f 'objectType' '')
                Tooltip        = [string](Get-JsonProp $f 'tooltip' '')
                Truncated      = [bool](Get-JsonProp $f 'truncated' $false)
            }
        }
        $byType[[string]$t.name] = [pscustomobject]@{ Name = [string]$t.name; Kind = [string](Get-JsonProp $t 'kind' ''); MenuName = [string](Get-JsonProp $t 'menuName' ''); Fields = $fields }
    }

    $equipTypes = @($byType.Values | Where-Object { $_.Kind -eq 'equipment' } | Sort-Object Name | ForEach-Object { $_.Name })
    $cardTypes  = @($byType.Values | Where-Object { $_.Kind -eq 'card' }      | Sort-Object Name | ForEach-Object { $_.Name })

    $equipLeaf = @{}
    foreach ($tn in $equipTypes) {
        foreach ($f in $byType[$tn].Fields) {
            if ($f.Role -eq 'nestedCardModifiers') { continue }
            $equipLeaf[$f.Path] = $true
        }
    }
    $cardLeaf = @{}
    foreach ($tn in $cardTypes) {
        foreach ($f in $byType[$tn].Fields) { $cardLeaf[$f.Path] = $true }
    }

    return [pscustomobject]@{
        SchemaVersion       = [int]$json.schemaVersion
        ByType              = $byType
        EquipmentTypeNames  = $equipTypes
        CardTypeNames       = $cardTypes
        EquipmentLeafColumns = @($equipLeaf.Keys | Sort-Object)
        CardLeafColumns      = @($cardLeaf.Keys | Sort-Object)
    }
}

function Get-ModifierFieldSpec {
    param($Schema, [string]$TypeName, [string]$Path)

    if (-not $Schema.ByType.ContainsKey($TypeName)) { return $null }
    foreach ($f in $Schema.ByType[$TypeName].Fields) { if ($f.Path -eq $Path) { return $f } }
    return $null
}

# ---------------------------------------------------------------------------------------------------
# Node -> cell text, following one field's schema spec
# ---------------------------------------------------------------------------------------------------

function Get-DottedField {
    <# Walks a dotted path ("on.matchesEffect") through nested mapping nodes. #>
    param($Node, [string]$DottedPath)

    $current = $Node
    foreach ($seg in ($DottedPath -split '\.')) {
        $current = Get-NodeField $current $seg
        if ($null -eq $current) { return '' }
    }
    return $current
}

function Get-ModifierLeafValue {
    <# One field's raw YAML value, off a modifier's own top-level mapping Node, rendered as sheet text. #>
    param($Node, $FieldSpec, $AssetIndex)

    $raw = Get-DottedField -Node $Node -DottedPath $FieldSpec.SerializedPath

    switch ($FieldSpec.Kind) {
        'int'    { return [string](ConvertTo-IntOrDefault $raw) }
        'float'  { return [string](ConvertTo-DoubleOrDefault $raw) }
        'bool'   { if (ConvertTo-BoolOrDefault $raw) { return 'TRUE' } else { return 'FALSE' } }
        'string' { return [string]$raw }
        'object' {
            if ($FieldSpec.IsList) {
                $names = @()
                if ($raw -is [System.Collections.IEnumerable] -and -not ($raw -is [string])) {
                    foreach ($ref in $raw) {
                        $n = Resolve-AssetName -Reference $ref -AssetIndex $AssetIndex
                        if ($n) { $names += $n }
                    }
                }
                return ($names -join ', ')
            }
            return (Resolve-AssetName -Reference $raw -AssetIndex $AssetIndex)
        }
        'enum' {
            if ($FieldSpec.IsList) {
                $ids = @(ConvertFrom-PackedEnumList ([string]$raw))
                $names = @()
                foreach ($id in $ids) {
                    if ($FieldSpec.EnumByValue.ContainsKey($id)) { $names += $FieldSpec.EnumByValue[$id] } else { $names += "Unknown($id)" }
                }
                return ($names -join ', ')
            }
            $id = ConvertTo-IntOrDefault $raw
            if ($FieldSpec.EnumByValue.ContainsKey($id)) { return $FieldSpec.EnumByValue[$id] }
            return "Unknown($id)"
        }
        default { return '' }
    }
}

function ConvertTo-ModifierLeafPayload {
    <#
        .SYNOPSIS
            One sheet cell -> the typed shape Assets/Editor/EquipmentSheetImporter.cs's LeafValue class
            expects, so Unity writes it via SerializedObject with zero interpretation of its own - the
            same "PowerShell resolves, Unity just writes" split CardSheetImporter follows for cards.json.

        .OUTPUTS
            [ordered]@{ path; kind; intValue; floatValue; boolValue; stringValue; isNull; intList;
                        stringList; problem }
            `problem`, when non-empty, means this cell could not be resolved against the schema (an enum
            name Unity does not have, for instance) - the caller must treat the WHOLE item as failed
            rather than write a half-resolved modifier, the same rule WriteCard's "resolve everything
            before writing anything" already follows for card effects.
    #>
    param([string]$Text, $FieldSpec)

    $t = if ($null -eq $Text) { '' } else { $Text.Trim() }
    $out = [ordered]@{
        path = $FieldSpec.Path; kind = $FieldSpec.Kind
        intValue = 0; floatValue = 0.0; boolValue = $false; stringValue = ''
        isNull = $false; intList = @(); stringList = @(); problem = ''
    }

    switch ($FieldSpec.Kind) {
        'int' {
            if ($t -eq '') { $out.intValue = 0 }
            else {
                $parsed = 0
                if ([int]::TryParse($t, [ref]$parsed)) { $out.intValue = $parsed }
                else { $out.problem = "'$t' is not a whole number" }
            }
        }
        'float' {
            if ($t -eq '') { $out.floatValue = 0.0 }
            else {
                $parsed = 0.0
                if ([double]::TryParse($t, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) { $out.floatValue = $parsed }
                else { $out.problem = "'$t' is not a number" }
            }
        }
        'bool'   { $out.boolValue = ($t.ToUpperInvariant() -eq 'TRUE') }
        'string' { $out.stringValue = $t }
        'object' {
            if ($FieldSpec.IsList) {
                $names = @($t -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
                $out.stringList = $names
            }
            else {
                if ($t -eq '') { $out.isNull = $true } else { $out.stringValue = $t }
            }
        }
        'enum' {
            $byName = @{}
            for ($i = 0; $i -lt $FieldSpec.EnumNames.Count; $i++) { $byName[[string]$FieldSpec.EnumNames[$i]] = [int]$FieldSpec.EnumValues[$i] }

            if ($FieldSpec.IsList) {
                $ints = @()
                $bad = @()
                foreach ($name in @($t -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
                    if ($byName.ContainsKey($name)) { $ints += $byName[$name] } else { $bad += $name }
                }
                if ($bad.Count -gt 0) { $out.problem = "unknown value(s) for $($FieldSpec.EnumType): $($bad -join ', ')" }
                $out.intList = $ints
            }
            else {
                if ($t -eq '') { $out.intValue = if ($FieldSpec.EnumValues.Count -gt 0) { [int]$FieldSpec.EnumValues[0] } else { 0 } }
                elseif ($byName.ContainsKey($t)) { $out.intValue = $byName[$t] }
                else { $out.problem = "'$t' is not a $($FieldSpec.EnumType)" }
            }
        }
        default { $out.problem = "field kind '$($FieldSpec.Kind)' is not writable from the sheet" }
    }

    return $out
}

function ConvertTo-ModifierLeafText {
    <#
        .SYNOPSIS
            The inverse of Get-ModifierLeafValue for a SHEET cell - normalises what a human typed back
            into the same canonical text Get-ModifierLeafValue would have produced, so
            Format-EquipmentModifiers compares like with like across both sides. Actual value resolution
            (enum name -> int, asset name -> reference) happens in Unity, driven by the same schema.
    #>
    param([string]$Text, $FieldSpec)

    if ($FieldSpec.Kind -eq 'bool') {
        if ($Text.Trim().ToUpperInvariant() -eq 'TRUE') { return 'TRUE' } else { return 'FALSE' }
    }
    return $Text.Trim()
}

# ---------------------------------------------------------------------------------------------------
# One EquipmentData asset, flattened
# ---------------------------------------------------------------------------------------------------

function Read-EquipmentAsset {
    <#
        .SYNOPSIS
            One equipment item: its own scalar fields, plus every modifier sub-asset in list order (each
            carrying its leaf values and, for a CardTuningModifier, its own nested CardModifier children
            in list order).
    #>
    param([string]$AssetPath, $AssetIndex, $Schema, [string]$RepoRoot, [hashtable]$ScriptGuidIndex)

    $docs = Read-AnchoredDocuments -AssetPath $AssetPath -ScriptGuidIndex $ScriptGuidIndex
    $mainId = Get-MainObjectFileId -AssetPath $AssetPath
    if (-not $mainId -or -not $docs.Contains($mainId)) {
        # Fall back to the (occasionally wrong) "first document" the shared reader would use, rather
        # than failing the whole item outright - surfaced as a problem by the caller instead.
        $mainId = @($docs.Keys)[0]
    }

    $main = $docs[$mainId]
    $n = $main.Node

    $relative = $AssetPath
    $prefix = (Join-Path $RepoRoot 'Assets\Data\Equipment') + '\'
    if ($relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $relative = $relative.Substring($prefix.Length) }
    $relative = $relative -replace '\\', '/'
    $segments = $relative -split '/'
    $folder = if ($segments.Count -gt 1) { ($segments[0..($segments.Count - 2)] -join '/') } else { '' }

    $modifiersNode = Get-BackingField $n 'modifiers'
    $modifiers = @()
    $problems = @()

    if ($modifiersNode -is [System.Collections.IEnumerable] -and -not ($modifiersNode -is [string])) {
        foreach ($ref in $modifiersNode) {
            if (-not ($ref -is [System.Collections.IDictionary]) -or -not $ref.Contains('fileID')) { continue }
            $fid = [string]$ref['fileID']
            if ($fid -eq '0' -or -not $docs.Contains($fid)) {
                $problems += "modifier reference {fileID: $fid} does not resolve to a document in this file"
                continue
            }

            $modDoc = $docs[$fid]
            $typeName = $modDoc.Type
            if (-not $Schema.ByType.ContainsKey($typeName)) {
                $problems += "modifier '$typeName' (fileID $fid) is not in modifier-schema.json - re-run Tools > Equipment > Write Modifier Schema"
                continue
            }

            $leaves = [ordered]@{}
            $cardTuning = @()
            foreach ($f in $Schema.ByType[$typeName].Fields) {
                if ($f.Role -eq 'nestedCardModifiers') {
                    $childRefsNode = Get-DottedField -Node $modDoc.Node -DottedPath $f.SerializedPath
                    if ($childRefsNode -is [System.Collections.IEnumerable] -and -not ($childRefsNode -is [string])) {
                        foreach ($childRef in $childRefsNode) {
                            if (-not ($childRef -is [System.Collections.IDictionary]) -or -not $childRef.Contains('fileID')) { continue }
                            $cfid = [string]$childRef['fileID']
                            if ($cfid -eq '0' -or -not $docs.Contains($cfid)) {
                                $problems += "$typeName (fileID $fid): nested modifier reference {fileID: $cfid} does not resolve"
                                continue
                            }
                            $childDoc = $docs[$cfid]
                            $childType = $childDoc.Type
                            if (-not $Schema.ByType.ContainsKey($childType)) {
                                $problems += "nested modifier '$childType' (fileID $cfid) is not in modifier-schema.json"
                                continue
                            }
                            $childLeaves = [ordered]@{}
                            foreach ($cf in $Schema.ByType[$childType].Fields) {
                                $childLeaves[$cf.Path] = Get-ModifierLeafValue -Node $childDoc.Node -FieldSpec $cf -AssetIndex $AssetIndex
                            }
                            $cardTuning += [pscustomobject]@{ SubId = $cfid; TypeName = $childType; Leaves = $childLeaves }
                        }
                    }
                    continue
                }
                $leaves[$f.Path] = Get-ModifierLeafValue -Node $modDoc.Node -FieldSpec $f -AssetIndex $AssetIndex
            }

            $modifiers += [pscustomobject]@{ ModId = $fid; TypeName = $typeName; Leaves = $leaves; CardTuning = $cardTuning }
        }
    }

    return [pscustomobject]@{
        Name          = [IO.Path]::GetFileNameWithoutExtension($AssetPath)
        Guid          = (Get-AssetGuid -AssetPath $AssetPath)
        Path          = $AssetPath
        Folder        = $folder
        EquipmentName = [string](Get-BackingField $n 'equipmentName')
        Description   = [string](Get-BackingField $n 'description')
        Rarity        = Get-EnumName -Table $script:RarityNames -Value (Get-BackingField $n 'rarity')
        Class         = ConvertTo-ClassName (Get-BackingField $n 'requiredClass')
        Slot          = Get-EnumName -Table $script:SlotNames -Value (Get-BackingField $n 'slot')
        NoReward      = if ((ConvertTo-IntOrDefault (Get-BackingField $n 'excludeFromRewards')) -ne 0) { 'TRUE' } else { 'FALSE' }
        Modifiers     = $modifiers
        Problems      = $problems
    }
}

# ---------------------------------------------------------------------------------------------------
# The canonical "Modifiers" merge string - one column standing in for the whole modifier tree, same
# trade Format-LevelLayout already makes for a level's board. Row/list order is preserved verbatim
# (never sorted) because order is semantically authoritative here - see CardTuningModifier, which
# applies its children FIFO.
# ---------------------------------------------------------------------------------------------------

function Format-EquipmentModifiers {
    param($Modifiers)

    $parts = @()
    $i = 0
    foreach ($m in @($Modifiers)) {
        $leafText = (@($m.Leaves.Keys) | ForEach-Object { "$_=$($m.Leaves[$_])" }) -join ';'
        $tuningText = ''
        if (@($m.CardTuning).Count -gt 0) {
            $subParts = @()
            $j = 0
            foreach ($s in @($m.CardTuning)) {
                $subLeafText = (@($s.Leaves.Keys) | ForEach-Object { "$_=$($s.Leaves[$_])" }) -join ';'
                $subParts += "$j|$($s.TypeName)|$subLeafText"
                $j++
            }
            $tuningText = '[' + ($subParts -join '~') + ']'
        }
        $parts += "$i|$($m.TypeName)|$leafText;;$tuningText"
        $i++
    }
    return ($parts -join '||')
}

# ---------------------------------------------------------------------------------------------------
# Column definitions - canonical here so the writer, the exporter and the merge cannot drift.
# ---------------------------------------------------------------------------------------------------

function Get-EquipmentColumns {
    return @('Key', 'Folder', 'Item Name', 'Description', 'Effect Preview', 'Rarity', 'Class', 'Slot',
             'No Reward', 'Modifier Summary', 'Mod Count', 'GUID', 'Sync', 'Notes')
}

function Get-EquipmentMergeColumns {
    return @('Key', 'Folder', 'Item Name', 'Description', 'Rarity', 'Class', 'Slot', 'No Reward', 'Modifiers')
}

function Get-ModifierColumns {
    param($Schema)
    return @('Item', 'Ord', 'Modifier Type', 'Describe') + @($Schema.EquipmentLeafColumns) + @('Mod Id', 'GUID', 'Sync', 'Notes')
}

function Get-CardTuningColumns {
    param($Schema)
    return @('Item', 'Ord', 'Sub', 'Card Modifier Type', 'Describe') + @($Schema.CardLeafColumns) + @('Sub Id', 'Mod Id', 'GUID', 'Sync', 'Notes')
}

function Get-IdeaColumns {
    return @('Key', 'Folder', 'Item Name', 'Description', 'Rarity', 'Class', 'Slot', 'Buildable', 'Engine Work', 'Source', 'Priority', 'Notes')
}

function Get-EquipmentIssues {
    <#
        Data hygiene, surfaced on the Balance tab - the Get-CardIssues analogue. Every check here is a
        real condition found in the current assets, not a hypothetical.
    #>
    param($Item)

    $issues = @()

    if (@($Item.Modifiers).Count -eq 0) { $issues += 'no modifiers' }
    if ([string]::IsNullOrWhiteSpace($Item.EquipmentName)) { $issues += 'blank item name' }
    if ([string]::IsNullOrWhiteSpace($Item.Description)) { $issues += 'blank description' }
    if ($Item.EquipmentName -and $Item.EquipmentName -ne $Item.Name) { $issues += "display name '$($Item.EquipmentName)' differs from Key '$($Item.Name)'" }
    if ($Item.Slot -eq 'Ring') { $issues += 'slot not set (defaults to Ring, unlimited)' }

    foreach ($m in $Item.Modifiers) {
        if ($m.TypeName -eq 'CardTuningModifier') {
            $tags = if ($m.Leaves.Contains('filter.tags')) { [string]$m.Leaves['filter.tags'] } else { '' }
            $cards = if ($m.Leaves.Contains('filter.cards')) { [string]$m.Leaves['filter.cards'] } else { '' }
            if ([string]::IsNullOrWhiteSpace($tags) -and [string]::IsNullOrWhiteSpace($cards)) {
                $issues += 'a Card Tuning modifier matches nothing (filter.tags and filter.cards both empty)'
            }
            if (@($m.CardTuning).Count -eq 0) { $issues += 'a Card Tuning modifier has no child rules' }
        }
    }

    return ($issues -join '; ')
}

function Get-EquipmentBalanceRecord {
    param($Item, $Schema)

    $tags = @()
    $cards = @()
    $tunes = $false
    foreach ($m in $Item.Modifiers) {
        if ($m.TypeName -ne 'CardTuningModifier') { continue }
        $tunes = $true
        if ($m.Leaves.Contains('filter.tags') -and $m.Leaves['filter.tags']) { $tags += $m.Leaves['filter.tags'] }
        if ($m.Leaves.Contains('filter.cards') -and $m.Leaves['filter.cards']) { $cards += $m.Leaves['filter.cards'] }
    }
    $projects = @($Item.Modifiers | Where-Object { $_.TypeName -ne 'CardTuningModifier' }).Count -gt 0

    return [ordered]@{
        Key             = $Item.Name
        'Item Name'     = $Item.EquipmentName
        Rarity          = $Item.Rarity
        Class           = $Item.Class
        Slot            = $Item.Slot
        'Mod Count'     = @($Item.Modifiers).Count
        'Modifier Types' = (($Item.Modifiers | ForEach-Object { $_.TypeName }) -join ', ')
        Projects        = if ($projects) { 'Yes' } else { 'No' }
        'Tunes Cards'   = if ($tunes) { 'Yes' } else { 'No' }
        'Cards Named'   = ($cards -join ', ')
        'Tags Named'    = ($tags -join ', ')
        'No Reward'     = $Item.NoReward
        Issues          = Get-EquipmentIssues -Item $Item
    }
}

# ---------------------------------------------------------------------------------------------------
# Sheet-side reconstruction - Modifiers/Card Tuning rows for one item -> the same tree shape
# Read-EquipmentAsset produces, so Format-EquipmentModifiers can compare like with like. Used by both
# the export's unsynced-edit guard and Import-EquipmentSheet.ps1's merge.
# ---------------------------------------------------------------------------------------------------

function Get-SheetModifierTree {
    <#
        .SYNOPSIS
            Groups this item's rows on the Modifiers and Card Tuning tabs into the Modifiers array shape
            Read-EquipmentAsset returns - ModId, TypeName, Leaves, CardTuning - so the sheet side of a
            merge can be formatted with the exact same Format-EquipmentModifiers function as the asset
            side.

        .DESCRIPTION
            Order is SHEET ROW POSITION, never the Ord cell's numeric value - reordering an item's
            modifiers means moving rows, not renumbering Ord (which the export writes as a display aid
            and this function ignores for ordering). A Card Tuning row is matched to its parent Modifiers
            row first by Mod Id (when both are non-blank and agree), falling back to matching Ord cells
            for a brand-new pair neither side has a Mod Id for yet. An ambiguous match - more than one
            candidate parent - is reported as a Problem and that Card Tuning row is dropped, never
            guessed at.
    #>
    param([string]$Key, $ModifierRows, $TuningRows, $Schema)

    $mine = @($ModifierRows | Where-Object { $_.PSObject.Properties.Name -contains 'Item' -and ([string]$_.Item).Trim() -eq $Key })
    $tuningMine = @($TuningRows | Where-Object { $_.PSObject.Properties.Name -contains 'Item' -and ([string]$_.Item).Trim() -eq $Key })

    $problems = @()
    $result = @()

    foreach ($row in $mine) {
        $typeName = ([string](Get-CellValue $row 'Modifier Type')).Trim()
        $modId = ([string](Get-CellValue $row 'Mod Id')).Trim()
        $ord = ([string](Get-CellValue $row 'Ord')).Trim()

        if (-not $Schema.ByType.ContainsKey($typeName)) {
            $problems += "$Key`: Modifiers row has unknown Modifier Type '$typeName'"
            continue
        }

        $leaves = [ordered]@{}
        foreach ($f in $Schema.ByType[$typeName].Fields) {
            if ($f.Role -eq 'nestedCardModifiers') { continue }
            $text = [string](Get-CellValue $row $f.Path)
            $leaves[$f.Path] = ConvertTo-ModifierLeafText -Text $text -FieldSpec $f
        }

        # Card Tuning children for this row: prefer a Mod Id match, fall back to an Ord match.
        $children = @()
        if ($typeName -eq 'CardTuningModifier') {
            $byModId = @($tuningMine | Where-Object { $modId -and (([string](Get-CellValue $_ 'Mod Id')).Trim() -eq $modId) })
            $candidates = if ($byModId.Count -gt 0) { $byModId }
                          else { @($tuningMine | Where-Object { $ord -and (([string](Get-CellValue $_ 'Ord')).Trim() -eq $ord) }) }

            $subOrd = 0
            foreach ($crow in $candidates) {
                $childType = ([string](Get-CellValue $crow 'Card Modifier Type')).Trim()
                if (-not $Schema.ByType.ContainsKey($childType)) {
                    $problems += "$Key`: Card Tuning row has unknown Card Modifier Type '$childType'"
                    continue
                }
                $childLeaves = [ordered]@{}
                foreach ($cf in $Schema.ByType[$childType].Fields) {
                    $text = [string](Get-CellValue $crow $cf.Path)
                    $childLeaves[$cf.Path] = ConvertTo-ModifierLeafText -Text $text -FieldSpec $cf
                }
                $children += [pscustomobject]@{
                    SubId = ([string](Get-CellValue $crow 'Sub Id')).Trim()
                    TypeName = $childType
                    Leaves = $childLeaves
                }
                $subOrd++
            }
        }

        $result += [pscustomobject]@{ ModId = $modId; TypeName = $typeName; Leaves = $leaves; CardTuning = $children }
    }

    return [pscustomobject]@{ Modifiers = $result; Problems = $problems }
}

function Get-CellValue {
    param($Row, [string]$Name)

    if ($null -eq $Row) { return '' }
    if (-not ($Row.PSObject.Properties.Name -contains $Name)) { return '' }
    $v = $Row.$Name
    if ($null -eq $v) { return '' }
    return ([string]$v).Trim()
}

function Get-EquipmentMergeRow {
    <# The current-ASSET side of the merge, from a Read-EquipmentAsset result. #>
    param($Item)

    return [ordered]@{
        Key         = $Item.Name
        Folder      = $Item.Folder
        'Item Name' = $Item.EquipmentName
        Description = $Item.Description
        Rarity      = $Item.Rarity
        Class       = $Item.Class
        Slot        = $Item.Slot
        'No Reward' = $Item.NoReward
        Modifiers   = Format-EquipmentModifiers -Modifiers $Item.Modifiers
    }
}

# ---------------------------------------------------------------------------------------------------
# Baseline
# ---------------------------------------------------------------------------------------------------

function Get-EquipmentBaselinePath {
    param([string]$ToolDir)
    return (Join-Path $ToolDir 'baseline.json')
}

function Read-EquipmentBaseline {
    param([string]$ToolDir)

    $path = Get-EquipmentBaselinePath -ToolDir $ToolDir
    $result = @{ Exists = $false; Items = @{}; WrittenUtc = '' }
    if (-not (Test-Path -LiteralPath $path)) { return $result }

    try { $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    catch {
        Write-Warning "Equipment baseline.json could not be read ($($_.Exception.Message)) - treating this as a first sync."
        return $result
    }

    $result.Exists = $true
    if ($json.PSObject.Properties.Name -contains 'writtenUtc') { $result.WrittenUtc = [string]$json.writtenUtc }

    if ($json.PSObject.Properties.Name -contains 'items') {
        foreach ($entry in @($json.items)) {
            if (-not $entry.guid) { continue }
            $cols = @{}
            foreach ($p in $entry.columns.PSObject.Properties) { $cols[$p.Name] = ConvertTo-Comparable $p.Value }
            $result.Items[[string]$entry.guid] = $cols
        }
    }

    return $result
}

function Write-EquipmentBaseline {
    param([string]$ToolDir, $Items)

    $rows = @()
    foreach ($item in @($Items)) {
        if (-not $item.Guid) { continue }
        $cols = Get-EquipmentMergeRow -Item $item
        $comparable = [ordered]@{}
        foreach ($k in $cols.Keys) { $comparable[$k] = ConvertTo-Comparable $cols[$k] }
        $rows += [ordered]@{ guid = $item.Guid; columns = $comparable }
    }

    $payload = [ordered]@{
        writtenUtc = (Get-Date).ToUniversalTime().ToString('o')
        items      = @($rows)
    }

    Set-Content -LiteralPath (Get-EquipmentBaselinePath -ToolDir $ToolDir) -Value ($payload | ConvertTo-Json -Depth 10) -Encoding utf8
}

# ---------------------------------------------------------------------------------------------------
# Describe cache - read-only preview text, generated by Unity (EquipmentModifierSchema.WriteDescribeCache)
# ---------------------------------------------------------------------------------------------------

function Import-DescribeCache {
    param([string]$ToolDir)

    $path = Join-Path $ToolDir 'describe-cache.json'
    $byGuid = @{}
    if (-not (Test-Path -LiteralPath $path)) { return $byGuid }

    try { $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    catch { return $byGuid }

    foreach ($entry in @($json.items)) {
        if (-not $entry.guid) { continue }
        $byGuid[[string]$entry.guid] = [pscustomobject]@{ Preview = [string]$entry.preview; Hash = [string]$entry.hash }
    }
    return $byGuid
}

function Get-DescribePreview {
    <#
        The Effect Preview cell text for one item: the cached Describe() text if the cache's hash still
        matches what is on disk right now, otherwise a staleness marker - never a guessed value.
    #>
    param($DescribeCache, $Item)

    if (-not $DescribeCache.ContainsKey($Item.Guid)) { return '(no preview - sync in Unity)' }

    $entry = $DescribeCache[$Item.Guid]
    $currentHash = Get-ModifierTreeHash -Modifiers $Item.Modifiers
    if ($entry.Hash -ne $currentHash) { return '(stale - sync in Unity)' }
    return $entry.Preview
}

function Get-ModifierTreeHash {
    <#
        .SYNOPSIS
            A STRUCTURAL fingerprint of an item's modifier tree - which types occupy which sub-asset ids,
            in order - computed identically here and in EquipmentModifierSchema.WriteDescribeCache (same
            "TypeName:Id" tokens, same '|'/';'/'[]' joins, same SHA1). Deliberately does not depend on
            Describe() text, which only C# can produce.

        .DESCRIPTION
            This is a best-effort staleness check, not a guarantee: it catches a modifier added, removed,
            reordered or retyped since the cache was written, which is the common case a stale preview
            actually comes from. It will NOT catch a numeric field edited in the Inspector with the type
            list unchanged - a bare PowerShell export right after such an edit can still show a preview
            whose wording no longer matches the new number. The normal sync path does not have this gap:
            it always runs WriteDescribeCache immediately before the refresh export, so the cache is
            never behind the assets it describes when a sync is what produced the workbook.
    #>
    param($Modifiers)

    $parts = @()
    foreach ($m in @($Modifiers)) {
        $token = "$($m.TypeName):$($m.ModId)"
        if (@($m.CardTuning).Count -gt 0) {
            $childParts = @()
            foreach ($s in @($m.CardTuning)) { $childParts += "$($s.TypeName):$($s.SubId)" }
            $token += '[' + ($childParts -join ';') + ']'
        }
        $parts += $token
    }
    return (Get-Sha1Hex -Text ($parts -join '|'))
}

function Get-Sha1Hex {
    param([string]$Text)

    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = $sha1.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text))
        return -join ($bytes | ForEach-Object { $_.ToString('x2') })
    }
    finally { $sha1.Dispose() }
}

Export-ModuleMember -Function *
