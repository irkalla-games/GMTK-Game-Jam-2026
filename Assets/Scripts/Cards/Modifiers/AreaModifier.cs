using UnityEngine;

/// <summary>
/// Rewrites the footprint of matching entries - Blasting Cap's "all Fire cards gain a splash", or an
/// upgraded Fireball's bigger blast.
///
/// Skips any entry whose effect does not support one: MoveEffect.SupportsArea is false because
/// MoveAction throws if it is ever handed more than one tile, and this modifier has no way to know that
/// is why a particular entry was left untouched - it just quietly leaves it Single, same as
/// Card.ResolveEffects already does for any area a card is authored with directly.
/// </summary>
[CreateAssetMenu(menuName = "Card Modifiers/Area")]
public class AreaModifier : CardModifier
{
    [SerializeField] private EntrySelector on;
    [SerializeField] private AreaShape area;

    public override void Apply(Card card)
    {
        for (int i = 0; i < card.EntryCount; i++)
        {
            CardEffectEntry entry = card.GetEntry(i);

            if (entry.effect == null || !on.Matches(entry, i)) { continue; }
            if (!entry.effect.SupportsArea) { continue; }

            entry.area = area;
            card.SetEntry(i, entry);
        }
    }

    public override string Describe() => area.Kind switch
    {
        AreaKind.Single => "Single target",
        AreaKind.Radius => $"Area: {area.Radius}",
        AreaKind.Pattern => "Area: pattern",
        _ => "Area changed",
    };
}
