using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Casts an aura around the Character it is attached to: a passive buff for whichever allies currently
/// stand in range, and one-shot reactions that fire when a resolved action belongs to an ally standing
/// in range.
///
/// Both halves ride ActionManager.ActionResolved rather than a per-turn tick. A tick-based refresh would
/// only pick up who is in range at the end of a phase, so summoning this mid-turn and immediately
/// stepping an ally into range - or attacking from inside it - would not take effect until the next
/// tick. Reacting to every resolved action instead means membership and triggers are both evaluated
/// against the board as it actually stands, the moment anything happens anywhere.
/// </summary>
[RequireComponent(typeof(Character))]
public class AuraSource : MonoBehaviour
{
    [SerializeField] private TargetRange range;

    [Tooltip("Buffs kept on every ally currently standing in range. Applied the instant they enter, "
             + "withdrawn the instant they leave - see Character.ApplyAura/RemoveAura.")]
    [SerializeField] private List<AuraPassiveBuff> passiveBuffs = new();

    [Tooltip("One-shot effects fired at whichever ally triggers them, the moment a matching action "
             + "resolves while they stand in range.")]
    [SerializeField] private List<AuraReaction> reactions = new();

    private Character owner;

    private readonly HashSet<Character> charactersInRange = new();

    private void Awake()
    {
        owner = GetComponent<Character>();
    }

    /// <summary>
    /// Subscriptions live in Start, not Awake, on the same reasoning as Character.PlaceOnStartTile:
    /// every Awake in the scene runs before any Start, so ActionManager.Instance is guaranteed set up
    /// by the time this runs - Awake would not have that guarantee.
    /// </summary>
    private void Start()
    {
        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved += HandleActionResolved; }
        if (owner != null) { owner.Died += HandleOwnerDied; }

        // Catches allies already standing in range when this aura is summoned mid-turn, rather than
        // waiting for the next action to resolve anywhere on the board.
        RefreshMembership();
    }

    private void OnDisable()
    {
        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved -= HandleActionResolved; }
        if (owner != null) { owner.Died -= HandleOwnerDied; }
    }

    private void HandleActionResolved(GameAction action, ActionContext ctx)
    {
        RefreshMembership();
        TryReact(action, ctx);
    }

    /// Withdraws every buff this aura is holding when its own source drops, rather than leaving them
    /// stuck on characters who are still, technically, standing in a dead aura's range.
    private void HandleOwnerDied(Character _)
    {
        foreach (Character character in charactersInRange) { character.RemoveAura(this); }
        charactersInRange.Clear();
    }

    /// <summary>
    /// Diffs who is currently in range against last time this ran, applying passiveBuffs to anyone
    /// newly in and withdrawing them from anyone newly out.
    ///
    /// Walking the whole roster on every resolved action is not cheap in the abstract, but this game's
    /// roster is small and actions are already throttled by ActionManager's resolve delay, so it is not
    /// worth a more targeted "did the mover cross my boundary" check.
    /// </summary>
    private void RefreshMembership()
    {
        if (passiveBuffs.Count == 0 || owner == null || owner.Tile == null || BattleManager.Instance == null)
        {
            return;
        }

        foreach (Character character in BattleManager.Instance.Characters)
        {
            bool inRange = character != null && !character.IsDead && character.Tile != null
                && range.Contains(owner.Tile, character.Tile)
                && Character.AreAllies(owner.Affiliation, character.Affiliation);

            bool wasInRange = charactersInRange.Contains(character);

            if (inRange && !wasInRange)
            {
                charactersInRange.Add(character);
                foreach (AuraPassiveBuff buff in passiveBuffs) { character.ApplyAura(this, buff.Type, buff.Stacks); }
            }
            else if (!inRange && wasInRange)
            {
                charactersInRange.Remove(character);
                character.RemoveAura(this);
            }
        }
    }

    /// Fires every reaction whose trigger matches the resolved action, provided it belongs to an ally
    /// standing in range right now.
    private void TryReact(GameAction action, ActionContext ctx)
    {
        if (reactions.Count == 0 || owner == null || owner.Tile == null) { return; }

        Character triggeringCharacter = ctx.source;
        if (triggeringCharacter == null || triggeringCharacter.Tile == null) { return; }
        if (!range.Contains(owner.Tile, triggeringCharacter.Tile)) { return; }
        if (!Character.AreAllies(owner.Affiliation, triggeringCharacter.Affiliation)) { return; }

        foreach (AuraReaction reaction in reactions)
        {
            if (!reaction.Matches(action) || reaction.Effect == null) { continue; }

            ActionContext reactionCtx = new(card: null, source: triggeringCharacter, target: triggeringCharacter.Tile);
            reaction.Effect.Resolve(reactionCtx);
        }
    }
}
