using UnityEngine;

/// <summary>
/// Where an effect lands, relative to the tile the player clicked.
///
/// PlayedTile = 0 so every effect asset authored before this field existed keeps aiming where it
/// always did.
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
    [Tooltip("Where this effect lands. Source aims it at the caster instead of the clicked tile, "
             + "which is how one card can damage an enemy and buff its own player.")]
    [SerializeField] private EffectTarget aimsAt;

    public EffectTarget AimsAt => aimsAt;

    /// <summary>
    /// Called when a card is played.
    /// The effect should queue one or more GameActions.
    /// </summary>
    public abstract void Resolve(ActionContext ctx);

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
