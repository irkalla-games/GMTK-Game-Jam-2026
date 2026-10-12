# Spire Lab NN handoff: <topic>

*Written YYYY-MM-DD by the balance-lab session. Everything below was tested in the bot-build mirror only;
nothing under `Assets/` was changed, and git is as the brief said (state it).*

This document is for whoever builds this content into the real game, if the user decides to. Full results:
the "Spire Lab NN" report (<url>). Builds on: <earlier handoffs or lab definitions>.

## Decision for the user

What was asked, in two lines. Then the 3–5 findings that decide it, each with its number ± se and what it
means for building.

### Recommended: build these

| Card / change | Hero | Rarity | Cost | Text | Rating or effect |
|---|---|---|--:|---|--:|

### Optional

### Do not build as written

## Specs (final numbers)

#### <Card name>

- Hero, rarity, cost, range, tags.
- Text exactly as it should read.
- Entries: effect, amount, target, and condition, in order.
- Started as / changed because (if retuned).

## Mechanics to build (none of these exist in Assets yet)

### 1. <mechanic>

Which files change and how. Point at the mirror implementation (`Tools/BotRunner/lab/labs/NN-slug/overlay/...`)
and the exported patch (`<name>.patch`, verified with `git apply --check` against the lab's frozen commit).

### N. The bot (required: CLAUDE.md, Balance bot)

Every new effect needs a `BotPlanner.Measure` rule and a `BotCardInfo.Value`. A new screen needs a `BotPilot`
answer. A new damage-over-time status needs `BotBoardModel` treatment. List each one here.

### N+1. Sheets

Which design sheet owns the new numbers, and the sync to run (CLAUDE.md, Design sheets).

## How to check it worked

1. Compile: `powershell -ExecutionPolicy Bypass -File Tools/compile-check.ps1 -IncludeEditor`.
2. Each card does what its text says, played in the live Editor's debug testbed, conditions on and off.
3. The bot sees it: a 30-run smoke batch where `errors.csv` is empty and every new card appears in `cards.csv`.
4. Ratings: an explore batch of at least 2,000 runs through `Tools/BotRunner/Rate-Rewards.py`.
5. Whole pool: rated Veteran, 250 paired seeds, current vs new, Normal and Hard III. Expect <the lab's number>.

## Where the lab lives

- Definition: `Tools/BotRunner/lab/labs/NN-slug/` (`definition.py`, `overlay/`, ratings, profiles, `brief.md`).
- Data: `BotRuns/labs/NN-slug/` (gitignored; cleaned, run.json and CSVs kept).
- Frozen at commit `<hash>`, uncommitted diff sha256 `<hash>` (`freeze.patch`).
