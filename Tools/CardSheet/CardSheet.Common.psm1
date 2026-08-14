<#
.SYNOPSIS
    Shared plumbing for the card design workbook: reads Unity .asset YAML, resolves GUID references,
    and turns both into the flat records the spreadsheet is built from.

.DESCRIPTION
    Read-only with respect to the Unity project. Nothing in here writes to Assets/ - Export-CardSheet
    parses, Import-CardSheet emits a JSON intermediate, and the Unity Editor owns every asset write.
    See Assets/Editor/CardSheetImporter.cs.

    Why parse the YAML by hand rather than drive Unity for the read direction too: Unity's batch mode
    refuses to run while the Editor holds Temp/UnityLockfile, which is essentially always. Reading is
    safe to do without it - the format is stable and every card writes identical key ordering.

    Three serialization quirks this has to know about, all confirmed against the current assets:

      * List<enum> and bool[] serialize as a packed little-endian hex string, NOT a YAML list.
        "0200000003000000" is [Defence, Movement]; an empty list is a blank value.
      * Long strings fold at ~80 chars with a deeper-indented continuation line. A fold is a space.
      * Strings with leading/trailing whitespace get single-quoted, with '' escaping a quote.
#>

Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------------------------------
# Enum tables. Int values are written into .asset files - these must match the C# declaration order.
# Sources: Assets/Scripts/Cards/CharacterClass.cs, Assets/Scripts/Loot/Rarity.cs,
# Assets/Scripts/Loot/CardTag.cs, Assets/Scripts/Cards/TargetRange.cs, Assets/Scripts/Cards/AreaShape.cs,
# Assets/Scripts/CardEffects/CardEffect.cs, Assets/Scripts/Cards/CardKeywordType.cs,
# Assets/Scripts/Statuses/StatusType.cs
# ---------------------------------------------------------------------------------------------------

$script:RarityNames  = @('Common', 'Uncommon', 'Rare', 'Legendary', 'NotOffered')
$script:TagNames     = @('None', 'Attack', 'Defence', 'Movement', 'Poison', 'Fire', 'Summon', 'Healing')
$script:ShapeNames   = @('Anywhere', 'Chebyshev', 'Manhattan', 'SelfTile')
$script:AreaKindNames= @('Single', 'Radius', 'Pattern')
$script:AimNames     = @('Tile', 'Self')
$script:KeywordNames = @('None', 'Innate', 'Cooldown', 'Dormant')
$script:StatusNames  = @('None', 'Strength', 'DoubleNextAttack', 'Poison', 'Frozen', 'Shield', 'Block',
                         'Parry', 'Rooted', 'DoubleShield', 'Dodge', 'Weaken', 'Taunt')

# Assets/Scripts/TileEffects/TileEffectType.cs
$script:TileEffectNames = @('None', 'WallOfForce', 'WallOfFlames')

# CharacterClass is a [Flags] mask, so it needs bit handling rather than an index lookup.
$script:ClassBits = [ordered]@{ Knight = 1; Mage = 2; Rogue = 4 }

function Get-EnumName {
    param([string[]]$Table, $Value, [string]$Fallback = '')

    $i = ConvertTo-IntOrDefault $Value -Default -1
    if ($i -ge 0 -and $i -lt $Table.Count) { return $Table[$i] }
    if ($Fallback) { return $Fallback }
    return "Unknown($Value)"
}

function Get-EnumValue {
    param([string[]]$Table, [string]$Name, [int]$Default = 0)

    if ([string]::IsNullOrWhiteSpace($Name)) { return $Default }

    for ($i = 0; $i -lt $Table.Count; $i++) {
        if ($Table[$i] -eq $Name.Trim()) { return $i }
    }
    return $Default
}

function ConvertTo-IntOrDefault {
    param($Value, [int]$Default = 0)

    if ($null -eq $Value) { return $Default }
    $text = ([string]$Value).Trim()
    if ($text -eq '') { return $Default }

    $parsed = 0
    if ([int]::TryParse($text, [ref]$parsed)) { return $parsed }
    return $Default
}

function ConvertTo-ClassName {
    <# 0 is Any; anything else is a bit mask, so 5 reads "Knight+Rogue". #>
    param($Mask)

    $value = ConvertTo-IntOrDefault $Mask
    if ($value -eq 0) { return 'Any' }

    $parts = @()
    foreach ($name in $script:ClassBits.Keys) {
        if (($value -band $script:ClassBits[$name]) -ne 0) { $parts += $name }
    }

    if ($parts.Count -eq 0) { return "Unknown($value)" }
    return ($parts -join '+')
}

function ConvertFrom-ClassName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return 0 }
    if ($Name.Trim() -eq 'Any') { return 0 }

    $mask = 0
    foreach ($part in ($Name -split '\+')) {
        $key = $part.Trim()
        if ($script:ClassBits.Contains($key)) { $mask = $mask -bor $script:ClassBits[$key] }
    }
    return $mask
}

# ---------------------------------------------------------------------------------------------------
# Packed hex blobs
# ---------------------------------------------------------------------------------------------------

function ConvertFrom-PackedEnumList {
    <#
        Unity writes List<SomeEnum> as little-endian 4-byte ints run together in hex, with no
        separators and no YAML list syntax at all. "0200000003000000" -> @(2, 3).
    #>
    param([string]$Hex)

    # Returns are deliberately NOT comma-wrapped: callers wrap with @(), and ",$array" plus @() compose
    # into a one-element array holding the array rather than the array itself.
    if ([string]::IsNullOrWhiteSpace($Hex)) { return }

    $clean = ($Hex -replace '[^0-9a-fA-F]', '')
    if ($clean.Length -lt 8) { return }

    $values = @()
    for ($i = 0; $i + 8 -le $clean.Length; $i += 8) {
        $word = $clean.Substring($i, 8)
        # Little-endian: reverse the byte pairs before parsing.
        $bytes = @()
        for ($b = 0; $b -lt 8; $b += 2) { $bytes += $word.Substring($b, 2) }
        [array]::Reverse($bytes)
        $values += [Convert]::ToInt32(($bytes -join ''), 16)
    }
    return $values
}

function ConvertTo-PackedEnumList {
    param([int[]]$Values)

    if ($null -eq $Values -or $Values.Count -eq 0) { return '' }

    $sb = New-Object System.Text.StringBuilder
    foreach ($v in $Values) {
        $bytes = [BitConverter]::GetBytes([int]$v)   # already little-endian on x86/x64
        foreach ($b in $bytes) { [void]$sb.Append($b.ToString('x2')) }
    }
    return $sb.ToString()
}

function Measure-PackedBoolCount {
    <# bool[] packs one byte per element: "000001000000..." - count the 01s. #>
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) { return 0 }

    $clean = ($Hex -replace '[^0-9a-fA-F]', '')
    $count = 0
    for ($i = 0; $i + 2 -le $clean.Length; $i += 2) {
        if ($clean.Substring($i, 2) -ne '00') { $count++ }
    }
    return $count
}

# ---------------------------------------------------------------------------------------------------
# YAML reader
#
# A targeted indentation parser rather than a general YAML implementation: Unity emits a tiny, very
# regular subset, and a real parser would be more surface area for no benefit. Produces nested
# hashtables and arrays; scalars stay strings so callers convert at the point of use.
# ---------------------------------------------------------------------------------------------------

function Get-LineIndent {
    param([string]$Line)

    $i = 0
    while ($i -lt $Line.Length -and $Line[$i] -eq ' ') { $i++ }
    return $i
}

function ConvertFrom-YamlScalar {
    param([string]$Text)

    $t = $Text.Trim()
    if ($t -eq '') { return '' }
    if ($t -eq '[]') { return @() }

    # Flow mapping: {fileID: 11400000, guid: abc, type: 2} / {x: 2, y: 0} / {r: 0.1, g: 0.2, ...}
    if ($t.StartsWith('{') -and $t.EndsWith('}')) {
        $inner = $t.Substring(1, $t.Length - 2)
        $map = @{}
        foreach ($pair in ($inner -split ',')) {
            $bits = $pair -split ':', 2
            if ($bits.Count -eq 2) { $map[$bits[0].Trim()] = $bits[1].Trim() }
        }
        return $map
    }

    # Single-quoted, used whenever the value has significant leading/trailing whitespace.
    if ($t.StartsWith("'") -and $t.EndsWith("'") -and $t.Length -ge 2) {
        return $t.Substring(1, $t.Length - 2).Replace("''", "'")
    }

    return $t
}

function ConvertFrom-UnityYaml {
    <#
        .SYNOPSIS
            Parses a Unity .asset body into nested hashtables. Returns the MonoBehaviour node.
    #>
    param([string[]]$Lines)

    # Drop the document header and find the object node ("MonoBehaviour:" at indent 0).
    $body = @()
    $started = $false
    foreach ($line in $Lines) {
        if (-not $started) {
            if ($line -match '^[A-Za-z_][A-Za-z0-9_]*:\s*$' -and (Get-LineIndent $line) -eq 0) { $started = $true }
            continue
        }
        $body += $line
    }
    if (-not $started) { return @{} }

    $idx = 0
    return (Read-YamlMapping -Lines $body -Index ([ref]$idx) -Indent 2)
}

function Read-YamlMapping {
    param([string[]]$Lines, [ref]$Index, [int]$Indent)

    $map = [ordered]@{}

    while ($Index.Value -lt $Lines.Count) {
        $line = $Lines[$Index.Value]

        if ($line.Trim() -eq '') { $Index.Value++; continue }

        $lineIndent = Get-LineIndent $line
        if ($lineIndent -lt $Indent) { break }
        if ($line.Trim().StartsWith('- ') -or $line.Trim() -eq '-') { break }

        if ($line -notmatch '^\s*([^:]+):\s?(.*)$') { $Index.Value++; continue }

        $key  = $Matches[1].Trim()
        $rest = $Matches[2]
        $Index.Value++

        if ($rest.Trim() -ne '') {
            # Scalar, possibly folded across following deeper-indented lines.
            $text = $rest.Trim()
            while ($Index.Value -lt $Lines.Count) {
                $next = $Lines[$Index.Value]
                if ($next.Trim() -eq '') { break }
                $nextIndent = Get-LineIndent $next
                if ($nextIndent -le $lineIndent) { break }
                if ($next -match '^\s*[^:\s][^:]*:\s') { break }
                if ($next -match '^\s*[^:\s][^:]*:\s*$') { break }
                if ($next.Trim().StartsWith('- ')) { break }
                $text += ' ' + $next.Trim()
                $Index.Value++
            }
            $map[$key] = ConvertFrom-YamlScalar $text
            continue
        }

        # Empty right-hand side: a nested block, a sequence, or genuinely empty.
        $peek = $Index.Value
        while ($peek -lt $Lines.Count -and $Lines[$peek].Trim() -eq '') { $peek++ }

        if ($peek -ge $Lines.Count) { $map[$key] = ''; continue }

        $peekIndent = Get-LineIndent $Lines[$peek]
        $peekTrim   = $Lines[$peek].Trim()

        if ($peekTrim.StartsWith('- ') -and $peekIndent -eq $lineIndent) {
            $map[$key] = Read-YamlSequence -Lines $Lines -Index $Index -Indent $lineIndent
        }
        elseif ($peekIndent -gt $lineIndent) {
            $map[$key] = Read-YamlMapping -Lines $Lines -Index $Index -Indent $peekIndent
        }
        else {
            $map[$key] = ''
        }
    }

    return $map
}

function Read-YamlSequence {
    param([string[]]$Lines, [ref]$Index, [int]$Indent)

    $items = @()

    while ($Index.Value -lt $Lines.Count) {
        $line = $Lines[$Index.Value]

        if ($line.Trim() -eq '') { $Index.Value++; continue }

        $lineIndent = Get-LineIndent $line
        if ($lineIndent -ne $Indent -or -not $line.Trim().StartsWith('- ')) { break }

        # Collect this item's lines, rewriting the leading "- " to spaces so the item body parses
        # as an ordinary mapping two columns in.
        $itemLines = @((' ' * ($Indent + 2)) + $line.Trim().Substring(2))
        $Index.Value++

        while ($Index.Value -lt $Lines.Count) {
            $next = $Lines[$Index.Value]
            if ($next.Trim() -eq '') { $Index.Value++; continue }
            $nextIndent = Get-LineIndent $next
            if ($nextIndent -le $Indent) { break }
            $itemLines += $next
            $Index.Value++
        }

        $first = $itemLines[0]

        # A flow mapping - "- {fileID: 11400000, guid: ..., type: 2}", which is how every object
        # reference list serializes - has to be tested for first. It otherwise matches the block
        # mapping pattern below ("{fileID" reads as a key) and parses into nonsense.
        if ($first.Trim().StartsWith('{')) {
            $items += (ConvertFrom-YamlScalar $first)
        }
        elseif ($first -match '^\s*[^:\s][^:]*:\s?') {
            $sub = 0
            $items += ,(Read-YamlMapping -Lines $itemLines -Index ([ref]$sub) -Indent ($Indent + 2))
        }
        else {
            $items += (ConvertFrom-YamlScalar $first)
        }
    }

    return ,$items
}

# ---------------------------------------------------------------------------------------------------
# Asset index
# ---------------------------------------------------------------------------------------------------

function Get-AssetGuid {
    param([string]$AssetPath)

    $meta = "$AssetPath.meta"
    if (-not (Test-Path -LiteralPath $meta)) { return '' }

    foreach ($line in (Get-Content -LiteralPath $meta)) {
        if ($line -match '^guid:\s*([0-9a-fA-F]+)') { return $Matches[1] }
    }
    return ''
}

function Get-ScriptGuidIndex {
    <#
        Maps a script GUID to its class name by pairing every .cs.meta with its .cs filename. Built by
        scanning rather than hard-coded so adding a CardEffect subclass needs no change here.
    #>
    param([string]$RepoRoot)

    $index = @{}
    $roots = @(
        (Join-Path $RepoRoot 'Assets\Scripts'),
        (Join-Path $RepoRoot 'Assets\Editor')
    )

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($meta in (Get-ChildItem -LiteralPath $root -Recurse -Filter '*.cs.meta' -File)) {
            $guid = ''
            foreach ($line in (Get-Content -LiteralPath $meta.FullName)) {
                if ($line -match '^guid:\s*([0-9a-fA-F]+)') { $guid = $Matches[1]; break }
            }
            if ($guid) { $index[$guid] = $meta.BaseName -replace '\.cs$', '' }
        }
    }

    return $index
}

function Get-AssetTypeName {
    <#
        m_EditorClassIdentifier is "Assembly-CSharp::DamageEffect" and is present on every asset here,
        so prefer it. Fall back to the script GUID index when it is blank, which happens on assets
        written before the class existed in its current assembly.
    #>
    param($Node, [hashtable]$ScriptGuidIndex)

    if ($Node.Contains('m_EditorClassIdentifier')) {
        $id = [string]$Node['m_EditorClassIdentifier']
        if ($id -match '::(.+)$') { return $Matches[1].Trim() }
    }

    if ($Node.Contains('m_Script') -and $Node['m_Script'] -is [hashtable]) {
        $guid = [string]$Node['m_Script']['guid']
        if ($ScriptGuidIndex.ContainsKey($guid)) { return $ScriptGuidIndex[$guid] }
    }

    return ''
}

function ConvertFrom-UnityYamlDocuments {
    <#
        .SYNOPSIS
            Parses every object in a multi-document file - which is what a .prefab is.

        .DESCRIPTION
            A .asset holds one MonoBehaviour; a .prefab holds dozens (GameObjects, Transforms, the
            components on each). Needed to read a Totem's auras out of Totem.prefab, which is the only
            way a Summon card's actual output is knowable.
    #>
    param([string[]]$Lines)

    $docs = @()
    $current = $null

    foreach ($line in $Lines) {
        if ($line -match '^---\s') {
            if ($null -ne $current) { $docs += ,$current }
            $current = @()
            continue
        }
        if ($null -ne $current) { $current += $line }
    }
    if ($null -ne $current) { $docs += ,$current }

    $nodes = @()
    foreach ($doc in $docs) {
        # Each document opens with "ClassName:" at indent 0 and the body two columns in.
        $body = @()
        $started = $false
        foreach ($line in $doc) {
            if (-not $started) {
                if ($line -match '^[A-Za-z_][A-Za-z0-9_]*:\s*$') { $started = $true }
                continue
            }
            $body += $line
        }
        if (-not $started) { continue }

        $idx = 0
        $nodes += ,(Read-YamlMapping -Lines $body -Index ([ref]$idx) -Indent 2)
    }

    return $nodes
}

function Read-UnityAsset {
    param([string]$AssetPath, [hashtable]$ScriptGuidIndex)

    $lines = Get-Content -LiteralPath $AssetPath
    $isPrefab = $AssetPath -like '*.prefab'

    $components = @()
    if ($isPrefab) {
        foreach ($n in (ConvertFrom-UnityYamlDocuments -Lines $lines)) {
            $type = Get-AssetTypeName -Node $n -ScriptGuidIndex $ScriptGuidIndex
            if ($type) { $components += ,([pscustomobject]@{ Type = $type; Node = $n }) }
        }
        $node = @{}
        if ($components.Count -gt 0) { $node = $components[0].Node }
    }
    else {
        $node = ConvertFrom-UnityYaml -Lines $lines
    }

    $name = [IO.Path]::GetFileNameWithoutExtension($AssetPath)
    if ($node -is [System.Collections.IDictionary] -and $node.Contains('m_Name') -and $node['m_Name']) {
        $name = [string]$node['m_Name']
    }

    $type = ''
    if (-not $isPrefab) { $type = Get-AssetTypeName -Node $node -ScriptGuidIndex $ScriptGuidIndex }
    else { $type = 'Prefab' }

    return [pscustomobject]@{
        Path       = $AssetPath
        Name       = $name
        Type       = $type
        Guid       = (Get-AssetGuid -AssetPath $AssetPath)
        Node       = $node
        Components = $components
    }
}

function Get-PrefabComponent {
    <# The first component of the named type on a prefab, or $null. #>
    param($Asset, [string]$TypeName)

    if ($null -eq $Asset) { return $null }
    foreach ($c in @($Asset.Components)) {
        if ($c.Type -eq $TypeName) { return $c.Node }
    }
    return $null
}

function Build-AssetIndex {
    <#
        .SYNOPSIS
            Reads every .asset under the given folders once, keyed by GUID, so effect references
            resolve to a human name without re-reading files per card.
    #>
    param([string]$RepoRoot, [string[]]$RelativeFolders, [hashtable]$ScriptGuidIndex)

    $byGuid = @{}
    $all    = @()

    foreach ($rel in $RelativeFolders) {
        $full = Join-Path $RepoRoot $rel
        if (-not (Test-Path -LiteralPath $full)) { continue }

        # Prefabs are indexed too: a Summon card's output lives on the thing it summons.
        foreach ($file in (Get-ChildItem -LiteralPath $full -Recurse -File | Where-Object { $_.Extension -in '.asset', '.prefab' })) {
            $asset = Read-UnityAsset -AssetPath $file.FullName -ScriptGuidIndex $ScriptGuidIndex
            $all += $asset
            if ($asset.Guid) { $byGuid[$asset.Guid] = $asset }
        }
    }

    return [pscustomobject]@{ ByGuid = $byGuid; All = $all }
}

# ---------------------------------------------------------------------------------------------------
# Effect interpretation
# ---------------------------------------------------------------------------------------------------

function Get-NodeField {
    param($Node, [string]$Key, $Default = '')

    if ($null -eq $Node) { return $Default }
    if (-not ($Node -is [System.Collections.IDictionary])) { return $Default }
    if (-not $Node.Contains($Key)) { return $Default }
    return $Node[$Key]
}

function Get-EffectDescriptor {
    <#
        .SYNOPSIS
            Normalises a CardEffect asset into (Kind, Amount, Detail) so the balance sheet can score it
            without knowing every subclass.
    #>
    param($Asset)

    if ($null -eq $Asset) {
        return [pscustomobject]@{ Name = ''; Class = ''; Kind = 'None'; Amount = 0; Detail = ''; Scoreable = $true }
    }

    $n = $Asset.Node
    $kind = 'Other'; $amount = 0; $detail = ''; $scoreable = $true

    switch ($Asset.Type) {
        'DamageEffect' {
            $kind = 'Damage'
            $amount = ConvertTo-IntOrDefault (Get-NodeField $n 'damageAmount')
            if ((ConvertTo-IntOrDefault (Get-NodeField $n 'canHitAllies')) -ne 0) { $detail = 'hits allies' }
        }
        'HealEffect' {
            $kind = 'Heal'
            $amount = ConvertTo-IntOrDefault (Get-NodeField $n 'healAmount')
            if ((ConvertTo-IntOrDefault (Get-NodeField $n 'canHitEnemies')) -ne 0) { $detail = 'hits enemies' }
        }
        'ShieldEffect' {
            $kind = 'Shield'
            $amount = ConvertTo-IntOrDefault (Get-NodeField $n 'shieldAmount')
        }
        'BlockEffect' {
            # blockAmount is dead weight since BlockStatus.AmountPerHit became a const 5; the charge
            # count is what actually matters now. Both are reported on the Effects tab.
            $kind = 'Block'
            $amount = ConvertTo-IntOrDefault (Get-NodeField $n 'blockCount')
            $detail = "legacy blockAmount=" + (ConvertTo-IntOrDefault (Get-NodeField $n 'blockAmount'))
        }
        'ParryEffect' {
            $kind = 'Parry'
            $amount = ConvertTo-IntOrDefault (Get-NodeField $n 'parryCharges')
        }
        'DrawEffect' {
            $kind = 'Draw'
            $amount = ConvertTo-IntOrDefault (Get-NodeField $n 'drawAmount')
        }
        'MoveEffect' {
            $kind = 'Move'
            $amount = 1
        }
        'SummonEffect' {
            $kind = 'Summon'
            $amount = 1
            $scoreable = $false
            $detail = [string](Get-NodeField $n 'summonedObject' | ForEach-Object { if ($_ -is [System.Collections.IDictionary] -and $_.Contains('guid')) { $_['guid'] } else { '' } })
        }
        'TauntEffect' {
            $kind = 'Taunt'
            $amount = ConvertTo-IntOrDefault (Get-NodeField $n 'turnsRemaining')
            $scoreable = $false
        }
        'AnimateEffect' {
            $kind = 'Animate'
            $amount = 0
        }
        'SwapEffect' {
            # Pure displacement - real value, but positional rather than numeric.
            $kind = 'Swap'
            $amount = 1
            $scoreable = $false
        }
        'ApplyTileEffect' {
            $tile   = Get-EnumName -Table $script:TileEffectNames -Value (Get-NodeField $n 'effect')
            $turns  = ConvertTo-IntOrDefault (Get-NodeField $n 'turns') -Default 2
            $mag    = ConvertTo-IntOrDefault (Get-NodeField $n 'magnitude')
            $kind   = "Tile:$tile"
            # Wall of Flames ticks its magnitude at the end of every player turn it survives, and the
            # damage is unblockable. Wall of Force has no number at all - magnitude is unused - so it
            # is denial, not output, and stays off the curve.
            if ($tile -eq 'WallOfFlames') { $amount = $mag * $turns } else { $amount = 0; $scoreable = $false }
            $detail = "turns=$turns magnitude=$mag"
        }
        'ApplyStatusEffect' {
            $statusId = ConvertTo-IntOrDefault (Get-NodeField $n 'status')
            $status   = Get-EnumName -Table $script:StatusNames -Value $statusId
            $kind     = "Status:$status"
            $amount   = ConvertTo-IntOrDefault (Get-NodeField $n 'stacks')
            $turns    = ConvertTo-IntOrDefault (Get-NodeField $n 'turnsRemaining') -Default -1
            $detail   = "turns=$turns"
            # Denial and displacement have no damage-equivalent value; see the Balance tab.
            if (@('Frozen', 'Rooted', 'Taunt', 'Dodge') -contains $status) { $scoreable = $false }
        }
        default {
            $kind = if ($Asset.Type) { $Asset.Type } else { 'Unknown' }
            $scoreable = $false
        }
    }

    return [pscustomobject]@{
        Name      = $Asset.Name
        Class     = $Asset.Type
        Kind      = $kind
        Amount    = $amount
        Detail    = $detail
        Scoreable = $scoreable
    }
}

# ---------------------------------------------------------------------------------------------------
# Area / range formatting
# ---------------------------------------------------------------------------------------------------

function Format-Range {
    param($RangeNode)

    $shape = Get-EnumName -Table $script:ShapeNames -Value (Get-NodeField $RangeNode 'shape')
    $min   = ConvertTo-IntOrDefault (Get-NodeField $RangeNode 'minDistance')
    $max   = ConvertTo-IntOrDefault (Get-NodeField $RangeNode 'maxDistance')

    if ($shape -eq 'Anywhere' -or $shape -eq 'SelfTile') { return $shape }
    return "$shape $min-$max"
}

function Format-Area {
    param($AreaNode, $AssetIndex)

    $kindId = ConvertTo-IntOrDefault (Get-NodeField $AreaNode 'kind')
    $kind   = Get-EnumName -Table $script:AreaKindNames -Value $kindId

    if ($kind -eq 'Single') { return 'Single' }

    if ($kind -eq 'Radius') {
        return (Format-Range (Get-NodeField $AreaNode 'radius'))
    }

    if ($kind -eq 'Pattern') {
        $ref = Get-NodeField $AreaNode 'pattern'
        $name = Resolve-AssetName -Reference $ref -AssetIndex $AssetIndex
        if ($name) { return "Pattern:$name" }
        return 'Pattern:<missing>'
    }

    return $kind
}

function Measure-ShapeTiles {
    <# Tile count of a filled ring between two distances, per RangeShape. #>
    param([int]$Shape, [int]$Min, [int]$Max, [int]$BoardTiles = 64)

    switch ($Shape) {
        1 {   # Chebyshev: a filled square ring
            $outer = (2 * $Max + 1) * (2 * $Max + 1)
            $inner = 0
            if ($Min -gt 0) { $inner = (2 * $Min - 1) * (2 * $Min - 1) }
            return [Math]::Max(1, $outer - $inner)
        }
        2 {   # Manhattan: a filled diamond ring. Cells within d = 2d^2 + 2d + 1.
            $f = { param($d) if ($d -lt 0) { 0 } else { 2 * $d * $d + 2 * $d + 1 } }
            return [Math]::Max(1, (& $f $Max) - (& $f ($Min - 1)))
        }
        3 { return 1 }                  # SelfTile
        default { return $BoardTiles }  # Anywhere
    }
}

function Measure-AreaTilesFromText {
    <#
        .SYNOPSIS
            The same count as Measure-AreaTiles, but read off the sheet's Area column rather than an
            asset. Needed to score Ideas rows, which have no asset behind them yet.
    #>
    param([string]$Text, $AssetIndex, [int]$BoardTiles = 64)

    $t = $Text.Trim()
    if ($t -eq '' -or $t -eq 'Single') { return 1 }

    if ($t -match '^Pattern:\s*(.+)$') {
        $name = $Matches[1].Trim()
        foreach ($a in $AssetIndex.All) {
            if ($a.Type -eq 'EffectPattern' -and $a.Name -eq $name) {
                return [Math]::Max(1, (Measure-PackedBoolCount ([string](Get-NodeField $a.Node 'cardinalCells'))))
            }
        }
        return 1
    }

    if ($t -match '^(Anywhere|Chebyshev|Manhattan|SelfTile)(?:\s+(\d+)\s*-\s*(\d+))?$') {
        $shape = Get-EnumValue -Table $script:ShapeNames -Name $Matches[1]
        $min = 0; $max = 0
        if ($Matches[2]) { $min = [int]$Matches[2] }
        if ($Matches[3]) { $max = [int]$Matches[3] }
        return Measure-ShapeTiles -Shape $shape -Min $min -Max $max -BoardTiles $BoardTiles
    }

    return 1
}

function Measure-AreaTiles {
    <#
        How many tiles the footprint covers. Used as the AoE multiplier on the Balance tab, scaled by
        the occupancy weight - a 9-tile blast does not reliably find 9 targets.
    #>
    param($AreaNode, $AssetIndex, [int]$BoardTiles = 64)

    $kindId = ConvertTo-IntOrDefault (Get-NodeField $AreaNode 'kind')

    if ($kindId -eq 0) { return 1 }

    if ($kindId -eq 1) {
        $r     = Get-NodeField $AreaNode 'radius'
        $shape = ConvertTo-IntOrDefault (Get-NodeField $r 'shape')
        $min   = ConvertTo-IntOrDefault (Get-NodeField $r 'minDistance')
        $max   = ConvertTo-IntOrDefault (Get-NodeField $r 'maxDistance')
        return Measure-ShapeTiles -Shape $shape -Min $min -Max $max -BoardTiles $BoardTiles
    }

    if ($kindId -eq 2) {
        $ref = Get-NodeField $AreaNode 'pattern'
        $pattern = Resolve-AssetReference -Reference $ref -AssetIndex $AssetIndex
        if ($null -eq $pattern) { return 1 }
        $count = Measure-PackedBoolCount ([string](Get-NodeField $pattern.Node 'cardinalCells'))
        return [Math]::Max(1, $count)
    }

    return 1
}

function Resolve-AssetReference {
    param($Reference, $AssetIndex)

    if ($null -eq $Reference) { return $null }
    if (-not ($Reference -is [System.Collections.IDictionary])) { return $null }
    if (-not $Reference.Contains('guid')) { return $null }

    $guid = [string]$Reference['guid']
    if ($AssetIndex.ByGuid.ContainsKey($guid)) { return $AssetIndex.ByGuid[$guid] }
    return $null
}

function Resolve-AssetName {
    param($Reference, $AssetIndex)

    $asset = Resolve-AssetReference -Reference $Reference -AssetIndex $AssetIndex
    if ($null -eq $asset) { return '' }
    if ($asset.Name) { return $asset.Name }
    return [IO.Path]::GetFileNameWithoutExtension($asset.Path)
}

# ---------------------------------------------------------------------------------------------------
# Card records
# ---------------------------------------------------------------------------------------------------

function Get-BackingField {
    param($Node, [string]$Name, $Default = '')
    return (Get-NodeField $Node "<$Name>k__BackingField" $Default)
}

function Format-Keywords {
    param($KeywordsNode)

    if ($null -eq $KeywordsNode) { return '' }
    if (-not ($KeywordsNode -is [System.Collections.IEnumerable]) -or $KeywordsNode -is [string]) { return '' }

    $parts = @()
    foreach ($entry in $KeywordsNode) {
        if (-not ($entry -is [System.Collections.IDictionary])) { continue }
        $type = Get-EnumName -Table $script:KeywordNames -Value (Get-NodeField $entry 'type')
        $mag  = ConvertTo-IntOrDefault (Get-NodeField $entry 'magnitude')
        if ($type -eq 'None') { continue }
        if ($mag -gt 0) { $parts += "$type $mag" } else { $parts += $type }
    }
    return ($parts -join ', ')
}

function Format-Tags {
    param($TagsValue)

    $ids = @(ConvertFrom-PackedEnumList ([string]$TagsValue))
    if ($ids.Count -eq 0) { return '' }

    # An explicit None entry is authoring noise - it carries no meaning LootTable can bias on, and
    # showing it in the column would imply the card is tagged when it is not.
    $names = @()
    foreach ($id in $ids) {
        $name = Get-EnumName -Table $script:TagNames -Value $id
        if ($name -ne 'None') { $names += $name }
    }
    return ($names -join ', ')
}

function Get-CardRecord {
    <#
        .SYNOPSIS
            One flat spreadsheet row for a CardData asset, plus the structured effect list the Balance
            sheet needs.
    #>
    param($Asset, $AssetIndex, [string]$RepoRoot)

    $n = $Asset.Node

    $relative = $Asset.Path
    $prefix   = (Join-Path $RepoRoot 'Assets\Data\CardData') + '\'
    if ($relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        $relative = $relative.Substring($prefix.Length)
    }
    $relative = $relative -replace '\\', '/'

    $segments  = $relative -split '/'
    $classDir  = $segments[0]
    $folder    = ''
    if ($segments.Count -gt 2) { $folder = ($segments[1..($segments.Count - 2)] -join '/') }

    $rangeNode = Get-BackingField $n 'range'

    # Effect entries, flattened into the three (Effect, Aim, Area) column slots.
    $entries = @()
    $entriesNode = Get-BackingField $n 'effectEntries'
    if ($entriesNode -is [System.Collections.IEnumerable] -and -not ($entriesNode -is [string])) {
        foreach ($entry in $entriesNode) {
            if (-not ($entry -is [System.Collections.IDictionary])) { continue }

            $ref    = Get-NodeField $entry 'effect'
            $effect = Resolve-AssetReference -Reference $ref -AssetIndex $AssetIndex
            $area   = Get-NodeField $entry 'area'

            $isNull = $true
            if ($ref -is [System.Collections.IDictionary] -and $ref.Contains('guid')) { $isNull = $false }

            $entries += [pscustomobject]@{
                EffectName = if ($null -ne $effect) { $effect.Name } elseif ($isNull) { '' } else { '<missing>' }
                Aim        = Get-EnumName -Table $script:AimNames -Value (Get-NodeField $entry 'aimsAt')
                Area       = Format-Area -AreaNode $area -AssetIndex $AssetIndex
                Tiles       = Measure-AreaTiles -AreaNode $area -AssetIndex $AssetIndex
                Descriptor  = Get-EffectDescriptor -Asset $effect
                EffectAsset = $effect
                IsNullRef   = $isNull
            }
        }
    }

    $record = [ordered]@{
        Key          = [IO.Path]::GetFileNameWithoutExtension($Asset.Path)
        'Card Name'  = [string](Get-BackingField $n 'cardName')
        Class        = ConvertTo-ClassName (Get-BackingField $n 'requiredClass')
        Folder       = $folder
        Cost         = ConvertTo-IntOrDefault (Get-BackingField $n 'cost')
        Rarity       = Get-EnumName -Table $script:RarityNames -Value (Get-BackingField $n 'rarity')
        Tags         = Format-Tags (Get-BackingField $n 'tags')
        'Range Shape'= Get-EnumName -Table $script:ShapeNames -Value (Get-NodeField $rangeNode 'shape')
        'Range Min'  = ConvertTo-IntOrDefault (Get-NodeField $rangeNode 'minDistance')
        'Range Max'  = ConvertTo-IntOrDefault (Get-NodeField $rangeNode 'maxDistance')
    }

    $entries = @($entries)

    for ($i = 0; $i -lt 3; $i++) {
        $slot = $i + 1
        if ($i -lt $entries.Count) {
            $record["Effect $slot"] = $entries[$i].EffectName
            $record["Aim $slot"]    = $entries[$i].Aim
            $record["Area $slot"]   = $entries[$i].Area
        }
        else {
            $record["Effect $slot"] = ''
            $record["Aim $slot"]    = ''
            $record["Area $slot"]   = ''
        }
    }

    $record['Keywords']    = Format-Keywords (Get-BackingField $n 'keywords')
    $record['No Reward']   = if ((ConvertTo-IntOrDefault (Get-BackingField $n 'excludeFromRewards')) -ne 0) { 'TRUE' } else { 'FALSE' }
    $record['Description'] = [string](Get-BackingField $n 'description')
    $record['Animation']   = Resolve-AssetName -Reference (Get-BackingField $n 'animation') -AssetIndex $AssetIndex
    $record['GUID']        = $Asset.Guid
    $record['Sync']        = 'Live'
    $record['Notes']       = ''

    return [pscustomobject]@{
        Row        = $record
        Entries    = $entries
        ClassDir   = $classDir
        AssetPath  = $relative
        Asset      = $Asset
    }
}

function Get-EffectDescriptorFromName {
    <#
        .SYNOPSIS
            Derives a descriptor from an effect NAME alone, for effect assets that do not exist yet.

        .DESCRIPTION
            Most proposed cards want a magnitude nobody has authored - "Damage 7", "Poison 6". Without
            this they would score as nothing and drop off the power curve, which is the opposite of
            useful when the whole point is checking a proposal against the curve before building it.

            Reads the same "<Kind> <Amount>" convention the existing assets follow and that
            Import-CardSheet uses to decide what to create, so a name that scores here is a name the
            importer can also build. Returns $null for anything that does not fit.
    #>
    param([string]$Name)

    $n = $Name.Trim()
    if ($n -eq '') { return $null }

    $make = {
        param($kind, $amount, $scoreable)
        [pscustomobject]@{
            Name = $n; Class = '(proposed)'; Kind = $kind; Amount = $amount
            Detail = 'effect asset does not exist yet'; Scoreable = $scoreable
        }
    }

    if ($n -match '^(Damage|Heal|Shield|Block|Parry|Draw)\s+(\d+)$') {
        return & $make $Matches[1] ([int]$Matches[2]) $true
    }

    if ($n -match '^(Poison|Strength|Weaken)\s+(\d+)$') {
        return & $make "Status:$($Matches[1])" ([int]$Matches[2]) $true
    }

    if ($n -match '^Dodge(?:\s+(\d+))?$') {
        $stacks = 1
        if ($Matches[1]) { $stacks = [int]$Matches[1] }
        return & $make 'Status:Dodge' $stacks $false
    }

    switch ($n) {
        'Freeze'             { return & $make 'Status:Frozen' 1 $false }
        'Root'               { return & $make 'Status:Rooted' 1 $false }
        'Taunt'              { return & $make 'Taunt' 2 $false }
        'Double Shield'      { return & $make 'Status:DoubleShield' 1 $true }
        'Double Next Attack' { return & $make 'Status:DoubleNextAttack' 1 $true }
        'Move'               { return & $make 'Move' 1 $true }
    }

    return $null
}

function Get-TotemProjection {
    <#
        .SYNOPSIS
            What a summoned Totem actually projects: its auras, and the effects its reactions fire.

        .DESCRIPTION
            A Summon card is otherwise a black box on the balance sheet - "summons a thing" scores as
            nothing, which is wrong for a totem granting Shield 3 every time an ally attacks. Reads the
            Totem component off the summoned prefab and reports the numbers so the card can be plotted.

            Returns $null when the summoned prefab carries no Totem (a skeleton warrior, say) - a
            summoned *character* is a genuinely different thing to value and is left unscored.
    #>
    param($SummonEffectAsset, $AssetIndex)

    if ($null -eq $SummonEffectAsset) { return $null }

    $ref = Get-NodeField $SummonEffectAsset.Node 'summonedObject'
    $prefab = Resolve-AssetReference -Reference $ref -AssetIndex $AssetIndex
    if ($null -eq $prefab) { return $null }

    $totem = Get-PrefabComponent -Asset $prefab -TypeName 'Totem'
    if ($null -eq $totem) { return $null }

    $auras = @()
    $aurasNode = Get-NodeField $totem 'auras'
    if ($aurasNode -is [System.Collections.IEnumerable] -and -not ($aurasNode -is [string])) {
        foreach ($a in $aurasNode) {
            if (-not ($a -is [System.Collections.IDictionary])) { continue }
            $auras += [pscustomobject]@{
                Status = Get-EnumName -Table $script:StatusNames -Value (Get-NodeField $a 'type')
                Stacks = ConvertTo-IntOrDefault (Get-NodeField $a 'stacks')
            }
        }
    }

    $reactions = @()
    $reactionsNode = Get-NodeField $totem 'reactions'
    if ($reactionsNode -is [System.Collections.IEnumerable] -and -not ($reactionsNode -is [string])) {
        foreach ($rx in $reactionsNode) {
            if (-not ($rx -is [System.Collections.IDictionary])) { continue }
            $effect = Resolve-AssetReference -Reference (Get-NodeField $rx 'effect') -AssetIndex $AssetIndex
            if ($null -eq $effect) { continue }
            $reactions += [pscustomobject]@{
                Trigger    = ConvertTo-IntOrDefault (Get-NodeField $rx 'trigger')
                Descriptor = Get-EffectDescriptor -Asset $effect
            }
        }
    }

    return [pscustomobject]@{
        Prefab    = $prefab.Name
        Range     = Format-Range (Get-NodeField $totem 'range')
        Auras     = $auras
        Reactions = $reactions
    }
}

function Get-GlossaryPath {
    <#
        The one asset holding every tooltip the game shows. Lives under Assets/Scripts rather than
        Assets/Data, which is why the normal asset index does not pick it up.
    #>
    param([string]$RepoRoot)

    return (Join-Path $RepoRoot 'Assets\Scripts\UI\Tooltips\Glossary.asset')
}

function Read-GlossaryRows {
    <#
        .SYNOPSIS
            Flattens Glossary.asset into one row per explained term, for the Glossary sheet.

        .DESCRIPTION
            The asset holds two parallel lists - statuses keyed by StatusType, keywords keyed by
            CardKeywordType - with identical fields. Kind says which list a row came from, and is what
            the importer uses to put it back in the right one.

            Body keeps its {stacks} / {amount} / {magnitude} tokens verbatim. They are filled from the
            live status when the tooltip is built, so stripping or resolving them here would turn one
            sentence that reads correctly for Parry 2 and Parry 7 into a wrong one.
    #>
    param([string]$RepoRoot, [hashtable]$ScriptGuidIndex)

    $path = Get-GlossaryPath -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $path)) { return @() }

    $asset = Read-UnityAsset -AssetPath $path -ScriptGuidIndex $ScriptGuidIndex
    $rows = @()

    $lists = @(
        @{ Field = 'statuses'; Kind = 'Status';  Table = $script:StatusNames },
        @{ Field = 'keywords'; Kind = 'Keyword'; Table = $script:KeywordNames }
    )

    foreach ($list in $lists) {
        $node = Get-NodeField $asset.Node $list.Field
        $seen = @{}

        if ($node -is [System.Collections.IEnumerable] -and -not ($node -is [string])) {
            foreach ($entry in $node) {
                if (-not ($entry -is [System.Collections.IDictionary])) { continue }

                $terms = @()
                $termsNode = Get-NodeField $entry 'terms'
                if ($termsNode -is [System.Collections.IEnumerable] -and -not ($termsNode -is [string])) {
                    foreach ($t in $termsNode) { if ($t -is [string] -and $t -ne '') { $terms += $t } }
                }

                $type = Get-EnumName -Table $list.Table -Value (Get-NodeField $entry 'type')
                $seen[$type] = $true

                $rows += [pscustomobject][ordered]@{
                    Kind              = $list.Kind
                    Type              = $type
                    Title             = [string](Get-NodeField $entry 'title')
                    Body              = [string](Get-NodeField $entry 'body')
                    Terms             = ($terms -join ', ')
                    'Default Stacks'  = ConvertTo-IntOrDefault (Get-NodeField $entry 'defaultStacks') -Default 1
                    'Default Amount'  = ConvertTo-IntOrDefault (Get-NodeField $entry 'defaultAmount') -Default 1
                }
            }
        }

        # Every enum value with no entry gets a blank row, so the tab is a complete checklist of what
        # the game can explain rather than only what it currently does. Dodge, Weaken and Taunt have no
        # tooltip today and that is invisible in the Inspector. A blank row is inert - the importer
        # skips any row still missing both a Title and a Body.
        foreach ($name in $list.Table) {
            if ($name -eq 'None' -or $seen.ContainsKey($name)) { continue }

            $rows += [pscustomobject][ordered]@{
                Kind              = $list.Kind
                Type              = $name
                Title             = ''
                Body              = ''
                Terms             = ''
                'Default Stacks'  = 1
                'Default Amount'  = 1
            }
        }
    }

    return $rows
}

function Get-CardIssues {
    <#
        Data hygiene, surfaced on the Balance tab. These are all real conditions found in the current
        assets, not hypotheticals.
    #>
    param($Card)

    $issues = @()
    $row = $Card.Row
    $entries = @($Card.Entries)

    if ($entries.Count -eq 0)                             { $issues += 'no effects' }
    if (@($entries | Where-Object { $_.IsNullRef }).Count -gt 0) { $issues += 'null effect reference' }
    if (@($entries | Where-Object { $_.EffectName -eq '<missing>' }).Count -gt 0) { $issues += 'unresolved effect guid' }
    if ([string]::IsNullOrWhiteSpace($row['Card Name']))  { $issues += 'blank card name' }
    if ([string]::IsNullOrWhiteSpace($row['Description'])){ $issues += 'blank description' }

    $expected = @{ Knight = 'Knight'; Mage = 'Mage'; Rogue = 'Rogue' }
    if ($expected.ContainsKey($Card.ClassDir)) {
        if ($row['Class'] -notlike "*$($Card.ClassDir)*") {
            $issues += "class '$($row['Class'])' does not match $($Card.ClassDir)/ folder"
        }
    }

    if ($row['Cost'] -lt 0) { $issues += 'negative cost' }

    return ($issues -join '; ')
}

Export-ModuleMember -Function *
