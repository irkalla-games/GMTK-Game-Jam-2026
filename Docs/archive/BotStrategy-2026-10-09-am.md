# Balance bot: strategy and findings (Oct 9 2026 pass)

11,820 bot runs of RealRun - Random at difficulty tier 0, across all six two-hero parties, after the
Oct 9 card, equipment and level changes (Veil Totem allowed). The full write-up, with charts and
sortable tables of every card, item and enemy, is the "Spire Balance Lab" artifact; this file is the
short version for the repo. The previous pass is in `Docs/archive/BotStrategy-2026-10-08.md`. Raw data
lives in `BotRuns/claude-n0*` (gitignored), the scripts in `BotRuns/claude-tools`.

## Headline

Average levels cleared out of 10, 200 matched seeds per party (seed 7000+). Old = the Tuned profile
with last night's ratings; new = Foresight with tonight's ratings.

| Party | Old | New | Gain (± 1 se) | Level 3 clear | Level 5 clear |
|---|--:|--:|--:|--:|--:|
| Knight + Cleric | 3.66 | **4.18** | +0.52 ± 0.15 | 82% | 50% |
| Cleric + Rogue | 3.00 | **3.92** | +0.92 ± 0.18 | 76% | 65% |
| Knight + Rogue | 2.58 | **2.76** | +0.18 ± 0.08 | 46% | 31% |
| Cleric + Mage | 2.14 | **2.56** | +0.42 ± 0.14 | 55% | 44% |
| Knight + Mage | 2.22 | **2.55** | +0.33 ± 0.08 | 38% | 26% |
| Mage + Rogue | 0.54 | **0.91** | +0.38 ± 0.10 | 42% | 25% |

Pooled over all 1,200 seeds: Foresight's play is worth **+0.34 ± 0.05** levels, and tonight's reward
ratings another **+0.11 ± 0.05** (almost all of it Cleric + Rogue, +0.70: last night's ratings still
rated Veil Totem the best reward in the game). 4 full clears in 1,200 runs (3 Knight + Cleric, 1
Cleric + Rogue).

## What the bot learned

Health is restored between levels (`carryDamageBetweenLevels` is off) and a fall costs a third of max
health for the rest of the run, so damage that never drops anyone is free. Three new planner features,
all in `BotPlanner`/`BotBoardModel`, all off by default so old profiles play exactly as before:

- **Triage** (`mitigationNeed`, `needCap`, `needFloor`): a point blocked is worth what the ally needs it
  - this round's incoming as if it repeated every round left, over their health.
- **Forecast** (`forecast`, `forecastMoves`): before a move, step the hero there and ask every enemy's
  own brain where it would now hit (`BattleManager.ForecastPlan`, the same `DecidePlan` the intent row
  uses), then step back. Replaces Move Safety and Exposure.
- **Count poison** (`countPoison`): Poison a hero carries bites at the end of their own phase, before
  enemies act, through Shield and Block; an enemy's poison is part of its threat. Without it a Light
  Bandit reads as the 1 damage its Venom Cut previews.

Each alone measured within noise of Tuned (forecast +0.06, poison +0.04, triage -0.04, 360 seeds each).
Together: +0.29. Tuning settled on Mitigation 0.9 / Need 1.0 (+0.08 over 0.5 / 1.4). More kill pressure,
a heavier forecast and a lower need cap were all noise; Adaptive is still -0.04 against Tuned.

The **Foresight** profile (`Assets/Data/Bots/Foresight.asset`, `BotPresets.Foresight`) is the new Bot
Runner default. Tonight's ratings (`BotRuns/claude-tools/ratings-v3.json`) are on Foresight, Tuned and
Adaptive.

## Game findings

1. **The Light Bandit is the run-killer.** 1.5 power (same as a Goblin) for 12 damage per body against
   the Goblin's 2.3 - five times the median damage per power point. 31% of all hero falls; only 3% are
   ever killed. Both its cards splash everything within 1 tile at range 4: Venom Cut adds 3 unblockable
   Poison per hero, Quick Cut adds Weaken. It keeps 2-4 tiles away with 30 health; the Knight has no
   reach past 1 tile and the party's best ranged damage is 4-5 per card.
2. **The level 3/5 cap of 2 power per enemy is why there are so many.** It keeps out the Enemy Ranger
   (2.88), Skeleton Warrior (2.59), Heavy Bandit and Golem (2.5), so the budget buys cheap bodies, and
   the strongest cheap body is the Light Bandit. Pricing the bandit above 2 would also remove it from
   those levels entirely.
3. **Weaken is a flat -3 per hit.** Quick Attack (3) does 0, Smite (4) does 1, Firebolt (5) does 2. One
   Quick Cut can take a Rogue or Mage out of the fight.
4. **Only the Cleric answers poison** (Cleansing Light, Purge, heals). Every party without one stalls at
   level 3.
5. **The Mage's 10 health** still loses Mage + Rogue 63% of its runs on level 1 (Flying Eyes).
6. **Evil Wizard 3** (Meteor Fall, 12 at range 5): level 5 cleared 20% of the time with it, against
   66-71% with the Black or Evil Knight. The Evil King barely hits but summons Light Bandits (37%).
7. **Rewards.** Best: Seraph's Ward +0.44, Dodge +0.37, Beacon Totem +0.32, Glacier Band +0.28, Fire
   Mage's Hat +0.27 (was the worst reward last pass). Worst: Leech Totem -0.29, Curse Totem -0.27, Mend and
   Fortify -0.24, Stormsplit Ring -0.20, Venomblade Totem -0.19. Veil Totem is fixed: -0.10, down from
   +0.78. Totems are still 41% of all offers and mostly rate at or below zero.
8. **Five items can never drop:** Buckler, Amber Signet, Harlequin Band, Manaflow Ring and Wardens'
   Braid were never offered in 2,100 runs - no Common-rarity reward appears in any reward screen.
9. **Overpriced enemies:** Pebble, Vampire Bat, Golem, Slime, Skeleton and Mushroom deal under 1.6
   damage per body; battles with a Vampire Bat, Slime, Skeleton, Mushroom or Crab were easier than the
   same level without one.

## Re-running

```
powershell -ExecutionPolicy Bypass -File Tools/BotRunner/Build-BotPlayer.ps1
powershell -ExecutionPolicy Bypass -File Tools/BotRunner/Run-BotBatch.ps1 -Job my.job.json -Processes 15

# ratings: a batch whose profile has rewards.exploreRewards = 1 (350 runs per party), then
python Tools/BotRunner/Rate-Rewards.py BotRuns/<explore batch> [...] --json ratings.json --csv ratings.csv
```

`BotRuns/claude-tools/n05-chain.py` is the whole overnight sequence (explore, rate, final comparison)
as one script. Foresight runs at ~15-20 ms per decision (Tuned ~7) because each forecast re-plans
every enemy; a 200-seed batch of all six parties takes about 90 minutes on 15 processes.

**Noise:** one profile's average over 200 runs moves about ±0.1 levels by luck alone. Compare on the
same seeds and read the per-seed difference.
