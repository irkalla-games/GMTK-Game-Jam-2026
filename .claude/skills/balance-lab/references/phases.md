# Phases: how each kind of run is measured

Read the section for the phase you are planning. Every phase ends in **evaluation arms**: complete batches on
shared seeds, compared run by run with `paired()`. Everything before that is search, and its numbers are
never the headline.

## Numbers to plan with (Knight + Cleric, 15 processes)

- Throughput is 800–1,450 runs/hour. Runs that clear more levels take longer, so rated arms on Normal are
  the slow end, while random drafting and Hard III run near 1,400. `runs_per_hour(lab, tier)` uses this lab's
  finished batches of 20+ runs; until one exists it assumes 800.
- An evaluation arm is 250 runs per tier. It gives a level mean ± about 0.13 to 0.15, and a paired
  difference ± about 0.14 to 0.16.
- A rating round of about 2,500 random-drafting runs pins a Knight Uncommon to about ±0.10 and a Cleric
  Uncommon to about ±0.16 (Card Lab III). Fewer cards in the pool means more offers each.
- Past labs' seeds were 1,000 to 61,000. `lab.seed(block)` hands out NN·100000 + 1000·k, so new blocks
  never collide with them.

## Seeds

- **Pair on one block.** Every arm compared in one table uses the same `lab.seed('eval')`, one block per
  tier (`'eval-t0'`, `'eval-t3'`).
- **Replicate on a new block.** Any arm chosen *because of* results on a block (the best variant, a pool
  with the traps removed) is re-run, together with its baseline, on a fresh block
  (`lab.seed('replicate-t0')`). The report shows each block and the `pool()` of both. Card Lab II's L4
  measured +0.57 on the seeds it was chosen on and +0.06 on fresh ones.
- Comparing with an earlier lab's arm is fair only on that lab's seeds *and* the same content apart from
  the change. Say which in the report.

## Measure

1. Build `base` from the frozen copy (done at kickoff).
2. Arms: the stock Veteran (`profiles.stock(lab.source, 'Veteran')`) per tier and party, 250 runs (400 for
   a headline tier).
3. **Ratings** are re-measured whenever content changed since the last ratings. Use `profiles.explore(vet)`
   (uniform-random drafting, so taking vs passing is causal) in rounds, then
   `rate(batches, profile='Explore')`, then `pool_rounds()`, then `card_ratings()`, then
   `profiles.with_ratings()`. A rated Veteran is a new profile name (e.g. `Veteran08`). Better ratings
   alone were worth +0.46 levels once, so say which ratings each arm used.
4. Report per tier: levels ± se, and paired differences against the comparison arm. Enemy threat comes
   from the batch CSVs (`enemies.csv`: falls per 100, damage per body).

## Enemies and levels

- A variant is a definition, or an `extra=` patch set, built as its own player. Use
  `enemy_patch(lab.source, 'Mimic', power=2.5)` (`folder='Bosses'` for bosses), `totem_hp`, or text
  patches on a `LevelData` `.asset` for budgets. Patches must match the **frozen** text, which `enemy_patch`
  reads from `lab.source`.
- Arms: base vs each variant on one block per tier. Run a variant alone first, then the promising ones
  combined; a combination never beats its parts by default.
- A difficulty change shows up most at the walls. Report how far runs reach (share of runs clearing each
  level), not just the mean.
- The Encounter Simulator is an Editor window. It is the user's tool, not the lab's: suggest it in the
  handoff for roll statistics.

## Cards

1. **Definition.** Create `Tools/BotRunner/lab/labs/NN-slug/definition.py` and build on the starting lab
   with `BASE = load('05-card-lab-2')` or similar. Copy the base's `overlay/` into this lab's and rename
   the builder (`CardLab8Builder`); add the new cards there.
   `patches(root) = merge(BASE.patches(root), CODE_NN)`. Card numbers go through `config()`
   (`n.<Card>.<field>=<value>`), so a retune is a config change, not a code change.
   **To take an earlier lab's cards out of the pool**, build on that lab's definition and skip its cards
   through config (`skip=Card A|Card B`). This keeps its engine and bot patches, which is what Card Lab IV did
   with Card Lab III's cards. Switch the bot weight it added off (`formation` 0) unless the brief keeps it.
2. **Generated cards live under `Assets/Data/CardData/<Sub>`.** The AllCards postprocessor rescans only that
   folder, and anything elsewhere is silently dropped from rewards. A gated Draw/Energy entry is
   *skipped* in `ResolveEffects` and in `BotPlanner.Measure`. Emptying its targets does nothing, because
   source effects ignore landed tiles.
3. **Smoke the lab player** (8 explore runs) and check that the new cards appear in `rewards[].offered`
   and get played. A passing build has already shipped zero offered cards once.
4. **Rating rounds**: explore profile, about 2,500 runs per round. Before each round,
   `labkit.build.check()` the definition against `lab.source`. Retune between rounds toward the target
   band, and pool only rounds that measured the **same version** of a card.
5. **Teaching the bot** a new mechanic is a mirror-only weight (e.g. Card Lab III's `formation`). Run plain
   and taught in the same round (two explore profiles, same seeds) and report both. Each half then has half
   the offers, so its ratings are about 1.4× noisier. Budget the round for that, or rate on the pooled table
   and report plain and taught only as levels.
6. **Evaluation**: the rated Veteran on fresh paired seeds at each tier, base pool vs base plus the new cards
   (plain and taught). Then the replication block for whichever arm becomes the verdict.
7. Condition cards: log how often the condition held (Card Lab IV's `LabCost.LogPlay`, written to the
   shard logs), because a payoff that never fires rates as its unconditional half.

## Bot

- Changes are definition patches against `Assets/Scripts/Bot/*` in the mirror, and profile changes are
  `profiles.tweak()`. A new `BotProfile` field must default to "off", so old profiles and jobs play the same
  (CLAUDE.md, Balance bot).
- Arms: the current default profile vs each variant on one block; the winner is then replicated on a fresh
  block. Planner cost matters: report `plannerMsAvg` (in `summary()`) next to levels.
- At wrap-up, `export_patch(definition, lab.source, '<data>/bot.patch', prefixes=('Assets/Scripts/Bot/',))`.
  The patch goes into the handoff. It is applied to the real tree only if the brief says so.
- New planner features that move bodies to look ahead must restore them in a `finally`, the way
  `DecidePlan` does.
