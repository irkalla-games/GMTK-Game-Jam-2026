# Cards, Statuses, and the Game Loop — Plan

Everything except manager wiring, which is in `manager-cleanup-plan.md` and should be done first.

Supersedes `tactics-deckbuilder-plan.tailored.md`, `class-cards-and-tuning-plan.md`,
`statuses-and-round-structure-plan.md`, and `game-loop-plan.md`. Corrections to the original
greenfield document `tactics-deckbuilder-plan.md` are in §8.

**The design in one line:** no turn order — player cards resolve immediately on play, any character in
any order — with a round boundary that exists only to refresh energy/armor and let enemies act on
telegraphed intents the player has spent the round disrupting.

---

## 1. Blockers

Five things are broken or missing in ways not visible from the card list. Nothing below works until
they're fixed.

### A. Four of six card effects are empty stubs

`DamageEffect` and `MoveEffect` are real `CardEffect` subclasses. **`ShieldEffect`, `DrawEffect`,
`HealEffect`, and `ParryEffect` are untouched Unity MonoBehaviour templates** — empty `Start()`/
`Update()`, deriving from `MonoBehaviour`, no `[CreateAssetMenu]`. They cannot be created as assets or
attached to a `CardData`. The card vocabulary today is *damage* and *move*, nothing else.

Their `GameAction` halves already exist and work, so each is ~15 lines following `DamageEffect`'s
shape. **Edit the stub files in place** — `CLAUDE.md` warns that deleting and recreating a `.cs`
changes its GUID.

### B. Armor does nothing

`Character.AddShield`, `GainBlock`, and `GainParry` are **empty method bodies** (`Character.cs:72-76`).
`ShieldAction` → `GridTile.GainShield` → `Character.AddShield` → nothing. Every armor card plays,
costs energy, animates, and has no effect. See §2.3.

### C. `Card.ResolveEffects` hands every effect the same target

`CLAUDE.md` states the intent: *"`ActionContext` is per action, not per card. That is what lets one
card aim its actions at different things: Bash damages the tile you picked but draws for whoever
played it."* The plumbing does not implement it:

```csharp
// Card.cs:64 - same target, every effect
foreach (var effect in effects) { effect.Resolve(new ActionContext(this, source, target)); }
```

**Steely Attack breaks on exactly this.** "Gain 5 armor, deal 5 damage" means damage on the enemy you
clicked and armor on *you*. As written the Knight armors the goblin it just hit. Fix in §3.2.

### D. `DrawAction` draws for the wrong character

```csharp
// DrawAction.cs:15-21 — the comment is right, the code does the opposite
//Drawing is about the character resolving the action, not about a tile on the board.
foreach (var target in ctx.targets) { target.DrawCards(drawAmount); }
```

It draws for whoever occupies the **targeted tile**. Quick Attack ("deal 9, draw 1") aimed at a goblin
draws a card *for the goblin* — and since every `Character` builds a deck, it silently succeeds.

```csharp
if (ctx.source != null) { ctx.source.DrawCards(drawAmount); }
```

This makes draw unaimable, which is correct for every card in both kits.

### E. `Assets/Scripts/CustomEditor.cs` will break player builds

It has `using UnityEditor;` and is **not** in an `Editor/` folder, so it compiles into
`Assembly-CSharp`. Move it to `Scripts/Editor/` before the first build. It's an editor-only class, so
nothing in a scene references it by GUID.

---

## 2. Statuses

### 2.1 Representation

Every temporary modifier is a status: buffs and curses alike, differing only in their numbers.

```csharp
/// Written into .asset files by StatusEffect — append only, never reorder.
/// None = 0 so an effect whose dropdown was never set does nothing loudly, rather than
/// silently granting whatever happened to be first. (Opposite of RangeShape.Anywhere = 0,
/// which had to preserve pre-existing assets. There are no status assets yet.)
public enum StatusType {
    None = 0,
    Strength = 1,           // buff  - +stacks to outgoing damage
    DoubleNextAttack = 2,   // buff  - x2 outgoing damage, spent by the next attack
    Poison = 3,             // curse - stacks damage at the start of each of your turns
    Frozen = 4,             // curse - cannot act at all
}

public enum StatusKind { Buff = 0, Curse = 1 }
```

**Two independent expiry mechanisms**, and a status may use either, both, or neither:

| | expires by | example |
|---|---|---|
| **Duration** | `turnsRemaining` ticking to 0 at `TurnStart` | Poison 3 turns, Frozen 1 turn |
| **Charge** | an event spending a stack | DoubleNextAttack, spent by an attack |
| **Neither** | `Indefinite` — lasts the whole combat | Strength |

Keeping these separate is what lets one type cover all four. Collapsing duration into stacks (the
Slay the Spire trick, where poison's stack count *is* its remaining turns) would make Strength and
Poison need different storage.

```csharp
/// One status on one character. Mutable and per-character - the enum is the type,
/// this is the copy, the same split as CardData/Card.
public class Status {
    public const int Indefinite = -1;

    public readonly StatusType type;
    public int stacks;           // magnitude, or remaining charges
    public int turnsRemaining;   // Indefinite never ticks down
}
```

`StatusDefinition` is a static lookup giving each type its unchanging metadata — `Kind` for UI
colouring and for future "remove all curses" effects, plus a display name. Static rather than a
ScriptableObject: nothing about "Poison is a curse" varies per asset, and an SO per status is four
files of ceremony for four constants.

On `Character`, matching the existing `readonly` collection pattern:

```csharp
private readonly List<Status> statuses = new();

public IReadOnlyList<Status> Statuses => statuses;
public int StatusStacks(StatusType type);
public void AddStatus(StatusType type, int stacks, int turns);   // stacks up if already present
public bool ConsumeStatus(StatusType type);                      // spends one charge
public void TickStatuses();                                      // called from TurnStart
```

`AddStatus` on an existing status adds stacks and takes the **longer** of the two durations, so
re-applying Poison never shortens it.

Enemies get statuses for free — both sides are `Character`.

### 2.1b What each type does, and where

- **Strength** — `+stacks` in `PreviewOutgoingDamage`. Indefinite.
- **DoubleNextAttack** — `×2`, spent in `ConsumeOutgoingDamage`. Charge-based, no duration.
- **Poison** — `TickStatuses` deals `stacks` damage at `TurnStart`, **bypassing armor** (it's not an
  attack). Then the duration decrements.
- **Frozen** — `Character.CanAct` returns false while any stack remains. For enemies, the brain is
  skipped and the AP is burnt; for players, `CardPlayManager.PlaySelectedOn` refuses **above the
  commit point** so a frozen character's click costs nothing. Frozen is an actor-state rule, not a
  targeting rule, so it does *not* belong in `Card.Refusal` — that answers "may this card go on this
  tile", and the answer here doesn't depend on the tile.

`CanAnyoneAct()` must also treat a frozen character as unable to act, or a fully frozen party stalls
`PlayerActing` forever.

### 2.2 The damage pipeline

Strength is an **attacker** property, but the chain loses the attacker:
`DamageAction.Execute → GridTile.DealDamage(int) → Character.TakeDamage(int)`.

Do **not** plumb the source through `GridTile.DealDamage` — tiles forward effects to their occupant
and have no business knowing attacker stats. Compute in `DamageAction`, where `ctx.source` already is:

```csharp
public override IEnumerator Execute(ActionContext ctx) {
    // ONCE per play, before the loop. An AoE must not consume the buff per tile,
    // nor double each tile independently.
    int amount = ctx.source != null ? ctx.source.ConsumeOutgoingDamage(damageAmount) : damageAmount;
    foreach (GridTile target in ctx.targets) { target.DealDamage(amount); }
    yield return new WaitForSeconds(ResolveDelay);
}
```

Zero signature changes elsewhere, and "cards target tiles" stays intact — the tile still just gets a
number.

**The consume/preview split.** `ConsumeOutgoingDamage` mutates: it spends the double. Anything calling
it to *display* a number destroys the buff without an attack happening. Two halves:

```csharp
/// Pure. Safe for tooltips, damage previews, enemy AI scoring.
public int PreviewOutgoingDamage(int amount) {
    amount += StatusStacks(StatusType.Strength);
    if (StatusStacks(StatusType.DoubleNextAttack) > 0) { amount *= 2; }
    return amount;
}

/// Mutating. Exactly one call site: DamageAction.Execute.
public int ConsumeOutgoingDamage(int amount) {
    amount += StatusStacks(StatusType.Strength);
    if (ConsumeStatus(StatusType.DoubleNextAttack)) { amount *= 2; }
    return amount;
}
```

**Order: additive first, then multiplicative.** `(base + strength) × 2`. Quick Attack at 9 with +3
Strength and a Buff deals `(9+3)×2 = 24`, not `(9×2)+3 = 21`. This makes Strengthen-then-Buff the
correct sequencing, which is the more interesting decision.

### 2.3 Armor

```csharp
[field: SerializeField, ReadOnlyField] public int Armor { get; private set; }

public void AddShield(int amount) { Armor += Mathf.Max(0, amount); }
public void ResetArmor() { Armor = 0; }

public void TakeDamage(int amount) {
    if (amount <= 0) { return; }
    int absorbed = Mathf.Min(Armor, amount);
    Armor -= absorbed;
    Health = Mathf.Max(0, Health - (amount - absorbed));
}
```

`[ReadOnlyField]` is the attribute already added for the `Health` readout — Armor gets the same
Inspector treatment free.

**Decaying, not persistent.** `BlockAction.cs:16` describes shield as "extra health," i.e. permanent.
That does not survive this card set: Steely Attack grants 5 armor for 1 energy, so the Knight banks 15
armor per round *while also dealing damage*. Persistent armor compounds and the Knight is unkillable
by round 3. Resetting at `TurnStart` is what makes "armor up or push damage?" a real choice.

`GainBlock` and `GainParry` stay empty and stay TODO — nothing in either kit needs per-instance
reduction or reflect. If they aren't wanted, delete `BlockAction`/`ParryAction` rather than leave
three half-built mitigation systems where the wrong one gets extended.

---

## 3. Card system additions

### 3.1 Class restriction

```csharp
/// Any = 0 so every card authored before this field deserializes as unrestricted —
/// the same load-bearing-int rule TargetRange/RangeShape already documents.
public enum CharacterClass { Any = 0, Knight = 1, Mage = 2 }
```

- `CardData`: `[field: SerializeField] public CharacterClass requiredClass { get; private set; }`
- `Character`: `[SerializeField] private CharacterClass characterClass;` + get-only property

**Not enforced in `Refusal`.** `CLAUDE.md` says anything that can refuse a play belongs there; class
is the exception. `Refusal` is asked on every tile click *and against every tile on the board* each
time a card is selected (`ShowPlayableTiles`). Class is static per card — re-deriving it 30 times per
selection is waste, and it produces the bad UX of a card in your hand that highlights nothing and
refuses every click. A Mage should never *hold* a Knight card.

Enforce where cards enter a deck instead:

```csharp
// Character.BuildDeck
if (data.requiredClass != CharacterClass.Any && data.requiredClass != characterClass) {
    Debug.LogWarning($"{name} ({characterClass}) cannot use {data.cardName} " +
                     $"({data.requiredClass}) - skipped");
    continue;
}
```

Plus `CardData.CanBeUsedBy(Character)` as the hook a future reward/draft screen filters on — which is
the real reason class restriction exists in a deckbuilder. Per-character authored decks already give
you class separation today; the enum catches mistakes and enables rewards.

### 3.2 Per-effect targeting — fixes blocker C

```csharp
// CardEffect
public enum EffectTarget { PlayedTile = 0, Source = 1 }
[SerializeField] private EffectTarget aimsAt;
public EffectTarget AimsAt => aimsAt;
```

```csharp
// Card.ResolveEffects
foreach (var effect in effects) {
    GridTile aim = effect.AimsAt == EffectTarget.Source ? source?.Tile : target;
    effect.Resolve(new ActionContext(this, source, aim));
}
```

`PlayedTile = 0` keeps existing assets working. Steely Attack becomes
`DamageEffect(5, PlayedTile)` + `ShieldEffect(5, Source)`.

**Refusal stays on the played tile.** `Card.Refusal` must keep asking about `target`, not the
aim-adjusted tile — otherwise a `Source`-aimed `ShieldEffect` refuses the play because the caster's
own tile is occupied (by the caster). Concretely: skip `Source`-aimed effects in `Card.Refusal`, or
Steely Attack becomes unplayable the moment `ShieldEffect` grows a refusal rule.

### 3.3 Shared side-checking

`DamageEffect` hand-rolls the side test (`DamageEffect.cs:35`); Heal, Strengthen, and Buff all need its
inverse. Four copies of one rule. Extract onto `CardEffect`:

```csharp
protected static string RefuseByOccupant(Character source, GridTile target, bool wantAlly) {
    Character occupant = target != null ? target.Occupant : null;
    if (occupant == null) { return "there is nobody there"; }
    if (source == null) { return null; }

    bool ally = occupant.IsPlayerControlled == source.IsPlayerControlled;
    if (wantAlly && !ally) { return $"{occupant.name} is not on your side"; }
    if (!wantAlly && ally) { return $"{occupant.name} is on your own side"; }
    return null;
}
```

`DamageEffect` becomes `RefuseByOccupant(source, target, wantAlly: canHitAllies)` — behaviour
preserved, including the off-by-default `canHitAllies` that existing assets rely on.

### 3.4 One generic status effect

```csharp
[CreateAssetMenu(menuName = "Card Effects/Apply Status")]
public class StatusEffect : CardEffect {
    [SerializeField] private StatusType status;
    [SerializeField] private int stacks = 1;
    [SerializeField] private bool alliesOnly = true;

    public override void Resolve(ActionContext ctx) =>
        ActionManager.Instance.AddAction(new StatusAction(status, stacks), ctx);

    public override string Refusal(Character source, GridTile target) =>
        RefuseByOccupant(source, target, wantAlly: alliesOnly);
}
```

`StatusAction : GameAction` holds `readonly` authoring numbers and forwards via
`GridTile.ApplyStatus(type, stacks)` → `Occupant.AddStatus(...)`. Strengthen and Buff are then the
same asset type with different numbers — no bespoke code for either.

---

## 4. The kits

All six cards cost 1.

### Knight — `requiredClass = Knight`, `TargetRange(Chebyshev, 1, 1)`

| Card | Effects |
|---|---|
| **Steely Attack** | `DamageEffect(5, PlayedTile)` + `ShieldEffect(5, Source)` |
| **Quick Attack** | `DamageEffect(9, PlayedTile)` + `DrawEffect(1)` |
| **Steely Resolve** | `ShieldEffect(8, Source)` + `DrawEffect(1)` — range `SelfTile` |

Chebyshev 1 is the 8 surrounding tiles, excluding your own. Steely Resolve touches nobody else, so it
uses `RangeShape.SelfTile`, which already exists for this.

Quick Attack is strictly better on offense (9 vs 5, both draw-neutral, Quick also cantrips). Steely
Attack's 5 armor pays for the 4 lost damage — near-parity when enemies hit for ~6, which only holds if
enemy damage stays in the 5–7 band (§5).

### Mage — `requiredClass = Mage`, `TargetRange(Chebyshev, 0, 5)`

| Card | Effects |
|---|---|
| **Heal** | `HealEffect(9, alliesOnly)` |
| **Strengthen** | `StatusEffect(Strength, 3, alliesOnly)` |
| **Buff** | `StatusEffect(DoubleNextAttack, 1, alliesOnly)` |

`minDistance = 0` so the Mage can self-cast all three.

**Range 5 is currently the whole board.** The grid is 5×6, and Chebyshev 5 covers essentially all 30
tiles from anywhere. "Range 5" reads as "anywhere" until the grid grows (§5 wants 7×7). Not a bug —
just don't tune around it being a real constraint yet. The highlight still behaves: `ShowPlayableTiles`
only blanks when a card is legal on *every* tile, and ally-only narrows these to the one or two tiles
holding allies — the same path Fireball already exercises.

**Buff persists until consumed**, which falls out free since nothing decays it. One consequence: it
consumes on the target's *next* damage card, not their best one. Buff the Knight, and if they play
Steely Attack (5) before Quick Attack (9), the doubling burns on the 5. That's real skill expression,
but it needs a **clearly visible status icon** or the first accidental waste reads as a bug.

---

## 5. Tuning

| | HP | Count |
|---|---|---|
| Player character | 60 | 2 (Knight, Mage) |
| Goblin Warrior | 34 | 3 |
| Goblin Archer | 24 | 3 |

`Character.maxHealth` defaults to 10. **Changing the default does not change instances already in the
scene** — serialized values win. Set these per prefab.

**The math.** Knight at 3 energy, all cards cost 1 → 3 plays/round. Ceiling 3× Quick Attack = 27
damage. Realistic Quick + Steely + Steely = 19 damage + 10 armor.

Enemy pool 3×34 + 3×24 = **174 HP**. With the Mage adding ~15/round the party clears ~35–42/round →
**a 4–5 round fight**.

**Enemy damage per attack is unspecified and it's the number that decides everything.** Six enemies ×
2 AP is up to 12 enemy actions against 6 player plays. At 6 damage with roughly half in range, that's
~36 incoming/round; a 60 HP Knight absorbing most of it dies in under 2 rounds naked, or ~4 with 10
armor/round — exactly long enough to win. **So enemy attacks want 5–7 damage.** Above 8 the Knight
can't armor fast enough; below 4 armor stops mattering and Quick Attack strictly dominates.

Two structural consequences of "many more enemies":

1. **5×6 = 30 tiles is crowded for 8 characters.** `MoveRefusal` rejects occupied tiles, so goblins
   will traffic-jam constantly — which makes partial-advance movement (§6.3) load-bearing rather than
   polish. Consider 7×7.
2. **6 player plays vs 12 enemy actions is a losing action economy.** Either the Mage gets AoE or the
   fight is unwinnable by attrition. `EffectPattern` (`Assets/Scripts/EffectPattern.cs`) is currently
   **dead code** — referenced by nothing but its own inspector — and is a bool-grid AoE stamp with a
   working editor. Finish it or delete it; don't leave a half-built targeting system beside a real one.

---

## 6. The game loop

```
TurnStart      refresh energy + armor on every character
               draw player characters back up to hand size
               each living enemy: Decide() once → telegraph that one action

PlayerActing   free-form. any character, any order, cards resolve immediately.
               ends on End Turn, or automatically when nobody can act

EnemyResolve   per enemy: step 1 executes the telegraphed intent as committed (may fizzle)
                          steps 2+ Decide() fresh against the live board (never fizzle)
               until AP spent or Decide() returns Wait

               check win/loss → TurnStart
```

A round, not a turn order. There is no ordering constraint among your characters.
`GameManager.SetActiveCharacter` is unchanged, and **`CardPlayManager.PlaySelectedOn` is unchanged** —
refusal, spend energy, resolve effects, discard, all inside the click.

### 6.1 What the commit rule removes

**The enemy's first action is the one it telegraphed and executes as committed, even if the player has
made it wrong. Everything after is decided fresh against the live board and is always valid.**

That deletes the most expensive machinery from the earlier drafts:

- **No `Clone()`, no `Apply()`, no multi-step planner.** Those existed to chain a simulated
  `[Move, Shoot]` up front. Only step 1 is committed; steps 2+ get recomputed anyway.
- **Telegraphs show one action.** Which is more honest — showing step 2 would promise something the
  enemy is going to re-decide regardless.

What survives is a **read-only** board view the brains query. Keep it a POCO so brains are testable
without a scene and can't mutate live state, but it needs no cloning. Lookahead ("is there a reachable
tile with a firing line?") is answered by `Flood()` + `HasLineTarget()` queries, not by simulation.

### 6.2 `BattleRunner`

New MonoBehaviour driving phases. `GameManager` keeps its current job and gains nothing.

```csharp
private IEnumerator RunBattle() {
    TurnsRemaining = turnsToSurvive;
    while (true) {
        yield return StartCoroutine(TurnStart());

        Phase = BattlePhase.PlayerActing;
        endTurnRequested = false;
        while (!endTurnRequested && CanAnyoneAct()) { yield return null; }

        yield return StartCoroutine(EnemyResolve());

        TurnsRemaining--;
        if (AllHeroesDead())     { Lose(); yield break; }
        if (TurnsRemaining <= 0) { Win();  yield break; }
    }
}
```

**Ending `PlayerActing`** — both triggers:

```csharp
private bool CanAnyoneAct() {
    foreach (Character c in GameManager.Instance.Characters) {
        if (c == null || !c.IsPlayerControlled || c.IsDead) { continue; }
        foreach (Card card in c.Hand) { if (c.CanAfford(card.cost)) { return true; } }
    }
    return false;
}
```

The condition is *"can anyone afford anything in hand"*, not *"is everyone at zero energy"* — a
character on 1 energy holding only 2-cost cards would otherwise stall the round forever, and an empty
hand falls out of the same check. Movement is a card, so there's no free fallback action.
`GameManager.characters` is private today; expose as `IReadOnlyList<Character>`.

**`TurnStart`** calls `ResetEnergy()` and `ResetArmor()` on every character. **This is the call site
`ResetEnergy` has been waiting for** — it exists and nothing calls it, so every playtest so far has
been one long turn. Hands top *up* (`handSize - Hand.Count`) rather than redrawing, so unplayed cards
carry over.

### 6.3 Enemy resolution

```csharp
foreach (Character e in LivingEnemies()) {
    for (int ap = 0; ap < e.ActionPoints && !e.IsDead; ap++) {
        Intent step = ap == 0 ? e.CommittedIntent : e.Brain.Decide(e, Board.Read());
        if (step.type == ActionType.Wait) { break; }

        yield return StartCoroutine(Execute(e, step));
        yield return new WaitUntil(() => ActionManager.Instance.IsIdle);
    }
}
```

Only step 1 can fizzle, in one of two shapes:

- **Blocked move → partial advance, not cancellation.** Walk the stored path, stop at the first
  occupied tile. `GridManager.MoveCharacter` already returns false and guards via `MoveRefusal`, so
  calling it per step gets the block check free. A goblin shouldering up against the Knight reads far
  better than one standing still.
- **Attack with nobody there → a visible miss.** Target died or left the line during `PlayerActing`.
  Play the swing, show the whiff.

Both cost 1 AP; neither cancels the turn. The enemy replans from step 2 and acts sensibly with what's
left — disruption costs the enemy one action, not a round of looking broken.

**The fizzle must be loud** — a whiff, a "?" pop, a flash. A goblin silently walking half as far as
promised reads as a bug rather than as your body-block working. Under this design it happens most
rounds.

**`ActionManager.IsIdle` is required.** `CLAUDE.md`: *"`StartCoroutine` runs synchronously up to the
first `yield`."* `AddAction` starts `ProcessQueue` inline, so the first action resolves inside the
call. Without gating, the next enemy acts mid-animation. Expose the existing private `isRunning`:
`public bool IsIdle => !isRunning && actions.Count == 0;`

### 6.4 Death and win/loss

- **Lose** when every `IsPlayerControlled` character `IsDead`. **Win** when `TurnsRemaining` hits 0.
- **Dead characters are not removed from tiles.** `Character.MoveTo` is the only thing that clears
  `GridTile.Occupant`, so a corpse permanently blocks its tile and soaks attacks. Add a death path
  calling `Tile.SetOccupant(null)` before enemies start pathing around bodies.

### 6.5 `GridTile.MoveCharacter` is a loaded gun

`GridSystem/GridTile.cs:66` forwards straight to `Occupant.MoveTo`, which swaps occupancy references
**without moving the transform** and without consulting `MoveRefusal`. No callers today — but it sits
in the row of tile forwarders where reaching for it is the natural move when wiring knockback. Delete
it or route it through `GridManager.Instance.MoveCharacter`.

---

## 7. Enemy AI

`EnemyBrain.Decide(self, readOnlyBoard)` returns one `Intent`, chosen by a prioritized rule list
written as literal ordered `if` checks. `Intent.target` is a `Vector2Int`, never a unit — `CLAUDE.md`:
*"Cards target tiles, never characters directly."*

**Warrior:** no target → Wait; adjacent to a hero → Attack; else move along the shortest path toward
the nearest hero, up to `moveRange`.

**Archer:** hero adjacent → move to a reachable tile not adjacent to any hero, preferring tiles that
also have a firing line, then tiles farthest from the nearest hero; hero in a straight line → attack
the *first* unit in that line; else move to the nearest reachable tile with a firing line; else Wait.
No facing — it fires along any of the 4 cardinals.

Brains score with `PreviewOutgoingDamage`, never `Consume` — a simulated attack must not spend a real
buff.

**Enemies need `actionPoints`.** Add `[SerializeField] private int actionPoints = 2;` to `Character`
with a get-only property, matching the `maxHealth`/`maxEnergy` pattern.

**Enemies have decks today.** Every `Character` runs `BuildDeck` in `Awake`, and `GameManager.Start`
deals opening hands to all of them. Either give enemies empty `deck` lists or skip non-player
characters when dealing — otherwise they hold cards nobody can see or play.

**Don't reuse `TargetRange` for pathing.** `TargetRange.Contains` is pure coordinate math answering
"may this card be aimed here"; it knows nothing about occupancy and deliberately never touches the
board. Movement needs BFS through free tiles. Note the metric mismatch: `RangeShape.Chebyshev` makes
diagonals cost the same as orthogonals and Move already uses it, so a 4-way BFS would disagree with
the Move card's own highlight. **8-way BFS matching Chebyshev is the smaller change** — settle this
before writing the brains, or they'll be tuned against the wrong reachability.

---

## 8. Corrections to `tactics-deckbuilder-plan.md`

The original was written greenfield. Where it conflicts with this repo:

- **§1 "no `MonoBehaviour` on anything that makes a decision."** The scene graph *is* the board here —
  `GridTile.Occupant` is the source of truth. Its stated reason (cloneable simulation) is moot under
  §6.1, which needs no cloning at all. Brains stay POCOs; everything else stays MonoBehaviours.
- **§3 player planning queue, reorder/cancel, `PlayerResolve`.** Deleted — player cards resolve
  immediately. This also deletes the stale-validation problem that a queue would have created.
- **§4 `Intent.targetUnit`.** Tiles, not units.
- **§8 "no DOTween yet."** Already in use and working (`GridManager.cs:95`, `CardPlayManager.cs:129`).
- **§8 "no raycasts, use `grid.WorldToCell`."** There is no Unity `Grid`/`Tilemap`. `GridManager`
  instantiates tile prefabs at its own `IsoToWorld`, and input arrives via `GridTile.OnMouseDown` →
  `GameManager.OnTileClicked`, which `CLAUDE.md` names as the one door for tile clicks. Keep colliders.
- **§8 Transparency Sort Axis.** Still applies — everything sits at `z = 0` with `y` carrying depth.
- **§8 `UnitData` ScriptableObject.** Skip it. `Character` serializes its stats directly and the prefab
  is the tuning surface; an SO layer for two heroes and two goblins is pure overhead.
- **§9 `Logic/View/Data` reorganization.** Don't. `CLAUDE.md`: deleting and recreating a `.cs` changes
  its GUID and breaks every scene/prefab reference. New files go into the existing folders.

Already built, don't rebuild: per-character decks/hands/energy, reshuffle-on-empty, `CardData`/`Card`
split, SO immutability, serialized tunables, hand size 5, energy 3.

---

## 9. Build order

`manager-cleanup-plan.md` first — the new effects should be written against `ActionManager.Instance`
rather than ported to it later.

**Blockers (no design content, nothing works without them):**
1. `DrawAction` → `ctx.source`. One line.
2. `ShieldEffect`, `DrawEffect`, `HealEffect` as real `CardEffect` subclasses (edit in place).
3. Armor on `Character`: `Armor`, `AddShield`, `ResetArmor`, absorbing `TakeDamage`.
4. Per-effect targeting: `EffectTarget`, aim-aware `ResolveEffects`, `Source`-aimed effects exempt
   from refusal.
5. Move `CustomEditor.cs` into `Scripts/Editor/`.

**Cards:**
6. `StatusType` + `statuses` dictionary + `AddStatus`/`StatusStacks`/`ConsumeStatus`.
7. `Preview`/`ConsumeOutgoingDamage`; wire Consume into `DamageAction` above the target loop.
8. `RefuseByOccupant` on `CardEffect`; refactor `DamageEffect` onto it.
9. `StatusAction` + `StatusEffect` + `GridTile.ApplyStatus`.
10. `CharacterClass` enum, `CardData`/`Character` fields, `BuildDeck` validation, `CanBeUsedBy`.
11. Author all six cards. Delete `Assets/Data/New Card.asset` first — `CLAUDE.md` notes it's broken
    and can no longer be instantiated, and it's what someone will try to duplicate.

**Loop:**
12. `BattlePhase`, `BattleRunner` skeleton, `TurnsRemaining`, win/loss stubs.
13. `GameManager.Characters` as `IReadOnlyList<Character>`; `ActionManager.IsIdle`.
14. `TurnStart`: `ResetEnergy` + `ResetArmor` + draw-up. **Highest value-per-line in the project** —
    energy currently never refreshes.
15. End Turn button + `CanAnyoneAct()` auto-end.
16. Death handling: clear `Occupant`, skip dead characters everywhere.
17. Fix/delete `GridTile.MoveCharacter`.

**Enemies:**
18. Read-only board view + `Intent` + `WarriorBrain.Decide` (one action, no simulation).
19. `EnemyResolve` with committed step 1 and live replanning after.
20. Partial-advance movement and the miss/fizzle beat.
21. `ArcherBrain`; telegraphs; status icons.
22. Retune HP on prefabs; set enemy damage to 6 and playtest the 5–7 band.

Steps 6–11 are testable with no round structure at all — play Strengthen on the Knight from the
current free-form mode and watch Quick Attack go 9 → 12. **Don't tune balance before step 14**: until
armor decays, the Knight is far tankier than §5 assumes.
