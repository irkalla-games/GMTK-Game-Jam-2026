# Spire Card Lab III - brief (reconstructed)

- **Kickoff:** 2026-10-11 02:46 · **Deadline:** 2026-10-11 14:00
- **Lab:** `06-card-lab-3` · ran before /balance-lab, scripts in `BotRuns/claude-tools` (`cardlab3.py`, `lab3*.py`)
- Reconstructed on 2026-10-12 from the session's two question rounds; the wording of the answers is the user's.

## The ask

> I have another Card Balance to get through. This is the results from the last balance. I want to use the L4
> pool and run random drafting to get a power level. I also want to move upgrading a card and removing a card
> from the deck to the level reward. You can skip and card add to instead upgrade or remove a card. This will
> no longer be offered during a level if it was. Additionally, I want to build out cards that give benefits
> when heroes are next to each other. Communion and Rally the Line can be part of this but cards that benefit
> a playstyle where the user moves towards each other is a fine way to play. So feel free to add some cards to
> the mirror test out in the power ranking.

## Standing answers

| Question | Answer | Settled by |
|---|---|---|
| Phases and time split | Cards: rating rounds, then evaluation arms | kickoff message |
| Party | Knight + Cleric only | asked |
| Tiers | Ratings on Normal (random drafting); evaluation arms on Normal (t0) and Hard III (t3) | asked |
| Start from | Card Lab II's L4 pool, with Rally the Line and Communion back in exactly as last tested (Rare, cost 2, 1 cheaper next to a hero) | asked |
| Source | Working tree | stated as given |
| What may change between rounds | The new cards AND the earlier L4 lab cards; enemies and non-lab cards stay fixed | asked |
| Bans or forced picks | None | - |
| Deliverables beyond the standard set | Mirror + report page + a handoff that lets the next session build the chosen cards into Assets | asked (free text) |
| Git | Nothing in Assets, no commits | asked |

## Phase answers

### Phase 1: cards - adjacency ("next to a hero") cards (all of the time)

| Question | Answer | Settled by |
|---|---|---|
| Level-clear upgrade/remove change | Already right (clears offer Skip / Remove / Upgrade instead of a card; mid-level only Heal 5): leave it | asked |
| How many cards | About 16, a wide net | asked |
| Payoff kinds | Bonus if next to a hero; shared with the neighbour; move into formation. NOT a formation status | asked |
| "Next to" | 8 tiles, diagonals count (as Card Lab II's discount) | asked |
| New code and the bot | New mirror-only code allowed; teach the bot to move next to allies; report plain-bot and taught-bot ratings | asked |
| Power target | Good L4 level, +0.10 to +0.30 (nerf above about +0.35, buff below 0) | asked |
| Clumping cost | None added; measure whether clumped heroes take more area damage | asked |

## Assumptions made later

- Rating rounds pooled only rounds that measured the same version of a card.
- R3's numbers were used for the evaluation arms (L7); R4 tested further tweaks.
