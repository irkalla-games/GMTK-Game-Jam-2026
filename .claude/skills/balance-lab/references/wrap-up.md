# Wrap-up: from the reserve to the notification

Starts when the reserve begins (`lab.reserve_start`) or when every planned round is done, whichever comes
first. Work through these steps in order. Each one ends in a file or a URL, so a step that was skipped is
visible.

## 1. Replicate

For every number that will appear in the verdict, ask whether the arm was chosen after seeing results on
these seeds. If it was, run it and its baseline on a fresh block (`lab.seed('replicate-t0')`), 250 runs each,
if `can_start` allows. If there is no time, the report says "not replicated" next to the number. Never
silently headline it.

## 2. Gather and render

- Write `scripts/render.py`: it gathers numbers with `levels`, `paired`, `pool` and `rate` (complete batches
  only) and calls `page()`.
- **The opening block is the user's whole morning**, so write it for someone deciding in two minutes:
  - `verdict`: one or two paragraphs. What changed and by how much, with ± se, as plain statements.
  - `decisions`: each is one thing the user must decide or do: "Build these five cards (handoff …)",
    "Mimic back to 2.5?". Lead with the action. If there are none, say why in `no_decisions=`.
  - `assumptions`: every entry from `brief.md`'s *Assumptions made later*, plus any reading of the brief the
    lab depended on. If there are none, say why in `no_assumptions=`.
- Sections: one per phase, each with the table or chart that carries its claim. The method section gets the
  freeze, builds, batches and seeds appended automatically. Add which ratings each arm used, which seed
  blocks were replicated, and the caveats.
- Publish `report.html` with the Artifact tool (a new artifact, icon `chart`; load the artifact-design skill
  first if it isn't loaded yet). Then
  `lab.update(url=..., date='YYYY-MM-DD', tested='<one line>', headline={'kcNormal': [m, se], 'kcHard3': [m, se], 'note': '<one line>'})`.
  Use the arm the verdict is about. Leave a key out if it wasn't measured.

## 3. Index

`labctl.py index` renders `BotRuns/labs/index.html`. Republish it to the URL in `index.json`. It is an
existing artifact, so read it first, then publish with `url`. The link never changes.

## 4. Docs/BotStrategy.md

Add a `## Spire Lab NN (date): topic` section above "What the bot learned". It holds 5–10 lines: the question,
the headline table (arm, Normal, Hard III, as paired differences ± se), the two or three findings that change
how someone designs, and links to the report and handoff. Don't rewrite other labs' sections.

## 5. Handoff (whenever the lab made mirror-only content)

`Docs/SpireLabNN-Handoff.md` from `assets/handoff-template.md`. It is the build plan the next session follows
to put the content into `Assets/` for real:
- Every card's final numbers.
- Each mechanic with the files to touch.
- `export_patch()` output for code (a patch file next to the handoff, verified with `git apply --check`).
- The bot work CLAUDE.md requires: a `BotPlanner.Measure` rule and a `BotCardInfo.Value` for each new effect,
  and for a new screen, a `BotPilot` answer.
- The design-sheet sync.
- "How to check it worked".

Bot changes go in the handoff too. If the brief said "apply", apply them only now: run `git apply --check`
first, then `git apply`; stop and report if the check fails.

## 6. Git, as the brief says

The default is nothing committed. "Lab files only" means `Tools/BotRunner/lab/labs/NN-slug/`, the
BotStrategy.md section and the handoff, staged by path. Never stage the user's own content edits unless the
brief says "everything". Never push.

## 7. Cleanup

Run `labctl.py cleanup <lab>` first: it's a dry run and lists what would go. Then `--apply`. It keeps every
run.json, report and CSV, plus the best and worst run's logs per batch. It deletes the other per-run logs,
`source/` and `players/`. Analysis that reads `events.jsonl` must already be finished.

## 8. Release and tell

1. `labctl.py release <lab>`.
2. Send a push notification: "Spire Lab NN done: <verdict in one line>". Add the report URL if the
   notification takes one.
3. In chat, the final message: the verdict, the decisions list, the report and index links, the handoff
   path, the cleanup result, and the git state. Nothing the report doesn't already say.

## Before you call it done

Re-read SKILL.md's rules table and check each one against this lab:
- No partial-batch numbers anywhere.
- Every chosen arm is replicated, or labelled as not replicated.
- The lab wrote nothing to the real `Assets/`. Compare `git status -- Assets` with kickoff's preflight list.
  Any difference should come from the user's other sessions: mention it, never revert it.
- The lock is released and `status.md` says done (`release` writes both).
