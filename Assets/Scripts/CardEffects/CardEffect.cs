using UnityEngine;

public abstract class CardEffect : ScriptableObject
{
    /// <summary>
    /// Called when a card is played.
    /// The effect should queue one or more GameActions.
    /// </summary>
    public abstract void Resolve(ActionContext ctx);
}