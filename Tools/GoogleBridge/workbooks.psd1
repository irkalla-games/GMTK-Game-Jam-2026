<#
    The one place tab/column layout knowledge lives for the Google bridge. Column names mirror the
    functions that already define them - Get-CardColumns, Get-IdeaBacklogColumns, Get-EquipmentColumns,
    Get-EquipmentMergeColumns and Get-RunColumns in their respective *.Common.psm1 - so this file is
    kept in step by re-reading those, not by retyping a second copy of the column lists to drift from.

    Two tab Kinds:

      Table  - a genuinely flat header/row tab, read the same way Import-Excel already reads it
               elsewhere in Tools/. AnchorColumns identify a row (composite for Glossary); AllowCreate
               says whether a row whose anchor is blank in Google may be appended as a new row (a blank
               GUID is exactly what Import-CardSheet.ps1/Import-EquipmentSheet.ps1 already read as
               "create this"). Pullable columns are written back on a pull; everything else on the tab
               is pushed for context but never written back.

      Body   - a label-scanned tab (one Google tab per prefab or per level): column A holds a label,
               column B its value, found by scanning rather than by row number - the same rule
               Read-BodySheetTab/Read-LevelSheetTab already follow, since Write-EnemyWorkbook.ps1 and
               Write-LevelWorkbook.ps1 do not promise a fixed row for anything. PullableFields lists the
               labels that may be written back; DeckField (if set) is that one row's value spread across
               row-and-onward columns instead of a single cell, pipe-joined on the Google side to match
               the same format Get-EnemyMergeRow already compares Decks with.

    Equipment's Modifiers and Card Tuning tabs are the one thing this file cannot list statically - their
    columns come from modifier-schema.json (EquipmentModifierSchema.cs regenerates it from every
    EquipmentModifier/CardModifier subclass), so DynamicSchema names which of
    Get-ModifierColumns/Get-CardTuningColumns in EquipmentSheet.Common.psm1 resolves them at push/pull
    time instead.
#>
@{
    Cards = @{
        Title = 'GMTK Cards'
        Xlsx  = 'Docs\CardDesign.xlsx'
        Tabs  = @(
            # The five class tabs. Pullable = every authoring column except GUID/Sync, which stay
            # visible (pushed) so the row-identity check has something stable to compare against.
            @{ Sheet = 'Knight'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Card Name','Cost','Rarity','Effect 1','Effect 2','Effect 3','Description',
                            'Range Shape','Range Min','Range Max','Key','Class','Folder','Tags',
                            'Aim 1','Area 1','Aim 2','Area 2','Aim 3','Area 3',
                            'Keywords','No Reward','Rotatable','Animation','Notes')
               Dropdowns = @{ 'Effect 1' = 'Effect'; 'Effect 2' = 'Effect'; 'Effect 3' = 'Effect'
                              'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Folder' = 'Folder'
                              'Range Shape' = 'Shape'; 'Aim 1' = 'Aim'; 'Aim 2' = 'Aim'; 'Aim 3' = 'Aim'
                              'No Reward' = 'Bool'; 'Rotatable' = 'Bool' } }
            @{ Sheet = 'Mage'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Card Name','Cost','Rarity','Effect 1','Effect 2','Effect 3','Description',
                            'Range Shape','Range Min','Range Max','Key','Class','Folder','Tags',
                            'Aim 1','Area 1','Aim 2','Area 2','Aim 3','Area 3',
                            'Keywords','No Reward','Rotatable','Animation','Notes')
               Dropdowns = @{ 'Effect 1' = 'Effect'; 'Effect 2' = 'Effect'; 'Effect 3' = 'Effect'
                              'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Folder' = 'Folder'
                              'Range Shape' = 'Shape'; 'Aim 1' = 'Aim'; 'Aim 2' = 'Aim'; 'Aim 3' = 'Aim'
                              'No Reward' = 'Bool'; 'Rotatable' = 'Bool' } }
            @{ Sheet = 'Rogue'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Card Name','Cost','Rarity','Effect 1','Effect 2','Effect 3','Description',
                            'Range Shape','Range Min','Range Max','Key','Class','Folder','Tags',
                            'Aim 1','Area 1','Aim 2','Area 2','Aim 3','Area 3',
                            'Keywords','No Reward','Rotatable','Animation','Notes')
               Dropdowns = @{ 'Effect 1' = 'Effect'; 'Effect 2' = 'Effect'; 'Effect 3' = 'Effect'
                              'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Folder' = 'Folder'
                              'Range Shape' = 'Shape'; 'Aim 1' = 'Aim'; 'Aim 2' = 'Aim'; 'Aim 3' = 'Aim'
                              'No Reward' = 'Bool'; 'Rotatable' = 'Bool' } }
            @{ Sheet = 'Cleric'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Card Name','Cost','Rarity','Effect 1','Effect 2','Effect 3','Description',
                            'Range Shape','Range Min','Range Max','Key','Class','Folder','Tags',
                            'Aim 1','Area 1','Aim 2','Area 2','Aim 3','Area 3',
                            'Keywords','No Reward','Rotatable','Animation','Notes')
               Dropdowns = @{ 'Effect 1' = 'Effect'; 'Effect 2' = 'Effect'; 'Effect 3' = 'Effect'
                              'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Folder' = 'Folder'
                              'Range Shape' = 'Shape'; 'Aim 1' = 'Aim'; 'Aim 2' = 'Aim'; 'Aim 3' = 'Aim'
                              'No Reward' = 'Bool'; 'Rotatable' = 'Bool' } }
            @{ Sheet = 'Neutral'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Card Name','Cost','Rarity','Effect 1','Effect 2','Effect 3','Description',
                            'Range Shape','Range Min','Range Max','Key','Class','Folder','Tags',
                            'Aim 1','Area 1','Aim 2','Area 2','Aim 3','Area 3',
                            'Keywords','No Reward','Rotatable','Animation','Notes')
               Dropdowns = @{ 'Effect 1' = 'Effect'; 'Effect 2' = 'Effect'; 'Effect 3' = 'Effect'
                              'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Folder' = 'Folder'
                              'Range Shape' = 'Shape'; 'Aim 1' = 'Aim'; 'Aim 2' = 'Aim'; 'Aim 3' = 'Aim'
                              'No Reward' = 'Bool'; 'Rotatable' = 'Bool' } }

            # The four backlog tabs. Import-CardSheet.ps1 never reads these - they exist for humans - so
            # a phone edit here can never reach an asset, only the next Refresh/Export round-trip.
            @{ Sheet = 'Knight Ideas'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Card Name','Cost','Rarity','Effect 1','Effect 2','Effect 3','Description',
                            'Range Shape','Range Min','Range Max','Key','Class','Folder','Tags',
                            'Aim 1','Area 1','Aim 2','Area 2','Aim 3','Area 3',
                            'Keywords','No Reward','Rotatable','Animation','Notes',
                            'Buildable','Engine Work','Source','Priority')
               Dropdowns = @{ 'Effect 1' = 'Effect'; 'Effect 2' = 'Effect'; 'Effect 3' = 'Effect'
                              'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Folder' = 'Folder'
                              'Range Shape' = 'Shape'; 'Aim 1' = 'Aim'; 'Aim 2' = 'Aim'; 'Aim 3' = 'Aim'
                              'No Reward' = 'Bool'; 'Rotatable' = 'Bool'
                              'Buildable' = 'Buildable'; 'Priority' = 'Priority' } }
            @{ Sheet = 'Mage Ideas'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Card Name','Cost','Rarity','Effect 1','Effect 2','Effect 3','Description',
                            'Range Shape','Range Min','Range Max','Key','Class','Folder','Tags',
                            'Aim 1','Area 1','Aim 2','Area 2','Aim 3','Area 3',
                            'Keywords','No Reward','Rotatable','Animation','Notes',
                            'Buildable','Engine Work','Source','Priority')
               Dropdowns = @{ 'Effect 1' = 'Effect'; 'Effect 2' = 'Effect'; 'Effect 3' = 'Effect'
                              'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Folder' = 'Folder'
                              'Range Shape' = 'Shape'; 'Aim 1' = 'Aim'; 'Aim 2' = 'Aim'; 'Aim 3' = 'Aim'
                              'No Reward' = 'Bool'; 'Rotatable' = 'Bool'
                              'Buildable' = 'Buildable'; 'Priority' = 'Priority' } }
            @{ Sheet = 'Rogue Ideas'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Card Name','Cost','Rarity','Effect 1','Effect 2','Effect 3','Description',
                            'Range Shape','Range Min','Range Max','Key','Class','Folder','Tags',
                            'Aim 1','Area 1','Aim 2','Area 2','Aim 3','Area 3',
                            'Keywords','No Reward','Rotatable','Animation','Notes',
                            'Buildable','Engine Work','Source','Priority')
               Dropdowns = @{ 'Effect 1' = 'Effect'; 'Effect 2' = 'Effect'; 'Effect 3' = 'Effect'
                              'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Folder' = 'Folder'
                              'Range Shape' = 'Shape'; 'Aim 1' = 'Aim'; 'Aim 2' = 'Aim'; 'Aim 3' = 'Aim'
                              'No Reward' = 'Bool'; 'Rotatable' = 'Bool'
                              'Buildable' = 'Buildable'; 'Priority' = 'Priority' } }
            @{ Sheet = 'Cleric Ideas'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Card Name','Cost','Rarity','Effect 1','Effect 2','Effect 3','Description',
                            'Range Shape','Range Min','Range Max','Key','Class','Folder','Tags',
                            'Aim 1','Area 1','Aim 2','Area 2','Aim 3','Area 3',
                            'Keywords','No Reward','Rotatable','Animation','Notes',
                            'Buildable','Engine Work','Source','Priority')
               Dropdowns = @{ 'Effect 1' = 'Effect'; 'Effect 2' = 'Effect'; 'Effect 3' = 'Effect'
                              'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Folder' = 'Folder'
                              'Range Shape' = 'Shape'; 'Aim 1' = 'Aim'; 'Aim 2' = 'Aim'; 'Aim 3' = 'Aim'
                              'No Reward' = 'Bool'; 'Rotatable' = 'Bool'
                              'Buildable' = 'Buildable'; 'Priority' = 'Priority' } }

            # Composite-keyed: no single column is unique on its own (Kind repeats across Types).
            @{ Sheet = 'Glossary'; Kind = 'Table'; AnchorColumns = @('Kind','Type'); AllowCreate = $false
               Pullable = @('Title','Body','Terms','Default Stacks','Default Amount') }

            # Balance weights. Not read by Import-CardSheet.ps1 at all - only Excel's own formulas
            # consume them - so a phone edit here changes what the NEXT sync's Export step writes back,
            # not an asset. 'Term' names (w_Damage, curve_Base, ...) are fixed; no new weight may be
            # added from the phone, only the Value beside an existing one changed.
            @{ Sheet = 'Model'; Kind = 'Table'; AnchorColumns = @('Term'); AllowCreate = $false
               Pullable = @('Value') }
        )
    }

    Enemies = @{
        Title = 'GMTK Enemies'
        Xlsx  = 'Docs\EnemySheets.xlsx'
        Kind  = 'BodyTabs'
        # Applies to every worksheet Read-BodySheetTab recognises (has a GUID row) - PowerLevel, Roster,
        # Enums, README and Summoned all fail that test and are skipped automatically, same as the
        # PowerShell importer. One Google tab per prefab, so there is no row-identity risk here at all:
        # tabs are matched by NAME, never by position.
        PullableFields = @('Display Name','Health','Actions Per Turn','Brain','Targeting','Loot Table',
                           'Role',"Brandon's Power Level",'Notes')
        # One card per CELL across the Deck row (not one pipe-joined string), so every slot can carry its
        # own dropdown - same shape the Excel body tabs use.
        DeckField      = 'Deck'
        DeckDropdown   = 'EnemyCard'
        FieldDropdowns = @{ 'Brain' = 'Brain'; 'Targeting' = 'Targeting'; 'Loot Table' = 'Loot'; 'Role' = 'Role' }

        # Read-only balance reference, pushed below the editable fields and NEVER pulled back: one column
        # per deck card, lining up with the Deck row above it. These labels are read straight off the body
        # tab by name (Write-EnemyWorkbook.ps1 already computes every one of them), so nothing here
        # re-derives a number the workbook already owns.
        #
        # Rows whose every cell is empty are dropped rather than pushed. That is what keeps the formula-
        # driven ones (Card Power, Status Power, Range) from showing up as a block of blanks: EPPlus does
        # not evaluate formulas, so they have no cached value until the workbook is opened in Excel once,
        # at which point they start appearing on their own.
        ReferenceFields = @('Estimated Power Level', "Delta (Estimated - Brandon's)",
                            'Average Card Power', 'Average Damage', 'Average Status Power', 'Average Summon Power',
                            'Actions from Card', 'Counted',
                            'Cost', 'Range Max', 'Tiles Hit', 'Damage', 'Range', 'Poison Total', 'Heal',
                            'Shield', 'Block Total', 'Parry', 'Dodge', 'Strength', 'Weaken', 'Vulnerable',
                            'Frozen', 'Rooted', 'Taunt', 'Stealth', 'Poison Blade', 'Double Next Attack',
                            'Double Shield', 'Draw', 'Move Tiles', 'Summon Power', 'Cooldown',
                            'Card Power', 'Status Power')

        # Pushed cell-for-cell so the pw_* / range_Power / pow_* named ranges can point at exactly the
        # addresses they point at in Excel, and the formulas that reference them need no rewriting.
        # Read-only: nothing here is ever pulled back.
        VerbatimTabs = @('PowerLevel')
    }

    # Deliberately a full copy of Enemies' settings rather than a reference to them: a .psd1 is parsed in
    # restricted language mode, so it cannot share a list between two entries. Bosses and enemies are the
    # same Character component and the same tab layout, so these two blocks must be kept in step - if you
    # add a dropdown or reference row to one, add it to the other.
    Bosses = @{
        Title = 'GMTK Bosses'
        Xlsx  = 'Docs\BossDesign.xlsx'
        Kind  = 'BodyTabs'
        PullableFields = @('Display Name','Health','Actions Per Turn','Brain','Targeting','Loot Table',
                           'Role',"Brandon's Power Level",'Notes')
        DeckField      = 'Deck'
        DeckDropdown   = 'EnemyCard'
        FieldDropdowns = @{ 'Brain' = 'Brain'; 'Targeting' = 'Targeting'; 'Loot Table' = 'Loot'; 'Role' = 'Role' }
        ReferenceFields = @('Estimated Power Level', "Delta (Estimated - Brandon's)",
                            'Average Card Power', 'Average Damage', 'Average Status Power', 'Average Summon Power',
                            'Actions from Card', 'Counted',
                            'Cost', 'Range Max', 'Tiles Hit', 'Damage', 'Range', 'Poison Total', 'Heal',
                            'Shield', 'Block Total', 'Parry', 'Dodge', 'Strength', 'Weaken', 'Vulnerable',
                            'Frozen', 'Rooted', 'Taunt', 'Stealth', 'Poison Blade', 'Double Next Attack',
                            'Double Shield', 'Draw', 'Move Tiles', 'Summon Power', 'Cooldown',
                            'Card Power', 'Status Power')

        # Pushed cell-for-cell so the pw_* / range_Power / pow_* named ranges can point at exactly the
        # addresses they point at in Excel, and the formulas that reference them need no rewriting.
        # Read-only: nothing here is ever pulled back.
        # Summoned as well as PowerLevel: seven of the eight bosses summon a plain enemy whose
        # pow_<Prefab> cell lives on that read-only mirror tab, and their Summon Power formulas
        # reference it.
        VerbatimTabs = @('PowerLevel', 'Summoned')
    }

    Levels = @{
        Title = 'GMTK Levels'
        Xlsx  = 'Docs\LevelDesign.xlsx'
        Kind  = 'LevelTabs'
        # Scalar rows, label-scanned exactly like a body tab.
        PullableFields = @('Board Width','Board Height','Turns To Survive','Hand Size','Loot Table',
                           'Clear Reward Table','Tile Sets','Notes')
        # Both table fields draw from the one Loot list - the same single list_Loot named range
        # Write-LevelWorkbook.ps1 points both of those cells at.
        FieldDropdowns   = @{ 'Loot Table' = 'Loot'; 'Clear Reward Table' = 'Loot' }
        # The Prefab column on each level's Placements tab.
        PlacementDropdowns = @{ 'Prefab' = 'Prefab' }
        # The Waves block (turn/Coords row pairs under the "Waves" label) and the Party row are the one
        # layout the bridge cannot treat as a flat table without risking a silent misalignment - see
        # Pull-GoogleSheet.ps1's Sync-LevelWaves. Both are positional: retyping a Prefab/Coord in an
        # existing slot is safe, but changing how many placements or party members there are is refused
        # and must be done at the desk first (add/remove waves, save, sync, THEN tune from the phone).
        WavesTable  = $true
        PartyTable  = $true
        # Deck Overrides and Runs are genuinely flat tables - Explore already confirmed Runs is read
        # with Import-Excel like a card tab.
        # Get-RunColumns is exactly Run/Levels/Carry Damage/GUID/Sync - no Notes column exists here.
        ExtraTables = @(
            @{ Sheet = 'Runs'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Run','Levels','Carry Damage')
               # Levels is a semicolon-joined list of level names, so no dropdown for it.
               Dropdowns = @{ 'Carry Damage' = 'Bool' } }
        )
    }

    Equipment = @{
        Title = 'GMTK Equipment'
        Xlsx  = 'Docs\EquipmentDesign.xlsx'
        Tabs  = @(
            # Effect Preview / Modifier Summary / Mod Count are derived display columns Import-
            # EquipmentSheet.ps1 never reads (see Get-EquipmentMergeColumns) - left off Pullable on
            # purpose so a phone edit there is never silently discarded by the next sync.
            @{ Sheet = 'Equipment'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $true
               Pullable = @('Key','Folder','Item Name','Description','Rarity','Class','Slot','No Reward','Notes')
               Dropdowns = @{ 'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Slot' = 'Slot'
                              'No Reward' = 'Bool'; 'Folder' = 'EquipFolder' } }

            # Only the unambiguously single-valued leaf columns get dropdowns - a multi-value cell like
            # filter.tags (semicolon-joined) would be actively worse with one.
            @{ Sheet = 'Modifiers'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $false
               DynamicSchema = 'Equipment'
               Dropdowns = @{ 'Modifier Type' = 'EquipModType'; 'grantedStatus' = 'Status'
                              'condition' = 'DamageCondition'; 'totemsOnly' = 'Bool'; 'perStack' = 'Bool' } }
            @{ Sheet = 'Card Tuning'; Kind = 'Table'; AnchorColumns = @('GUID'); AllowCreate = $false
               DynamicSchema = 'CardTuning'
               Dropdowns = @{ 'Card Modifier Type' = 'CardModType'; 'entry.effect' = 'CardEffect'
                              'entry.aimsAt' = 'EffectTarget'; 'area.kind' = 'AreaKind'
                              'entry.area.kind' = 'AreaKind'; 'area.radius.shape' = 'RangeShape'
                              'entry.area.radius.shape' = 'RangeShape'; 'overrideShape' = 'RangeShape'
                              'add' = 'CardKeyword'; 'remove' = 'CardKeyword'
                              'on.matchesEffect' = 'CardEffect'; 'replacement' = 'CardEffect' } }

            # Never read by the importer - human backlog only, same as the Card sheet's Ideas tabs. No
            # GUID column exists here to anchor on (unlike every other tab), so this one has NO identity
            # column at all: alignment falls back to row COUNT only (see Push/Pull-GoogleSheet.ps1's
            # empty-AnchorColumns path) - safe here specifically because nothing ever imports this tab,
            # so the worst a misalignment can do is scramble a backlog row, not a real asset.
            @{ Sheet = 'Ideas'; Kind = 'Table'; AnchorColumns = @(); AllowCreate = $true
               Pullable = @('Key','Folder','Item Name','Description','Rarity','Class','Slot',
                            'Buildable','Engine Work','Source','Priority','Notes')
               Dropdowns = @{ 'Rarity' = 'Rarity'; 'Class' = 'Class'; 'Slot' = 'Slot'
                              'Buildable' = 'Buildable'; 'Priority' = 'Priority' } }
        )
    }
}
