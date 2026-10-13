# Card Lab 4 handoff: Knight taunts and "alone" cards

> **Built 2026-10-12** at distance 3 (`CardConditions.AloneDistance`). Lone Stand, Solitary Guard, Last Bastion, Hermit's Smite, Distant Grace, Far Mend, Vigil and Judgement are in `Assets/Data/CardData` with the numbers below, untrimmed. Not built: Challenge, Taunting Blow, Provoke, Defiant Roar, Outrider, Withdraw, Lone Wolf, Iron Will. See Docs/CHANGELOG.md 2026-10-12.

*Written 2026-10-11 by the evening balance-bot session. Everything below was tested in the bot-build mirror only; nothing under `Assets/` was changed and nothing was committed.*

This document is for whoever builds these cards into the real game, if the user decides to. It has the card list, the exact numbers, the mechanics they need, and how to check the result. The full results are in the "Spire Card Lab IV" report artifact (https://claude.ai/artifact/5dnUcaxFRKVZUa8wQWFzRy). It builds on `Docs/CardLab3-Handoff.md`, which this lab took out of the pool and which the user plans to bring back.

## Decision for the user

The user asked for more Knight taunt cards, and for cards that reward a hero for standing apart from the other hero ("alone"). The distance was to be tested at 2, 3 and 4 tiles. Party: Knight + Cleric. Base pool: Card Lab II's L4 pool with every "next to another hero" card removed.

- **The distance made no measurable difference.** At 2, 3 and 4 tiles the condition held on 74%, 48% and 25% of plays. Runs cleared the same levels (4.44–4.56). The lone cards' average rating did not move (+0.09 / +0.11 / +0.10). The fitted value of "alone" itself is 0.00 ± 0.12 levels. The cards are worth what their unconditional half is worth.
- **If the rule ships, use distance 3:** "every other hero at least 3 tiles away", i.e. nobody within 2. That is where it is a real decision (about half the time). At 2 it is nearly always on, and at 4 nearly never.
- **A lone Knight is free; a lone Cleric is not.** The Cleric loses 2.0 HP per enemy phase next to the Knight and 3.2 at 4+ tiles. The Knight loses about 2.0 at any distance. Knight lone cards plus taunts are the coherent version of this playstyle.
- **Taunt cards work and land in the target band** (+0.10 to +0.17). The existing Taunt card rated +0.11 to +0.34 in every round of this lab, against −0.14 in the Veteran preset's ratings. Re-measure it.
- **Whole pool:** adding the 16 cards to the base pool changed levels per run by +0.81 ± 0.16 on Normal and +0.62 ± 0.12 on Hard III (Veteran drafting by this lab's ratings, 250 paired seeds). **The set as tuned makes the game clearly easier**, much more than Lab 3's cards did (those only matched Lab II's pool). The power comes from the base halves, not from "alone". Build a subset (the taunts plus the Knight lone cards), or take a point off the strongest base halves (Judgement, Vigil, Lone Stand, Last Bastion), and re-measure with step 5 below.

### Recommended: build these

| Card | Hero | Rarity | Cost | Text | Rating |
|---|---|---|--:|---|--:|
| Challenge | Knight | Uncommon | 1 | Taunt an enemy. Gain 7 Shield. | +0.16 ± 0.09 |
| Taunting Blow | Knight | Uncommon | 1 | Deal 8 damage. Taunt the target. | +0.10 ± 0.08 |
| Provoke | Knight | Uncommon | 1 | Taunt every enemy within 2. Gain 3 Block. | +0.15 ± 0.17 |
| Defiant Roar | Knight | Rare | 1 | Taunt every enemy within 2. Gain 9 Shield. Draw 1. | +0.17 ± 0.10 |
| Last Bastion | Knight | Rare | 1 | Taunt every enemy within 2. Gain 6 Shield. Alone: gain 12 and draw 1. | +0.18 ± 0.07 |
| Lone Stand | Knight | Uncommon | 1 | Gain 6 Shield. Alone: gain 12 instead. | +0.21 ± 0.12 |
| Outrider | Knight | Uncommon | 0 | Move up to 2 tiles. Then, if alone, gain 6 Shield and draw 1. | +0.15 ± 0.12 |

### Optional (rate fine, but on their unconditional half)

| Card | Hero | Rarity | Cost | Text | Rating |
|---|---|---|--:|---|--:|
| Judgement | Cleric | Rare | 1 | Deal 9 damage. Alone: costs 0 and draw 1. | +0.27 ± 0.11 |
| Vigil | Cleric | Rare | 2 | Heal 12. Draw 1. Costs 1 when alone. | +0.20 ± 0.11 |
| Hermit's Smite | Cleric | Uncommon | 1 | Deal 8 damage. Heal 3. Alone: deal 12 instead. | +0.22 ± 0.20 |
| Withdraw | Cleric | Uncommon | 0 | Move up to 2 tiles. Then, if alone, heal 2 and draw 1. | +0.21 ± 0.27 |
| Distant Grace | Cleric | Uncommon | 1 | Give 6 Shield. Alone: give 10 instead. | +0.17 ± 0.16 |

### Do not build as written

| Card | Hero | Rarity | Cost | Text | Rating |
|---|---|---|--:|---|--:|
| Solitary Guard | Knight | Uncommon | 1 | Gain 3 Block. Alone: gain 5 instead. | −0.02 ± 0.15 |
| Lone Wolf | Knight | Uncommon | 1 | Deal 9 damage. Alone: deal 13 and draw 1. | +0.06 ± 0.16 |
| Iron Will | Knight | Uncommon | 1 | Gain 8 Shield. Draw 1. Costs 0 when alone. | −0.33 ± 0.17 |
| Far Mend | Cleric | Uncommon | 1 | Heal 7. Alone: draw 1. | +0.05 ± 0.23 |

Rating noise: one 1,200-run round pins a Knight Uncommon to about ±0.17 and a Cleric Uncommon to about ±0.22. Ratings above pool every round that measured the same card (same numbers, and the same distance for lone cards). Treat differences under 0.15 as ties. Iron Will's −0.33 comes from one round. It was +0.04 to +0.09 in the three distance rounds with one point less Shield, so treat it as about 0, not as a bad card.

## Card specs (final numbers, distance 3)

Same conventions as the Lab 3 handoff. Each card is a copy of an existing card (for art, tags and animation) with its effect entries rebuilt as listed. "amountDelta" is added to the effect asset's authored number. "Alone, +N while met" is an entry bonus. "Alone, only while met" is an entry gate. "Alone, 1 less" is the card-level discount. "Radius Chebyshev 1-2" is every tile 1 or 2 steps from the caster, diagonals included, excluding the caster's own tile.

#### Challenge

*Knight · Uncommon · cost 1* — "Taunt an enemy. Gain 7 Shield."

- Copy art/tags/animation from **Taunt**; requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: template (Chebyshev 1-3); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Taunt | PlayedTile | Single | +0 | None |
  | 1 | Shield 7 | Source | Single | +0 | None |

#### Taunting Blow

*Knight · Uncommon · cost 1* — "Deal 8 damage. Taunt the target."

- Copy art/tags/animation from **Slash**; requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: template (melee 1); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Damage 9 | PlayedTile | Single | -1 | None |
  | 1 | Taunt | PlayedTile | Single | +0 | None |

#### Provoke

*Knight · Uncommon · cost 1* — "Taunt every enemy within 2. Gain 3 Block."

- Copy art/tags/animation from **Taunt**; requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: SelfTile; card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Taunt | Source | Radius Chebyshev 1-2 | +0 | None |
  | 1 | Block 2 | Source | Single | +1 | None |

#### Defiant Roar

*Knight · Rare · cost 1* — "Taunt every enemy within 2. Gain 9 Shield. Draw 1."

- Copy art/tags/animation from **Taunt**; requiredClass Knight, rarity Rare, excludeFromRewards off, upgradedForm none; range: SelfTile; card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Taunt | Source | Radius Chebyshev 1-2 | +0 | None |
  | 1 | Shield 10 | Source | Single | -1 | None |
  | 2 | Draw 1 | Source | Single | +0 | None |

#### Last Bastion

*Knight · Rare · cost 1* — "Taunt every enemy within 2. Gain 6 Shield. Alone: gain 12 and draw 1."

- Copy art/tags/animation from **Taunt**; requiredClass Knight, rarity Rare, excludeFromRewards off, upgradedForm none; range: SelfTile; card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Taunt | Source | Radius Chebyshev 1-2 | +0 | None |
  | 1 | Shield 7 | Source | Single | -1 | Alone, +6 while met |
  | 2 | Draw 1 | Source | Single | +0 | Alone, only while met |

#### Lone Stand

*Knight · Uncommon · cost 1* — "Gain 6 Shield. Alone: gain 12 instead."

- Copy art/tags/animation from **Shield**; requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: SelfTile; card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Shield 7 | Source | Single | -1 | Alone, +6 while met |

#### Outrider

*Knight · Uncommon · cost 0* — "Move up to 2 tiles. Then, if alone, gain 6 Shield and draw 1."

- Copy art/tags/animation from **Move**; requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: Chebyshev 1-2, requiresRoute; card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Move | PlayedTile | Single | +0 | None |
  | 1 | AfterMove: Shield 6 to you, draw 1 | Source | Single | +0 | Alone, checked at the destination |

#### Judgement

*Cleric · Rare · cost 1* — "Deal 9 damage. Alone: costs 0 and draw 1."

- Copy art/tags/animation from **Smite**; requiredClass Cleric, rarity Rare, excludeFromRewards off, upgradedForm none; range: template (Chebyshev 1-4); card-level discount: Alone, 1 less.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Damage 9 | PlayedTile | Single | +0 | None |
  | 1 | Draw 1 | Source | Single | +0 | Alone, only while met |

#### Vigil

*Cleric · Rare · cost 2* — "Heal 12. Draw 1. Costs 1 when alone."

- Copy art/tags/animation from **Heal Word**; requiredClass Cleric, rarity Rare, excludeFromRewards off, upgradedForm none; range: template (Chebyshev 0-4); card-level discount: Alone, 1 less.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Heal 10 | PlayedTile | Single | +2 | None |
  | 1 | Draw 1 | Source | Single | +0 | None |

#### Hermit's Smite

*Cleric · Uncommon · cost 1* — "Deal 8 damage. Heal 3. Alone: deal 12 instead."

- Copy art/tags/animation from **Smite**; requiredClass Cleric, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: template (Chebyshev 1-4); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Damage 4 | PlayedTile | Single | +4 | Alone, +4 while met |
  | 1 | Heal 3 | Source | Single | +0 | None |

#### Withdraw

*Cleric · Uncommon · cost 0* — "Move up to 2 tiles. Then, if alone, heal 2 and draw 1."

- Copy art/tags/animation from **Move**; requiredClass Cleric, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: Chebyshev 1-2, requiresRoute; card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Move | PlayedTile | Single | +0 | None |
  | 1 | AfterMove: Heal 2 to you, draw 1 | Source | Single | +0 | Alone, checked at the destination |

#### Distant Grace

*Cleric · Uncommon · cost 1* — "Give 6 Shield. Alone: give 10 instead."

- Copy art/tags/animation from **Heal Word**; requiredClass Cleric, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: template (Chebyshev 0-4); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Shield 5 | PlayedTile | Single | +1 | Alone, +4 while met |

#### Solitary Guard

*Knight · Uncommon · cost 1* — "Gain 3 Block. Alone: gain 5 instead."

- Copy art/tags/animation from **Block**; requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: SelfTile; card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Block 2 | Source | Single | +1 | Alone, +2 while met |

#### Lone Wolf

*Knight · Uncommon · cost 1* — "Deal 9 damage. Alone: deal 13 and draw 1."

- Copy art/tags/animation from **Slash**; requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: template (melee 1); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Damage 9 | PlayedTile | Single | +0 | Alone, +4 while met |
  | 1 | Draw 1 | Source | Single | +0 | Alone, only while met |

#### Iron Will

*Knight · Uncommon · cost 1* — "Gain 8 Shield. Draw 1. Costs 0 when alone."

- Copy art/tags/animation from **Shield**; requiredClass Knight, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: SelfTile; card-level discount: Alone, 1 less.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Shield 7 | Source | Single | +1 | None |
  | 1 | Draw 1 | Source | Single | +0 | None |

#### Far Mend

*Cleric · Uncommon · cost 1* — "Heal 7. Alone: draw 1."

- Copy art/tags/animation from **Heal Word**; requiredClass Cleric, rarity Uncommon, excludeFromRewards off, upgradedForm none; range: template (Chebyshev 0-4); card-level discount: None.
- Entries:

  | # | Effect asset | Aim | Area | amountDelta | Condition |
  |---|---|---|---|---|---|
  | 0 | Heal 5 | PlayedTile | Single | +2 | None |
  | 1 | Draw 1 | Source | Single | +0 | Alone, only while met |

## Mechanics to build (none of these exist in Assets yet)

Card Lab 4 ran as patches in the bot-build mirror, on top of Card Lab 3's (see `Docs/CardLab3-Handoff.md`, "Mechanics to build"). If the Lab 3 cards are built first, most of this is already there: Lab 4 adds one condition and one flag.

Source of truth: `BotRuns/claude-tools/cardlab4.py` (patch list), `cardlab4/overlay/Assets/Scripts/CardLab/LabCost.cs` and `LabAfterMoveEffect.cs`, and `lab4_isolation.cs.txt` (the card set, spliced into `CardLab4Builder.cs` by `lab4_mkbuilder.py`).

### 1. "Alone": one condition with a distance

Definition: every other hero is at least N tiles away, measured Chebyshev (a diagonal step counts as 1). N = 2 means nobody next to you. A hero counts if it is living, player-controlled and not a totem; enemies, summons and totems never break it. With no other hero alive, you are alone. `LabCost.AloneAt(self, tile, n)` is the lab's version.

- **The lab encodes N in the condition value** (`AloneBase + N`, so 12/13/14). Do not ship that. Give the real `CardCondition` enum (Lab 3 handoff §2) an `Alone` value appended at the end, and put the distance in a separate authored int on the card (`conditionDistance`, default 0). Lab 4 found no distance that changes results (see the report), so one game-wide constant of **3** is also fine. Distance 3 is where the condition holds about half the time, which makes it a real choice.
- **Everything else is Lab 3's machinery, unchanged:** the card-level discount ("costs 0 when alone"), the entry bonus ("Alone: gain 12 instead") and the entry gate ("Alone: draw 1") all go through the same `Met(holder, condition)` call. `Met` gains one case.
- **The card face must refresh** when any hero moves. Lab 3 only had to refresh the mover's hand. Here, moving away from someone also makes *them* alone, so refresh every hero's hand after any move.
- **Keyword text:** "Alone: no other hero within 2 tiles" (for N = 3). Add a Glossary keyword so the hover explains it. Players will read "alone" as "no one next to me" unless told otherwise.

### 2. After-move payoff with a condition: "Move up to 2. Then, if alone, ..."

Lab 3's `LabAfterMoveEffect` (the Fall In / Sanctuary Step payoff) gains a `condition` field. When it is set, the payoff (Shield/Heal/Block for yourself, plus the draw) only lands if the destination meets it. It is checked when the action runs after the move, so it reads where the hero actually ended up. Recipients are the mover alone; it does not reach neighbours. In the real build, give the after-move effect an optional `CardCondition`. The bot's measure must check the destination, not the origin (`MetAt(hero, destination, condition)`), for the payoff and for the draw. The lab's first cut counted the draw unconditionally; `cardlab4.py`'s `code4()` fixes that.

### 3. Area taunt: "Taunt every enemy within 2"

No new code. The existing `TauntEffect` asset (Assets/Data/EffectData/Status/Taunt.asset) on an entry aimed at Source, area Radius Chebyshev 1–2, on a card whose range is SelfTile. `TauntEffect.Audience` is Enemy, so the area filter lands it on enemies only. Checked in a turn log: after a Provoke, an enemy that had been aiming at a totem switched to the Knight, and one outside the radius kept its target. The bot already scores taunts (`BotPlanner.Measure`, `case TauntEffect`, half credit for threat moved onto the taunter).

### 4. The bot

- `BotPlanner.Measure` / `BotCardInfo.Value`: same as Lab 3 (`EntryDelta`, `EntryLive`, the after-move case), plus the destination check above.
- **Formation term:** Lab 3's `LabCost.FormationPayoff` now also counts alone conditions (`IsPositional`): a switched-on discount or bonus in hand, and the share of the draw and discard piles whose alone condition holds where the hero stands. A move changes the condition for *both* heroes, and the payoff sums over the party, so walking away from the Cleric is credited for her cards too. Measured effect: small (see the report, "The bot"). Keep the weight at 0 on the shipped Veteran preset unless a later pass shows otherwise.
- Ratings: re-run `Tools/BotRunner/Rate-Rewards.py` on an explore batch after the cards land.

### 5. Sheets

As Lab 3: `Tools/CardSheet` does not know the condition fields. Add columns, or author in the Inspector.


## How to check it worked

1. **Compile:** `powershell -ExecutionPolicy Bypass -File Tools/compile-check.ps1 -IncludeEditor`, or `unity command recompile` then `console_status` with the Editor open.
2. **Each card does what its text says.** In the live Editor (Build Mode Debug, the debug testbed), play each lone card with the heroes 2 and 3 tiles apart. Check that the bonus, the draw and the discount switch on only at 3+. Check that the cost on the card face updates for *both* heroes when either one moves. Check that Outrider and Withdraw decide at the destination. For the area taunts, the enemy intent arrows within 2 tiles should swing to the Knight.
3. **The bot sees the same thing.** A 30-run smoke batch (Knight + Cleric, Veteran): `errors.csv` empty, every new card played in `cards.csv`.
4. **Ratings:** an explore batch of at least 2,000 runs through `Tools/BotRunner/Rate-Rewards.py`, pasted into Veteran's `cardRatings`.
5. **Whole pool:** Veteran on 250 paired seeds (40000+ Normal, 41000+ Hard III), base pool vs base + new cards. The lab's result is above; a real build should land close to it.

## Where the lab lives

- `BotRuns/claude-tools/cardlab4.py` builds a variant (`python cardlab4.py Builds\BotPlayerX <distance> [iso=0]`). `cardlab4/overlay/` holds `LabCost.cs` (alone, the play log, the formation payoff), `LabAfterMoveEffect.cs` (condition) and `CardLab4Builder.cs`, which `lab4_mkbuilder.py` generates from lab 3's builder plus `lab4_isolation.cs.txt`.
- `lab4-numbers.json` holds the tune numbers. `lab4-ratings.json` (cardRatings shape) and `lab4-ratings-detail.json` hold the ratings. `lab4-Veteran.json` and `lab4-VeteranF.json` are the evaluation profiles.
- `lab4stats.py` (per-batch analysis, including the `labplay-*.txt` condition logs beside each shard log), `lab4ratings.py`, `lab4render.py`, `lab4text.py`, `lab4handoff.py`.
- Batches: `BotRuns/claude-lab4-R-I2/I3/I4-t0` (distance rounds), `claude-lab4-T-IT-t0` (tune), `claude-lab4-E-*` (evaluation). `BotRuns/` is gitignored, so these exist only on this machine.
- Bot builds: `Builds/BotPlayerI0` (base pool), `I2`, `I3`, `I4`, `IT` (tuned, distance 3).
