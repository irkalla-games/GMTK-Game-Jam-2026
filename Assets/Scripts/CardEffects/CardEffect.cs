using UnityEngine;

public abstract class CardEffect : ScriptableObject
{
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
    /// </summary>
    public virtual string Refusal(Character source, GridTile target) => null;
}