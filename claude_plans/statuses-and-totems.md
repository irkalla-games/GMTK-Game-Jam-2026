# Statuses with hooks, and auras emitted by Totems

> **Status: implemented** on branch `statuses-and-totems`. Three decisions changed during
> implementation and the text below reflects them:
> - `DamageInfo` is an immutable `readonly struct` that hooks *return* rather than mutate.
> - `PreventsActing` became `ActRefusal(carrier)`, so both gates return null-or-reason like every
>   other `Refusal` in the codebase.
> - A reflected parry goes back through `TakeDamage`, not `TakeUnblockableDamage` — so parries can be
>   parried, bounded by `Character.MaxParryBounces`.
> - `Status` became an abstract base with two halves: `StatusEffect` (carried by a character) and
>   `Aura` (projected by a totem, applies first). Same capabilities — `Aura` wraps a `StatusEffect` and
>   forwards every hook. This forced the old `StatusEffect : CardEffect` ScriptableObject to be renamed
>   `ApplyStatusEffect`, and `AuraPassiveBuff` to `AuraData`.
> - `Character.PlaceOnStartTile` no longer writes `transform.position`; `GridManager.PlaceCharacter`
>   owns it, alongside `MoveCharacter`.

## Context

`Character` has become the place where every combat rule is resolved. `TakeDamage` hard-codes the
Parry → Block → Shield order inline, `ComputeOutgoingDamage` hard-codes Strength and Double Attack,
`TickStatuses` special-cases Poison with `if (status.type == StatusType.Poison)`, and `CanAct`
special-cases Frozen. Every new status means editing `Character` again, and the four defensive numbers
(`Shield`, `BlockAmount`, `BlockCharges`, `ParryCharges`) are a second storage mechanism sitting beside
`statuses` that does the same job.

Auras have the same problem from the other side: `AuraSource` *pushes* `AppliedAura` entries into a list
on `Character` and has to diff membership on every resolved action to withdraw them again. The character
carries state it does not own, and the bookkeeping only exists because the model is push.

The outcome we want:

- `Status` is a behaviour, not a record. It has `OnDealDamage`, `OnTakeDamage`, `OnTurnStart`,
  `OnTurnEnd`. Shield, Block, Parry, Strength, Double Attack, Poison, Frozen and Rooted are all just
  statuses.
- `Character` runs the hooks and subtracts what survives them. It resolves nothing itself, and carries
  no Shield/Block/Parry of its own — not as fields, and not as derived properties either. Anything that
  wants one asks the status list for it by type.
- Auras are emitted by **Totems** with a range, and are *pulled*: a character asks which totems cover
  the tile it is standing on. No list of auras on `Character`, no membership diffing.
- Auras always apply before the character's own statuses.

## Design

### The status pipeline

`Status` becomes an abstract base carrying the same data it has now (`type`, `stacks`,
`turnsRemaining`) plus:

```csharp
// Notifications - something happened, react to it.
public virtual DamageInfo OnDealDamage(DamageInfo info) => info;
public virtual DamageInfo OnTakeDamage(DamageInfo info) => info;
public virtual void OnTurnStart(Character carrier) { }
public virtual void OnTurnEnd(Character carrier) { }

// Gates - asked before, repeatedly, and answered null-or-reason like every other Refusal here.
public virtual string ActRefusal(Character carrier) => null;                          // Frozen
public virtual string MoveRefusal(Character carrier, GridTile destination) => null;   // Rooted

public virtual void Merge(Status incoming) { /* current AddStatus stacking rule */ }

/// What a UI shows for this status. Virtual because Block has two numbers - "Block 5 x3", not
/// "Block x3" - and nothing outside the status should have to know that.
public virtual string Describe() => $"{type} x{stacks}";

public static Status Create(StatusType type, int stacks, int turnsRemaining) => /* switch */;
```

What each type implements:

| Status | Hook |
|---|---|
| Parry | `OnTakeDamage` — negate, `info.reflected += info.amount`, spend a charge |
| Block | `OnTakeDamage` — `info.amount -= amountPerHit`, spend a charge |
| Shield | `OnTakeDamage` — absorb into `stacks`; `OnTurnStart` — `stacks = 0` |
| DoubleNextAttack | `OnDealDamage` — `info.amount *= 2`, spend a charge if `info.consumeCharges` |
| Strength | `OnDealDamage` — `info.amount += stacks` |
| Poison | `OnTurnEnd` — `carrier.TakeUnblockableDamage(stacks)` |
| Frozen | `ActRefusal` — the carrier's whole turn is gone |
| Rooted | `MoveRefusal` — refuses every destination; everything else still works |

**Hooks run in FIFO order: the order the statuses sit on the character.** No priority key, no sort — a
status added earlier runs earlier. That is the simple thing, and it is deliberately *not* what the old
code did, so two documented numbers become order-dependent:

- Mitigation is no longer pinned to Parry → Block → Shield. Gain Shield before Block and the shield
  absorbs first, so Block's flat reduction comes off a smaller number and buys less.
- `ComputeOutgoingDamage` no longer guarantees `(9 x 2) + 3 = 21`. Double Attack applied first gives
  `(9 x 2) + 3 = 21`; Strength first gives `(9 + 3) x 2 = 24`.

Both are acceptable for now and worth knowing before tuning against them. If either starts to matter, a
single `virtual int Order` on `Status` plus a stable sort in `ActiveStatuses()` restores fixed ordering
without touching any individual status.

`BlockStatus` is the one with an extra number: `stacks` is the charge count, and a public
`amountPerHit` field holds the reduction. Its `Merge` override keeps the higher `amountPerHit` and adds
the charges — exactly `Character.GainBlock`'s current rule, moved.

### Frozen stuns, Rooted holds you in place

Two separate statuses, deliberately orthogonal:

- **Frozen** takes the whole turn. `ActRefusal` feeds `Character.CanAct`, which
  `CardPlayManager.PlaySelectedOn` checks **above the commit point** so a frozen click costs nothing,
  and which `BattleManager.EnemyResolve` checks to skip a frozen enemy entirely. Frozen is an
  actor-state rule, not a targeting one — it does *not* belong in `Card.Refusal`, which answers "may
  this card go on this tile", and the answer here doesn't depend on the tile. `CanAnyoneAct()` must
  keep treating a frozen character as unable to act, or a fully frozen party stalls `PlayerActing`
  forever.
- **Rooted** takes only movement. The carrier still plays cards, still swings — it just cannot walk.

Frozen needs no `MoveRefusal` of its own: a character who cannot act cannot play a Move card either.
The two hooks stay separate so the statuses compose rather than one being a superset of the other.

Rooted lands in `GridManager.MoveRefusal`, already documented as the one place every rule about where a
character may move lives, and consulted from both ends:

```csharp
public static string MoveRefusal(Character character, GridTile destination)
{
    ...existing occupancy checks...

    foreach (Status status in character.ActiveStatuses())
    {
        string refusal = status.MoveRefusal(character, destination);
        if (refusal != null) { return refusal; }
    }

    return null;
}
```

That one insertion buys everything, because `MoveEffect.Refusal` forwards straight to it:
`Card.Refusal` refuses the click **above the commit point** so a rooted move costs no energy;
`GridManager.ShowPlayableTiles` is built from the same call so the Move card lights nothing, which is
what makes the root *visible* on the board; and `EnemyBrain.TryFindMove` filters on `card.Refusal`
too, so a rooted enemy stops proposing walks and falls through to attacking with no brain change.

This is a `MoveRefusal` rather than a void `OnMove` for that reason — the answer is needed before the
move happens, not as a notification after it. The name matches the `Card.Refusal` /
`CardEffect.Refusal` / `GridManager.MoveRefusal` convention the codebase already runs on.

Known looseness, pre-existing and not made worse by Rooted: `CanAnyoneAct()` asks "can anyone *afford*
anything in hand", not "is any play legal". A rooted character holding only Move cards keeps the turn
alive until End Turn — exactly as one boxed in by bodies already does.

New `DamageInfo` (plain class, `Assets/Scripts/Statuses/DamageInfo.cs`) is the mutable thing hooks pass
around: `attacker`, `target`, `amount`, `reflected`, `negated`, `consumeCharges`.

### Auras are pulled from Totems

`Character.ActiveStatuses()` builds a **fresh** list each call:

1. every aura a totem is projecting onto this character, as a fresh `Status` built via `Status.Create`
2. then this character's own `statuses`, in the order they were gained

No sorting — construction order *is* hook order, which is what makes "auras always apply before
statuses" true by building the list that way rather than by enforcing it afterwards.

Fresh instances matter twice. They make "auras apply before statuses" fall out of construction order,
and they mean a hook that spends a charge on an aura-granted status mutates a throwaway — an aura is
maintained by its totem, so it cannot deplete while you stand in it. A fresh `List` per call rather than
a reusable buffer, because a Poison tick can kill the carrier and re-enter through `Died`.

### Totem

`AuraSource` is **renamed in place** to `Totem` — `git mv` both `AuraSource.cs` and `AuraSource.cs.meta`
so the script GUID `9a37933c48214d343b8ebefaa1dd1204` survives and `Cleric.prefab` (which is what
`SummonTotem.asset` summons) keeps its authored range, Strength x1 buff and on-Damage reaction. Field
names `range` / `passiveBuffs` / `reactions` stay identical for the same reason. It keeps
`[RequireComponent(typeof(Character))]`: the totem takes its origin tile and its side from the Character
it rides, so it can be summoned, occupy a tile and be killed like anything else.

What changes inside it:

- a `static readonly List<Totem> active` registry, maintained in `OnEnable`/`OnDisable`, plus
  `static void CollectAuras(Character, List<Status>)` walking it
- `bool Covers(Character)` — `range.Contains(owner.Tile, character.Tile)` and the side test
- `RefreshMembership`, `charactersInRange` and the `Died` subscription all **delete**. Pull needs none
  of it; a dead totem's `Covers` returns false and its auras vanish on their own.
- the `ActionResolved` subscription stays, but only for `TryReact` — reactions are a separate working
  feature and are not in scope for removal
- new serialized `AuraAudience affects` with `Allies = 0`, so the existing prefab deserializes to
  today's allies-only behaviour. `Enemies = 1` is what a poison-cloud totem wants.

## What each status is for, game-wise

The numbers below are what is actually authored today: Knight 30 HP / 3 energy, Priest 10 HP / 3
energy, most cards cost 1 (Quick Attack and Summon Skeleton Warrior cost 0), so a character gets
roughly three plays a turn. Enemies get **1 action point each**: Skeleton Warrior 34 HP hitting for 5
in melee, Ranger 34 HP shooting for 3 at range 6. Player offense is Slash 9 melee, Fireball 5 at range
5, Quick Attack 3 melee for free, Steely Attack 5 damage + 5 Shield.

**The single fact that shapes all three defenses: enemy hits are small (3–5) and frequent.**

### Shield — a pool of extra health, wiped every turn

*Cards: Shield (8), Steely Resolve (8 + draw), Steely Attack (5 alongside 5 damage).*

Soaks total damage regardless of how it arrives, then it is gone. Wiped at TurnStart, which is what
makes "armor up or push damage?" a real question every turn instead of a pile you bank. Against
5-damage melee, 8 Shield eats one and a half hits.

Its niche is **burst**: it does not care whether the 8 arrives as one hit or four, so it is the answer
to being swarmed. It is also the only defense that comes attached to an attack (Steely Attack), so it
is the one the Knight gets to use without giving up a turn of offense.

### Block — a flat cut off each of the next N hits

*Card: Block (5 x3).*

Per-hit, not pooled: Block 5 against three 5-damage skeletons negates all three outright. Against
3-damage arrows it negates them too. **Against the current enemy damage band, Block 5 x3 at 1 energy
prevents up to 15 where Shield at the same price prevents 8** — and unlike Shield, its charges are not
wiped at TurnStart, so leftover charges carry into the next round.

That makes Block the strongest-per-energy defense in the deck right now, and it gets *better* the more
enemies there are, which is the wrong direction for a survive-N-turns game with waves. Two levers if it
proves too strong: give the Block status a duration so unspent charges expire at end of turn like
Shield, or drop `amountPerHit` below the 5-damage melee hit so it reduces rather than negates.

### Parry — negate the hit and throw it back

*Card: Parry (x2).*

Full denial plus reflection: two 5-damage melee hits parried is 10 prevented **and 10 dealt back**, a
20-point swing for 1 energy. Reflected damage goes through `TakeUnblockableDamage`, so it cannot itself
be parried or blocked.

Design role is the **melee punish** — it reads as "come at me" and it is the only defense that kills
things. It is deliberately bad against the Ranger, whose 3-damage arrows waste a charge each. Same
carry-over caveat as Block: charges persist across turns, so Parry is currently a strictly better
Shield for 1 energy against melee. Worth a duration or a higher cost.

**Net ordering as authored: Parry > Block > Shield at the same price.** Shield's only advantages are
that it comes stapled to Steely Attack and that it does not care about hit count. That is a real
balance question, not a bug in this refactor — the refactor just makes all three legible side by side
for the first time.

### Strength — permanent, additive, compounds

*Card: Strengthen (+3, indefinite, ally within 5).*

Adds to **every** attack for the rest of the fight, so its value is (bonus x attacks remaining). Front-
load it: cast on turn 1 it is worth far more than cast on turn 5, which is a clean "invest early"
decision that costs you tempo exactly when you can least afford it.

It scales with *frequency*, not size — +3 on the free Quick Attack (3 → 6) doubles it, while on Slash
(9 → 12) it is a third. So Strength wants the character throwing the most cheap attacks.

### Double Next Attack — one-shot multiplier, wants the biggest hit

*Card: Buff (ally within 5).*

Doubles the card's own number, then Strength is added on top — the reason that order was chosen is to
keep Double Attack's value tied to the card it lands on rather than scaling with however much Strength
has piled up. On Slash that is 9 → 18; on Quick Attack only 3 → 6.

**The known trap: it spends on the target's *next* attack, not their best one.** Buff the Knight, and
if they play Quick Attack before Slash the doubling burns for +3. That is real skill expression — you
have to sequence — but the first accidental waste reads as a bug, so it needs a clearly visible status
icon before playtesting. Note under FIFO the total is now sequencing-dependent too (21 or 24 depending
on which buff landed first).

### Poison — chip damage nothing can stop

*New. Suggested asset: 3 stacks, 3 turns, `alliesOnly: 0`.*

Deals its stacks at the end of the carrier's own phase and **bypasses Shield, Block and Parry entirely**
by routing through `TakeUnblockableDamage` — those are defenses held up against something thrown at
you, and they do nothing about something already in your blood.

Its job is to be the answer to the thing your damage cannot solve: a target that armors up every turn,
or one you cannot reach. It is also slow and total-damage-cheap, so it should never compete with Slash
on raw numbers — 3 x 3 turns = 9, the same as one Slash, but unstoppable and paid over time.

Two consequences of the timing: poison on an enemy ticks during EnemyResolve *after* that enemy has
acted, so it never denies an action; and re-applying takes the longer duration, so a second dose
extends rather than refreshing from scratch.

### Frozen — the target loses its whole turn

*Card: Freeze (1 stack, 1 turn, enemies).*

Total denial for one of the target's own turns: no attack, no move, nothing.

**Its value is asymmetric, and that is the thing to watch.** Enemies have 1 action point, so freezing
one denies exactly one action — 5 damage from a Skeleton Warrior, 3 from a Ranger. Against a player
character it denies 3 energy worth of plays, roughly three cards. So the same card is ~3x stronger
pointed at the party than at the enemy, which matters the moment an enemy deck gets a Freeze.

Priced against the alternatives it is currently modest: 1 energy to deny ~5 damage, where Block at the
same cost prevents up to 15. Its real job in a survive-N-turns game is **tempo, not damage** — enemies
have 34 HP and killing them is slow, so buying a turn is often worth more than the arithmetic suggests.
Freezing a Warrior still walking toward you delays contact entirely; freezing one already adjacent just
denies its 5.

If Freeze needs to be stronger, the lever is duration (2 turns) rather than magnitude — stacks do
nothing for a status whose effect is binary.

### Rooted — positional denial, everything else still works

*New. Suggested asset: 1 stack, 1 turn, enemies.*

The other half of Frozen, split out: the target still attacks and still plays cards, it just cannot
walk. Where Frozen is a blunt tempo tax, Rooted is a **counter to enemies whose plan requires moving**,
which is exactly what the two brains express:

- The **Ranger** wants to stand at the edge of its reach and retreats the moment something is adjacent.
  Root it, then step into melee: it cannot back off, so it keeps plinking for 3 while you Slash for 9.
  This is the strongest use of the card, and it is a genuine tactical read rather than a stat check.
- The **Warrior** only ever attacks-if-adjacent or walks toward you. Root one that is out of reach and
  it does *nothing at all* that turn — strictly better than Freeze there. Root one already adjacent and
  you have wasted the card, where Freeze would have denied its 5 damage.

So the two are not redundant: Freeze is reliable and flat, Root is situational and swingy, and knowing
which to hold is the decision. Root should be the cheaper of the two.

Because the Move card's highlight goes dark for a rooted character, the effect is legible on the board
rather than hidden in a stat panel — worth keeping when tuning, since Frozen has no such tell and will
need an icon.

On a player character Root is the mirror: the party survives by kiting, and a rooted Knight eats the
melee it was walking away from.

## Files

**New** — `Assets/Scripts/Statuses/`: `DamageInfo.cs`, `StrengthStatus.cs`, `DoubleNextAttackStatus.cs`,
`PoisonStatus.cs`, `FrozenStatus.cs`, `RootedStatus.cs`, `ShieldStatus.cs`, `BlockStatus.cs`,
`ParryStatus.cs`.

Plus two `StatusEffect` assets in `Assets/Scripts/CardEffects/EffectData/` (+ `.meta` each), copying
the format of the existing `Freeze.asset` (script guid `b038c3d54fa6e1b458a5586895a2e465`,
`mainObjectFileID: 11400000`):

- `Poison.asset` — `status: 3, stacks: 3, turnsRemaining: 3, alliesOnly: 0`
- `Root.asset` — `status: 8, stacks: 1, turnsRemaining: 1, alliesOnly: 0`

`Freeze.asset` already exists (`status: 4, stacks: 1, turnsRemaining: 1, alliesOnly: 0`) and needs no
change.

**Renamed** — `Assets/Scripts/Aura/AuraSource.cs` (+ `.meta`) → `Totem.cs` (+ `.meta`), via `git mv`.

**Deleted** — `Assets/Scripts/Statuses/AppliedAura.cs` (+ `.meta`). Nothing references it once the pull
model lands.

**Modified**

- `Assets/Scripts/Statuses/Status.cs` — abstract base, hooks, `MoveRefusal`, `Merge`, `Describe`,
  `Create`. Keep `Indefinite = -1` and `IsExpired`.
- `Assets/Scripts/GridSystem/GridManager.cs` — `MoveRefusal` walks the mover's statuses after its
  existing occupancy checks. This is the only place Rooted is enforced.
- `Assets/Scripts/Statuses/StatusType.cs` — append `Shield = 5, Block = 6, Parry = 7, Rooted = 8`.
  Append only; these ints are written into `.asset` files.
- `Assets/Scripts/Character/Character.cs` — the bulk of the change, below.
- `Assets/Scripts/GridSystem/GridTile.cs` — `GainShield` / `GainBlock` / `GainParry` forward to
  `AddStatus` instead of to removed `Character` methods.
- `Assets/Scripts/Aura/AuraPassiveBuff.cs` — doc comment only; the "Shield/Block/Parry don't suit an
  aura" caveat is now "charge-spending statuses never deplete inside an aura".
- `Assets/Scripts/MainSystems/BattleManager.cs` — `ResetShield()` → `OnTurnStart()` at line ~440;
  `TickStatuses()` → `OnTurnEnd()` at line ~596; update the class doc and the `TickStatuses(bool)`
  cross-references. The `if (!enemy.CanAct)` frozen-skip in `EnemyResolve` (~line 500) and the
  `CanAct` test in `CanAnyoneAct()` both stay exactly as they are — `CanAct` keeps its meaning.
- `Assets/Scripts/MainSystems/SelectedCharacterPanel.cs` — currently the only reader of
  `Character.Shield` / `BlockCharges` / `BlockAmount` / `ParryCharges`, so it has to change. Same
  layout, same serialized TMP fields, no scene rewiring: `healthText` uses
  `StatusStacks(StatusType.Shield)`, `blockText` and `parryText` use
  `FindStatus(Block/Parry)?.Describe() ?? ""`, and `statusesText` lists everything from
  `ActiveStatuses()` via `Describe()` — skipping the three that already have their own line, and now
  showing totem auras too. Which stats deserve a dedicated line is the panel's business; that it has to
  ask the list for them is the point.
- `CLAUDE.md` — new conventions section; correct the "Not implemented yet" paragraph that currently says
  mitigation lives in `Character.TakeDamage`.

### Character, specifically

Deleted outright: the `auraEffects` list, `ApplyAura`, `RemoveAura`, `AddShield`, `GainBlock`,
`GainParry`, `ResetShield`, `ConsumeStatus` (its only caller was `ComputeOutgoingDamage`), and
`Shield` / `BlockAmount` / `BlockCharges` / `ParryCharges` **entirely** — the serialized backing fields
*and* any replacement property. A `Shield` getter on `Character` would still be Character claiming to
have a shield; it does not, it has a status list that may contain one.

Everything that survives is generic over the list, so a status added later needs no new accessor:

```csharp
public List<Status> ActiveStatuses()               // aura copies first, then own, FIFO
public int          StatusStacks(StatusType type)  // sums matching stacks across ActiveStatuses
public Status       FindStatus(StatusType type)    // the instance, or null - for UI and Describe()
```

`UpdateHealthBar` keeps showing shield in the world-space bar, but reads it as
`StatusStacks(StatusType.Shield)` — a query over the list, not a stat on the character.

Rewritten:

```csharp
public bool CanAct => !IsDead && !AnyStatus(s => s.PreventsActing);   // Frozen

public void TakeDamage(int amount, Character attacker = null)
{
    if (amount <= 0) { return; }

    DamageInfo info = new(attacker, this, amount);

    foreach (Status status in ActiveStatuses())
    {
        status.OnTakeDamage(info);
        if (info.negated) { break; }
    }

    PruneExpired();

    // Reflected before the hit lands, same order Parry had inside TakeDamage.
    if (info.reflected > 0 && attacker != null) { attacker.TakeUnblockableDamage(info.reflected); }

    Health = Mathf.Max(0, Health - info.amount);
    UpdateHealthBar();
    CheckDeath();
}

public int ComputeOutgoingDamage(int amount, bool shouldConsume)   // signature unchanged
{
    DamageInfo info = new(this, null, amount) { consumeCharges = shouldConsume };
    foreach (Status status in ActiveStatuses()) { status.OnDealDamage(info); }
    if (shouldConsume) { PruneExpired(); }
    return info.amount;
}

public void OnTurnStart()   // replaces ResetShield
public void OnTurnEnd()     // replaces TickStatuses: run hooks, then age durations on own statuses only
```

`AddStatus` gains an overload `AddStatus(Status)` that merges via `Status.Merge` — that is how
`GridTile.GainBlock` passes Block's two numbers through. The existing
`AddStatus(StatusType, int, int)` stays and routes through `Status.Create`. Both end with
`UpdateHealthBar()` so gaining Shield refreshes the readout.

`TakeUnblockableDamage` is unchanged and deliberately runs **no** hooks — that is what makes Poison and
a reflected parry bypass mitigation, and what stops a parry being parried.

Aura durations do not tick: `OnTurnEnd` ages `statuses` only, never the throwaway aura copies.

## Card coverage

Every status needs a card that applies it, in a deck somebody actually plays. Three gaps turned up
that predate this work: `Freeze.asset` existed but no card referenced it, and the `Block` and `Parry`
cards existed but were in nobody's deck. `SummonCleric` was likewise unplayable, so the totem could
never appear in a battle.

The scene runs off the six characters placed directly in `Game.unity` (`levelData` is unassigned, so
`SpawnParty`/`SpawnEnemies` are skipped), and no scene instance overrides its prefab `deck` — so
editing the prefabs is what reaches play.

| Status | Card | Deck |
|---|---|---|
| Shield | Shield, Steely Attack, Steely Resolve | Knight (already) |
| Block | Block | **Knight, x2 added** |
| Parry | Parry | **Knight, x2 added** |
| Strength | Strengthen | Priest (already) |
| DoubleNextAttack | Buff | Priest (already) |
| Poison | **Poison Dart** (new, 1 energy, range 1–5) | **Priest, x2 added** |
| Frozen | **Freeze** (new, 2 energy, range 1–5) | **Priest, x2 added** |
| Rooted | **Entangle** (new, 1 energy, range 1–5) | **Priest, x2 added** |
| *(totem auras)* | Summon Cleric | **Priest, x2 added** |

All three new cards are `requiredClass: Any` and target enemies only (`alliesOnly: 0` on the effect,
so `RefuseByOccupant` rejects allies and empty tiles). Freeze is priced above Entangle deliberately -
see the game-wise notes above.

## Verification

1. `powershell -ExecutionPolicy Bypass -File Tools/compile-check.ps1` — exit 0. Focus the Unity Editor
   first if `Assembly-CSharp.csproj` doesn't list the new files.
2. In the Editor, confirm `Cleric.prefab` still shows a **Totem** component with range Chebyshev 0–2,
   one Strength x1 buff and its Damage reaction intact — a "Missing (Mono Script)" there means the meta
   rename didn't take.
3. Play the battle scene and check each path:
   - **Shield** — play `Shield` (8 Shield) on the Knight, confirm the health bar reads `10/10 (8)`,
     take a hit and watch shield absorb before health, then confirm it is gone after the next TurnStart.
   - **Block** — `Block 5x3`, take three hits of 8, expect three hits of 3 through, fourth hit full.
   - **Parry** — `Parry x2`, take a hit, expect zero damage taken and the attacker losing that amount.
   - **FIFO** — with all three up, confirm the hit runs them in the order they were gained, and that a
     Parry gained first negates outright and spends only the parry charge.
   - **Strength / Double Attack** — on a 9-damage card, Double Attack applied first lands 21 and
     Strength first lands 24. Both are correct under FIFO; check the panel's damage readout does not
     consume the Double charge just by being shown.
   - **Frozen** — play `Freeze` on an enemy and confirm `EnemyResolve` skips it outright, then that it
     thaws after one of its own turns. Freeze a player character and confirm every card is refused
     above the commit point (no energy spent), and that a fully frozen party ends `PlayerActing`
     instead of hanging it.
   - **Rooted** — play `Root` on an enemy. It must still *attack* if something is in reach, but the
     Move card must light no tiles for it and its brain must fall through to attacking rather than
     walking. Root the Ranger, step adjacent, and confirm it cannot retreat. On a player character,
     confirm the Move highlight goes dark and a click costs no energy.
   - **Poison** — wire the new `Poison.asset` onto a card, apply it, confirm damage at the end of the
     carrier's own phase, that Shield does *not* absorb it, and that it expires on schedule.
   - **Totem aura** — summon the Cleric, step an ally in and out of its 2-tile radius, and confirm the
     ally's damage rises and falls with no card played. Kill the Cleric and confirm the bonus is gone
     immediately.
4. Confirm nothing regressed at the seams: the enemy telegraph still draws, `CanAnyoneAct` still ends a
   turn when the whole party is frozen, and a character dying to Poison still drops loot on its tile.
