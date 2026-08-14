<#
.SYNOPSIS
    Diffs Docs/CardDesign.xlsx against the authored assets and writes Tools/CardSheet/cards.json.

.DESCRIPTION
    Does NOT touch the Unity project. It only produces the work order; Assets/Editor/CardSheetImporter.cs
    reads that file and creates the assets through AssetDatabase, which is what keeps GUID minting and
    serialization Unity's business.

    What lands in cards.json:
      * every class-tab row whose GUID is blank           -> action "create"
      * every class-tab row whose values differ from disk -> action "update"
      * every Glossary row whose wording differs from disk
      * nothing else. Rows that match are skipped, which is what makes the round-trip identity test
        (export, import, expect an empty file) meaningful.

    THE IDEAS TABS ARE NEVER IMPORTED. They are design space only. A card becomes real by being added
    to its class tab - copy the row across (the first columns are identical by design) and it will be
    created on the next sync. Nothing on an Ideas tab can create, change or delete an asset, whatever
    its Buildable column says.

    Editing a Description on a class tab, or a Title/Body on the Glossary tab, and syncing is a
    supported way to reword cards and tooltips without opening the Inspector.

    Cards that exist as assets but are absent from the sheet are reported and left alone. The importer
    never deletes.

.PARAMETER WorkbookPath
    Defaults to Docs/CardDesign.xlsx at the repo root.

.PARAMETER OutputPath
    Defaults to Tools/CardSheet/cards.json.

.PARAMETER FromCsv
    Read the Docs/CardDesign/*.csv mirror instead of the workbook. Useful when the workbook is open in
    Excel, which locks it.

.PARAMETER OnlyTab
    Only consider these tabs. Without it every tab is read, which on a full Ideas backlog means a
    single sync creates a hundred cards at once - rarely what you want.

.PARAMETER OnlyKey
    Only consider rows whose Key matches one of these wildcard patterns.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/CardSheet/Import-CardSheet.ps1

.EXAMPLE
    # Just the Knight backlog, and only a couple of cards from it.
    Tools/CardSheet/Import-CardSheet.ps1 -OnlyTab 'Knight Ideas' -OnlyKey 'Shield*','Bulwark'
#>
[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [string]$OutputPath,
    [switch]$FromCsv,

    # Named OnlyTab/OnlyKey, not Tab/Key: PowerShell variable names are case-insensitive, so a
    # parameter called $Tab is the same variable as the loop's $tab and gets silently overwritten on
    # the first iteration. The filter then matches everything and the flag looks like it does nothing.
    [string[]]$OnlyTab,
    [string[]]$OnlyKey
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Resolve-Path (Join-Path $scriptDir '..\..')
Import-Module (Join-Path $scriptDir 'CardSheet.Common.psm1') -Force -DisableNameChecking

if (-not $WorkbookPath) { $WorkbookPath = Join-Path $repoRoot 'Docs\CardDesign.xlsx' }
if (-not $OutputPath)   { $OutputPath   = Join-Path $scriptDir 'cards.json' }
$csvDir = Join-Path $repoRoot 'Docs\CardDesign'

if (-not $FromCsv) {
    if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
        throw "The ImportExcel module is required. Install with: Install-Module ImportExcel -Scope CurrentUser"
    }
    Import-Module ImportExcel -DisableNameChecking
}

$cardTabs  = @('Knight', 'Mage', 'Rogue', 'Neutral')
$ideaTabs  = @('Knight Ideas', 'Mage Ideas', 'Rogue Ideas')

# Which sub-folder of Assets/Data/CardData a tab's cards belong under.
$tabRoot = @{
    'Knight'       = 'Knight'
    'Mage'         = 'Mage'
    'Rogue'        = 'Rogue'
    'Neutral'      = 'Generic'
    'Knight Ideas' = 'Knight'
    'Mage Ideas'   = 'Mage'
    'Rogue Ideas'  = 'Rogue'
}

function Read-Tab {
    param([string]$SheetName)

    if ($FromCsv) {
        $path = Join-Path $csvDir "$SheetName.csv"
        if (-not (Test-Path -LiteralPath $path)) { return @() }
        return @(Import-Csv -LiteralPath $path)
    }

    try {
        return @(Import-Excel -Path $WorkbookPath -WorksheetName $SheetName -ErrorAction Stop -WarningAction SilentlyContinue)
    }
    catch {
        Write-Verbose "Sheet '$SheetName' missing or empty."
        return @()
    }
}

function Get-Cell {
    param($Row, [string]$Name, $Default = '')

    if ($null -eq $Row) { return $Default }
    if (-not ($Row.PSObject.Properties.Name -contains $Name)) { return $Default }
    $v = $Row.$Name
    if ($null -eq $v) { return $Default }
    return ([string]$v).Trim()
}

function ConvertTo-AreaSpec {
    <#
        Parses the Area column back into the three fields AreaShape serializes:
          "Single"           -> kind 0
          "Manhattan 0-2"    -> kind 1 + a radius
          "Pattern:Cone3"    -> kind 2 + a pattern name
        Anything unrecognised falls back to Single rather than guessing.
    #>
    param([string]$Text)

    $t = $Text.Trim()
    if ($t -eq '' -or $t -eq 'Single') {
        return [ordered]@{ kind = 0; shape = 0; min = 0; max = 0; pattern = '' }
    }

    if ($t -match '^Pattern:\s*(.+)$') {
        return [ordered]@{ kind = 2; shape = 0; min = 0; max = 0; pattern = $Matches[1].Trim() }
    }

    if ($t -match '^(Anywhere|Chebyshev|Manhattan|SelfTile)(?:\s+(\d+)\s*-\s*(\d+))?$') {
        $shape = Get-EnumValue -Table @('Anywhere', 'Chebyshev', 'Manhattan', 'SelfTile') -Name $Matches[1]
        $min = 0; $max = 0
        if ($Matches[2]) { $min = [int]$Matches[2] }
        if ($Matches[3]) { $max = [int]$Matches[3] }
        return [ordered]@{ kind = 1; shape = $shape; min = $min; max = $max; pattern = '' }
    }

    Write-Warning "Unrecognised Area '$Text' - treating as Single."
    return [ordered]@{ kind = 0; shape = 0; min = 0; max = 0; pattern = '' }
}

function ConvertTo-KeywordSpec {
    param([string]$Text)

    $out = @()
    foreach ($part in ($Text -split ',')) {
        $p = $part.Trim()
        if ($p -eq '') { continue }
        if ($p -match '^(\w+)(?:\s+(\d+))?$') {
            $type = Get-EnumValue -Table @('None', 'Innate', 'Cooldown', 'Dormant') -Name $Matches[1]
            if ($type -eq 0) { continue }
            $mag = 0
            if ($Matches[2]) { $mag = [int]$Matches[2] }
            $out += [ordered]@{ type = $type; magnitude = $mag }
        }
    }
    return $out
}

function ConvertTo-TagSpec {
    param([string]$Text)

    $names = @('None', 'Attack', 'Defence', 'Movement', 'Poison', 'Fire', 'Summon', 'Healing')
    $out = @()
    foreach ($part in ($Text -split ',')) {
        $p = $part.Trim()
        if ($p -eq '') { continue }
        $v = Get-EnumValue -Table $names -Name $p -Default -1
        if ($v -ge 0) { $out += $v }
    }
    return $out
}

function ConvertTo-EffectSpec {
    <#
        The Effect column holds the effect asset's NAME. If no asset with that name exists, the name is
        parsed into a request to create one, following the "<Kind> <Amount>" convention the existing
        assets already use (Damage 5, Shield 3, Block 2, Strength 3).
    #>
    param([string]$Name, $EffectsByName)

    $n = $Name.Trim()
    if ($n -eq '') { return $null }

    if ($EffectsByName.ContainsKey($n)) {
        return [ordered]@{ name = $n; exists = $true; create = $null }
    }

    $create = $null
    if ($n -match '^(Damage|Heal|Shield|Block|Parry|Draw)\s+(\d+)$') {
        $kind = $Matches[1]
        $amount = [int]$Matches[2]
        $folder = @{ Damage = 'Damage'; Heal = 'Heal'; Shield = 'Shield'; Block = 'Block'; Parry = 'Status'; Draw = 'Draw' }[$kind]
        $create = [ordered]@{ kind = $kind; amount = $amount; folder = $folder; status = '' }
    }
    elseif ($n -match '^(Poison|Strength|Weaken|Freeze|Frozen|Root|Rooted|Dodge|Taunt|Double Shield|Double Next Attack)(?:\s+(\d+))?$') {
        $statusNames = @{
            'Poison' = 'Poison'; 'Strength' = 'Strength'; 'Weaken' = 'Weaken'
            'Freeze' = 'Frozen'; 'Frozen' = 'Frozen'; 'Root' = 'Rooted'; 'Rooted' = 'Rooted'
            'Dodge' = 'Dodge'; 'Taunt' = 'Taunt'
            'Double Shield' = 'DoubleShield'; 'Double Next Attack' = 'DoubleNextAttack'
        }
        $status = $statusNames[$Matches[1]]
        $stacks = 1
        if ($Matches[2]) { $stacks = [int]$Matches[2] }
        $create = [ordered]@{ kind = 'Status'; amount = $stacks; folder = 'Status'; status = $status }
    }

    return [ordered]@{ name = $n; exists = $false; create = $create }
}

# ---------------------------------------------------------------------------------------------------
# Read the assets so the diff has something to compare against
# ---------------------------------------------------------------------------------------------------

Write-Host "Reading assets..." -ForegroundColor Cyan
$scriptGuids = Get-ScriptGuidIndex -RepoRoot $repoRoot
$assetIndex  = Build-AssetIndex -RepoRoot $repoRoot -RelativeFolders @('Assets\Data', 'Assets\Prefabs') -ScriptGuidIndex $scriptGuids

$cardsByGuid = @{}
$cardsByKey  = @{}
foreach ($asset in ($assetIndex.All | Where-Object { $_.Type -eq 'CardData' })) {
    $rec = Get-CardRecord -Asset $asset -AssetIndex $assetIndex -RepoRoot $repoRoot
    if ($asset.Guid) { $cardsByGuid[$asset.Guid] = $rec }
    $cardsByKey[$rec.Row.Key] = $rec
}

$effectsByName = @{}
foreach ($asset in $assetIndex.All) {
    if ($asset.Type -like '*Effect' -or $asset.Type -eq 'EffectPattern') { $effectsByName[$asset.Name] = $asset }
}

Write-Host "  $($cardsByKey.Count) cards, $($effectsByName.Count) effect assets"

# ---------------------------------------------------------------------------------------------------
# Walk the sheet
# ---------------------------------------------------------------------------------------------------

# The authoring columns that actually round-trip into an asset. Sync, Notes and the Ideas-only backlog
# columns are deliberately absent: they are for humans, and a change to one is not a change to a card.
$comparedColumns = @(
    'Card Name', 'Class', 'Folder', 'Cost', 'Rarity', 'Tags',
    'Range Shape', 'Range Min', 'Range Max',
    'Effect 1', 'Aim 1', 'Area 1',
    'Effect 2', 'Aim 2', 'Area 2',
    'Effect 3', 'Aim 3', 'Area 3',
    'Keywords', 'No Reward', 'Description', 'Animation'
)

$actions        = @()
$seenKeys       = @{}
$skipped        = 0
$problems       = @()
$pendingEffects = @{}

$filtered = 0

# Class tabs only. The Ideas tabs are a design backlog and are deliberately never read here - promoting
# an idea means copying its row onto the class tab, which is an explicit act rather than a side effect
# of having written the idea down.
foreach ($tab in $cardTabs) {
    if ($OnlyTab -and ($OnlyTab -notcontains $tab)) { continue }

    foreach ($row in (Read-Tab -SheetName $tab)) {
        $key = Get-Cell $row 'Key'
        if ($key -eq '') { continue }

        if ($OnlyKey) {
            $match = $false
            foreach ($pattern in $OnlyKey) { if ($key -like $pattern) { $match = $true; break } }
            if (-not $match) { $filtered++; continue }
        }

        if ($seenKeys.ContainsKey($key)) {
            $problems += "Duplicate Key '$key' (second copy on '$tab') - skipped."
            continue
        }
        $seenKeys[$key] = $tab

        $guid     = Get-Cell $row 'GUID'
        $existing = $null
        if ($guid -and $cardsByGuid.ContainsKey($guid)) { $existing = $cardsByGuid[$guid] }
        elseif ($cardsByKey.ContainsKey($key))          { $existing = $cardsByKey[$key] }

        # Resolve the three effect slots.
        $entries = @()
        $entrySpecs = @()
        $missingEffect = $false
        for ($i = 1; $i -le 3; $i++) {
            $name = Get-Cell $row "Effect $i"
            if ($name -eq '') { continue }

            $spec = ConvertTo-EffectSpec -Name $name -EffectsByName $effectsByName
            if (-not $spec.exists -and $null -eq $spec.create) {
                $problems += "'$key': effect '$name' does not exist and its name does not follow a pattern the importer can create (try 'Damage 7' or 'Poison 3')."
                $missingEffect = $true
                continue
            }

            $aim  = Get-Cell $row "Aim $i" 'Tile'
            $area = ConvertTo-AreaSpec (Get-Cell $row "Area $i")

            # Flat, not nested: Unity's JsonUtility handles List<T> of [Serializable] classes but has
            # no time for dictionaries or deep anonymous shapes, and pulling in Newtonsoft for one
            # editor script is not worth it.
            $entries += [ordered]@{
                effectName  = $spec.name
                aimsAt      = Get-EnumValue -Table @('Tile', 'Self') -Name $aim
                areaKind    = $area.kind
                areaShape   = $area.shape
                areaMin     = $area.min
                areaMax     = $area.max
                areaPattern = $area.pattern
            }
            $entrySpecs += $spec
        }
        if ($missingEffect) { continue }

        $folder = Get-Cell $row 'Folder'
        $root   = $tabRoot[$tab]
        $path   = "Assets/Data/CardData/$root"
        if ($folder) { $path += "/$folder" }
        $path += "/$key.asset"

        $noReward = (Get-Cell $row 'No Reward').ToUpperInvariant() -eq 'TRUE'

        $card = [ordered]@{
            key            = $key
            assetPath      = $path
            guid           = $guid
            cardName       = Get-Cell $row 'Card Name' $key
            cost           = [int](ConvertTo-IntOrDefault (Get-Cell $row 'Cost'))
            requiredClass  = ConvertFrom-ClassName (Get-Cell $row 'Class')
            rarity         = Get-EnumValue -Table @('Common', 'Uncommon', 'Rare', 'Legendary', 'NotOffered') -Name (Get-Cell $row 'Rarity')
            tags           = @(ConvertTo-TagSpec (Get-Cell $row 'Tags'))
            rangeShape     = Get-EnumValue -Table @('Anywhere', 'Chebyshev', 'Manhattan', 'SelfTile') -Name (Get-Cell $row 'Range Shape')
            rangeMin       = [int](ConvertTo-IntOrDefault (Get-Cell $row 'Range Min'))
            rangeMax       = [int](ConvertTo-IntOrDefault (Get-Cell $row 'Range Max'))
            excludeFromRewards = $noReward
            description    = Get-Cell $row 'Description'
            animation      = Get-Cell $row 'Animation'
            keywords       = @(ConvertTo-KeywordSpec (Get-Cell $row 'Keywords'))
            effectEntries  = $entries
            sourceTab      = $tab
            action         = ''
            changed        = @()
        }

        # Effect assets this card needs that do not exist yet, collected for the payload below.
        foreach ($spec in $entrySpecs) {
            if (-not $spec.exists -and $null -ne $spec.create) { $pendingEffects[$spec.name] = $spec.create }
        }

        if ($null -eq $existing) {
            $card['action'] = 'create'
            $actions += $card
            continue
        }

        # Compare only what an asset actually stores.
        $changed = @()
        foreach ($col in $comparedColumns) {
            $sheetValue = Get-Cell $row $col
            $assetValue = [string]$existing.Row[$col]
            if ($null -eq $assetValue) { $assetValue = '' }
            if ($sheetValue.Trim() -ne $assetValue.Trim()) { $changed += "$col ('$assetValue' -> '$sheetValue')" }
        }

        if ($changed.Count -gt 0) {
            $card['action'] = 'update'
            $card['assetPath'] = $existing.AssetPath -replace '^', 'Assets/Data/CardData/'
            $card['changed'] = $changed
            $actions += $card
        }
    }
}

# ---------------------------------------------------------------------------------------------------
# Glossary - tooltip wording
# ---------------------------------------------------------------------------------------------------

$glossary = @()

if (-not $OnlyTab -and -not $OnlyKey) {
    $onDisk = @{}
    foreach ($row in (Read-GlossaryRows -RepoRoot $repoRoot -ScriptGuidIndex $scriptGuids)) {
        $onDisk["$($row.Kind)/$($row.Type)"] = $row
    }

    foreach ($row in (Read-Tab -SheetName 'Glossary')) {
        $kind = Get-Cell $row 'Kind'
        $type = Get-Cell $row 'Type'
        if ($kind -notin @('Status', 'Keyword') -or $type -eq '') { continue }

        $title = Get-Cell $row 'Title'
        $body  = Get-Cell $row 'Body'

        # A row still blank in both is a term nobody has written a tooltip for yet. Writing it back
        # would author an empty entry that renders as a titled box with no words in it.
        if ($title -eq '' -and $body -eq '') { continue }

        $terms = @()
        foreach ($t in ((Get-Cell $row 'Terms') -split ',')) {
            $trimmed = $t.Trim()
            if ($trimmed) { $terms += $trimmed }
        }

        $entry = [ordered]@{
            kind          = $kind
            type          = $type
            title         = $title
            body          = $body
            terms         = @($terms)
            defaultStacks = [int](ConvertTo-IntOrDefault (Get-Cell $row 'Default Stacks') -Default 1)
            defaultAmount = [int](ConvertTo-IntOrDefault (Get-Cell $row 'Default Amount') -Default 1)
        }

        $existing = $null
        if ($onDisk.ContainsKey("$kind/$type")) { $existing = $onDisk["$kind/$type"] }

        # A placeholder row - one Read-GlossaryRows synthesised for an enum value the asset has no entry
        # for - counts as absent. It is on disk only in the sense that the sheet listed it.
        if ($null -ne $existing -and
            [string]::IsNullOrWhiteSpace([string]$existing.Title) -and
            [string]::IsNullOrWhiteSpace([string]$existing.Body)) {
            $existing = $null
        }

        if ($null -eq $existing) {
            $entry['action'] = 'create'
            $glossary += $entry
            continue
        }

        $changed = @()
        if ($title -ne [string]$existing.Title)                { $changed += 'Title' }
        if ($body  -ne [string]$existing.Body)                 { $changed += 'Body' }
        if (($terms -join ', ') -ne [string]$existing.Terms)   { $changed += 'Terms' }
        if ($entry.defaultStacks -ne [int]$existing.'Default Stacks') { $changed += 'Default Stacks' }
        if ($entry.defaultAmount -ne [int]$existing.'Default Amount') { $changed += 'Default Amount' }

        if ($changed.Count -gt 0) {
            $entry['action'] = 'update'
            $entry['changed'] = $changed
            $glossary += $entry
        }
    }
}

# Assets with no row in the sheet. Reported, never removed. Suppressed entirely under a filter, where
# "not seen" only means "not looked at" and reporting it would be actively misleading.
$orphans = @()
if (-not $OnlyTab -and -not $OnlyKey) {
    foreach ($key in $cardsByKey.Keys) {
        if (-not $seenKeys.ContainsKey($key)) { $orphans += $key }
    }
}

# New effect assets, but only the ones a card that is actually being written still needs. A card whose
# row turned out to match disk exactly contributes nothing, which keeps the identity test clean.
$neededNames = @{}
foreach ($card in $actions) {
    foreach ($entry in $card.effectEntries) { $neededNames[$entry.effectName] = $true }
}

$newEffects = @()
foreach ($name in ($pendingEffects.Keys | Sort-Object)) {
    if (-not $neededNames.ContainsKey($name)) { continue }
    $spec = $pendingEffects[$name]
    $newEffects += [ordered]@{
        name   = $name
        kind   = $spec.kind
        amount = $spec.amount
        folder = $spec.folder
        status = $spec.status
    }
}

$payload = [ordered]@{
    generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    source       = if ($FromCsv) { 'csv' } else { 'xlsx' }
    cards        = @($actions)
    newEffects   = @($newEffects)
    glossary     = @($glossary)
    orphans      = @($orphans | Sort-Object)
    problems     = @($problems)
}

$json = $payload | ConvertTo-Json -Depth 12
Set-Content -LiteralPath $OutputPath -Value $json -Encoding utf8

# ---------------------------------------------------------------------------------------------------

$creates = @($actions | Where-Object { $_.action -eq 'create' })
$updates = @($actions | Where-Object { $_.action -eq 'update' })

Write-Host ""
Write-Host "Create : $($creates.Count)" -ForegroundColor $(if ($creates.Count) { 'Green' } else { 'DarkGray' })
foreach ($c in $creates) { Write-Host "    $($c.key)  ->  $($c.assetPath)" -ForegroundColor DarkGray }

Write-Host "Update : $($updates.Count)" -ForegroundColor $(if ($updates.Count) { 'Yellow' } else { 'DarkGray' })
foreach ($u in $updates) { Write-Host "    $($u.key)  :  $($u.changed -join '; ')" -ForegroundColor DarkGray }

Write-Host "New effect assets : $($newEffects.Count)" -ForegroundColor $(if ($newEffects.Count) { 'Green' } else { 'DarkGray' })
foreach ($n in $newEffects) { Write-Host "    $($n.name)  ($($n.kind) $($n.amount))" -ForegroundColor DarkGray }

Write-Host "Glossary : $($glossary.Count)" -ForegroundColor $(if ($glossary.Count) { 'Yellow' } else { 'DarkGray' })
foreach ($g in $glossary) {
    $what = if ($g.action -eq 'create') { 'new tooltip' } else { $g.changed -join ', ' }
    Write-Host "    $($g.kind) $($g.type)  :  $what" -ForegroundColor DarkGray
}

Write-Host "Ideas tabs                       : not read (design only)" -ForegroundColor DarkGray
if ($filtered -gt 0)         { Write-Host "Rows excluded by -OnlyKey filter : $filtered" -ForegroundColor DarkGray }
if ($orphans.Count -gt 0)    { Write-Host "In assets but not in the sheet   : $($orphans -join ', ')" -ForegroundColor DarkGray }
if ($problems.Count -gt 0) {
    Write-Host "Problems :" -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "    $p" -ForegroundColor Red }
}

Write-Host ""
if ($actions.Count -eq 0 -and $newEffects.Count -eq 0 -and $glossary.Count -eq 0) {
    Write-Host "Nothing to do - the sheet and the assets agree." -ForegroundColor Green
}
else {
    Write-Host "Wrote $OutputPath" -ForegroundColor Green
    Write-Host "Now focus the Unity Editor and pick  Tools > Cards > Sync From Sheet" -ForegroundColor Cyan
}
