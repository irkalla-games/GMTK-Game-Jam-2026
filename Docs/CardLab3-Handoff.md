# Card Lab 3 handoff: Knight + Cleric adjacency cards

> **Built 2026-10-12.** Smite Together, Sanctified Ground, Laying on of Hands, Shield of Faith, Close Prayer, Guiding Smite, Benediction, Shoulder to Shoulder, Vanguard's Oath, Phalanx, Hold the Line, Covering Strike and Flanking Strike are in `Assets/Data/CardData` with the numbers below, alongside the Card Lab II cards (with this lab's retunes) and the whole L4 pool. Not built: Fall In, Guardian's Stance, Sanctuary Step (so no after-move effect exists). The mechanics landed as `CardCondition` (`Assets/Scripts/Cards/CardCondition.cs`), not `LabCost`: see CLAUDE.md "Board conditions are asked live" and Docs/CHANGELOG.md 2026-10-12. The bot's Formation term was not ported.

*Written 2026-10-11 by the overnight balance-bot session. Everything below was tested in the bot-build mirror only; nothing under `Assets/` was changed and nothing was committed.*

This document is for the session that builds these cards into the real game, if the user decides to. It is self-contained: the card list, the exact numbers, the engine and bot changes they need, and how to check the result. The full results are in the "Spire Card Lab III" report artifact (https://claude.ai/artifact/2UUSVnw5NWjLk7ud5KEK6x).

## Decision for the user

The user asked for Knight + Cleric cards that reward heroes standing next to each other, tuned to the level of the good Card Lab II cards (rating about +0.10 to +0.30). Sixteen were built and tuned over four rounds of random drafting (10,000 runs).

- **Five cards landed in the band** and one Rare sits just under it (table below). Build those first. Four are Knight cards; the Cleric's only one is Smite Together.
- **They add a playstyle at equal power, not more power.** With the bot drafting by rating, the lab 3 pool (round 3 numbers) cleared +0.55 ± 0.16 more levels per run than the current game on Normal, but Card Lab II's pool drafted with the same ratings cleared +0.62 ± 0.16; the two are level (−0.07 ± 0.16). On Hard III every arm is within noise of today. So adopting them is a design choice about the move-together playstyle, and it is not expected to change difficulty.
- **Separately, the bot drafts much better with this lab's ratings:** +0.46 ± 0.15 levels per run on Card Lab II's pool alone. `BotRuns/claude-tools/lab3-ratings.json` is in `cardRatings` shape; whoever next updates the Veteran preset should use it (re-measure after the cards land).
- **Rally the Line and Communion can stay.** They rate about 0, and removing them changed nothing measurable (+0.10 ± 0.15). They were traps in Card Lab II, but not with these cards beside them.
- **What works:** cheap shared defence (Shield or Block for you and allies next to you), a free 2-tile move that shields on arrival, and "draw 1". **What doesn't:** bigger damage next to a hero (Flanking Strike, Covering Strike) and nearly every Cleric shield card. Random drafting keeps rating extra damage and extra shield near zero; draw and lower cost are what move a card.
- **Teaching the bot to move together is optional.** It made the bot play 75% of its cards next to the other hero (52% without it) but did not clearly change results: +0.14 ± 0.14 on Normal, −0.10 ± 0.10 on Hard III. The game does not need it, but if these cards ship, the bot's Formation term is how the balance bot will keep using them sensibly.

### Recommended: build these

| Card | Hero | Rarity | Cost | Text | Rating |
|---|---|---|--:|---|--:|
| Fall In | Knight | Uncommon | 0 | Move up to 2 tiles. Then you and heroes next to you gain 4 Shield. Draw 1. | +0.18 ± 0.08 |
| Smite Together | Cleric | Uncommon | 1 | Deal 7 damage. Heal 3. Next to another hero: deal 10 instead. | +0.17 ± 0.10 |
| Shoulder to Shoulder | Knight | Uncommon | 1 | You and allies next to you gain 8 Shield. | +0.12 ± 0.06 |
| Phalanx | Knight | Uncommon | 1 | You and allies next to you gain 2 Block. | +0.11 ± 0.05 |
| Vanguard's Oath | Knight | Rare | 1 | Deal 11 damage. Draw 1. Costs 0 next to another hero. | +0.11 ± 0.07 |
| Hold the Line | Knight | Rare | 2 | You and allies next to you gain 12 Shield. Draw 1. | +0.06 ± 0.04 |

### Optional (in the pool, measured, not clearly worth it)

| Card | Hero | Rarity | Cost | Text | Rating |
|---|---|---|--:|---|--:|
| Guardian's Stance | Knight | Uncommon | 1 | Gain 3 Block. Next to another hero: gain 6 instead. | +0.05 ± 0.08 |
| Sanctuary Step | Cleric | Uncommon | 0 | Move up to 2 tiles. Then you and heroes next to you heal 3. Draw 1. | +0.05 ± 0.13 |
| Sanctified Ground | Cleric | Rare | 1 | You and allies next to you gain 5 Shield and heal 5. | +0.03 ± 0.08 |
| Laying On of Hands | Cleric | Uncommon | 1 | Heal 7 to you and allies next to you. | 0.00 ± 0.14 |
| Shield of Faith | Cleric | Uncommon | 1 | Give 5 Shield. Next to another hero: give 9 and draw 1. | −0.01 ± 0.12 |
| Rally the Line | Knight | Rare | 2 | Give 6 Shield to you and allies within 2. Draw 1. Costs 1 next to another hero. | −0.03 ± 0.03 |
| Communion | Cleric | Rare | 2 | Heal 5 to you and allies within 2. Draw 1. Costs 1 next to another hero. | 0.00 ± 0.04 |

### Do not build as written

| Card | Hero | Rarity | Cost | Text | Rating |
|---|---|---|--:|---|--:|
| Covering Strike | Knight | Uncommon | 1 | Deal 9 damage. Allies next to you gain 5 Shield. | −0.04 ± 0.06 |
| Flanking Strike | Knight | Uncommon | 1 | Deal 7 damage and gain 4 Shield. Next to another hero: deal 11 instead. | −0.06 ± 0.12 |
| Close Prayer | Cleric | Uncommon | 1 | Heal 7. Next to another hero: draw 1. | −0.09 ± 0.12 |
| Guiding Smite | Cleric | Uncommon | 1 | Deal 7 damage. You and allies next to you heal 4. | −0.10 ± 0.12 |
| Benediction | Cleric | Uncommon | 1 | Heal 4 to an ally and every ally next to them. Draw 1. | −0.15 ± 0.20 |

**Card Lab II cards retuned here** (lab 2's L4 pool; build these numbers only if those cards are built too): Totem Strike 9 → 12 damage (+0.08 ± 0.08), Brothers in Arms 7 → 8 Shield (+0.01 ± 0.06), Shrine Blessing 6 → 9 Shield (+0.03 ± 0.12), Blessed Shield 8 → 10 Shield (−0.13 ± 0.10, still weak). Cleric shield cards rate poorly whatever their number.

**Rating noise.** A single 2,500-run round pins a Knight Uncommon to about ±0.10 and a Cleric Uncommon to about ±0.16. The ratings above pool every round with the same text. Treat differences under 0.15 as ties.

## Card specs (final numbers)

Each card is a copy of an existing card (for its art, tags and animation) with its effect entries rebuilt as listed. "amountDelta" is added to the effect asset's authored number (Shield 7 with +1 gives 8). "Radius Chebyshev 0-1" is the 3×3 around the aim tile including it; "1-1" excludes it. Conditions are explained under Mechanics.

#### Fall In

*Knight · Uncommon · cost 0* — "Move up to 2 tiles. Then you and heroes next to you gain 4 Shield. Draw 1."

- Copy art/tags/animation from **Move**; set requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: Chebyshev 1-2, requiresRoute; card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Move | PlayedTile | Single | +0 | None |
  | 1 | AfterMove: Shield 4 to you + heroes next to you, draw 1 | Source | Single | +0 | None |

#### Smite Together

*Cleric · Uncommon · cost 1* — "Deal 7 damage. Heal 3. Next to another hero: deal 10 instead."

- Copy art/tags/animation from **Smite**; set requiredClass Cleric, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: template (Chebyshev 1-4); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Damage 4 | PlayedTile | Single | +3 | NextToHero, +3 while met |
  | 1 | Heal 3 | Source | Single | +0 | None |

#### Shoulder to Shoulder

*Knight · Uncommon · cost 1* — "You and allies next to you gain 8 Shield."

- Copy art/tags/animation from **Shield**; set requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: SelfTile (0); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Shield 7 | Source | Radius Chebyshev 0-1 | +1 | None |

#### Phalanx

*Knight · Uncommon · cost 1* — "You and allies next to you gain 2 Block."

- Copy art/tags/animation from **Block**; set requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: SelfTile (0); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Block 2 | Source | Radius Chebyshev 0-1 | +0 | None |

#### Vanguard's Oath

*Knight · Rare · cost 1* — "Deal 11 damage. Draw 1. Costs 0 next to another hero."

- Copy art/tags/animation from **Slash**; set requiredClass Knight, rarity Rare, excludeFromRewards off, upgradedForm none; range: template (melee 1); card-level discount: NextToHero, 1 less.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Damage 9 | PlayedTile | Single | +2 | None |
  | 1 | Draw 1 | Source | Single | +0 | None |

#### Hold the Line

*Knight · Rare · cost 2* — "You and allies next to you gain 12 Shield. Draw 1."

- Copy art/tags/animation from **Shield**; set requiredClass Knight, rarity Rare, excludeFromRewards off, upgradedForm none; range: SelfTile (0); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Shield 10 | Source | Radius Chebyshev 0-1 | +2 | None |
  | 1 | Draw 1 | Source | Single | +0 | None |

#### Guardian's Stance

*Knight · Uncommon · cost 1* — "Gain 3 Block. Next to another hero: gain 6 instead."

- Copy art/tags/animation from **Block**; set requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: SelfTile (0); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Block 2 | Source | Single | +1 | NextToHero, +3 while met |

#### Sanctuary Step

*Cleric · Uncommon · cost 0* — "Move up to 2 tiles. Then you and heroes next to you heal 3. Draw 1."

- Copy art/tags/animation from **Move**; set requiredClass Cleric, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: Chebyshev 1-2, requiresRoute; card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Move | PlayedTile | Single | +0 | None |
  | 1 | AfterMove: Heal 3 to you + heroes next to you, draw 1 | Source | Single | +0 | None |

#### Sanctified Ground

*Cleric · Rare · cost 1* — "You and allies next to you gain 5 Shield and heal 5."

- Copy art/tags/animation from **Heal Word**; set requiredClass Cleric, rarity Rare, excludeFromRewards off, upgradedForm none; range: SelfTile (0); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Shield 7 | Source | Radius Chebyshev 0-1 | -2 | None |
  | 1 | Heal 5 | Source | Radius Chebyshev 0-1 | +0 | None |

#### Laying On of Hands

*Cleric · Uncommon · cost 1* — "Heal 7 to you and allies next to you."

- Copy art/tags/animation from **Heal Word**; set requiredClass Cleric, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: SelfTile (0); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Heal 5 | Source | Radius Chebyshev 0-1 | +2 | None |

#### Shield of Faith

*Cleric · Uncommon · cost 1* — "Give 5 Shield. Next to another hero: give 9 and draw 1."

- Copy art/tags/animation from **Heal Word**; set requiredClass Cleric, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: template (Chebyshev 0-4); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Shield 5 | PlayedTile | Single | +0 | NextToHero, +4 while met |
  | 1 | Draw 1 | Source | Single | +0 | NextToHero, only while met |

#### Rally the Line

*Knight · Rare · cost 2* — "Give 6 Shield to you and allies within 2. Draw 1. Costs 1 next to another hero."

- Copy art/tags/animation from **Guardian's Aegis**; set requiredClass Knight, rarity Rare, excludeFromRewards off, upgradedForm none; range: template (SelfTile); card-level discount: NextToHero, 1 less.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Shield 5 | Source | Radius Manhattan 0-2 (template) | +1 | None |
  | 1 | Draw 1 | Source | Single | +0 | None |

#### Communion

*Cleric · Rare · cost 2* — "Heal 5 to you and allies within 2. Draw 1. Costs 1 next to another hero."

- Copy art/tags/animation from **Guardian's Aegis**; set requiredClass Cleric, rarity Rare, excludeFromRewards off, upgradedForm none; range: template (SelfTile); card-level discount: NextToHero, 1 less.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Heal 5 | Source | Radius Manhattan 0-2 (template) | +0 | None |
  | 1 | Draw 1 | Source | Single | +0 | None |

## Mechanics to build (none of these exist in Assets yet)

The lab ran everything as patches in the bot-build mirror. Source of truth for each piece:
`BotRuns/claude-tools/cardlab3.py` (the patches, as exact before/after strings), `cardlab3/overlay/Assets/Scripts/CardLab/`
(`LabCost.cs`, `LabAfterMoveEffect.cs`) and `cardlab3/overlay/Assets/Editor/CardLab/CardLab3Builder.cs` (how each card is
put together). Port them properly rather than copying the patches: the lab took shortcuts listed under each item.

### 1. "Next to another hero"

One definition everywhere: a living, player-controlled, non-totem character other than you on one of the 8 tiles around
you (diagonals count; Chebyshev distance 1). `LabCost.HeroesNextTo(self, tile)` is the lab's version. Summoned allies
count; totems do not.

### 2. Card-level discount — "Costs N less next to another hero"

Lab: `CardData.labCondition` + `labDiscount`; `Card.cost` became a property `max(0, baseCost - discount)` asked live, with
`Card.Holder` stamped in `Character.NewCard` and `CostModifier` writing `baseCost`.

Real build needs:
- A home for the rule. It is per-card authoring (CardData), but the answer depends on the holder's position, so it is
  computed on `Card` against its holder. Do not write the discounted value into a field: the hand, `CanAfford`,
  `SpendEnergy`, the card face and the bot all have to see the same number at the moment they look.
- **The card face must refresh.** `CardViewer.Setup` writes `cost.text` once. A hero moving next to (or away from) an
  ally has to re-run that for every card in the active hand, e.g. from `Character.Moved`/`StatsChanged` or the hand
  viewer's existing refresh hooks. Show the discounted cost in a different colour so the player can see the rule fire.
- Enum values are written into assets: add a `CardCondition` enum with `None = 0` first and append later ones.

### 3. Entry-level condition — "Next to another hero: deal 12 instead" / "...: draw 1"

Lab: `CardEffectEntry.labCondition` + `labBonus`.
- **Bonus** (`labBonus != 0`): the entry's number gets `+labBonus` while the caster meets the condition. Every place
  that builds an `ActionContext` from `entry.amountDelta` (Card.ResolveEffects, PreviewDamage, OutgoingDamage,
  OutgoingRiders, BotPlanner.Measure) reads `LabCost.EntryDelta(entry, source)` instead.
- **Gate** (`labBonus == 0`): the entry only resolves while the condition holds. `Card.ResolveEffects` and
  `BotPlanner.Measure` **skip** a gated entry whose condition fails (`LabCost.EntryLive`); `Card.Landed` and
  `Card.EntryFootprint` also return nothing for it so previews agree. Skipping is the part that matters: Draw, Energy,
  Discard and Self Damage resolve on `ctx.source` and ignore their targets, so emptying the landed tiles alone does
  not stop them. The lab shipped that mistake for two rounds ("next to another hero: draw 1" drew every time) and the
  bot agreed with the game, so nothing looked wrong until a hand-size check.
- Both fields are appended to the `CardEffectEntry` struct, so every existing asset deserialises them as 0 = no
  condition. Keep that property.
- Gate only Source-aimed or ActsOnSource entries (Draw). A gated entry aimed at the played tile would still claim
  tiles in `Card.Refusal`; the lab never authored one, and the real build should either refuse to author it or teach
  `Refusal` to skip gated entries.
- Card text is authored, so the description must spell out both numbers ("Deal 8 damage. Next to another hero: deal
  12 instead."). The Glossary could get a "Formation"/"Adjacent" keyword for the hover tooltip.

### 4. Shared entries — "You and allies next to you gain 6 Shield"

No new code. An entry aimed at Source with a Radius area: Chebyshev, min 0 (include yourself) or 1 (neighbours only),
max 1. The existing area machinery (Guardian's Aegis uses the same with Manhattan 2) filters by the effect's own
refusal, so shields and heals only land on allies. Note "allies" includes allied totems and summons; the card text says
"allies" for that reason.

Benediction is the same shape aimed at the played tile ("an ally and every ally next to them").

### 5. After-move payoff — "Move up to 2 tiles. Then you and heroes next to you gain 5 Shield"

Lab: `LabAfterMoveEffect` (kind Shield/Heal/Block, amount, includeSelf) as entry 2 behind a Move entry, on a card with
range Chebyshev 1–2, requiresRoute. Its `Resolve` only queues `LabAfterMoveAction`; the action runs after the
`MoveAction` finishes (the ActionManager queue is FIFO) and reads `source.Tile` then, so it reaches whoever is next to
the destination. It refuses every tile so `Card.Refusal` never lets it claim one (an effect that refuses nothing would
make the caster's own tile playable); `ActsOnSource => true`, `SupportsArea => false`.

Real build: keep it a `CardEffect` subclass + `GameAction`, give it a proper name (e.g. `RallyEffect`), and reuse
ShieldAction/HealAction/BlockAction inside as the lab does. Needs a `CreateAssetMenu` entry, and `CardRulesText`
should not describe it as an area.

### 6. The bot (required, see CLAUDE.md "Balance bot")

- `BotPlanner.Measure`: a case for the after-move effect, adding Shield/Heal/Charges ops for
  `Recipients(hero, destinationTile)` (the hero itself plus heroes next to the destination, the hero excluded from
  the neighbour search because it still stands at its origin when measured).
- `BotPlanner.Measure` and `BotCardInfo.Value`: read the conditional delta (`EntryDelta`) instead of `amountDelta`.
- `BotCardInfo.Build`/`Value`: classify and value the after-move effect (Defense or Heal; 1.5x its amount).
- **Formation term** (the bot "taught to move next to its allies"): new `BotWeights.formation` and
  `BotFeatures.formation`; a `Formation` pass before `MoveLookahead` that, for every move candidate, steps the hero to
  the destination, computes `LabCost.FormationPayoff(model.Heroes)` (switched-on discounts at 2.5 points per energy,
  conditional bonuses and gated entries at their point value, shared entries times the heroes they would reach, plus
  7.5 x the share of each standing-together hero's draw and discard piles that wants company), steps back, and adds
  after - before. Same relocate-and-restore as `Forecast`; it must stay side-effect free.
- Profile: `formation` weight on the Veteran preset (value from the lab, see results). Every new field defaults to 0
  so old jobs and profiles play exactly as before.
- Run `Tools/BotRunner/Rate-Rewards.py` again after the cards land and paste the result into Veteran's `cardRatings`.

### 7. Sheets

`Tools/CardSheet` round-trips CardData. It does not know the new fields (card-level condition/discount, entry
condition/bonus, the after-move effect). Add them as columns, or author these cards in the Inspector and expect the
sheet to show them without those columns. Check `$script:StatusNames`-style enum tables in `CardSheet.Common.psm1` if
a new enum is added.


## How to check it worked

1. **Compile:** `powershell -ExecutionPolicy Bypass -File Tools/compile-check.ps1 -IncludeEditor`, or `unity command recompile` then `console_status` with the Editor open.
2. **Each card does what its text says.** In the live Editor (Build Mode Debug, the debug testbed), play each card with the two heroes adjacent and apart. Check: the conditional number changes; "draw 1 next to a hero" draws only when adjacent (hand size); the cost on the card face changes as soon as a hero steps next to or away from the other one; the after-move payoff lands on whoever is next to the *destination*.
3. **The bot sees the same thing.** Run a 30-run smoke batch (`Tools/Bot/Bot Runner`, Knight + Cleric, Veteran) and check `errors.csv` is empty and every new card shows up in `cards.csv` as played. In `turns.log` a conditional card's score breakdown should differ between adjacent and apart plays.
4. **Ratings:** an explore batch (a profile with `rewards.exploreRewards = 1`, at least 2,000 runs — fewer and a Cleric Uncommon's rating is ±0.2) through `Tools/BotRunner/Rate-Rewards.py`, pasted into Veteran's `cardRatings`.
5. **Whole pool:** Veteran on 250 paired seeds, current game vs new pool, Normal and Hard III. The lab measured the round-3 pool level with Card Lab II's pool when both draft with the same ratings (−0.07 ± 0.16), both about +0.6 over the current game on Normal; a real build should not move difficulty much either way.

## Where the lab lives

- `BotRuns/claude-tools/cardlab3.py` — every engine and bot patch, as exact before/after strings (`CODE3`; `cardlab2.CODE` for the card-level discount).
- `BotRuns/claude-tools/cardlab3/overlay/Assets/Scripts/CardLab/` — `LabCost.cs` (adjacency, conditions, the Formation payoff), `LabAfterMoveEffect.cs`.
- `BotRuns/claude-tools/cardlab3/overlay/Assets/Editor/CardLab/CardLab3Builder.cs` — how each card was authored (`Adjacency()`), with every number read from config.
- `BotRuns/claude-tools/lab3-numbers.json` — each round's numbers; `lab3cards.py` / `lab3spec.py` turn them into text and specs.
- `BotRuns/claude-tools/lab3-ratings.json` — the pooled ratings, in `cardRatings` shape.
- Batches: `BotRuns/claude-lab3-*` (R1–R4 rating rounds, E-* evaluation arms). `BotRuns/` is gitignored, so these exist only on this machine.
- Bot builds: `Builds/BotPlayerL5`–`L8`, `L7x`.
