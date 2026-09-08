# Changelog

Significant changes and the reasoning behind them. This is the "how we found this" file; `CLAUDE.md`
is the "what to do" file. When a gotcha there is compressed to a rule and a one-line why, the full
story is here.

Newest first. Older entries reconstructed from git history, so they are titled by commit rather than
written up.

---

## 2026-09-05 — Wall of Force blocks walking, not just standing

**Asked for:** most move cards blocked by Wall of Force, with a teleport that ignores it — the Bat's
new Move 2 card should be forced around a wall rather than hopping straight across it, while a boss's
teleport passes through.

**Movement was destination-only.** `TargetRange.Contains` is pure coordinate math on the two tiles
involved, and `GridManager.MoveRefusal` only ever asked about the tile a move would *land on* —
occupant, `GridTile.EnterRefusal`, Rooted. Nothing examined the tiles in between, and
`GridManager.MoveCharacter` committed occupancy and then played one straight-line `DOMove` tween to
the destination. So `WallOfForceTileEffect` genuinely refused you standing on it, but a two-tile Move
card just slid over it — visibly crossing a wall it was supposed to be stopped by.

**Fix: `TargetRange.RequiresRoute`.** A new serialized bool, false by default (same reasoning as
`RangeShape.Anywhere = 0`: every card authored before this field existed keeps today's behaviour).
`Card.Refusal`/`CommittedRefusal` ask `GridManager.RouteRefusal` when it's set, which runs a BFS
(`Board.Routes`, walls block it, bodies don't) from the mover's tile and checks whether the target is
reachable within `range.MaxDistance` steps. Because this lives in `Card.Refusal`, the tile highlight,
the click gate, and enemy candidate filtering all get the rule for free — a walled-off tile falls out
of the green "playable" set and into the hatched "in range but refused" one with no separate wiring.

**The rule lives on the range, not the effect**, because all four move/teleport cards share one
`MoveEffect` asset (`Assets/Data/EffectData/Move.asset`) — a flag there couldn't tell Move 2 from
Teleport. `RangeShape.Anywhere` cards skip the check outright regardless of the flag, so Teleport
needs no special case.

**`Board.Flood` and `Board.PathTo` were already sitting there, unused, exactly for this.**
`PathTo`'s own doc comment already said "the path is what gets stored on an Intent... a blocked move
has to advance as far as it can" — nothing had ever called either of them. `Flood` is renamed `Routes`
and changed from "walk empty tiles only" to "walk anything but a wall", since a route may pass through
an occupied tile (`MoveRefusal` already refuses *landing* on one) — treating an ally as a wall would
jam a column of enemies solid in a one-wide corridor, which is exactly the failure mode `CostField`'s
own comment already warns about for the brains' *scoring*.

**`MoveAction` now walks its route tile by tile** instead of tweening straight to the destination,
mirroring `PushAction`'s existing shape for sequencing several `MoveCharacter` + `WaitForSeconds`
calls. A teleport (RequiresRoute off) keeps the original single straight-line tween.

**`GridManager.BoardChanged()`** is a version counter bumped by `GridTile.SetOccupant`,
`AddTileEffect`, `TickTileEffects` and `BuildGrid` — anything that changes what a route can cross.
`ShowPlayableTiles` asks a route question once per tile on the board every time a card is picked up,
so the BFS is memoized against this counter rather than recomputed per tile.

**New: `Assets/Data/CardData/Enemy/TeleportInnate.asset`**, an enemy-only Innate "Teleport" (Chebyshev
1–3, cost 1, `RequiresRoute` off) built from the same shared `Move.asset` effect, and the three Evil
Wizard bosses (`EvilWizard`/`EvilWizard2`/`EvilWizard3`) now carry it in place of `MoveInnate` — see
`Assets/Editor/MovementAuthoring.cs`, run through `Tools > Cards > Author Movement and Teleport`
rather than hand-edited YAML, since the Editor holds the assets open.

---

## 2026-09-05 — Bosses split into their own design workbook

**Asked for:** bosses and enemies in two separate sheets, with a new tool syncing the boss one.

`Docs/EnemySheets.xlsx` carried all 29 bodies — 20 enemies, 8 bosses, 1 ally — discovered by a single
folder regex. Bosses and trash mobs are budgeted completely differently but shared one Roster tab and,
more importantly, **one `baseline.json`**, so a boss retune and an enemy retune landed in the same
three-way merge. Now `Docs/BossDesign.xlsx` + `Tools/BossSheet` + `Tools > Sync Bosses With Sheet` own
`Assets/Prefabs/Bosses`, and the enemy tool keeps `Enemies` + `Allies`.

**No second copy of the tool.** Bosses and enemies are the same `Character` component with the same tab
layout, merge columns and prefab writes, so a cloned `BossSheet.Common.psm1` would have been ~105KB of
PowerShell guaranteed to drift. Instead `Get-RosterDomain` in `EnemySheet.Common.psm1` holds the dozen
values that differ (folders, paths, labels, menu names) and `Export-`/`Import-EnemySheet.ps1` take a
`-Domain`; `Tools/BossSheet` is two ~40-line forwarders plus its own `baseline.json`. On the Unity side
`Assets/Editor/RosterSheetSync.cs` is the extracted body of both importers, which are now just their
`[MenuItem]`s plus a config. The other three sheet tools keep their own copies on purpose — they each
describe a *different* asset shape, so they are similar, not identical.

**The trap: `$domain` and `$Domain` are the same variable.** PowerShell variable names are
case-insensitive, so assigning the resolved descriptor to `$domain` inside a script whose parameter is
`[ValidateSet('Enemies','Bosses')][string]$Domain` re-runs that validator against a `PSCustomObject` and
throws `ValidationMetadataException` at the assignment. A parameter's validation attributes stay
attached to the variable for the whole script scope, not just at binding. The descriptor is now
`$rosterDomain`.

**Seven of the eight bosses summon a plain enemy** — BlackKnight and Reaper→Skeleton,
EvilKnight→HeavyBandit, EvilWizard→Goblin, EvilWizard2→Bat, EvilWizard3→Slime,
ReinforcedGolem→Pebble, EvilKing→LightBandit — and Summon Power is a live `=pow_<Prefab>` reference to
that body's own power cell. Excel names are **workbook-scoped**, so those seven had no target in a
bosses-only workbook. Giving each summoned enemy a real tab would have fixed the formula and broken
something worse: the same prefab described by two workbooks with two baselines, both importers queueing
writes for it on every sync, fighting forever. So `Get-DiscoveredRoster` gained `-SummonsAsReference`,
which routes a summoned *Character* (not a Totem) into a third bucket that becomes a read-only
`Summoned` tab holding only the `pow_*` cells. **It has no `GUID` row** — that is what makes
`Read-BodySheetTab` return `$null` for it, so every reader skips it without needing to know it exists.
Verified: the boss import reads 8 tabs, not 15.

**PowerLevel is mirrored, not duplicated.** Boss power is only meaningful on the same scale as enemy
power, so `BossDesign.xlsx`'s PowerLevel tab is rebuilt from `EnemySheets.xlsx`'s on every export.
Editing it there does nothing — a silently-reverted edit being the worst possible outcome, the tab now
carries a red READ-ONLY banner saying so, and the README repeats it.

**Migration.** `Tools/EnemySheet/baseline.json`'s 8 boss entries were moved to
`Tools/BossSheet/baseline.json` before the first run; without that every boss column verdicts
`noBaseline` and the first sync reports all 8 UNRESOLVED and writes nothing. A one-time
`-SeedPreservedFrom` switch on the export carried the bosses' hand-authored Brandon's Power Level and
Notes across, since those live only in the workbook and would otherwise have started blank — which is
why the boss export must run *before* the enemy workbook is rebuilt without its boss tabs.

**How the refactor was proved neutral:** the pre-change export was restored from `HEAD` into a scratch
folder inside `Tools/` (so `$scriptDir\..\..` still resolved) and run against the same working tree.
Comparing its Roster and Decks CSVs against the new code's, on every prefab-derived column, the *only*
difference was the 8 boss rows being absent. All 21 surviving enemies were byte-identical.

---

## 2026-09-03 — Status icon sizing, and the layout trap that bit a third time

**Reported:** status icons on the Enemy/Hero panels looked off-centre, and on the Tab party sheet the
Parry icon looked narrower than Strength and Block.

Two real bugs, and two wrong diagnoses on the way to the second one.

**Fixed — `StatusChip`'s icon sat in the chip's top-left quarter.** The `Icon` child was anchored
`(0, 0.5)-(0.5, 1)` with a zero `sizeDelta`, which is exactly one quadrant: on a 44-unit chip the glyph
drew at 20x20 in the corner while the `Count` badge sat alone in the opposite one. Both callers size
only the chip *root* (`SelectedCharacterPanel.Place` at 44, `HeroPortrait.Place` at 42) and neither
touches the icon, so the quadrant was the whole story. New `Assets/Editor/StatusChipIconWiring.cs`
stretches it to the full rect inset 7 units — the frame's 9-slice border draws 7.5 units thick, so the
glyph now sits inside the dark centre. `Count` was deliberately left alone: it is second in child
order, so it still draws over the glyph's corner, which is the intended look.

**Fixed — `StatusDetailRow`'s icon was compressed by its neighbouring text.** The real cause of the
original "Parry is narrower" report, and the third distinct form of the `LayoutElement` negative-value
trap. The icon had `preferredWidth: 36` with `minWidth: -1`. A preferred size is only a preference: a
`HorizontalLayoutGroup` with `childControlWidth` that cannot fit its children compresses them toward
their *minimums*, and a `-1` is skipped before priority is consulted, so the `Image` on the same object
answered with `0`. The icon was therefore squeezed by however much the row's description text wanted —
so a status with a long description got a **narrower icon than one with a short description**, and
because the `Image` was not `preserveAspect` the glyph inside was horizontally **squashed** to match.
Fixed in `PartySheetStyling.AddFixedSize`, which now sets minimum as well as preferred size;
`AddFixedHeight` got the same treatment, and the row icon now sets `preserveAspect`.

The user diagnosed this one, not me, and the evidence was a **neighbouring** element: the description
text started at a different x in each row. That is only possible if the icon's box differs per row, and
it is far stronger evidence than looking at the art. Ordering matched description length exactly —
Strength (shortest) rightmost, Parry (longest) leftmost, Rooted between.

**Reverted — status icon "optical normalization".** I built
`Assets/Editor/StatusIconNormalizer.cs` to rescale each glyph toward the set's median ink coverage,
generating into `Assets/Sprites/StatusIcons/` and repointing `StatusIcons.asset`. The premise was
wrong: the icons were never the problem. All 19 are 512x512, `spriteMode: 1` with no custom sprite
rect, nothing trimmed, and each one's ink touches the canvas edge on its longest axis — the pack is
already normalised on bounding box. What differs is the *drawn* aspect (White Return is 512x452, wide
and short; White Anchor is 448x512, tall), which is a property of the artwork. Reverted entirely:
asset repointed to the pack originals, generated folder and the normalizer deleted.

Worth keeping from that dead end: **three of those pack files are shared.** `White Skull` is the
Skeleton Warrior's `Character.portrait`; `White Snow` and `White Wind` are the Freeze and Status
`CardAnimation.projectileSprite`s. Never modify the pack originals in place.

**Two measurement errors worth remembering.** First, I reported that the icons were padded with the
glyph shoved into a corner — an artifact of my own script. `System.Drawing`'s
`DrawImage(img, x, y)` honours the file's DPI, and these PNGs are 300 DPI, so a 512px image drew at
164px in the corner of a 512px canvas. Always pass an explicit destination `Rectangle`, and measure
alpha bounds from the pixel buffer rather than from a rendered composite. Second, the first version of
the normalizer centred every glyph in a 640px canvas so light ones had room to grow — but a UI `Image`
fits the *canvas* to its box, so that shrank all nineteen by a fifth on screen and cancelled out the
chip fix landing in the same pass. It read as "nothing happened."

**Unrelated, found while diagnosing:** an Asset Store import ("25 FREE hunter skill icons", which also
added ~1,200 files under `Assets/Card Art/`) shipped its own 2023-era TextMesh Pro essentials and
overwrote the project's — font assets, `TMP_SDF-Mobile.shader`, `TMPro_Properties.cginc`. All text in
the game rendered fuzzy and wrong. Restored from git. **Check `git status` after any Asset Store
import**; packages bundling TMP essentials is common and silent.

---

## Post-mortems carried over from CLAUDE.md

These predate this changelog and are undated. Each is the long form of a rule that is still stated in
`CLAUDE.md`.

### A row whose preferred height was 40 rendered at 118.5

`LayoutUtility.GetLayoutProperty` skips any component returning a negative value *before* it looks at
`layoutPriority`, so the next `ILayoutElement` on the object answers instead. On a row that is itself a
`HorizontalLayoutGroup` with `childForceExpandHeight = true`, that group reports `flexibleHeight = 1`,
and the row then advertises "I will take spare vertical space" to its column no matter what its
`preferredHeight` says. Four such rows splitting a 314px surplus produced the 118.5. The symptom is
unmistakable and misleading: the Inspector shows Preferred Height 40 next to a driven Height of 118.5,
and re-running the wiring changes nothing, because every value it writes already matches.

### A row that wanted 875px of width

The same trap through a different component. A row whose background is a **Simple `Image`** reports
that sprite's native size as its `preferredWidth`; since the `Image` and the layout group are both
priority 0, `LayoutUtility` takes the larger. A row backed by the 875px `list_item_background` wanted
875px however little its contents needed.

### A button crushed into a vertical letter stack

`SharpSkin.EnsureChild` makes a command idempotent for what it still builds, not for what it used to.
When a row changed from a captioned `ButtonRow` to a caption-less `ButtonBar`, re-running left the old
`Label` and `Button` beside the new `Button0` — three children in a horizontal row. Row builders now
call `SharpSkin.PruneChildren` with exactly the children they create, so a container holds what the
current code says rather than the union of every version that ever ran.

### A default-size board with nothing on it

`AssetDatabase.StartAssetEditing()` defers every import in the block, so `LoadAssetAtPath` returns null
for anything created in that same block — silently. Every file appears, so the Project window looks
right, while every cross-reference is written as `{fileID: 0}` and every `SerializedObject` edit to a
just-created asset is dropped. In game this surfaced as an empty, default-size board:
`RunManager.CurrentLevel` null means `BuildGrid(Vector2Int.zero)` falls back to `GridManager`'s own
width/height, and both `SpawnParty` and `SpawnEnemies` early-return. The fix is to create in dependency
order and pass each new asset *down as a live object* rather than re-loading it by path — see
`TutorialContentGenerator`.

A related rule came out of the same episode: **a content generator should repair, not skip.** A bare
`if (Exists(path)) return;` guard makes a half-built set permanently unfixable except by deleting files
by hand, which is exactly the state the bug above leaves behind.

---

## Balance notes

Consequences of the numbers as authored rather than bugs.

Parry charges survive `TurnStart` while Shield and Block do not. Block decrements one stack per turn
regardless of whether it took a hit (`BlockStatus.OnTurnStart`), so "Block 3" is a flat -3 to every hit
for the next three turns rather than three hits' worth. Against the current 3–5 enemy damage band that
fully negates most single-enemy hits for as long as it lasts; against several enemies in one turn it is
worth far more than the same-cost Shield, which drains its pool faster the more hits arrive.

A reflected parry goes back through `TakeDamage`, so the attacker's own statuses answer it and a parry
can itself be parried. `Character.MaxParryBounces` caps the resulting bounce war, which only fails to
terminate on its own if an aura is granting Parry.

---

## Project history

From git, newest first.

| Date | Change |
| --- | --- |
| 2026-09-02 | UI redesign and equipment additions |
| 2026-09-01 | Totem redesign + debug test |
| 2026-08-31 | Lots of updates |
| 2026-08-27 | Randomize enemies |
| 2026-08-23 | Wave panel and tutorial |
| 2026-08-22 | Tutorial fixes and Excel authoring |
| 2026-08-21 | Redesign and tutorial bugs |
| 2026-08-20 | Board generation and new assets; card and level updates |
| 2026-08-17 | New updates plus tutorial |
| 2026-08-16 | Tooltip and card updates |
| 2026-08-14 | UI updates and new cards; card design workbook and its export/import tooling |
| 2026-08-11 | Bug fixes |
| 2026-08-09 | Level reward + shield update; spawning update |
| 2026-08-08 | Animation updates |
| 2026-08-07 | ItemPickup implementation |
| 2026-08-06 | New enemy brain plus icon; LevelData implementation |
| 2026-08-05 | Hover text update; enemy status completion |
| 2026-08-04 | Split Status into StatusEffect and Aura; move placement to GridManager |
