# Manager Cleanup — Plan

Prerequisite for `game-loop-plan.md`: `BattleRunner` needs a clean way to reach `ActionManager`, and
adding a sixth manager on top of the current wiring makes the tangle permanent.

Pure refactor. No behaviour changes, no new features. Do it in one sitting while there are only six
call sites to fix.

---

## 1. The problem, concretely

Five managers, three different ways of being reachable:

| Manager | How you reach it |
|---|---|
| `GameManager` | `Singleton<GameManager>` |
| `CardPlayManager` | `Singleton<CardPlayManager>` |
| `CardHoverManager` | `Singleton<CardHoverManager>` |
| `CreateCardViewer` | `Singleton<CreateCardViewer>` |
| `GridManager` | hand-rolled `public static Instance` + its own `Awake` |
| `ActionManager` | **not reachable at all** — only via `CardPlayManager.Instance.actionManager` |
| `HandViewer` | serialized separately into two different managers |

The consequence is visible in every effect asset:

```csharp
// DamageEffect.cs:14, MoveEffect.cs:8
CardPlayManager.Instance.actionManager.AddAction(new DamageAction(damageAmount), ctx);
```

An effect reaches through the *player's card UI* to find the action queue. That's fine today because
only players play cards. It stops being fine the moment enemies queue actions — enemy resolution
would depend on `CardPlayManager`, which has nothing to do with it and may legitimately have no
selection, no hand, and no active character.

---

## 2. Changes

### 2.1 `ActionManager` becomes a singleton

```csharp
public class ActionManager : Singleton<ActionManager>
```

Then:

```csharp
// DamageEffect, MoveEffect, and every future effect
ActionManager.Instance.AddAction(new DamageAction(damageAmount), ctx);
```

Two call sites today (`DamageEffect.cs:14`, `MoveEffect.cs:8`), plus the four effects still to be
written (`ShieldEffect`, `DrawEffect`, `HealEffect`, `StatusEffect`). Doing this now means those four
are written against the right API instead of being ported later.

### 2.2 Drop the three redundant serialized fields on `CardPlayManager`

Delete `gameManager`, `actionManager`, `handViewer`. Replace with:

- `gameManager.ActiveCharacter` → `GameManager.Instance.ActiveCharacter` (2 uses: lines 54, 105)
- `actionManager` → gone entirely, see 2.1
- `handViewer` → `HandViewer` becomes a singleton too, or is reached via `GameManager`. Prefer the
  singleton: `HandViewer` is a scene-level service like the rest, and `GameManager` shouldn't become
  a lookup table for other managers.

**Keep `discardAnchor` and `discardDuration`.** The anchor is a world position a designer places —
exactly the thing serialization is for. The duration is a real tunable.

After this, `CardPlayManager`'s serialized block is two fields, both of which earn their place.

### 2.3 `GridManager` uses `Singleton<GridManager>`

It currently hand-rolls the identical logic:

```csharp
// GridManager.cs:21-32 - this is Singleton<T>.Awake, copied
private void Awake() {
    if (Instance != null) { Destroy(gameObject); return; }
    Instance = this;
    CreateGrid();
}
```

Becomes:

```csharp
public class GridManager : Singleton<GridManager> {
    protected override void Awake() {
        base.Awake();
        CreateGrid();
    }
}
```

**Check the ordering carefully.** `Singleton<T>.Awake` destroys the duplicate and returns, but it does
not stop the derived `Awake` from continuing — `base.Awake()` returning does not return from the
override. A duplicate `GridManager` would build a second grid before dying. Guard it:

```csharp
protected override void Awake() {
    base.Awake();
    if (Instance != this) { return; }
    CreateGrid();
}
```

This bug is latent in every subclass that overrides `Awake`, not just this one — worth checking
`CardHoverManager` and `CreateCardViewer` for the same shape while you're in here.

### 2.4 Action timing becomes tunable

Seven files hardcode `yield return new WaitForSeconds(0.15f)`: `Block`, `Damage`, `Draw`, `Heal`,
`Move`, `Parry`, `Shield`. That is the pacing you will actually retune, and none of it is reachable
without a recompile.

Put it on `GameAction` once:

```csharp
public abstract class GameAction {
    /// How long the queue pauses after this action, so effects read one at a time instead of
    /// all landing on the same frame. Virtual so a slow action (a multi-tile move) can lengthen it.
    protected virtual float ResolveDelay => ActionManager.Instance.DefaultResolveDelay;
}
```

with `[SerializeField] private float defaultResolveDelay = 0.15f;` on `ActionManager`. One number, one
Inspector field, all seven actions follow it.

If that feels like over-plumbing for a jam, the smaller version is a single `public const float` — but
then it is still not tunable at runtime, which was the whole complaint.

---

## 3. Two bugs in `Singleton<T>` worth fixing while here

```csharp
protected virtual void OnApplicationQuit() {
    Instance = null;
    Destroy(gameObject);   // <-- pointless, and actively harmful
}
```

**`Destroy(gameObject)` on quit.** The scene is being torn down anyway. Destroying objects during
shutdown can fire `OnDestroy` on other components after their dependencies are already gone, which
produces null-reference spam in the console at exactly the moment it's least useful. Drop the
`Destroy`; keep `Instance = null`.

**No `OnDestroy` clearing `Instance`.** On a scene reload the static still points at the old, destroyed
object. Unity's overloaded `==` makes `Instance != null` evaluate false for a destroyed object, so the
next instance does claim the slot and this mostly self-heals — but any code holding the stale
reference in a local sees a fake-null it didn't expect. Add:

```csharp
protected virtual void OnDestroy() { if (Instance == this) { Instance = null; } }
```

---

## 4. Order

1. `Singleton<T>`: drop `Destroy` from `OnApplicationQuit`, add `OnDestroy`.
2. `ActionManager : Singleton<ActionManager>`; update `DamageEffect` + `MoveEffect`.
3. `HandViewer : Singleton<HandViewer>`; drop the serialized field from `CardPlayManager` and
   `GameManager`.
4. Drop `gameManager` and `actionManager` fields from `CardPlayManager`; switch to `.Instance`.
5. `GridManager : Singleton<GridManager>`, with the `Instance != this` guard.
6. Audit `CardHoverManager` / `CreateCardViewer` for the same `Awake` override hazard.
7. `ResolveDelay` on `GameAction`; replace the seven hardcoded `0.15f`s.

Steps 1–5 are mechanical and independently verifiable with `Tools/compile-check.ps1`.

**Re-wire the scene after each of 2–5.** Deleting a serialized field drops the Inspector link; the
`.Instance` path replaces it, but any *other* component still pointing at the removed field will
silently read null. Play-test between steps rather than at the end.
