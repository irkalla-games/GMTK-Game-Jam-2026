using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Base for every card asset. This is the card *type* - it is shared by every copy in a deck, so
/// nothing here may be written to at runtime (in the Editor those writes persist into the .asset
/// file). Per-copy and per-run state belongs on Card.
///
/// The fields are private + read-only properties for that reason: the Inspector can author them,
/// code can't assign them. They can't be const or readonly - Unity's serializer skips both, and the
/// field would disappear from the Inspector.
///
/// Shared presentation lives here; the numbers a card actually does live on the subclass, one per
/// card (see Bash).
/// </summary>
public abstract class CardData : ScriptableObject
{

    [SerializeField] private string cardName;
    [SerializeField] private int cost;
    [SerializeField] private string description;
    [SerializeField] private Sprite image;

    public string CardName => cardName;
    public int Cost => cost;
    public string Description => description;
    public Sprite Image => image;

    /// <summary>
    /// The actions this card performs, each paired with its own context. Building them here rather
    /// than serializing them is what keeps actions stateless, and pairing each with a context is what
    /// lets a single card hit different things with different actions.
    /// </summary>
    /// <param name="source">The character playing the card.</param>
    /// <param name="target">The tile the player picked.</param>
    public abstract IEnumerable<(GameAction action, ActionContext ctx)> CreateActions(Character source, Tiles target);

}
