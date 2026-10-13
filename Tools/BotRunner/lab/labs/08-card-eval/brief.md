# Spire Lab 08: New Knight and Cleric cards: strength, enemy balance and win rate - brief

- **Kickoff:** 2026-10-12 01:50 · **Deadline:** 2026-10-12 11:00 · **Reserve from:** 09:00
- **Lab:** `08-card-eval` · data in `BotRuns/labs/08-card-eval/` · definitions in `Tools/BotRunner/lab/labs/08-card-eval/`

## The ask

> can you use the balance lab skill

> Specifically, I want to do a full retune for the analysis on the cards. I am creating a bunch of new cards
> for the Knight and Cleric and want to check those. That session is still working so I want you to run the
> lab after it finishes

Answers to the question round, verbatim where free text:

> **Scope:** No need to retune, for now I just want to evaluate the strenght of the new cards and see how the
> enemies are balanced against the new set. What enemies are good, what are bad and so forth. Also how often
> the bot is winning the game. I want to check between Normal mostly and then a tiny bit on Hard 3
>
> **Target:** Just an evaualtion, not a tuning for the cards.

In my words: the lab measures and changes nothing. It answers three questions about the game as it stands
once the card session (Claude session `gmtk-game-jam-2026-7a`) finishes: how strong each new Knight/Cleric
card is, which enemies are too strong or too weak against the new set, and how often the bot wins a whole
run. Almost all runs go to Normal, with a small slice on Hard III. "Full retune" in the first message was
replaced by "no need to retune" in the answers; the answers win.

## Standing answers

| Question | Answer | Settled by |
|---|---|---|
| Phases and time split | Measure only. (1) Card ratings on Normal, explore drafting, ~2,500 runs. (2) Evaluation on Normal with a Veteran rated by this lab's ratings: win rate, levels cleared, enemy threat, card picks and plays, ~1,000 runs, plus a baseline arm without the new cards on the same seeds. (3) A small Hard III arm, ~15% of the runs. Spare time before the reserve goes to a second ratings round on fresh seeds. | asked (scope answer) |
| Party | Knight + Cleric | stated as given (same as last time) |
| Tiers | Normal (t0) mostly; Hard III (t3) a little | asked |
| Start from | The current game plus the new cards, as the card session leaves the tree | kickoff message |
| Source | The working tree **after session 7a finishes**. At kickoff, 23 uncommitted files under Assets: CardSheetImporter.cs; enemy prefabs EnemyRanger, FlyingEye, HeavyBandit, LightBandit, Mimic, SkeletonWarrior, Skull (backline health cuts and powerLevel changes); Bot/BotBoardModel.cs, BotCardInfo.cs, BotPlanner.cs; Cards/ActiveHandViewer.cs, Card.cs, CardData.cs, CardViewer.cs, Modifiers/CostModifier.cs; Character.cs; BattleManager.cs; Glossary.cs; new CardCondition.cs and Editor/CardLabContentBuilder.cs (not yet run at kickoff) | kickoff message |
| What may change between rounds | Nothing. No card, enemy or bot changes. | asked |
| Bans or forced picks | None | default |
| Deliverables beyond the standard set | Report covering per-card strength, an enemy good/bad table against the new set, and win rate per tier. No bot patch to apply. Handoff only if the lab ends up making mirror content (it should not). | asked / default |
| Git | Nothing committed | stated as given (same as last time) |

## Phase answers

### Phase 1: measure - new-card strength, enemy balance, win rate (all of the time)

| Question | Answer | Settled by |
|---|---|---|
| Which cards count as "new" | The cards session 7a reports when it finishes. Fallback: every Knight/Cleric CardData whose asset did not exist at HEAD `ed2880d8`. | stated as given |
| Re-measure card ratings | Yes. The content changed, and the Veteran preset's ratings predate every new card. | default (phases.md) |
| Comparison | Baseline arm: the same tree with the new cards taken out of rewards (mirror only), paired seeds, Normal. It measures what the set as a whole adds. | stated as given |
| Win rate | Share of runs ending Won, per tier, ± se, from the rated Veteran arm. | asked |
| Enemy balance | From `enemies.csv` across the evaluation arms: falls per 100 appearances, damage per body, threat tiers. Called "good" or "bad" against the rest of the roster, with how often each is seen. | asked |

## Stated as given (not asked)

- **Start signal:** session 7a was asked (01:50) to message this session when it is done; an idle notice from it is also subscribed. The lab freezes only once 7a says it's done, or has gone idle and the tree compiles and has been quiet for 15 minutes. (User chose "Ask it to message me".)
- **Freeze, build and smoke happen after the wait, not at kickoff.** The tree is still being edited, so there is nothing stable to compile-check yet. A working-tree compile-check at 01:50 passed (`COMPILE OK`, Editor included).
- **If the new cards exist only as `CardLabContentBuilder` (not run into Assets),** the lab runs the builder in the mirror, never in `Assets/`.
- **If the frozen tree fails to build:** HEAD is no fallback, because it has none of the new cards. The lab
  marks itself stuck, sends a push notification with the compiler errors, tells session 7a, and waits for
  a new "done" before freezing again. Nothing in `Assets/` is touched to make it compile.
- **If no start signal arrives:** the lab can only use what fits before the 09:00 reserve. It waits for the
  signal however late it comes, and `can_start` cuts the plan down: ratings first, then the Normal
  evaluation, then Hard III. The baseline arm is dropped before any of those three.
- **Past definitions (05/06/07) no longer apply** to the working tree (`check-defs`: `public int cost;` moved, because the conditional cost is now real code). This lab needs none of them: it measures the tree as it is.

## Assumptions made later

<!-- Appended during the run, whenever the brief did not decide something: time, the question, what was
chosen, why. The report copies every entry into its "Assumptions I made" panel. -->
- **03:05, start.** Session 7a reported done at 03:05 (47 cards built into Assets, the L4 pool live, compiles,
  smoke clean). Froze the working tree at 03:05.
- **03:10, baseline.** 7a pointed out that a "before" game would be the previous commit (ed2880d8). That commit
  also lacks the enemy edits and the bot changes, so it would measure more than the cards. Kept the brief's
  plan instead: the same frozen tree with the 47 new cards set excludeFromRewards (definition.py, player
  `nonew`). The L4 pool settings stay on in both arms, which leaves 39 Knight/Cleric reward cards without the
  new ones and 86 with them.
- **03:10, which bot.** The evaluation uses Veteran08: stock Veteran with its cardRatings replaced by round r1's
  measured ratings. Stock Veteran has no rating for any new card, so it would draft them by
  BotCardInfo.Value alone (7a's note). The win rate in the report is Veteran08's.
- **03:10, no replication block.** Nothing in this lab is chosen from results: the ratings come from the
  `ratings-1` seeds, and the evaluation runs on separate `eval-t0`/`eval-t3` seeds. The "pick on S, replicate
  on fresh seeds" rule has nothing to act on. Spare time goes to a second ratings round instead (brief).
- **03:12, smoke.** 8 Veteran + 8 Explore runs: no stalls or errors, about 1,000 runs/hour. 46 of the 47 new
  cards were offered and 40 played. Smite Together was not offered, but it is in AllCards, Uncommon and not
  excluded, so that is chance at 16 runs. Round r1 checks it.
- **06:35, extra Hard III arm.** Round r2 finished early, so after the second ratings round I added r4: Veteran08 on
  `nonew` at Hard III, 300 runs paired with r2's Hard III arm. It gives "what the set adds on Hard III" for about
  20 minutes of runs. It is still a small slice, as the brief asked.
- **07:50, how single cards are called.** Rounds r1 and r3 correlate at only 0.17 across the 47 new cards, though
  their error bars hold up (round-to-round differences match them). The cards are genuinely close in power: about
  ±0.07 levels of real spread. The report therefore calls a card "top" or "weak" only when its pooled rating is
  1.5 se clear of zero and both rounds agree in sign, rather than ranking all 47.
- **07:50, title.** The lab's page is titled "Spire Lab 08" (a name, not the topic sentence `labctl new` wrote).
