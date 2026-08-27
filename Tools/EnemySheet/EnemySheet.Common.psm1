<#
.SYNOPSIS
    Shared plumbing for the enemy design workbook: enum tables specific to enemies, the power-weight
    schema, and the numeric fact extraction that turns a deck of CardData assets into a scoreable block.

.DESCRIPTION
    Sits alongside Tools/CardSheet/CardSheet.Common.psm1 and imports it rather than re-parsing Unity
    YAML a second time - the .asset/.prefab reader, the GUID resolver and Get-CardRecord/
    Get-EffectDescriptor/Get-TotemProjection are all reused verbatim. This module only adds what is
    specific to bodies (Character/Totem prefabs) rather than cards.

    Read-only with respect to the Unity project, same guarantee as CardSheet.Common.
#>

Set-StrictMode -Version Latest

$commonPath = Join-Path $PSScriptRoot '..\CardSheet\CardSheet.Common.psm1'
Import-Module $commonPath -Force -DisableNameChecking

# ---------------------------------------------------------------------------------------------------
# Enum table specific to enemies. Index = the enum's int value - mirrors Assets/Scripts/Simulation/
# EnemyBrain.cs's BrainType. Append only, never reorder - same rule CardSheet.Common documents for its
# own tables.
# ---------------------------------------------------------------------------------------------------

$script:BrainNames = @('None', 'Warrior', 'Ranger', 'Summoner')

# Assets/Scripts/Character/BattleRole.cs - [Flags], but only 4 legal combinations ever exist, so this
# maps each mask directly rather than bit-joining like CharacterClass's ConvertTo-ClassName does. 0
# means "unconstrained" here (not "Any"/all classes) - see BattleRole's own doc comment.
$script:BattleRoleNames = [ordered]@{ 0 = 'None'; 1 = 'Frontline'; 2 = 'Backline'; 3 = 'Both' }

function ConvertTo-BattleRoleName {
    param($Mask)

    # Masked to the two real bits before the lookup. Unity's [Flags] Inspector serialises "Everything"
    # as -1 rather than 3, so a role picked that way would otherwise read as Unknown(-1) - and the next
    # sync would hand that string to ConvertFrom-BattleRoleName, which answers 0 for anything it does
    # not recognise, silently demoting a Both character to None. Masking makes -1 round-trip as Both.
    $value = (ConvertTo-IntOrDefault $Mask) -band 3
    if ($script:BattleRoleNames.Contains($value)) { return $script:BattleRoleNames[$value] }
    return "Unknown($value)"
}

function ConvertFrom-BattleRoleName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return 0 }
    $trimmed = $Name.Trim()
    foreach ($key in $script:BattleRoleNames.Keys) {
        if ($script:BattleRoleNames[$key] -eq $trimmed) { return $key }
    }
    return 0
}

# ---------------------------------------------------------------------------------------------------
# Power weight table - the PowerLevel tab's row order and starting values. Column C is the "why" note
# shown next to each. Kept as one ordered list so the sheet, the named cells and this module's own
# formula generation can never disagree about a weight's name.
# ---------------------------------------------------------------------------------------------------

function Get-PowerWeightDefaults {
    <# name, defaultValue, note - the same shape Add-ModelSheet uses for the card workbook's Model tab. #>
    return @(
        # --- Brandon's original block, values kept exactly as authored ---
        @('pw_Damage',   1.00, 'The numeraire. One point of damage to a hero = 1 power.'),
        @('pw_Poison',   0.90, 'Per point of lifetime damage-over-time (the triangular total of the stacks applied, not the stack count itself).'),
        @('pw_Shield',   0.50, 'Per point. ShieldStatus is wiped at OnTurnStart, so it only ever blocks one round.'),
        @('pw_Block',    0.50, 'Per point actually prevented (BlockStatus.AmountPerHit is a const 5, so a charge is worth 5 of these).'),
        @('pw_Teleport', 4.00, 'Reserved for a future teleport/swap effect - no enemy card uses this yet.'),
        @('pw_Move',     1.00, 'Per tile of forced repositioning (Move / Swap). "Move 2" is one Move card whose range reaches 2 tiles.'),
        @('pw_Health',   0.50, 'Per point of max health.'),
        @('pw_Divisor',  20.0, 'Everything above is summed and divided by this to land in the same range as Brandon''s hand-authored numbers.'),

        # --- Offence, beyond plain damage ---
        @('pw_Heal',      0.80, 'Only useful once already damaged, so worth less than the same number as damage.'),
        @('pw_Strength',  0.90, 'Per stack per future attack this enemy is assumed to still make (see pw_Horizon).'),
        @('pw_DoubleNextAttack', 3.00, 'Per stack. Roughly one extra average hit.'),
        @('pw_PoisonBlade', 1.00, 'Per stack - the weapon-poison variant, scored the same as Poison stacks.'),

        # --- Defence ---
        @('pw_Parry',       1.50, 'Per charge. Negates a hit and reflects it - prevention plus a second attack.'),
        @('pw_Dodge',       1.50, 'Per charge. A fully avoided hit, but only ever one hit.'),
        @('pw_DoubleShield', 2.00, 'Per stack.'),

        # --- Control, applied to the party ---
        @('pw_Frozen',     3.00, 'Per turn. Denies a whole hero turn - the single strongest thing an enemy card can do to a player.'),
        @('pw_Rooted',     1.50, 'Per turn. Denies movement only, so worth less than Frozen.'),
        @('pw_Weaken',     0.90, 'Per stack per future hero attack this is assumed to still reduce (see pw_Horizon).'),
        @('pw_Vulnerable', 0.90, 'Per stack per future hit this enemy is assumed to still amplify (see pw_Horizon). Symmetric with Weaken.'),
        @('pw_Taunt',      1.50, 'Per turn forced onto the party.'),
        @('pw_Stealth',    1.00, 'Per turn this enemy cannot be targeted.'),

        # --- Summoning ---
        @('pw_Summon',         1.00, 'Multiplier on the summoned body''s own Estimated Power Level (Totems use their Totem Power instead) - see the Roster tab.'),
        @('pw_SummonHorizon',  5.00, 'Turns a limited-lifetime summon is assumed worth. A permanent summon (lifetimeTurns 0) always scores in full; a summon lasting fewer turns than this is scaled down by lifetime/this.'),
        @('pw_TotemActivations', 3.00, 'How many times a totem''s auras/reactions are assumed to pay out over its life - the same 3 Export-CardSheet.ps1 hardcodes for the card workbook, now tunable here.'),

        # --- Shape ---
        @('pw_Area',   0.30, 'Per extra tile an area-effect card''s footprint covers, beyond the one tile it aims at.'),
        @('pw_Horizon', 3.00, 'How many future attacks a Strength/Weaken/Vulnerable stack is assumed to affect. Also gates how long a Poison Blade weapon-buff is assumed to keep landing.'),
        @('pw_Draw',    1.00, 'Per card. Enemies rarely draw, but a few summon/ritual cards do.'),
        @('pw_Energy',  1.00, 'Reserved - no enemy card currently grants energy.'),
        @('pw_TileEffect', 0.50, 'Per point of an unblockable tile-hazard''s total payout (Wall of Flames magnitude * turns).'),
        @('pw_SelfDamage', -1.00, 'Per point. Negative - a card that hurts its own owner is a cost, not a benefit.'),
        @('pw_ActionScale', 1.00, 'Multiplies Average Card Power * Actions Per Turn before it is added to Health. 1 assumes every action in a turn is, on average, as strong as the deck''s average card.'),
        @('pw_Tolerance', 0.25, 'How far Estimated Power Level may sit from Brandon''s before the Roster tab flags it OVER/UNDER.')
    )
}

function Get-RangePowerDefaults {
    <#
        .SYNOPSIS
            The Range Power table's starting rows - a card's own reach (Range Max) scored per exact
            value rather than a per-tile rate, since reach does not get linearly better: the jump from
            melee to "just barely ranged" matters more than the jump from Range 4 to Range 5.

        .DESCRIPTION
            Looked up with VLOOKUP's approximate-match mode (see Add-BodySheet's Card Power formula), so
            a Range Max not yet in the table reads as the largest listed value at or below it - a new
            Range 7 card scores as Range 6 until a row is added for it, rather than erroring. Range 1
            (melee) is 0 by design: it is the baseline every other row is a bonus over.
    #>
    return @(
        @(1, 0.0), @(2, 1.5), @(3, 4.0), @(4, 5.0), @(5, 6.0), @(6, 7.0)
    )
}

# Assets/Scripts/Aura/AuraReaction.cs TriggeringActionType - display only, so an unmatched index falls
# back to a numbered label rather than failing.
$script:TriggerNames = @('Damage', 'Heal', 'Move', 'Status', 'Block', 'Shield', 'Parry', 'Draw', 'Summon')

# ---------------------------------------------------------------------------------------------------
# Status -> weight name. One place, so a status added to StatusType.cs only has to be added here to be
# scoreable - everywhere else (Card Facts rows, Totem Power) reads through this map.
# ---------------------------------------------------------------------------------------------------

$script:StatusWeightMap = [ordered]@{
    Strength         = 'pw_Strength'
    DoubleNextAttack = 'pw_DoubleNextAttack'
    Poison           = 'pw_Poison'      # amount is the TRIANGULAR total, not the raw stack count - see Get-PoisonTotal
    Frozen           = 'pw_Frozen'
    Shield           = 'pw_Shield'
    Block            = 'pw_Block'       # amount is charges*5 - see Get-BlockTotal
    Parry            = 'pw_Parry'
    Rooted           = 'pw_Rooted'
    DoubleShield     = 'pw_DoubleShield'
    Dodge            = 'pw_Dodge'
    Weaken           = 'pw_Weaken'
    Taunt            = 'pw_Taunt'
    PoisonBlade      = 'pw_PoisonBlade'
    Stealth          = 'pw_Stealth'
    Vulnerable       = 'pw_Vulnerable'
}

function Get-DeckCards {
    <#
        .SYNOPSIS
            Resolves a Character's `deck` field (a List<CardData> reference list) into the CardData
            assets it names, in authored order and WITH duplicates - Character.deck is a flat list, not
            a (card, count) pair, so a card appearing four times in the Inspector appears four times here.
    #>
    param($DeckNode, $AssetIndex)

    $cards = @()
    if ($DeckNode -is [System.Collections.IEnumerable] -and -not ($DeckNode -is [string])) {
        foreach ($ref in $DeckNode) {
            $asset = Resolve-AssetReference -Reference $ref -AssetIndex $AssetIndex
            $cards += [pscustomobject]@{
                Name  = if ($null -ne $asset) { $asset.Name } else { '<missing>' }
                Asset = $asset
            }
        }
    }
    return $cards
}

function Get-CharacterBody {
    <#
        .SYNOPSIS
            One prefab's Character stats, or $null if it carries no Character component.
    #>
    param($Asset, $AssetIndex)

    $char = Get-PrefabComponent -Asset $Asset -TypeName 'Character'
    if ($null -eq $char) { return $null }

    return [pscustomobject]@{
        Prefab       = $Asset.Name
        Guid         = $Asset.Guid
        Path         = $Asset.Path
        Boss         = ($Asset.Path -match '[\\/]Prefabs[\\/]Bosses[\\/]')
        DisplayName  = [string](Get-NodeField $char 'displayName')
        MaxHealth    = ConvertTo-IntOrDefault (Get-NodeField $char 'maxHealth') -Default 10
        ActionPoints = ConvertTo-IntOrDefault (Get-NodeField $char 'actionPoints') -Default 1
        Brain        = Get-EnumName -Table $script:BrainNames -Value (Get-NodeField $char 'brain')
        Targeting    = Resolve-AssetName -Reference (Get-NodeField $char 'targetingPattern') -AssetIndex $AssetIndex
        LootTable    = Resolve-AssetName -Reference (Get-NodeField $char 'lootTable') -AssetIndex $AssetIndex
        Deck         = @(Get-DeckCards -DeckNode (Get-NodeField $char 'deck') -AssetIndex $AssetIndex)
        Role         = ConvertTo-BattleRoleName (Get-NodeField $char 'battleRole')

        # The prefab's CURRENT powerLevel/isBoss, as opposed to Boss above (folder-derived ground
        # truth) or the sheet's resolved values computed in Import-EnemySheet.ps1 - kept apart so the
        # importer can tell whether a one-way write is actually needed instead of rewriting every
        # prefab on every sync.
        PowerOnAsset = ConvertTo-DoubleOrDefault (Get-NodeField $char 'powerLevel')
        BossOnAsset  = ConvertTo-BoolOrDefault (Get-NodeField $char 'isBoss')
    }
}

function Find-SummonEffectFor {
    <# The SummonEffect asset (if any) whose summonedObject points at this prefab. #>
    param($PrefabAsset, $AssetIndex)

    foreach ($a in $AssetIndex.All) {
        if ($a.Type -ne 'SummonEffect') { continue }
        $ref = Get-NodeField $a.Node 'summonedObject'
        if ($ref -is [System.Collections.IDictionary] -and [string]$ref['guid'] -eq $PrefabAsset.Guid) { return $a }
    }
    return $null
}

function Get-TotemBody {
    <#
        .SYNOPSIS
            One totem prefab's identity and projection, or $null if it carries no Totem component.

        .DESCRIPTION
            Reuses Get-TotemProjection from CardSheet.Common rather than re-decoding the aura/reaction
            YAML here - that function already knows the StatusType/EnemyBrain-independent name tables
            (it lives in the module that owns them) and is exercised today by the card workbook's
            Balance tab. Found by locating the SummonEffect that points at this prefab, since that is
            the only reference CardSheet.Common's helper accepts.
    #>
    param($Asset, $AssetIndex)

    $totem = Get-PrefabComponent -Asset $Asset -TypeName 'Totem'
    if ($null -eq $totem) { return $null }

    $summonEffect = Find-SummonEffectFor -PrefabAsset $Asset -AssetIndex $AssetIndex
    $projection = Get-TotemProjection -SummonEffectAsset $summonEffect -AssetIndex $AssetIndex

    return [pscustomobject]@{
        Prefab    = $Asset.Name
        Guid      = $Asset.Guid
        Path      = $Asset.Path
        Range     = Format-Range (Get-NodeField $totem 'range')
        Affects   = @('Allies', 'Enemies', 'Everyone')[(ConvertTo-IntOrDefault (Get-NodeField $totem 'affects'))]
        Auras     = if ($projection) { @($projection.Auras) } else { @() }
        Reactions = if ($projection) { @($projection.Reactions) } else { @() }
    }
}

function Get-TriggerName {
    param($Value)
    return Get-EnumName -Table $script:TriggerNames -Value $Value
}

function Get-PoisonTotal {
    <# PoisonStatus deals `stacks` damage at turn end and decays by one - the lifetime total of n
       stacks applied at once is the triangular number, matching Export-CardSheet.ps1's own reading. #>
    param([int]$Stacks)
    return [int](($Stacks * ($Stacks + 1)) / 2)
}

function Get-AllCharacterPrefabNames {
    <#
        .SYNOPSIS
            Every prefab under Assets/Prefabs/Enemies, Bosses or Allies - the full set a level's board
            may legally place, as opposed to Get-DiscoveredRoster's narrower set (which exists to decide
            which bodies get their own balance TAB, and deliberately excludes Tutorial variants since
            they add nothing a tab would show beyond their base prefab).

            Folder-based rather than "carries a Character component", because a prefab VARIANT (every
            Tutorial/* prefab) stores only a PrefabInstance document with a modifications list - the
            Character component itself lives on the base prefab it points at, not in the variant's own
            YAML, so Get-PrefabComponent legitimately finds nothing to read there. A level is allowed to
            place a Tutorial variant - Tutorial.asset itself places EnemyRangerTutorial - so validating a
            board token against the balance-tab roster instead of this would reject a placement the game
            already uses.
    #>
    param($AssetIndex)

    $names = @{}
    foreach ($p in $AssetIndex.All) {
        if ($p.Type -ne 'Prefab') { continue }
        if ($p.Path -notmatch '[\\/]Prefabs[\\/](Enemies|Bosses|Allies)[\\/]') { continue }
        $names[$p.Name] = $true
    }
    return $names
}

function Get-DiscoveredRoster {
    <#
        .SYNOPSIS
            Every prefab the enemy workbook covers: the primary roster (Enemies/Bosses/Allies, minus
            Tutorial variants), plus whatever their decks summon that is not already in it.

        .DESCRIPTION
            Shared between Export-EnemySheet.ps1 and Import-EnemySheet.ps1 so the two scripts can never
            disagree about which prefabs the workbook is describing - a body Import does not know about
            is a body whose sheet edits are silently never applied.

            The summon pass repeats to a fixed point (a newly-discovered body might itself summon
            something not yet seen) rather than walking a real dependency graph - see the header comment
            in Write-EnemyWorkbook.ps1 for why that is safe with the data this project actually has.
    #>
    param($AssetIndex, [string]$RepoRoot)

    $bodyAssets = [ordered]@{}
    foreach ($p in $AssetIndex.All) {
        if ($p.Type -ne 'Prefab') { continue }
        if ($p.Path -notmatch '[\\/]Prefabs[\\/](Enemies|Bosses|Allies)[\\/]') { continue }
        if ($p.Path -match '[\\/]Tutorial[\\/]') { continue }
        if ($null -eq (Get-PrefabComponent -Asset $p -TypeName 'Character')) { continue }
        $bodyAssets[$p.Name] = $p
    }

    $totemAssets = [ordered]@{}
    for ($pass = 0; $pass -lt 4; $pass++) {
        $added = $false
        foreach ($p in @($bodyAssets.Values)) {
            $char = Get-PrefabComponent -Asset $p -TypeName 'Character'
            foreach ($c in @(Get-DeckCards -DeckNode (Get-NodeField $char 'deck') -AssetIndex $AssetIndex)) {
                if ($null -eq $c.Asset) { continue }
                $rec = Get-CardRecord -Asset $c.Asset -AssetIndex $AssetIndex -RepoRoot $RepoRoot

                foreach ($e in @($rec.Entries)) {
                    if ($e.Descriptor.Kind -ne 'Summon' -or $null -eq $e.EffectAsset) { continue }

                    $targetRef = Get-NodeField $e.EffectAsset.Node 'summonedObject'
                    $target = Resolve-AssetReference -Reference $targetRef -AssetIndex $AssetIndex
                    if ($null -eq $target) { continue }
                    if ($bodyAssets.Contains($target.Name) -or $totemAssets.Contains($target.Name)) { continue }

                    if ($null -ne (Get-PrefabComponent -Asset $target -TypeName 'Totem')) {
                        $totemAssets[$target.Name] = $target
                        $added = $true
                    }
                    elseif ($null -ne (Get-PrefabComponent -Asset $target -TypeName 'Character')) {
                        $bodyAssets[$target.Name] = $target
                        $added = $true
                    }
                }
            }
        }
        if (-not $added) { break }
    }

    return [pscustomobject]@{ BodyAssets = $bodyAssets; TotemAssets = $totemAssets }
}

# ---------------------------------------------------------------------------------------------------
# Sync-back: reading a body tab back out of the workbook, and the baseline/three-way merge over it.
#
# Body tabs are NOT flat header/row tables - Import-Excel/Import-Csv cannot read them. Read-BodySheetTab
# scans column A for labels instead, the same "find by label, never by row number" rule
# Write-EnemyWorkbook.ps1 is written to guarantee, so a spacer row or a reordered block there cannot
# silently break this reader.
# ---------------------------------------------------------------------------------------------------

function Read-BodySheetTab {
    <#
        .SYNOPSIS
            One body tab's synced fields, read from an open EPPlus worksheet. $null if the sheet has no
            GUID row at all - not a body tab (PowerLevel, Roster, Enums, README all fail this).
    #>
    param($Worksheet)

    if ($null -eq $Worksheet.Dimension) { return $null }
    $last = $Worksheet.Dimension.End.Row

    $values = @{}
    $deckRow = -1
    for ($r = 1; $r -le $last; $r++) {
        $label = ([string]$Worksheet.Cells[$r, 1].Text).Trim()
        if ($label -eq '') { continue }
        $values[$label] = [string]$Worksheet.Cells[$r, 2].Text
        if ($label -eq 'Deck' -and $deckRow -lt 0) { $deckRow = $r }
    }

    if (-not $values.ContainsKey('GUID')) { return $null }

    $deck = @()
    if ($deckRow -gt 0) {
        $lastCol = $Worksheet.Dimension.End.Column
        for ($c = 2; $c -le $lastCol; $c++) {
            $name = ([string]$Worksheet.Cells[$deckRow, $c].Text).Trim()
            if ($name -eq '') { break }
            $deck += $name
        }
    }

    $get = { param($key) if ($values.ContainsKey($key)) { $values[$key] } else { '' } }

    return [pscustomobject]@{
        Prefab         = $Worksheet.Name
        Guid           = & $get 'GUID'
        DisplayName    = & $get 'Display Name'
        Health         = & $get 'Health'
        ActionsPerTurn = & $get 'Actions Per Turn'
        Brain          = & $get 'Brain'
        Targeting      = & $get 'Targeting'
        LootTable      = & $get 'Loot Table'
        Role           = & $get 'Role'
        Brandon        = & $get "Brandon's Power Level"
        Estimated      = & $get 'Estimated Power Level'
        Notes          = & $get 'Notes'
        Deck           = $deck
    }
}

function Get-EnemyMergeColumns {
    <# The columns a three-way merge arbitrates - Deck is one column here even though it is several
       cells on the sheet, compared as one joined string so a reorder or a single swapped card reads as
       exactly one change rather than an avalanche of per-slot diffs.

       Role is merged here because it is designer-authored, same as Brain/Targeting. PowerLevel and
       Boss are NOT here - they are derived (Brandon's/Estimated, and the prefab's own folder) rather
       than something either side authors independently, so Import-EnemySheet.ps1 writes them one-way
       instead of arbitrating a conflict that cannot actually happen. #>
    return @('DisplayName', 'Health', 'ActionsPerTurn', 'Brain', 'Targeting', 'LootTable', 'Deck', 'Role')
}

function Get-SheetEffectivePower {
    <#
        .SYNOPSIS
            Brandon's Power Level if authored, else the sheet's own resolved estimate - what
            Import-EnemySheet.ps1 writes to Character.powerLevel. $null (never 0) when neither side has
            anything usable, so a deliberately-authored zero-power body can never be confused with an
            unscored one.

        .DESCRIPTION
            Reads either shape Import-EnemySheet.ps1 produces: a CSV-mirror row already carries a
            pre-resolved EffectivePower column (computed once at export time, see Export-EnemySheet.ps1),
            while a row read fresh from an open workbook tab (Read-BodySheetTab) carries Brandon's and
            Estimated separately and this resolves them the same way on the fly.
    #>
    param($SheetRow)

    $brandon = 0.0
    if ($SheetRow.Brandon -and [double]::TryParse([string]$SheetRow.Brandon, [ref]$brandon)) { return $brandon }

    if ($SheetRow.PSObject.Properties.Name -contains 'EffectivePower') {
        $effective = 0.0
        if ($SheetRow.EffectivePower -and [double]::TryParse([string]$SheetRow.EffectivePower, [ref]$effective)) {
            return $effective
        }
        return $null
    }

    $estimated = 0.0
    if ($SheetRow.PSObject.Properties.Name -contains 'Estimated' -and $SheetRow.Estimated `
        -and [double]::TryParse([string]$SheetRow.Estimated, [ref]$estimated)) {
        return $estimated
    }

    return $null
}

function ConvertTo-DeckComparable {
    param([string[]]$Names)
    return ($Names -join '|')
}

function Get-EnemyMergeRow {
    <# The current-ASSET side of the merge, from a Get-CharacterBody result. Same column names and
       shapes Read-BodySheetTab produces, so Resolve-ThreeWay never compares apples to oranges. #>
    param($Body)

    return [ordered]@{
        DisplayName    = $Body.DisplayName
        Health         = $Body.MaxHealth
        ActionsPerTurn = $Body.ActionPoints
        Brain          = $Body.Brain
        Targeting      = $Body.Targeting
        LootTable      = $Body.LootTable
        Deck           = ConvertTo-DeckComparable -Names @($Body.Deck | ForEach-Object { $_.Name })
        Role           = $Body.Role
    }
}

function Get-EnemyBaselinePath {
    param([string]$ToolDir)
    return (Join-Path $ToolDir 'baseline.json')
}

function Read-EnemyBaseline {
    <# Mirrors Read-Baseline in CardSheet.Common, but keyed on the enemy sheet's own merge columns -
       kept separate rather than shared, since the two schemas (cards vs bodies) have nothing in common
       beyond the GUID-keyed shape and Resolve-ThreeWay, both of which already live in CardSheet.Common. #>
    param([string]$ToolDir)

    $path = Get-EnemyBaselinePath -ToolDir $ToolDir
    $result = @{ Exists = $false; Bodies = @{}; WrittenUtc = '' }

    if (-not (Test-Path -LiteralPath $path)) { return $result }

    try { $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    catch {
        Write-Warning "Enemy baseline.json could not be read ($($_.Exception.Message)) - treating this as a first sync."
        return $result
    }

    $result.Exists = $true
    if ($json.PSObject.Properties.Name -contains 'writtenUtc') { $result.WrittenUtc = [string]$json.writtenUtc }

    if ($json.PSObject.Properties.Name -contains 'bodies') {
        foreach ($entry in @($json.bodies)) {
            if (-not $entry.guid) { continue }
            $cols = @{}
            foreach ($p in $entry.columns.PSObject.Properties) { $cols[$p.Name] = ConvertTo-Comparable $p.Value }
            $result.Bodies[[string]$entry.guid] = $cols
        }
    }

    return $result
}

function Write-EnemyBaseline {
    <# Records the state both sides now agree on. Called only after a successful sync, same rule
       Write-Baseline follows for cards. #>
    param([string]$ToolDir, $Bodies)   # each needs .Guid plus what Get-EnemyMergeRow reads

    $rows = @()
    foreach ($b in @($Bodies)) {
        if (-not $b.Guid) { continue }

        $cols = Get-EnemyMergeRow -Body $b
        $comparable = [ordered]@{}
        foreach ($k in $cols.Keys) { $comparable[$k] = ConvertTo-Comparable $cols[$k] }

        $rows += [ordered]@{ guid = $b.Guid; columns = $comparable }
    }

    $payload = [ordered]@{
        writtenUtc = (Get-Date).ToUniversalTime().ToString('o')
        bodies     = @($rows)
    }

    Set-Content -LiteralPath (Get-EnemyBaselinePath -ToolDir $ToolDir) -Value ($payload | ConvertTo-Json -Depth 8) -Encoding utf8
}

function Get-CardFacts {
    <#
        .SYNOPSIS
            The numeric facts one deck card contributes, keyed by the same names Get-PowerWeightDefaults
            and $script:StatusWeightMap use - so the workbook writer can lay them out as a block and
            apply the matching pw_* weight to each without a second translation table.

        .DESCRIPTION
            Takes a Get-CardRecord result (from CardSheet.Common), which already resolves every effect
            entry to a descriptor. Facts here are RAW - Poison is the triangular total, Block is
            charges*5, everything else is the authored amount. No pw_* weight is applied; that happens
            as an Excel formula in Write-EnemyWorkbook.ps1, which is what lets retuning a weight move
            every tab without re-running this script.

            Move / Cooldown detection lives here too (IsMove, CooldownTurns) so Export-EnemySheet.ps1
            and the workbook writer agree on what "the deck minus Move minus Cooldown cards" means.
    #>
    param($CardRecord)

    $facts = [ordered]@{
        Damage = 0; Heal = 0; Shield = 0; BlockCharges = 0; Parry = 0; Dodge = 0
        Strength = 0; Weaken = 0; Vulnerable = 0; Frozen = 0; Rooted = 0; Taunt = 0
        Stealth = 0; PoisonBlade = 0; DoubleNextAttack = 0; DoubleShield = 0
        PoisonStacks = 0; Draw = 0; MoveTiles = 0
        SummonEffectAsset = $null
        TilesHit = 1
    }

    $entries = @($CardRecord.Entries)
    foreach ($e in $entries) {
        $d = $e.Descriptor
        $tiles = [int]$e.Tiles
        if ($tiles -gt $facts.TilesHit) { $facts.TilesHit = $tiles }

        switch -Regex ($d.Kind) {
            '^Damage$' { $facts.Damage += $d.Amount }
            '^Heal$'   { $facts.Heal   += $d.Amount }
            '^Shield$' { $facts.Shield += $d.Amount }
            '^Block$'  { $facts.BlockCharges += $d.Amount }
            '^Parry$'  { $facts.Parry  += $d.Amount }
            '^Draw$'   { $facts.Draw   += $d.Amount }
            '^Move$'   { $facts.MoveTiles += [Math]::Max(1, [int]$CardRecord.Row.'Range Max') }
            '^Taunt$'  { $facts.Taunt  += $d.Amount }
            '^Summon$' { $facts.SummonEffectAsset = $e.EffectAsset }
            '^Status:' {
                $status = $d.Kind.Substring(7)
                switch ($status) {
                    'Poison'     { $facts.PoisonStacks += $d.Amount }
                    'Strength'   { $facts.Strength += $d.Amount }
                    'Weaken'     { $facts.Weaken += $d.Amount }
                    'Vulnerable' { $facts.Vulnerable += $d.Amount }
                    'Frozen'     { $facts.Frozen += $d.Amount }
                    'Rooted'     { $facts.Rooted += $d.Amount }
                    'Shield'     { $facts.Shield += $d.Amount }
                    'Parry'      { $facts.Parry += $d.Amount }
                    'Dodge'      { $facts.Dodge += $d.Amount }
                    'Taunt'      { $facts.Taunt += $d.Amount }
                    'Stealth'    { $facts.Stealth += $d.Amount }
                    'PoisonBlade'      { $facts.PoisonBlade += $d.Amount }
                    'DoubleNextAttack' { $facts.DoubleNextAttack += $d.Amount }
                    'DoubleShield'     { $facts.DoubleShield += $d.Amount }
                }
            }
        }
    }

    $facts.BlockTotal = $facts.BlockCharges * 5
    $facts.PoisonTotal = Get-PoisonTotal -Stacks $facts.PoisonStacks
    $facts.IsMove = ($entries.Count -gt 0 -and (@($entries | Where-Object { $_.Descriptor.Kind -eq 'Move' }).Count -eq $entries.Count))
    $facts.IsSummon = $null -ne $facts.SummonEffectAsset

    $keywords = [string]$CardRecord.Row.Keywords
    $facts.CooldownTurns = 0
    if ($keywords -match 'Cooldown\s+(\d+)') { $facts.CooldownTurns = [int]$Matches[1] }
    $facts.IsCooldown = $facts.CooldownTurns -gt 0

    return $facts
}

Export-ModuleMember -Function * -Variable StatusWeightMap
