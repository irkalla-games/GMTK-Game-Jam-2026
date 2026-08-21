using System;
using System.Collections.Generic;

/// <summary>
/// Facts about StatusType that do not belong to any one Status subclass - shared by three otherwise
/// unrelated callers instead of being re-derived by each.
/// </summary>
public static class StatusTypes
{
    /// <summary>
    /// True for GainMultiplier, Potency and TurnTick - the three types a totem projects to describe
    /// what it does to whoever stands in range, rather than a modifier the character holds. All three
    /// are parameterised over another status type, which is why they read
    /// AuraData.subject/magnitude/timing, why StatusEffect.Create refuses to build one (no card grants
    /// "Potency"), and why no character-facing readout should show them - see AuraData.HasSubject,
    /// which is built on this.
    /// </summary>
    public static bool IsTotemOnly(this StatusType type) =>
        type is StatusType.GainMultiplier or StatusType.Potency or StatusType.TurnTick;

    /// <summary>
    /// Every status worth putting in front of the player, in enum declaration order: None (the "never
    /// set" sentinel) and the three totem-only types above are excluded, Shield is not - callers that
    /// draw it elsewhere (SelectedCharacterPanel, HeroPortrait; HealthBarFill puts it on the bar) skip
    /// it themselves, same as they already did.
    ///
    /// Built once rather than walked fresh per call - Enum.GetValues allocates a fresh array every
    /// time, and this list is walked once per visible character on every resolved action.
    /// </summary>
    public static readonly IReadOnlyList<StatusType> Displayable = BuildDisplayable();

    private static IReadOnlyList<StatusType> BuildDisplayable()
    {
        List<StatusType> types = new();

        foreach (StatusType type in (StatusType[])Enum.GetValues(typeof(StatusType)))
        {
            if (type == StatusType.None || type.IsTotemOnly()) { continue; }

            types.Add(type);
        }

        return types;
    }
}
