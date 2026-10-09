# Balance bot: strategy and findings (Oct 2026)

About 8,400 bot runs of RealRun - Random at difficulty tier 0, across all six two-hero parties. This
covers what the best way to play currently is, what the bot now does, and what the runs say about the
game itself. Raw data lives in `BotRuns/claude-*` (gitignored); every number below can be regenerated
with the commands at the end.

## Headline

Average levels cleared out of 10, same seeds for every profile. Knight + Cleric ran 200 seeds, every
other party 100. "Full wins" counts runs that cleared all ten levels.

| Party | Balanced (old default) | **Tuned** | **Adaptive** | Tuned vs Balanced | Adaptive vs Tuned | Full wins (Tuned / Adaptive) |
|---|--:|--:|--:|--:|--:|--:|
| Knight + Cleric | 2.33 | **2.77** | 2.67 | +0.44 ± 0.09 | -0.11 ± 0.08 | 0 / 1 |
| Knight + Mage | 1.75 | **2.27** | 2.24 | +0.52 ± 0.10 | -0.03 ± 0.07 | 0 / 0 |
| Knight + Rogue | 1.90 | 3.38 | **3.85** | +1.48 ± 0.25 | +0.47 ± 0.26 | 7 / 11 |
| Cleric + Mage | 1.52 | **1.75** | 1.68 | +0.23 ± 0.13 | -0.07 ± 0.09 | 0 / 0 |
| Cleric + Rogue | 1.99 | 3.59 | **3.89** | +1.60 ± 0.29 | +0.30 ± 0.21 | 8 / 9 |
| Mage + Rogue | 0.43 | 0.74 | **0.83** | +0.31 ± 0.13 | +0.09 ± 0.13 | 0 / 1 |

(± is one standard error of the per-seed difference.)

> **Since fixed (2026-10-09):** totems no longer project their auras onto themselves or other totems,
> and Thornwood reflects once (findings 1 and 5). On the same seeds, Knight + Rogue now clears 2.51 /
> 2.49 levels (Tuned / Adaptive) and Cleric + Rogue 2.66 / 2.66, with no full wins. The Rogue parties'
> lead, and Adaptive's edge on them, came from the Veil Totem exploit. The reward ratings were measured
> before the fixes; re-measure them.

- **Tuned beats the old default for every party,** by 0.2 to 1.6 levels. That is the profile to collect
  balance data with, and it is now the Bot Runner's default.
- **Adaptive (aggressive early, defensive late) wins only for Rogue parties.** Those are the parties
  that can make themselves untargetable (next point). Once you can't be hit, killing is what is left
  to do. Everywhere else it is slightly behind Tuned.
- **The first full-run wins nearly all come from one combo.** 35 of the 37 wins took **Veil Totem**, and 36
  took **Ring of Focus**; see game finding 1.

Totals: about 8,400 runs.

## What wins: survive, don't race

Battles are won by surviving `TurnsToSurvive`, not by clearing the board, and from level 3 on the
board cannot be cleared. With the best Knight + Cleric profile:

| Level | Clear rate | Enemies spawned | Enemies killed | Share killed | Damage taken |
|--:|--:|--:|--:|--:|--:|
| 1 | 99% | 13.6 | 4.9 | 36% | 51 |
| 2 | 100% | 12.6 | 4.4 | 35% | 16 |
| 3 | 39% | 23.4 | 3.7 | 16% | 119 |
| 4 | 60% | 25.8 | 4.7 | 18% | 124 |
| 5 (boss) | 46% | 28.6 | 3.2 | 11% | 133 |
| 6+ | ~50% | 40-59 | 3-4 | 5-10% | 110-160 |

A party deals about 75 damage per battle from level 3 on, into 23+ enemies with 25-60 health each.
Every kill is a rounding error. What decides the battle is how much incoming damage is stopped, and
where the heroes stand.

### "Aggressive early, defensive late" was tested and loses slightly

The idea: kill hard in the first rounds, while a kill still saves many rounds of damage, then turn
defensive. It is built in (the **Adaptive** profile, below), and was measured three ways on Knight +
Cleric, 200 matched seeds each:

| Variant | vs the same profile without it |
|---|--:|
| Aggressive phase lowers the defence weights | -0.07 levels |
| Aggressive phase only adds kill pressure (kill 8, future-threat credit) | -0.12 levels |
| Same, plus heavier damage and loot weights | -0.09 levels |

The final cross-party table says the same for every party without a Rogue. The reason is the kill
table: when you kill about 16% of what spawns, energy spent chasing kills is energy not spent on
Block and Shield.

The exception proves the rule. With a Rogue, Stealth and Veil Totem can make the party untargetable,
and then incoming damage stops being the problem. There, Adaptive is ahead by +0.09 to +0.47 levels
and has the most full wins. So aggression pays exactly when defence is already solved. Both profiles
ship: Tuned as the default, and Adaptive for Rogue parties or for re-running the comparison after
content changes.

The second half of the idea, taking cards freely in the early levels and keeping the deck lean later,
measured **no difference** (-0.01 levels, 200 seeds). *Which* cards were taken mattered a lot (next
section); how many did not.

### What did move the needle (Knight + Cleric, 200 matched seeds per step)

| Change | Gain in levels cleared |
|---|--:|
| Draft by measured reward ratings (below) | **+0.21** (33 seeds better, 12 worse) |
| Stop over-valuing totems when drafting | +0.14 (same seeds, across two builds) |
| Stronger rating weight (10 -> 20) | +0.08 |
| Step out of reach (Move Safety 1.0 -> 1.5) and into covered spots (Exposure 2) | +0.19 (48 seeds, noisy) |
| No "play Defense first" override, plan on score instead | +0.12 (40 seeds, noisy) |
| Whole-turn search instead of one-play greedy | +0.03 (no measurable gain) |

Positioning in practice: get into a corner or beside your partner. A hero in open board has eight
sides to be hit from; in a corner beside an ally it has two.

## Card and equipment strength

Measured, not guessed. 2,151 runs picked rewards **uniformly at random** while playing battles well,
so "took it" vs "passed on it" differs only by the card. The number is how many more levels a run
cleared after taking the reward than after passing on it (shrunk towards 0 for rarely seen rewards).
Pooled across all six parties. Full table: `BotRuns/claude-tools/ratings-v2.csv`.

**Strongest:**

| Reward | Offers | Taken | Levels gained when taken |
|---|--:|--:|--:|
| Veil Totem | 419 | 72 | **+0.78** |
| Ring of Focus | 1169 | 169 | +0.31 |
| Seraph's Ward | 237 | 43 | +0.32 |
| Scholar Totem | 226 | 60 | +0.30 |
| Bastion Totem | 568 | 82 | +0.26 |
| Glacier Band | 305 | 83 | +0.23 |
| Poison Blade | 432 | 80 | +0.18 |
| Sunder | 694 | 172 | +0.18 |
| Guardian's Aegis | 603 | 159 | +0.17 |
| Shambles | 715 | 136 | +0.16 |

**Weakest** (taking them made runs end sooner):

| Reward | Offers | Taken | Levels when taken |
|---|--:|--:|--:|
| Fire Mage's Hat | 134 | 29 | -0.42 |
| Venom Totem | 725 | 142 | -0.33 |
| Sharpening Ring | 198 | 50 | -0.29 |
| Bloom Totem | 537 | 62 | -0.28 |
| Spike Totem | 421 | 73 | -0.28 |
| Sporecap Ring | 284 | 53 | -0.26 |
| Tower Totem | 466 | 64 | -0.26 |
| Ruin Totem | 210 | 49 | -0.26 |
| Thornwood Totem | 592 | 96 | -0.26 (see the bug below) |
| Contagion Totem | 446 | 84 | -0.25 |

Patterns worth a designer's look:

- **Totems dominate the loot tables.** They are 44% of everything offered (Knight + Cleric, 31,821
  offers). Six rares (Lodestone, Twin and Shield Totem; Whirlwind, Adrenaline and Bloodied Resolve)
  each show up about 1,400 times in 720 runs, nearly every other pickup screen. Most totems rate near
  zero or below. A deck that takes them stops being able to attack or block, which is exactly what
  the old bot did: it took Bloodmoon Totem 877 times out of 877.
- **Defensive auras win, offensive auras lose.** Veil (Stealth), Bastion and Sanctuary are near the
  top. Venom, Spike, Contagion, Ruin and Bloom are at the bottom. That matches "survive, don't race".
- **Equipment matters as much as cards.** Three of the top six are equipment (Ring of Focus, Seraph's
  Ward, Glacier Band). The old bot picked equipment by rarity alone.

## Game findings (outside the bot)

1. **Fixed - Veil Totem was a win button.** A Rogue card, it projects Stealth ("enemies cannot target you") onto every ally
   within 2 tiles, for as long as it stands, and nothing in its range can be targeted. Since battles
   are won by surviving, a party parked inside it wins. 35 of the bot's 37 full-run wins took it (all
   18 Knight + Rogue wins). It is the top-rated reward by a wide margin: +0.78 levels when taken,
   against +0.31 for the next best. The Rogue's own Stealth card (2 turns) feeds the same plan, and
   **Ring of Focus** (+1 energy every turn) pays for it. Options: a duration or charges on the aura,
   letting area attacks hit stealthed heroes, or making the totem itself targetable while it hides
   others. Fixed the last way: `Totem.Project` skips every totem, so a totem never buffs itself or
   another totem.
2. **Level 3 is a difficulty cliff.** Levels 1-2 are cleared 99-100% of the time and level 3 39% with
   the best play. Level 3's budget doubles the per-enemy cap (1.5 -> 3) and the per-wave cap (3 -> 6)
   while the board grows to 6x6. That is what brings HeavyBandit, EnemyRanger and SkeletonWarrior in
   from round 1, and by round 3 there are about 15 enemies on the board.
3. **EnemyRanger is the run-killer.** Across 1,000 Knight + Cleric runs it caused 802 hero falls and 398
   run-ending wipes, the most of any enemy. Parties take +27 damage per battle when it is present, and
   only 2% of Rangers are ever killed: they stay out of melee reach.
4. **Mage + Rogue cannot clear level 1** (24-32% clear rate; FlyingEye causes most falls). The Mage
   starts with 10 health. Its 15-card starter deck has 3 damage cards (Firebolt, 5 each), 7 debuffs
   (4 Dull Blade for Weaken, 3 Rust Armor for Vulnerable), 3 Teleports, 2 totems, and nothing that
   blocks. Paired with the Rogue, nobody tanks and nobody heals. Cleric + Mage is the next weakest party. The bot plays the Mage sensibly
   (Teleport to a corner, Firebolt); the deck has nothing more to give.
5. **Fixed - Thornwood Totem looped forever.** Its reaction ("when an enemy attacks nearby, deal 3
   damage back") resolves with the attacker as its source (see `AuraReaction`: the effect fires
   "against that character's own tile"), so the reflected DamageAction counts as the enemy attacking
   again and re-triggers Thornwood. Normally that repeats until the attacker dies, which makes Thornwood
   an instant kill on anything that swings nearby. Against a HeavyBandit whose Block absorbs the 3
   every time, it never ends: the battle hangs in EnemyResolve. This aborted 2-3% of all bot runs
   ("endless loop ... DamageAction (no card - a reaction?) by HeavyBandit"). A player would see the
   game freeze. Fixed by marking reaction contexts (`ActionContext.isReaction`) so a reaction's own
   actions never trigger reactions; verified with no stalls on 300 seeds that hung 5 times before.

## What changed in the bot

All in `Assets/Scripts/Bot`; every new field defaults to "off", so old job files and profile assets play
exactly as before.

- **Planner split: measure once, score many** (`BotPlanner`). Each candidate play is measured once
  through the game's previews (`BotMeasure`), then scored against a partial turn (`BotTurnState`):
  damage already dealt, attackers already dead, incoming already blocked, energy and copies already
  spent.
- **Whole-turn search** (`searchDepth`, `beamWidth`, `branching`): a beam search over sequences of
  plays, taking the first play of the best turn. It measured no better than greedy on its own, but it
  is what makes "this kill also saves my Cleric" visible. That credit (`lifeSaved` from killing or
  freezing the attacker) is new for every profile.
- **Adaptive weights** (`adaptive`, `lateWeights`, `aggressiveUntil`, `defensiveFrom`, `lowHpGuard`,
  `crowdGuard`, `minAggression`) via `BotAggression`; the turn log shows `[aggr 0.63]` per decision.
- **New features:** `futureThreat` (a kill credited with the damage that enemy would deal over the
  remaining rounds), `exposure` (open sides closed by a move), `lootApproach` (moving towards loot).
- **Totem fixes:** a totem is worth what its aura's audience is. Enemy-aura totems count enemies by how
  dangerous they are, capped at 4. An aura that reaches everyone counts what it gives the enemies
  against us; Bloodmoon used to score its enemy Strength as a plus. Draft value dropped from "covers
  two allies" to one. Before this, a Knight at 3 health with 34 damage incoming would spend his last
  turn placing Lodestone Totem.
- **Rewards:** level-split thresholds (`earlyLevels`, `lateTakeCardsMinValue`,
  `lateRemoveWhenDeckAbove`, `lateLootScale`), measured `cardRatings` × `ratingWeight` for cards *and*
  equipment, and `exploreRewards` for measuring.
- **Profiles:** new **Tuned** (the measured best, now the Bot Runner's default) and **Adaptive** assets
  in `Assets/Data/Bots`, both carrying the v2 ratings.

## Re-running

```
powershell -ExecutionPolicy Bypass -File Tools/BotRunner/Build-BotPlayer.ps1
# a job file from Tools/Bot/Bot Runner's "Save job...", e.g. Tuned + Adaptive + Balanced, 100 runs:
powershell -ExecutionPolicy Bypass -File Tools/BotRunner/Run-BotBatch.ps1 -Job my.job.json -Processes 14

# Re-measure reward ratings after a balance pass: a batch whose profile has
# rewards.exploreRewards = 1 (300+ runs per party), then
python Tools/BotRunner/Rate-Rewards.py BotRuns/<explore batch> [...] --json ratings.json
```

Paste the JSON into the Tuned asset's `Card Ratings` (or set it through `BotProfile.SetData`). Ratings
describe the content they were measured on, so re-measure after changing cards, loot tables or levels.

**Noise:** one profile's average over 100 runs moves about ±0.1 levels by luck alone. Compare profiles
on the same seeds (same `baseSeed`) and look at the per-seed difference. Anything under about 0.15
levels needs 200+ runs per side.
