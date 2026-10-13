# Balance bot: strategy and findings (Oct 9 2026, evening pass)

11,509 bot runs of RealRun - Random across all six two-hero parties, after the afternoon's
changes: Mage 10 → 20 health, Cleric 20 → 25, Sap Life in the Mage starter, two Leeching Blades in the
Rogue starter, higher enemy prices and lower per-enemy caps. The full write-up, with charts and sortable
tables of every card, item, enemy and hero, is the "Spire Balance Lab III" artifact (https://claude.ai/artifact/GednDaSut2WKabHrijJuBT).
The previous pass is in `Docs/archive/BotStrategy-2026-10-09-am.md`. Raw data lives in `BotRuns/claude-p0*`
(gitignored), the scripts in `BotRuns/claude-tools` (`p-chain.py` is the whole evening as one script).

## Headline

Average levels cleared out of 10. Oct 9 = the morning's Foresight result on the old content. Content = the
same Foresight bot on today's content (the content change alone). Veteran = this pass's bot, same seeds as
the Content column's comparison run (188 seeds per party, seed 15000+).

| Party | Oct 9 | Content | Veteran | Bot gain | Full clears | Clears L1 |
|---|--:|--:|--:|--:|--:|--:|
| Cleric + Rogue | 3.92 | 6.04 | **6.32** | +0.28 ± 0.19 | 15 / 188 | 97% |
| Knight + Cleric | 4.17 | 6.11 | **5.99** | -0.12 ± 0.16 | 6 / 188 | 99% |
| Cleric + Mage | 2.56 | 5.37 | **5.76** | +0.39 ± 0.19 | 3 / 188 | 96% |
| Knight + Mage | 2.54 | 4.24 | **4.65** | +0.41 ± 0.14 | 1 / 188 | 99% |
| Knight + Rogue | 2.75 | 4.18 | **4.42** | +0.23 ± 0.13 | 1 / 188 | 99% |
| Mage + Rogue | 0.91 | 2.13 | **2.52** | +0.39 ± 0.21 | 1 / 188 | 64% |

Pooled: Veteran beats Foresight by **+0.27 ± 0.07** levels per run on the same seeds.

**Hard mode** (Veteran, 100 seeds per party): Normal 4.94 levels, Hard I 4.13, Hard II 3.61.

## What the bot learned

The **Veteran** profile (`Assets/Data/Bots/Veteran.asset`, `BotPresets.Veteran`) is the new Bot Runner
default: Foresight plus

- **Lean drafting** (+0.31 ± 0.10): take a
  card only at value 5 (was 4); otherwise upgrade, remove past 11 cards, or take the pickup's Heal 5. About
  13 cards a run instead of 16 and three times the upgrades. The biggest single gain the bot has found.
  Raising the bar to 6 changed nothing: no offered card scores between 5 and 6.
- **Exact Weaken** (`exactWeaken`, +0.17 ± 0.10 on top of
  lean): Weaken is 3 off each hit, subtracted from what each victim faces, so a Weaken that keeps an
  ally standing counts as a saved life. The planner used to count stacks (turns) as damage stopped.
- **On-hit buffs counted** (`countOnHit`, +0.01 ± 0.08): Leeching Blade
  and Poison Blade valued as what they return. No measurable effect; kept because it is correct.

Tested and left off: **focus** (damage weighted by the target's threat per health,
+0.11 ± 0.10 at weight 5, +0.11 ± 0.11 on top of lean) and **hunt**
(moving toward that target, -0.06 ± 0.10). The
dangerous enemies stand in the back line and waves keep coming; surviving the count beats racing it.

## Game findings

1. **The content change did most of the work**: every party gained 1.2 to 2.8 levels with the bot
   unchanged, Cleric + Mage most.
2. **The Light Bandit is still the run-killer** at 2.5 power: 11.5 damage per body,
   18% of hero falls, +9 points on the chance to lose a level,
   2% killed. The price is not the problem; two splash cards at range 4 are.
3. **The Enemy Ranger** (3.5) adds 47 damage to every battle it is in, the most of any
   regular enemy. Strongest per price after these two: Skull, Flying Eye.
4. **Overpriced now:** Heavy Bandit, Skeleton Warrior and Mimic make levels easier than whatever the budget
   would otherwise buy (-19, -10, -9 points).
5. **Poison stops parties without a Cleric.** Plague Rats and Mushrooms are among the top killers of
   Knight + Mage, Knight + Rogue and Mage + Rogue and nearly harmless to any Cleric party.
6. **Mage + Rogue** still loses 36% of its runs on level 1.
7. **Dull Blade was misread, not weak:** with exact Weaken its value per play rose from
   1.9 to 11.1, level with Firebolt. Rust Armor (×3, Vulnerable) is the dead
   starter card. The Rogue leaves 26 energy unspent per run, more than the other three heroes together.
8. **Evil Wizard 3** is still the hardest boss (44% of its levels cleared).
9. **Rewards** (ratings v4, `BotRuns/claude-tools/ratings-v4.json`): best Seraph's Ward, Rimeguard Band, Ring of Focus, Glacier Band, Beacon Totem;
   worst Guardian's Aegis, Bloom Totem, Bloodpact Totem, Veil Totem, Thornwood Totem.

## Re-running

```
powershell -ExecutionPolicy Bypass -File Tools/BotRunner/Build-BotPlayer.ps1
powershell -ExecutionPolicy Bypass -File Tools/BotRunner/Run-BotBatch.ps1 -Job my.job.json -Processes 15
python Tools/BotRunner/Rate-Rewards.py BotRuns/<explore batch> [...] --json ratings.json --csv ratings.csv
```

Veteran plays at ~20 ms per decision. A run now lasts 20 to 60 seconds (longer than on Oct 9, because
parties get further), so 15 processes manage about 18 runs a minute.

**Noise:** one profile's average over 200 runs moves about ±0.15 levels by luck alone. Compare on the same
seeds and read the per-seed difference.
