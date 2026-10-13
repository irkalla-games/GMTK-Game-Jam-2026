# Kickoff: the one question round

The user answers once, then leaves, often to sleep. Everything the lab will need from them has to come out
of this round. Every question costs them time they wanted to spend leaving, so ask only what is open.

## Order of work

1. `python Tools/BotRunner/lab/labctl.py preflight`. It prints disk, running lab, bot processes, mirror lock,
   HEAD, uncommitted files, the previous brief's path, and the next lab number.
   - A lab is running: tell the user. This one starts when that one finishes, and its deadline still counts
     from now, so the run is shorter. Do the whole kickoff anyway: questions, `new` (it prints WAITING),
     freeze and build, so compile problems still surface while the user is here. Then run
     `labctl.py wait <lab>` in the background; it takes the lock when the other lab releases. Smoke and
     the rounds start after that.
   - **The tree is still being edited** (the user says "after that session finishes", or preflight shows
     files changing). The freeze cannot happen while the user is here, so the protection of building now is
     lost. Make up for it before they leave:
     1. Add a start-signal question to the round. Recommended: message the editing session (`SendMessage`)
        asking it to report when done, which content it added, and whether a builder still needs running.
        Fallback: the tree has been quiet for 15 minutes and compiles.
     2. Compile-check the tree now (`powershell -ExecutionPolicy Bypass -File Tools/compile-check.ps1`) as an
        early canary, and tell the user the result.
     3. Have the brief pre-decide what happens if the tree never compiles, since nobody can answer later:
        freeze HEAD, freeze the last compiling state, or go stuck and notify.
     4. Do `new` and the brief now. Then run `labctl.py wait-tree <lab> --quiet 15` in the background. It
        exits 0 once the tree is quiet and compiles, and exits 1 after `--give-up` hours (default 6), which
        is when the brief's fallback applies. Freeze, build and smoke run after it.
     5. If the brief needs "which cards are new" and the editing session has not said, take the CardData
        assets that are absent at HEAD.
   - Under 20 GB free: say so with the number. Cleanup of old `BotRuns/claude-*` folders is the user's call.
2. Read the kickoff message twice. List every answer it already gives. Those are never asked.
3. Read the previous lab's `brief.md`, its `lab.json` (headline, `tested`), and its handoff if it has one
   (`lab.json` → `handoff`). The handoff is where its cards and mechanics are listed. The brief's standing
   answers are the "(Same as last time)" defaults.
4. If the ask describes a game change, **check the code first**. It may already exist. Card Lab III's user
   asked to "move upgrade and remove to the level reward", and it already worked that way; the right
   question offered "Already right, leave it" as the recommended answer. If the change does not exist yet,
   it becomes mirror content in this lab (a definition patch, then the handoff), never an edit to `Assets/`.
   Ask about it only if there is a real design choice inside it.
5. If the clock time and a stated duration disagree ("until 2 pm, so 12 hours", when 2 pm is 11 hours away),
   the clock time wins. Say so in the stated-as-given line.
6. Build the question round (below) and ask it.
7. `labctl.py new <slug> --topic "..." --deadline "YYYY-MM-DD HH:MM" --phases cards,measure` (phases in the
   order they will run). It prints the lab id and the `brief.md` path. Write `brief.md` there from
   `assets/brief-template.md`. The title is just "Spire Lab NN"; the topic is stored separately in
   `lab.json` (`topic`), for the report's lede and the Artifact's description.
8. `labctl.py freeze <lab>` (add `--head` if the brief says HEAD), then `labctl.py build <lab> base`, then
   `labctl.py smoke <lab> base`. A build takes several minutes, so run it in the background. A failed build
   prints the compiler errors; if the tree is mid-edit (another session), tell the user and offer HEAD.
9. Tell the user, in four lines: the lab id and title, when the reserve starts, the smoke throughput (runs per
   hour), and "going quiet now: status in BotRuns/labs/<id>/status.md, a notification when done or stuck".

## The standing checklist (every lab)

| # | Question | Default when the previous brief has none |
|---|---|---|
| 1 | **Deadline**: a clock time. Convert "until 9pm" to `YYYY-MM-DD 21:00`. The last 2 h are the reserve. | Must come from the user |
| 2 | **Phases and the time split**: which run types, and their order and share. | From the ask |
| 3 | **Party**: which heroes. | Knight + Cleric |
| 4 | **Tiers**: which difficulty tiers the evaluation arms use. | Normal + Hard III |
| 5 | **Start from**: the current game, or a past lab's pool (e.g. Card Lab II's L4). For a past lab's pool, run `labctl.py check-defs` before asking. If its patches no longer match the tree, the options are freezing an older commit or rebuilding the pool on today's code. | The previous lab's |
| 6 | **Source**: working tree with uncommitted edits (name the files preflight listed), HEAD, or working tree plus a baseline arm measuring the uncommitted edits alone (Card Lab IV's choice). Ask when the tree is dirty. A dirty tree also breaks like-for-like comparison with an earlier lab's seeds, so say so in the options. | Working tree |
| 7 | **What may change between rounds**: nothing (measure only), the new content, earlier lab content, enemies, bot code. All of it mirror only. | New content only |
| 8 | **Bans or forced picks**: any card, item or totem the bot must never or always take. | None |
| 9 | **Deliverables beyond the standard set**: the standard set is report, index, `BotStrategy.md` section, chat summary, and a handoff when mirror content was made. Ask whether to also apply the bot patch to the real tree. | Standard set, no apply |
| 10 | **Git**: nothing committed, lab files only, or everything. | Nothing committed |

## Questions for each phase type (only for the phases in this lab)

- **Measure**: parties and tiers (if they differ from 3–4); whether to re-measure card ratings (yes after any
  content change); which earlier lab to compare against, on its seeds.
- **Enemies / levels**: which enemies or levels; the variants to try, or "free to propose"; whether a
  variant may be carried forward into later phases.
- **Cards**: which heroes get cards; how many and at what rarity; payoff kinds (offer the concrete shapes:
  bigger number, shared with neighbours, after-move, cheaper, draw); whether new mirror mechanics (new C#) are
  allowed or only existing effects; condition semantics (distance counted as `LabCost` does: Chebyshev,
  diagonals count; "at least N" vs "exactly N"; whether totems and summons count as heroes);
  the power target (default band +0.10 to +0.30 levels; nerf above about +0.35, buff below 0); whether to
  teach the bot the new mechanic (default yes, and report plain and taught numbers both); how to test a
  parameter sweep (one setting per round is cleaner than mixing).
- **Bot**: how far it may go (profile knobs only, or new planner features as mirror patches); whether a winner
  becomes the default profile (it can't during the lab, so this is a handoff question).

## Asked or stated as given

- **Asked**: anything the kickoff message leaves open that the new ask bears on. Examples: a new topic, a
  dirty tree, a parameter the user wants explored, a change that may already exist.
- **Stated as given**: anything the previous brief settled that this ask does not touch (party, tiers, git
  most of the time), and anything with one sane answer. Put these in one line before the questions, each
  marked "(same as last time)" where it is, e.g. "Taking as given: Knight + Cleric, Normal + Hard III
  (same as last time); nothing committed." The user can overturn any of it in their answer.
- **Cut by priority** when too much is open. Keep, in this order:
  1. What gets built: pool, mechanics, how many.
  2. What gets measured and compared: source, sweeps, the time split.
  3. Deliverables and git.

  Card Lab IV's real round was 4 questions, and the user was content to let the session pick the taunt
  shapes and card count itself.

## How to ask

- **One round.** The question tool takes at most 4 questions per call. If more than 4 are genuinely open,
  ask a second call straight after the first, before the user leaves, and no third. Aim for 4–6 questions
  in total.
- Each question offers 2–4 concrete options, recommended first. Label the recommended one "(Recommended)",
  or "(Recommended, same as last time)" when it is the previous brief's answer. Use multi-select only for
  "which kinds of X" questions where several can be true (payoff kinds, taunt shapes).
- Put the numbers in the options: run counts, hours, how many runs per card. "About 2,000 runs per round, so
  each card is pinned to about ±0.10" is an answer someone can choose. The numbers to size with:
  - Throughput: 800 runs/hour for rated arms that run deep on Normal; about 1,400 for random-drafting rounds
    and Hard III (15 processes).
  - Evaluation: 250 runs per arm per tier.
  - Ratings: about 2,500 random-drafting runs per round pins a Knight Uncommon to ±0.10 and a Cleric
    Uncommon to ±0.16. Splitting a round between a plain and a taught bot makes each about 1.4× noisier.
  - Wrap-up: the last 2 hours. On a short lab (under about 6 hours), say what the reserve costs, and offer
    `--reserve 1.5` if the user wants more runs.
- If only one round fits, don't split it three ways to compare settings. Recommend one setting, and log the
  variable (e.g. the distance to the nearest hero) so the report can say how often other settings would
  have held.
- Read the answers literally. "Other" text often changes the plan (Card Lab II: "they won't be + cards, the
  starter deck sits below the curve"). Restate any free-text answer in `brief.md` in your own words.
- If the user adds instructions after answering, before they leave, fold them into `brief.md`. They count
  as answers.
