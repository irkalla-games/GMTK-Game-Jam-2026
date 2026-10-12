# Spire Card Lab IV - brief (reconstructed)

- **Kickoff:** 2026-10-11 15:18 · **Deadline:** 2026-10-11 21:00
- **Lab:** `07-card-lab-4` · ran before /balance-lab, scripts in `BotRuns/claude-tools` (`cardlab4.py`, `lab4*.py`)
- Reconstructed on 2026-10-12 from the session's question round; the wording of the answers is the user's.

## The ask

> I have a new balance run and cards to experiment with. Last time, I added a bunch of cards that provide
> benefits to the heroes when they are together. We can remove those cards from the pool for now (I am going
> to add them back but should have the handoff to work with) and I want to add more taunt effects for the
> knight and cards that provide benefit for the heroes when they are standing 2 spaces away from an ally. So
> more damage when they are alone, better tanking when alone and such. We can experiement with 2 or 3 or 4
> spaces away. This will mainly be for testing the value of those types of cards and seeing how they fit into
> the balance of the game. Please run a lot of runs and provide a report at the end about how those mechanics
> worked out and the balance for the bot.

## Standing answers

| Question | Answer | Settled by |
|---|---|---|
| Phases and time split | Cards: one rating round per distance, one tune round, evaluation arms | asked |
| Party | Knight + Cleric (same as Lab 3, so results compare directly) | asked |
| Tiers | Ratings on Normal; evaluation arms on Normal (t0) and Hard III (t3) | same as last time |
| Start from | Card Lab II's L4 pool without any next-to cards (Rally the Line, Communion, Brothers in Arms, Prayer Circle, Mending Light, Ward Blessing all out) | asked |
| Source | Working tree, including the uncommitted backline health cuts (Ranger 25 to 10, Flying Eye 15 to 10, Light Bandit 30 to 15, Skull 15 to 10) and Skull prefab edits; one baseline arm first | asked |
| What may change between rounds | The new cards' numbers | stated as given |
| Bans or forced picks | None | - |
| Deliverables beyond the standard set | Report on how the mechanics worked and the balance for the bot; handoff (Docs/CardLab4-Handoff.md) | kickoff message |
| Git | Nothing committed | same as last time |

## Phase answers

### Phase 1: cards - Knight taunts and "alone" cards (all of the time)

| Question | Answer | Settled by |
|---|---|---|
| Who gets the alone cards | Knight (taunts and lone-tank cards) and Cleric | asked |
| "Alone" | No other hero within N tiles, diagonals count (same counting as Lab 3's "next to") | stated as given |
| Testing distances 2 / 3 / 4 | One distance per round (all alone cards at 2, then 3, then 4), then tune at the best distance | asked |

## Assumptions made later

- Tuned at distance 3: the condition held about half the time there (74% at 2, 25% at 4), so it is a real choice.
