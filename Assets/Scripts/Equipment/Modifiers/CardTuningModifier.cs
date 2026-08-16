using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rewrites every card matching `filter` using the CardModifier vocabulary - Blasting Cap's "all Fire
/// cards gain a splash", Featherweight Grips' "Movement cards cost 1 less". Where equipment and an
/// upgraded card variant meet: the same CardModifier subclasses (CostModifier, AreaModifier,
/// MagnitudeModifier, ...) that CardData.upgradedForm authoring uses directly are what this applies at
/// runtime to every matching card in the wearer's deck.
///
/// Applied by Character.RefreshCardModifiers, which resets every held Card to its authored state before
/// re-running every equipped CardTuningModifier - see that method for why that makes re-equipping
/// mid-battle idempotent rather than compounding.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Card Tuning")]
public class CardTuningModifier : EquipmentModifier
{
    [SerializeField] private CardFilter filter;
    [SerializeField] private List<CardModifier> modifiers = new();

    public override void Apply(Card card, CardData data)
    {
        if (!filter.Matches(data)) { return; }

        foreach (CardModifier modifier in modifiers)
        {
            if (modifier != null) { modifier.Apply(card); }
        }
    }

    public override string Describe()
    {
        if (modifiers.Count == 0) { return "No change"; }

        List<string> lines = new();
        foreach (CardModifier modifier in modifiers)
        {
            if (modifier != null) { lines.Add(modifier.Describe()); }
        }

        return string.Join(", ", lines);
    }
}
