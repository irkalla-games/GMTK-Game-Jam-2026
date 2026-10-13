---
name: balance-lab
description: Use when the user wants a balance run of Escape the Spire with the headless balance bot - a session of hours or overnight testing enemy power budgets, level budgets, new or retuned cards, difficulty tiers or bot strategy, usually with a deadline ("until 9pm", "I'll be back at 2pm") and questions asked up front. Also for "run the bot", "card lab", "balance lab", "rate the cards", "power level of the cards".
---

# Balance Lab

A **lab** is one balance session: one round of questions at kickoff while the user is there, then hours of
unattended bot batches, then a report. Internal skill for this repo. The toolkit is `Tools/BotRunner/lab/`:
`labctl.py` for single steps and `labkit` for lab scripts. Each module's docstring is its manual.

**After the kickoff question round, never ask again.** The user is asleep or away. Decide by the brief,
append the decision to `brief.md` under *Assumptions made later*, and carry on. Those assumptions end up in
the report.

## The lab, start to finish

**1. Kickoff, while the user is here.** Load `references/kickoff.md` before writing a single question.
1. `python Tools/BotRunner/lab/labctl.py preflight`. If another lab is running, say this one waits. If the source tree is still being edited (by the user or another session), the freeze waits for it, and the brief pre-decides what happens if it never compiles. If free disk is under 20 GB, warn.
2. One question round covering only what the kickoff message left open, each answer pre-filled from the previous brief as "(Same as last time)".
3. `labctl.py new`, then write `brief.md` from `assets/brief-template.md`.
4. `labctl.py freeze`, then `build <lab> base`, then `smoke <lab> base`. A failure here is reported **now**, while the user can still answer.
5. Tell the user: lab id, the time the reserve starts, the smoke throughput, and that you are going quiet.

**2. Running, while the user is away.** Load `references/phases.md` before planning each phase, and
`references/toolkit.md` before writing the first lab script.
- Every round is a script in `BotRuns/labs/NN-slug/scripts/`, copied from `assets/chain-template.py`. Launch it with Bash `run_in_background`; you are re-invoked when it exits. Use Monitor only to catch `STUCK`/`Traceback` early. Never foreground-sleep.
- Ask `can_start(lab, runs, tier)` before every batch. If it says no, go to wrap-up.
- Each script keeps `status.md` current. If a lab is stuck (a build fails, or a batch fails after its resume), the script records it with `lab.status(..., stuck=...)` and you send a push notification (load the `PushNotification` tool via ToolSearch). Then carry on with any work that doesn't depend on the failure.

**3. Wrap-up, when the reserve begins or every round is done.** Load `references/wrap-up.md` and work through
it in order: replication, report, index, `BotStrategy.md`, handoff, git, cleanup, release, notification.

## Rules that hold every minute

| Rule | What enforces it |
|---|---|
| Never read a number from an unfinished batch. Short runs finish first, so a half-done batch is biased. | `runs()`, `levels()` and `rate()` raise `IncompleteBatch` |
| An arm picked after seeing results on seeds S gets re-run on a **fresh** seed block before it's headlined. Report each seed set and the pooled number. | `lab.seed('<new block>')`; wrap-up step 1 |
| Compare arms run by run on the same seeds, and report ± se. | `paired()` refuses different base seeds |
| Name a per-card winner or loser only after two rating rounds on separate seed blocks agree. A small per-card se does not show the cards differ. | `agreement(t_a, t_b)`; phases.md, Measure step 3 |
| Every number in the report is read back against its table before publishing ("X of N", superlatives, caveats). `page()` checks structure, not claims. | `report_text(path)`; wrap-up step 2 |
| Nothing writes the real `Assets/` during a lab, bot code included. Lab content and bot changes are definition patches applied in the mirror only. | `build()` patches the mirror; `export_patch()` for the handoff |
| Build from the lab's frozen copy, never the live tree. | `build()` syncs from `lab.source` |
| One lab at a time. | `active-lab.json` lock; `run()` refuses while another lab holds it |
| A batch starts only if it can finish before the reserve. | `can_start()` |
| A new mirror card effect needs a `BotPlanner.Measure` rule and a `BotCardInfo.Value`, or the bot rates it worthless. Rules roll `GameDice`, never `UnityEngine.Random`. | Check the smoke batch's `rewards[].offered` and plays |

## Red flags: stop and re-read the rule

- "The batch is 80% done and the trend is obvious." Wait for it to finish.
- "This arm won, so it's the headline." Was it chosen on these seeds? Replicate first.
- "Here are the best and worst new cards." From one round? Lab 08's second round correlated with its first at 0.17.
- "I'll quickly ask the user." Not after kickoff. Make an assumption and record it.
- "A small fix straight in `Assets/`." No. It becomes a definition patch.
- "One more round should just about fit." `can_start` decides. Past the reserve, wrap up.
- "Cleanup can wait for next time." That's how `BotRuns/` reached 43 GB.

## Reference files (load at the moment named)

- `references/kickoff.md`: **at kickoff**, before writing the question round.
- `references/phases.md`: **before planning each phase** (measure, enemies/levels, cards, bot).
- `references/toolkit.md`: **before writing the first lab script**, and whenever a labkit call fails.
- `references/wrap-up.md`: **when the reserve begins**, or earlier if every round is done.
- `assets/brief-template.md`, `assets/chain-template.py`, `assets/handoff-template.md`: copy, don't retype.

If you notice you handled one of these moments without loading its file, log it as a skill observation.
