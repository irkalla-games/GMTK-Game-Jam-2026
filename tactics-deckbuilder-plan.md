# Iso-Grid Tactics Deckbuilder — Implementation Plan

A Slay-the-Spire-like deckbuilder played on a 2D isometric grid, with multiple
player characters and multiple enemies. Unity, C#. The developer is a strong
backend engineer but new to Unity, so prefer explicit, readable code over Unity
idioms that rely on editor magic.

---

## 1. Non-negotiable architectural rule

**All game logic lives in plain C# classes. No `MonoBehaviour` inheritance on
anything that makes a decision.**

- `Battlefield`, `Unit`, `Card`, `Deck`, `Intent`, `EnemyBrain`, `Planner` —
  all POCOs, no Unity types except `Vector2Int`.
- `MonoBehaviour`s are a thin view layer: they read logic state and draw it,
  and they translate input into calls on the logic layer.
- Rationale: the enemy planner must be able to `Clone()` the battlefield and
  simulate moves. That is trivial on POCOs and infeasible on a scene graph.
  It also makes the AI unit-testable without opening the editor.

Grid coordinates are `Vector2Int` everywhere. Isometry is *purely a rendering
concern*. The only two places world-space appears are `CellToWorld` (drawing)
and `WorldToCell` (mouse input).

---

## 2. Core rules

### Player
- Two characters: **Knight** (melee) and **Mage** (ranged/support).
- Each character has its own deck, hand, and energy.
- Hand size 5, energy 3 per round, 10 starting cards per character.
- Unplayed cards are discarded at end of round.
- When the draw pile empties, shuffle the discard pile back into it.

### Enemies
- **Goblin Warrior** — melee.
- **Goblin Archer** — ranged, fires in a straight line, hits the first unit in
  that line.
- Enemies do not use cards or energy. They spend **action points (AP)**,
  a serialized per-unit-type value (start with 2).

### Win / lose
- A **turn counter** starts at N and decrements at end of round.
- Player **wins** when it reaches 0 (survival objective).
- Player **loses** if both heroes die.

---

## 3. Round structure

Implemented as a coroutine-driven state machine.

```
RoundStart  → EnemyPlanning → PlayerPlanning → PlayerResolve → EnemyResolve → RoundEnd → (loop)
```

**RoundStart**
- Reset each character's energy to 3.
- Draw each character back up to 5 cards (reshuffle discard if needed).
- Clear last round's telegraph visuals.

**EnemyPlanning**
- Each enemy produces a *plan*: an ordered `List<Intent>` (see §5).
- Each plan is telegraphed on screen, numbered (① move, ② shoot).

**PlayerPlanning**
- The only phase that blocks on input.
- Player clicks a card, then a target. The action is appended to a **single
  shared FIFO queue** covering both characters — interleaving Knight and Mage
  actions is the core tactical decision, so one queue, not one per character.
- Energy is deducted at queue time so the UI stays truthful.
- The player **must be able to reorder and cancel** queued entries before
  committing. This is not optional polish; getting the order wrong is constant
  and re-picking every card to fix one entry is miserable.
- Ends when the player presses End Turn.

**PlayerResolve**
- Dequeue and execute one action at a time, each yielding on its animation.

**EnemyResolve**
- Execute enemy plans in the order they were generated, with invalidation
  handling (§6).

**RoundEnd**
- Discard all remaining hand cards.
- Decrement turn counter. Check win/lose.

### Driver sketch

```csharp
IEnumerator RunBattle() {
    while (true) {
        StartRound();
        yield return StartCoroutine(EnemyPlanningPhase());

        planningDone = false;
        while (!planningDone) yield return null;   // End Turn sets this

        yield return StartCoroutine(ResolvePlayerQueue());
        yield return StartCoroutine(ResolveEnemyPlans());

        DiscardHands();
        turnsRemaining--;
        if (turnsRemaining <= 0) { Win(); yield break; }
        if (AllHeroesDead())     { Lose(); yield break; }
    }
}
```

---

## 4. Data model

```csharp
public enum ActionType { Move, Attack, Wait }

public struct Intent {
    public ActionType type;
    public List<Vector2Int> path;   // step-by-step, excludes the start tile
    public Unit targetUnit;
}

public struct PlannedAction {       // player side
    public Unit actor;
    public Card card;
    public Vector2Int targetTile;
    public Unit targetUnit;
}
```

**Store the whole path, not just the destination.** Otherwise you cannot
distinguish "destination blocked" from "someone is standing halfway along the
route," and partial movement is required behaviour (§6).

`Battlefield` needs:
- `Clone()` — deep enough that simulated moves and damage don't touch live state.
- `Apply(Intent)` — mutate the simulated board.
- `Flood(start, maxSteps)` → `Dictionary<Vector2Int,int>` — BFS distances.
  Use BFS, not A*; the grids are small and BFS is ~20 lines.
- `IsOccupied`, `IsWalkable`, `NearestPlayer`, `AnyPlayerAdjacent`,
  `FirstTargetInLine`, `HasLineTarget`.

---

## 5. Enemy AI

### Single-step decision (per brain)

`EnemyBrain.Decide(self, field)` returns one `Intent`, chosen by a **prioritized
rule list**. Write the priorities as literal ordered `if` checks — readability
here matters more than cleverness.

**Warrior**
1. No target at all → Wait.
2. Adjacent to a hero → Attack.
3. Otherwise → Move along the shortest path toward the nearest hero, up to
   `moveRange`.

**Archer**
1. A hero is adjacent (melee threat) → Move to a reachable tile that is not
   adjacent to any hero; among those, prefer tiles that also have a firing line,
   then tiles farthest from the nearest hero.
2. A hero is in a straight line → Attack the *first* unit in that line
   (shots stop at the first body or wall).
3. Otherwise → Move to the nearest reachable tile that has a firing line.
4. Nothing available → Wait.

**Archers have no facing.** They may fire along any of the 4 cardinal
directions. Facing adds a rotation action and player confusion for little
tactical gain; it can be added later if desired.

### Multi-step planning

Plans are produced by running `Decide` in a loop against a simulated board:

```csharp
public List<Intent> Plan(Unit self, Battlefield field, int ap) {
    var sim = field.Clone();
    var simSelf = sim.Get(self.id);
    var plan = new List<Intent>();

    for (int i = 0; i < ap; i++) {
        var step = Decide(simSelf, sim);
        if (step.type == ActionType.Wait) break;
        plan.Add(step);
        sim.Apply(step);
    }
    return plan;
}
```

Sequencing emerges rather than being hand-written: an archer with no firing line
moves on iteration 1, the sim applies that move, and on iteration 2 it now has a
line and shoots — producing `[Move, Shoot]` with no special-case code.

---

## 6. Invalidation and priority

Player actions resolve before enemy actions, so **an enemy's plan may be stale
by the time it executes.**

### Player priority
If a hero moves onto a tile an enemy intended to occupy, the hero wins. The
enemy is blocked.

### Partial movement
A blocked move is **not** cancelled outright — the enemy advances along its path
until it hits an occupied tile, then stops. A goblin shouldering up against the
Knight reads far better than one standing still.

```csharp
IEnumerator ExecuteMove(Unit enemy) {
    foreach (var step in enemy.intent.path) {
        if (field.IsOccupied(step)) break;
        yield return StartCoroutine(enemy.StepTo(step));
    }
}
```

### Invalid intent → always costs 1 AP, then replan
This is a **uniform rule with no exceptions**. Whether the destination got
blocked, the target died, or the target stepped out of the firing line, the
enemy burns the action point and re-plans the remainder with what's left.

```csharp
IEnumerator ResolveEnemy(Unit e) {
    int spent = 0;
    while (spent < e.actionPoints && e.plan.Count > 0) {
        var step = e.plan[0];
        e.plan.RemoveAt(0);

        if (IsValid(e, step)) {
            yield return Execute(e, step);
        } else {
            yield return ShowFizzle(e);
            e.plan = e.brain.Plan(e, field, e.actionPoints - spent - 1);
        }
        spent++;
    }
}
```

Consequence to accept: a dodged archer whiffs, repositions with its remaining
AP, and fires next round. This is intended.

### No tile reservations
Because plans are re-computed mid-turn, pre-reserving destination tiles during
the planning phase does not survive contact. **Check live occupancy at each
execution step instead.** Enemy-vs-enemy collisions resolve through the same
fizzle rule as player blocks.

---

## 7. Telegraph visuals

- Archer: a `LineRenderer` (or stretched sprite) from the archer's tile to the
  tile where the shot stops, with a marker on the first unit hit.
- Warrior: a chain of arrow tiles along the path, plus a crosshair on the target.
- Multi-step plans are **numbered** (①, ②) so ordering is legible.
- When a recompute fires, play a visible beat — a "?" pop, a flash, the new
  intent sliding in. Players tolerate an enemy changing plans if they see the
  moment it happens; a silent swap reads as a bug.
- Telegraphs render above the floor tilemap but below units.

---

## 8. Unity setup

- **Grid + Tilemap**, layout set to Isometric.
- **Transparency Sort Axis**: Project Settings → Graphics → Transparency Sort
  Mode = Custom Axis, axis `(0, 1, 0)`. Without this, sprites draw in the wrong
  order and units behind walls render on top of them.
- **Input**: no physics raycasts needed.
  `grid.WorldToCell(Camera.main.ScreenToWorldPoint(Input.mousePosition))`
  gives the hovered tile directly.
- **UI**: uGUI Canvas for hand, energy pips, turn counter, End Turn button.
  Do not use UI Toolkit.
- **Animation**: `Vector3.Lerp` inside coroutines. No DOTween yet.

### ScriptableObject vs Prefab

| Thing | Type |
|---|---|
| Card definition (cost, damage, range, art) | ScriptableObject |
| Unit stats (HP, moveRange, attackRange, AP) | ScriptableObject |
| Goblin warrior / archer in scene | Prefab |
| Knight / Mage in scene | Prefab |
| Card in hand (UI element) | Prefab |
| Telegraph line / path arrow | Prefab |

Rule: has a position on screen → prefab. Is a tunable number → ScriptableObject.

**Never mutate a ScriptableObject at runtime.** SOs are assets; edits persist
after play mode ends. `CardData` is the template, the runtime `Card` is a
separate instance. Same for `UnitData` vs the live `Unit` holding `currentHp`.

All tunables (`moveRange`, `attackRange`, damage, AP, starting turn counter)
must be `[SerializeField]` or SO fields, never constants. They will be retuned
dozens of times.

---

## 9. Suggested file layout

```
Scripts/
  Logic/            # plain C#, no UnityEngine except Vector2Int
    Battlefield.cs
    Unit.cs
    Card.cs  Deck.cs
    Intent.cs
    Brains/  EnemyBrain.cs  WarriorBrain.cs  ArcherBrain.cs
    Planner.cs
  Data/             # ScriptableObjects
    CardData.cs  UnitData.cs
  View/             # MonoBehaviours
    BattleRunner.cs      # the phase state machine
    UnitView.cs
    GridView.cs
    TelegraphView.cs
    HandUI.cs  QueueUI.cs
  Tests/
    ArcherBrainTests.cs
    WarriorBrainTests.cs
```

---

## 10. Build order

1. Grid + `Battlefield` + BFS flood fill, with a console-only test harness.
2. Units on the grid, `CellToWorld` rendering, transparency sort axis.
3. Warrior brain, single-step, no cards — enemies chase and hit a dummy hero.
4. Archer brain, single-step, including line-of-fire and retreat rules.
5. Unit tests for both brains (flanked archer, blocked warrior, no-target).
6. Multi-step planner with `Clone()`/`Apply()`.
7. Phase state machine and turn counter.
8. Cards, energy, hands, draw/discard/reshuffle.
9. Player action queue with reorder/cancel.
10. Telegraph visuals and recompute feedback.
11. Win/lose conditions and tuning pass.

Steps 1–6 need no UI at all and should be validated with tests.

---

## 11. Open design questions

- **Does a blocked warrior lose its attack?** With shared AP, a blocked
  approach costs the swing entirely, which may make body-blocking mandatory
  rather than merely strong. Alternative: separate move and attack budgets so a
  blocked enemy still swings if it ends up adjacent. Playtest both.
- **Starting turn counter value** — unset; tune once combat resolves end to end.
- **Mage kit** — support/heal role implied but not yet specified.
