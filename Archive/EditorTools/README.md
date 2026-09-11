# Archived Editor tools

Nothing in this folder is compiled. It lives outside `Assets/`, so Unity never sees it, no menu item
it used to register still exists, and none of its `[MenuItem]`, `[CustomEditor]` or
`AssetPostprocessor` attributes are live. Restoring a file is what makes it live again.

## Why this exists

Most of `Assets/Editor` was one-shot: a generator that authored a batch of content once, or a wiring
command that built a UI panel once, back when the panel didn't exist. Once run, they don't need to run
again — and running one again is not a no-op. Every one of them **repairs rather than skips** (per
CLAUDE.md's convention): it re-writes every field it owns on every run, against whatever the project
looks like *now*. That is exactly right the first time and actively destructive afterwards — retuned
numbers, hand-placed layout, sheet-synced stats all get silently overwritten back to whatever the
generator's source constants say.

These 53 scripts (plus the Unity template's `TutorialInfo/` and the frozen `StatusIcons/` work order)
were archived for that reason, not because they're broken. If a menu item you used to reach for is
gone, it's here.

## Restoring a file

1. `git mv` both the `.cs` **and its `.cs.meta`** back into `Assets/Editor/` (or
   `Assets/Scripts/Editor/` for the four `PropertyDrawer`s that never lived in `Assets/Editor`).
   Moving the pair together keeps the GUID, so anything that already referenced the type by GUID
   (nothing currently does, but a future re-add might) stays linked.
2. Reopen or refocus Unity so it compiles the restored file.
3. **Read the file's header comment before running it.** It was written for a project state that has
   since moved on. In particular:
   - **`EnemyRosterGenerator.cs` + `EnemyRoster.cs` will overwrite sheet-tuned enemy stats.** The
     enemy design workbook (`Tools/EnemySheet`) owns those numbers now; re-running the generator
     against an existing body rewrites every field it owns back to the hardcoded table in
     `EnemyRoster.cs`, discarding anything tuned through the sheet since. Only run it for a genuinely
     new enemy body, and run `Tools/Sync Enemies With Sheet` immediately after.
   - Any `*ContentGenerator`/`*Authoring` file only creates what's missing but **always rewrites the
     numbers it owns** — safe to re-run for a new asset, not safe to re-run to "refresh" an existing
     one you've since hand-tuned.
   - `EnemyIdleAnimationSetup.cs` hardcodes specific prefab and scene-object names
     (`SkeletonWarrior`, `EnemyRanger`, `"SkeletonWarrior (1)"`) from before `EnemyRosterGenerator`
     existed. It is almost certainly stale against the current prefab set.

## What's here and why

| File | Registered | Why archived |
| --- | --- | --- |
| `AimHintWiring.cs` | `Tools/Battle HUD/Wire Aim Hint` | One-shot scene wiring |
| `AreaOfEffectExampleContent.cs` | `Tools/Cards/Author Area Of Effect Examples` | One-shot content generator |
| `BattleHudWiring.cs` | `Tools/Battle HUD/Wire Info Panels` | One-shot scene wiring |
| `BoardCameraWiring.cs` | `Tools/Board/2 - Wire Cameras`, `.../3 - Wire Board Objects` | One-shot scene wiring |
| `BoardTuning.cs` | `Tools/Board/5 - Apply Tuning` | One-shot scene/prefab tuning |
| `BossIdentityWiring.cs` | `Tools/Bosses/Wire Boss Identities` | One-shot prefab wiring |
| `CardArtWindowResize.cs` | `Tools/Cards/3 - Narrow Card Art Window` | One-shot layout tweak, already applied |
| `CardChoicePanelWiring.cs` | `Tools/UI/Wire Card Choice Panel` | One-shot UI wiring |
| `CardEffectEntryMigration.cs` | `Tools/Cards/Migrate Effects To Entries` | Explicit one-shot data migration; genuinely obsolete once run |
| `CardFaceLayout.cs` | `Tools/Cards/Apply Card Face Layout` | One-shot layout, already applied |
| `CardFaceV2Builder.cs` | `Tools/Cards/Build/Use Card Face V2...` | One-shot prefab build + a swap already made |
| `CardFolderNormaliser.cs` | `Tools/Cards/Normalise Card Folders` | One-shot asset-folder migration |
| `CardPileWiring.cs` | `Tools/Battle HUD/Wire Card Piles` | One-shot scene wiring |
| `CharacterSelectResizeWiring.cs` | `Tools/Main Menu/Resize Character Select` | One-shot layout tweak |
| `CharacterSelectSkinWiring.cs` | `Tools/Main Menu/Skin Character Select` | One-shot restyle |
| `CharacterSelectWiring.cs` | `Tools/Main Menu/Wire Character Select`, `Set Character Select Slot Spacing` | One-shot scene wiring (dev commands extracted to `Assets/Editor/DevCommands.cs`) |
| `ClericContentGenerator.cs` | `Tools/Cards/Author Cleric` | One-shot content generator |
| `DebugPanelWiring.cs` | `Tools/UI/Wire Debug Panel` | One-shot UI wiring |
| `EnemyIdleAnimationSetup.cs` | `Tools/Setup Enemy Idle Animations`, `.../Fix Enemy Sprite Pivots` | One-shot, hardcoded prefab names; superseded by `EnemyRosterGenerator` |
| `EnemyPrefabWiring.cs` | `Tools/Enemies/Wire Totem Targeting And Portraits` | One-shot prefab wiring |
| `EnemyRoster.cs` | (no menu item — data table) | Superseded: `Tools/EnemySheet` now owns these numbers |
| `EnemyRosterGenerator.cs` | `Tools/Enemies/Generate Roster` | Superseded: re-running overwrites sheet-tuned stats |
| `EnergyPipArtWiring.cs` | `Tools/Battle HUD/Wire Energy Pips` | One-shot art wiring |
| `EquipmentExampleContent.cs` | `Tools/Equipment/Author Equipment Examples` | One-shot content generator |
| `EquipmentUIWiring.cs` | `Tools/Equipment/Wire Equipment UI` | One-shot UI wiring |
| `HeroPortraitResizeWiring.cs` | `Tools/Battle HUD/Resize Hero Portrait` | One-shot layout tweak |
| `IntentBackgroundWiring.cs` | `Tools/Battle HUD/Wire Intent Backgrounds` | One-shot scene wiring |
| `LevelRewardWiring.cs` | `Tools/Level Rewards/Wire Level Reward UI` | One-shot UI wiring |
| `MainMenuCanvasWiring.cs` | `Tools/Main Menu/Normalise Canvas` | One-shot scene wiring |
| `MenuSettingsWiring.cs` | `Tools/Main Menu/Wire Settings Panel` | One-shot UI wiring |
| `MovementAuthoring.cs` | `Tools/Cards/Author Movement and Teleport` | One-shot content authoring |
| `NextWaveWiring.cs` | `Tools/Battle HUD/Wire Next Wave Preview` | One-shot scene wiring |
| `OverheadStatusIconWiring.cs` | `Tools/Battle HUD/Wire Overhead Status Icons` | One-shot prefab wiring |
| `PartyPortraitWiring.cs` | `Tools/Battle HUD/Wire Party Portraits` | One-shot scene wiring |
| `PartySheetStyling.cs` | `Tools/Battle HUD/Restyle Party Sheet` | One-shot restyle |
| `PauseMenuWiring.cs` | `Tools/UI/Wire Pause Menu` | One-shot UI wiring |
| `ProgressionContentGenerator.cs` | `Tools/Progression/*` (dev commands extracted to `DevCommands.cs`) | One-shot content authoring |
| `PushWallAuthoring.cs` | `Tools/Cards/Author Push and Rotatable Walls` | One-shot content authoring |
| `RingContentGenerator.cs` | `Tools/Equipment/Author Ring Set` | One-shot content generator |
| `SettingsRowBuilder.cs` | (no menu item — shared library) | Only used by archived wiring files |
| `SharpSkin.cs` | (no menu item — shared library) | Only used by archived wiring files |
| `SharpSkinWiring.cs` | `Tools/Battle HUD/Skin Buttons And Panels` | One-shot restyle |
| `StarterCharacterAuthoring.cs` | `Tools/Main Menu/Author Starter Character Data` | One-shot content authoring |
| `StatusChipIconWiring.cs` | `Tools/Battle HUD/Center Status Chip Icon` | One-shot layout tweak |
| `StatusIconArtWiring.cs` | `Tools/Battle HUD/Fill Missing Status Icons` | One-shot art wiring |
| `StatusIconAssignmentImporter.cs` | `Tools/Battle HUD/Apply Status Icon Assignments` | Sheet-sync *shaped* but not one: no export leg, no baseline, applies a frozen hand-written work order (`StatusIcons/assignments.json`) already behind the `StatusType` enum |
| `SummonAuthoring.cs` | `Tools/Cards/Author Summons and Walls` | One-shot content authoring |
| `TileSetContentGenerator.cs` | `Tools/Board/4 - Author Default Tile Set` | One-shot content generator |
| `TooltipCanvasSortingFix.cs` | `Tools/UI/Fix Tooltip Canvas Sorting Order` | One-shot fix, already applied |
| `TooltipPanelWiring.cs` | `Tools/UI/Wire Tooltip Panel` | One-shot UI wiring |
| `TotemContentGenerator.cs` | `Tools/Cards/Generate Card Batch`, etc. | One-shot content generator + shared totem-prefab factory |
| `TutorialContentGenerator.cs` | `Tools/Tutorial/1 - Generate Tutorial Content` | One-shot content generator |
| `TutorialWiring.cs` | `Tools/Tutorial/2 - Wire Tutorial Overlay`, `.../3 - Wire Main Menu` | One-shot scene wiring |
| `TutorialInfo/` | `ReadmeEditor.cs` (`[InitializeOnLoad]`), `Readme.cs`, `Readme.asset` | Unity project-template boilerplate, never ours; force-selected a readme on every Editor session start against an already-stale hardcoded path |
| `StatusIcons/assignments.json` | consumed by `StatusIconAssignmentImporter.cs` | Already-applied one-shot work order, frozen as of 2026-09-03 |

## What's still live, for contrast

`Assets/Editor` keeps: the five design-sheet syncs (`CardSheetImporter`, `EnemySheetImporter`,
`BossSheetImporter`, `LevelSheetImporter`, `EquipmentSheetImporter`) and their shared engine
(`RosterSheetSync`, `SheetSyncProcess`, `GoogleBridgeSync`, `EquipmentModifierSchema`); the Inspector
and asset-import infrastructure (`CardLibraryEditor`, `EquipmentLibraryEditor`, and the four
`PropertyDrawer`s in `Assets/Scripts/Editor`); five recurring workflow tools (`CardArtAssigner`,
`EnemyRegistryGenerator`, `CardArtImportNormaliser`, `BlockSpriteImporter`, `DebugTestbedGenerator`);
the read-only `EncounterSimulation`/`EncounterSimulatorWindow`; and `DevCommands.cs`, the
testing/unlock shelf pulled out of two of the files archived here.
