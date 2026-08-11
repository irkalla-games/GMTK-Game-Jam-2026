using UnityEngine;

/// <summary>
/// Where an effect lands, relative to the tile the player clicked.
///
/// PlayedTile = 0 so every effect asset authored before this field existed keeps aiming where it
/// always did. Lives here rather than nowhere because AuraReaction and CardEffectEntry both need the
/// vocabulary, even though the aim choice itself now lives on CardEffectEntry, not on this asset - see
/// that struct's doc comment for why.
/// </summary>
public enum EffectTarget
{
    /// The tile the card was played on.
    PlayedTile = 0,

    /// The acting character's own tile, whatever they clicked. Steely Attack damages the enemy you
    /// picked but armors *you*.
    Source = 1,
}

public abstract class CardEffect : ScriptableObject
{
    /// <summary>
    /// Called when a card is played.
    /// The effect should queue one or more GameActions.
    /// </summary>
    public abstract void Resolve(ActionContext ctx);

    /// <summary>
    /// False for an effect that can only ever mean one tile - Move, principally, since MoveAction
    /// throws if it is handed more than one target. Card.ResolveEffects forces an entry back to
    /// AreaKind.Single before building its footprint when this is false, no matter what area the card
    /// was authored with, so a Move entry can never be handed a multi-tile ActionContext.
    /// </summary>
    public virtual bool SupportsArea => true;

    /// <summary>
    /// Why this effect could not land on `target`, or null if it can. Asked before the card is paid
    /// for, so a refusal costs the player nothing.
    ///
    /// Range is not checked here - that is the card's rule, on CardData. This is for rules only the
    /// effect knows, like Move being unable to land on a tile somebody is already standing on.
    ///
    /// Note `target` is always the *played* tile, even for a Source-aimed effect. See Card.Refusal.
    /// </summary>
    public virtual string Refusal(Character source, GridTile target) => null;

    /// <summary>
    /// The shared "is there somebody there, and are they the right somebody" rule. Damage wants an
    /// enemy, heals and buffs want an ally, and hand-rolling that in each of them is four copies of
    /// one comparison waiting to disagree.
    ///
    /// Sides are just "player controlled or not" - there are no teams beyond that yet.
    /// </summary>
    protected static string RefuseByOccupant(Character source, GridTile target, bool wantAlly)
    {
        Character occupant = target != null ? target.Occupant : null;

        if (occupant == null) { return "there is nobody there"; }

        if (source == null) { return null; }

        bool ally = Character.AreAllies(occupant.Affiliation, source.Affiliation);

        if (wantAlly && !ally) { return $"{occupant.name} is not on your side"; }

        if (!wantAlly && ally) { return $"{occupant.name} is on your own side"; }

        return null;
    }
}
