# Spire Card Lab II - brief (reconstructed)

- **Kickoff:** 2026-10-10 17:15 · **Deadline:** 2026-10-10 23:00 ("back around 11pm tonight")
- **Lab:** `05-card-lab-2` · ran before /balance-lab, scripts in `BotRuns/claude-tools` (`cardlab2.py`, `r-*.py`)
- Reconstructed on 2026-10-12 from the session's question round; the wording of the answers is the user's.

## The ask

> I want to just make cards for the Cleric and Knight for now, but I want you to test a bunch of new
> possibilities of cards so we can get stronger combos. Base totems will just have 1 health but lets try making
> the stronger totems have 10 health instead. I want it to be easier to try and combo them. I want to also add
> stronger uncommon cards that are just better versions of the cards we usually play. More block, increased
> damage, better shield etc. This way we can incentive picking up the stronger version of these cards and start
> getting stronger decks. The goal will be to have a deck that is about 30 cards at the end of the run. I also
> want more pieces that work by comboing off each other so having cards reduce cost if you are next to a totem
> is a good start or reduce cost if you have shield, or reduce cost if you are standing next to another hero,
> stuff like that and add draw to those effects so we can get through the deck. I dont want the cards to be
> committed to the game yet, just testing for now to see what works.

## Standing answers

| Question | Answer | Settled by |
|---|---|---|
| Phases and time split | Cards only | kickoff message |
| Party | Knight + Cleric | kickoff message |
| Tiers | Normal + Hard III (evaluation arms) | stated as given |
| Start from | Current game | kickoff message |
| Source | Working tree | stated as given |
| What may change between rounds | The new cards and the pool's reward settings (all mirror only) | stated as given |
| Bans or forced picks | None | - |
| Deliverables beyond the standard set | Report page (Spire Card Lab II) | stated as given |
| Git | Nothing committed; cards in the bot mirror only | asked: "Bot mirror only" |

## Phase answers

### Phase 1: cards - a stronger Knight/Cleric reward pool (all of the time)

| Question | Answer | Settled by |
|---|---|---|
| Where the test cards live | Bot mirror only (BotRuns/claude-tools/cardlab2) | asked |
| Which totems get 10 health | Rare totems (Bloodmoon, Lodestone, Shield, Twin, Aegis, Reinforce, Warcry) plus any new upgraded totems; everything else stays at 1 | asked |
| Deck size target | About 25 cards at the end of a run, reached by raising card power so a big deck becomes the winning strategy, not by forcing the drafter | asked (free text) |
| How the stronger cards relate to the starters | Not "+" cards. The starter deck sits below the power curve; every draftable card is better than what the starter deck provides | asked (free text) |
| Combo shapes | Cost reduced while next to a totem / holding Shield / next to another hero, with draw | kickoff message |

## Assumptions made later

- Level-clear rewards were widened (40/50/10 Uncommon/Rare/Legendary) so the Cleric, with six Rares, ever sees
  an Uncommon.
- Rally the Line and Communion were removed in the L4 arm after rating as traps (the deck was built around them).
