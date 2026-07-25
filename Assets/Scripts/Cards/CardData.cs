using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

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
[CreateAssetMenu(menuName = "CardData")]
public class CardData : ScriptableObject
{

    [field: SerializeField] public string cardName { get; private set; }
    [field: SerializeField] public int cost { get; private set; }
    [field: SerializeField] public string description { get; private set; }
    [field: SerializeField] public Sprite image { get; private set; }
    [field: SerializeField] public List<CardEffect> effects { get; private set; }

    //public string CardName => cardName;
    //public int Cost => cost;
    //public string Description => description;
    // public Sprite Image => image;

    // public List<CardEffect> Effects => effects;

}
