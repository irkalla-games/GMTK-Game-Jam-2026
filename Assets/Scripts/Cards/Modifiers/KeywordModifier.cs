using UnityEngine;

/// <summary>
/// Adds or strips a keyword - an upgraded Bash losing its Cooldown, an upgraded card gaining Innate.
/// None on either side means "do nothing" on that side, so a modifier can be authored to only add or
/// only remove.
/// </summary>
[CreateAssetMenu(menuName = "Card Modifiers/Keyword")]
public class KeywordModifier : CardModifier
{
    [SerializeField] private CardKeywordType add;
    [SerializeField] private int addMagnitude;

    [SerializeField] private CardKeywordType remove;

    public override void Apply(Card card)
    {
        if (remove != CardKeywordType.None) { card.RemoveKeyword(remove); }
        if (add != CardKeywordType.None) { card.AddKeyword(add, addMagnitude); }
    }

    public override string Describe()
    {
        if (add != CardKeywordType.None && remove != CardKeywordType.None) { return $"-{remove} +{add}"; }
        if (add != CardKeywordType.None) { return $"+{add}"; }
        if (remove != CardKeywordType.None) { return $"-{remove}"; }
        return "No change";
    }
}
