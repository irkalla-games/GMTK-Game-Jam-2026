# Balance bot: strategy and findings (Oct 10 2026, overnight pass)

8,567 bot runs of Knight + Cleric on RealRun - Random, every difficulty tier, on the Oct 10 enemy changes
(uncommitted at the time): Enemy Ranger 25 → 10 health, Flying Eye 15 → 10, Light Bandit 30 → 15, Skull 15 → 10, Heavy
Bandit 3.5 → 2.7 power, Mimic 2.5 → 2.0, Skeleton Warrior 3.0 → 2.5. The full write-up with charts and tables is the
"Spire Balance Lab IV" artifact (https://claude.ai/artifact/8FgfwzKoHavBnmx19gM8on). The previous pass is in `Docs/archive/BotStrategy-2026-10-09-pm.md`. Raw data
lives in `BotRuns/claude-q*` (gitignored), the scripts in `BotRuns/claude-tools` (`q-measure.py`, `q-chain*.py`).

## Headline

Veteran bot, average levels cleared out of 10. Oct 9 = last night's runs on the same seeds.

| Tier | Oct 9 | Oct 10 | Same seeds |
|---|--:|--:|--:|
| Normal | 5.99 | 5.28 | −0.64 ± 0.19 |
| Hard I | 5.30 | 4.09 | −1.06 ± 0.31 |
| Hard II | 4.95 | 3.52 | −1.38 ± 0.32 |
| Hard III | | 3.60 | |
| Hard IV | | 3.16 | |
| Hard V | | 3.26 | |
| Hard VI | | 2.98 | |

## Game findings

1. **The Mimic reprice made the game harder.** At 2.0 power the roller buys twice as many Mimics (4.7 → 9.7 per run on
   Normal) and Mimic hero falls per run doubled. Mimic back at 2.5, everything else kept: +0.78 ± 0.16 levels per run
   (Normal +0.59 ± 0.20, Hard I +1.08 ± 0.29, Hard III +1.07 ± 0.28, Hard VI +1.13 ± 0.28). Reverting Heavy Bandit and
   Skeleton Warrior as well adds nothing (+0.81 ± 0.17), so their new prices are fine.
2. **The health cuts worked.** Light Bandit hero falls per run 0.63 → 0.42; Skull, Flying Eye, Ranger and Light Bandit
   are all killed more often.
3. **Hard tiers have a level 1 wall.** From Hard I up, level 1 loses 22-30% of runs (Normal: 1%). Every one of those
   losses had a Skull in the opening encounter, usually with a Mimic (Vulnerable from Bone Chill, then Chomp). Mimic at
   2.5 takes Hard III's level 1 from 80% to 95% cleared.
4. **Hard III-VI barely differ** (3.60 → 2.98): extra power and health re-hit the same level 1 and level 5-6 walls.
5. **Totems die the round they land.** 54% of totems are gone before their first round ends; the average
   stands 2.5 rounds. Every 1-HP totem at 5 HP: +0.16 ± 0.12 levels.
6. **Knight + Cleric is energy-bound.** 0.11 (Knight) and 0.15 (Cleric) energy unspent per round,
   while encounters grow from 13 bodies on level 1 to 55 on level 10. Any added card competes with the one it displaces.

## Card lab

Seven Knight/Cleric ideas built only in the bot build mirror (`BotRuns/claude-tools/cardlab/`, never in `Assets`), each
added to its hero's starter deck and measured on 250 paired seeds (Normal + Hard III), against the plain starter.

| Test | Levels per run |
|---|--:|
| Knight + Retribution Totem (allies with Block return 4 damage to attackers; new code) | +0.11 ± 0.17 |
| Knight + 2 Shield Bash (deal 5, gain 4 Shield and 1 Block) | +0.06 ± 0.15 |
| Knight + 2 Shield Slam (deal 3 plus your Shield; new code) | +0.06 ± 0.16 |
| Cleric + 2 Sanctified Shield (give 4 Shield and 1 Block) | −0.03 ± 0.13 |
| Cleric + Conviction Totem (heal nearby → Strength 1) | −0.12 ± 0.14 |
| Cleric + Ward Totem (shield nearby → Block 1) | −0.18 ± 0.14 |
| Knight + Vanguard Totem (attack nearby → Shield 3) | −0.25 ± 0.15 |
| *Existing:* Knight + 2 Attack and Block | +0.08 ± 0.14 |
| *Existing:* Knight + Fury Totem | −0.12 ± 0.14 |
| *Existing:* Cleric + Tower Totem | −0.17 ± 0.14 |
| *Existing:* Cleric + Wellhead Totem | −0.20 ± 0.14 |
| Combo: Retribution + Ward | +0.01 ± 0.13 |
| Combo: 2 Shield Slam + Wellhead | −0.20 ± 0.15 |
| Combo: 2 Shield Bash + Tower | −0.35 ± 0.14 |
| Combo: Vanguard + Wellhead | −0.36 ± 0.14 |
| Every 1-HP totem at 5 HP | +0.16 ± 0.12 |
| ... plus Retribution + Ward | +0.16 ± 0.15 |
| ... plus Vanguard + Wellhead | +0.06 ± 0.15 |
| Mimic 2.5 and 5-HP totems | +0.95 ± 0.15 |

No single card moves the needle, including the best existing ones. Retribution and Shield Bash are the keepers if new
content is wanted; no combo beats its parts, with or without tough totems. Late-game scaling probably needs more energy
per round (an energy totem, cheaper plays near totems), which is untested.

## Card lab 2 (Oct 10 evening): a stronger Knight/Cleric reward pool

Goal: starter decks stay below the curve, every draftable card beats them, combo cards cost 0 while a condition holds
(next to a totem / another hero, holding Shield / Block) and draw, Rare totems at 10 health, decks near 25 cards at the end.
Built in the bot mirror only (`BotRuns/claude-tools/cardlab2/`, `cardlab2.py`); nothing in `Assets`. Full write-up:
"Spire Card Lab II" artifact (https://claude.ai/artifact/ToofrpXijjszfh1WrCmLmM). Veteran, 250 paired seeds per tier, levels per run vs the current game:

| Arm | Normal | Hard III |
|---|--:|--:|
| L1: new pool as designed (starter-level cards out of rewards) | −0.70 ± 0.13 | −0.46 ± 0.10 |
| L1 drafting to 25 | −0.70 ± 0.14 | −0.48 ± 0.10 |
| L1, picky drafter (cards must be worth 9) | −0.46 ± 0.14 | |
| L2: lab cards added, nothing removed | −0.04 ± 0.15 | −0.25 ± 0.09 |
| L3: L1 + Uncommons in clear rewards (40/50/10), Shield Wall and Heavy Slash out, Spring Totem, 4 free combos | +0.04 ± 0.14 | −0.11 ± 0.09 |
| L4: L3 without Rally the Line and Communion | +0.57 ± 0.14 | +0.12 ± 0.09 |
| L4 on fresh seeds 17000+ (L4 was chosen after seeing 15000+) | +0.06 ± 0.14 | |
| L4 on fresh seeds 18000+ | +0.35 ± 0.15 | |
| **L4 pooled over all three Normal seed sets (750 paired runs)** | **+0.33 ± 0.08** | |
| Rare totems at 10 HP alone (random drafter, 300 paired) | −0.06 ± 0.06 | |

- Today the Knight's best rewards are skips: Heal 5 (4.6 per run) and Upgrade Slash (3.5). The bot takes those only when no
  card is worth 5, so any stronger pool crowds them out. A new card has to beat an upgrade, not just a starter card.
- Level clears roll Rare 80 / Legendary 20 and only widen below 5 cards; the Cleric has 6 Rares, so it never saw an Uncommon.
- Rally the Line and Communion rate positive alone but are traps: taken every time, the deck is built around them.
  Without them the Cleric's best card is Chapel Totem (healing nearby gives 1 energy), and the Knight chains Iron Stance
  into a free Bulwark Stance (95% of its plays discounted).
- Energy-bound: unspent energy stays near 0.25 per round, so "draw 1" adds little and "costs 0" is the real lever.
- Veteran never drafts a Rare totem in the current game (0 of 250 runs), so content changes to them are invisible to it.

## Card lab 3 (Oct 11 overnight): Knight + Cleric adjacency cards

Sixteen cards that pay off when the heroes stand next to each other (bigger next to a hero, shared with allies next to
you, a move that pays off on arrival, cheaper next to a hero), added to L4 with Rally the Line and Communion back in, tuned
over four rounds of random drafting (10,000 runs). Mirror only (`BotRuns/claude-tools/cardlab3/`, `cardlab3.py`); nothing
in `Assets`. Full write-up: "Spire Card Lab III" artifact (https://claude.ai/artifact/2UUSVnw5NWjLk7ud5KEK6x). Build plan:
`Docs/CardLab3-Handoff.md`.

| Veteran, 250 paired seeds, levels per run vs the current game | Normal | Hard III |
|---|--:|--:|
| L4, lab-2 ratings (300 runs) | +0.16 ± 0.14 | 0.00 ± 0.11 |
| **L4, lab-3 ratings (10,000 runs)** | **+0.62 ± 0.16** | |
| L7 (lab 3 pool), plain bot | +0.55 ± 0.16 | +0.13 ± 0.12 |
| L7, bot taught to move together (Formation 1) | +0.69 ± 0.16 | +0.03 ± 0.12 |
| L7 without Rally the Line and Communion | +0.65 ± 0.17 | +0.12 ± 0.11 |

- In the +0.10 to +0.30 band: Fall In (cost 0), Smite Together, Shoulder to Shoulder, Phalanx, Vanguard's Oath; Hold the
  Line just under. Extra damage next to a hero and Cleric shields stayed at or below 0 through every buff.
- The cards add a playstyle at equal power: L7 vs L4 with the same ratings, −0.07 ± 0.16.
- **Ratings matter more than content here**: the same L4 pool drafts +0.46 ± 0.15 better with 10,000-run ratings than
  with 300-run ones. `BotRuns/claude-tools/lab3-ratings.json` is in `cardRatings` shape. One 2,500-run round pins a Knight
  Uncommon to about ±0.10 and a Cleric Uncommon to about ±0.16.
- Rally the Line and Communion are no longer traps (removing them: +0.10 ± 0.15).
- Gotcha found: gating a Draw entry by emptying its targets does nothing, because Draw resolves on `ctx.source`. Skip
  the entry instead (in `ResolveEffects` and `BotPlanner.Measure`).

## Spire Lab 08 (Oct 12 overnight): the 47 lab cards, built into the game

How strong are the 47 Knight/Cleric cards from Card Labs II–IV now that they (and the L4 pool) are real content, how
often does the bot win, and which enemies do the damage? Measure only, 6,000 runs. Report: "Spire Lab 08" artifact
(https://claude.ai/artifact/7Tuv8GaMZif3nth1iTYtvG). No handoff: nothing was built in the mirror.

| Veteran08 (Veteran + this lab's r1 ratings), paired seeds | Normal | Hard III |
|---|--:|--:|
| With the 47 cards: levels cleared / win rate | 6.07 ± 0.08 / 7.3% | 4.04 ± 0.14 / 0.7% |
| Without them (same tree, excludeFromRewards) | 5.72 ± 0.07 / 1.5% | 3.88 ± 0.14 / 0% |
| **What the set adds** | **+0.34 ± 0.09** | **+0.15 ± 0.08** |

- The cards sit close together: two 2,500/1,700-run rating rounds correlate at only 0.17 across them, with honest
  error bars, so the real spread is about ±0.07 levels per card. Consistent in both rounds: Shield Bash, Sanctified
  Guard, Smite Together, Blessed Shield, Hermit's Smite, Last Bastion on top; Far Mend and Shield of Faith at the
  bottom. Pooled ratings in `cardRatings` shape: `Tools/BotRunner/lab/labs/08-card-eval/lab8-ratings.json`.
- In an 86-card pool, cards rate lower than in their own smaller labs (Totem Guard +0.33 → −0.03, Communion +0.29 →
  −0.06). Per-card lab ratings do not carry over to a much larger pool.
- Enemies: Light Bandit lands 28% of run-ending knockdowns on Normal; Mimic (power 2.0 after the uncommitted cut),
  Light Bandit and Enemy Ranger do the most damage per power point. Hard III loses 22% of runs on level 1 (Skull).
- The Knight gets about five card pickups for every one the Cleric gets, so Cleric cards need about 2.5× the runs
  for the same precision.

## What the bot learned

- **Focus 5 now pays**: +0.54 ± 0.12 levels (Normal +0.45 ± 0.14, Hard III +0.82 ± 0.24); on held-out seeds
  +0.41 ± 0.20. Last night it measured +0.11; the health cuts made the backline worth focusing. Hunt on top:
  +0.60 ± 0.14. Kill weight 8: +0.03 ± 0.11.
- The card lab runs used a mirror-only patch so the bot never removes a card under test; `Card.PreviewDamage` asks a
  virtual `DamageEffect.DamageFor(source)` there, so Shield Slam's real number reaches the planner.

## Re-running

```
powershell -ExecutionPolicy Bypass -File Tools/BotRunner/Build-BotPlayer.ps1
cd BotRuns/claude-tools
python cardlab.py          # the card lab player, built in the mirror only
python q-measure.py        # the tier sweep
```
