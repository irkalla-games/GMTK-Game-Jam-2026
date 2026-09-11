using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One character offered at the select screen: who they look like, which prefab an instance is built
/// from, and which starting decks they may take into a run. CharacterRoster is a list of these; picking
/// one for a party slot plus one of its decks is what CharacterSelectPanel builds a RunData.PartyEntry
/// from.
///
/// prefab is a GameObject rather than a Character for the same reason RunData.StartingParty is - a
/// ScriptableObject's Inspector object picker lists only main assets, and a prefab's main asset is its
/// GameObject, so a Character-typed field here would offer an empty picker. Resolved to a Character
/// exactly once, at RunManager.Begin, same as StartingParty already is.
///
/// Also the unit character unlocks are tracked against - see CharacterUnlocks. A hero the player has
/// not earned yet still exists as an asset and can be authored and playtested; Locked only governs
/// whether the select screen will start a run with them.
/// </summary>
[CreateAssetMenu(menuName = "Character/Character Option")]
public class CharacterOption : ScriptableObject
{
    [SerializeField] private string displayName;

    [Tooltip("Prefab this option builds an instance from. Needs a Character component.")]
    [SerializeField] private GameObject prefab;

    [Tooltip("Starting decks this character may be given. Leave empty to fall back to the prefab's own "
             + "authored deck (Character.AuthoredDeck) with no select-screen deck choice at all.")]
    [SerializeField] private List<DeckData> decks = new();

    [Tooltip("Off by default on purpose, same reasoning as DeckData.locked: a CharacterOption "
             + "authored before this field existed must deserialize to \"playable\", not \"hidden\". "
             + "On: the select screen shows this hero greyed out and refuses to start a run with "
             + "them until CharacterUnlocks says otherwise.")]
    [SerializeField] private bool locked;

    [Tooltip("Stable id CharacterUnlocks saves against. Leave blank to fall back to this asset's "
             + "name - but renaming the asset later then orphans anyone's save, so a hero meant to "
             + "ship locked should have this set explicitly.")]
    [SerializeField] private string unlockId;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    /// Read off the prefab's own Character rather than authored twice here - the select screen and the
    /// battle portrait row are asking the same question, and two fields would drift.
    public Sprite Portrait => prefab != null && prefab.TryGetComponent(out Character character)
        ? character.Portrait
        : null;

    public GameObject Prefab => prefab;

    public IReadOnlyList<DeckData> Decks => decks;

    /// Whether this hero has to be earned before a run may start with them - see the field's tooltip
    /// for why false is the only safe default.
    public bool Locked => locked;

    /// Stable save id, falling back to the asset name. Same contract as DeckData.UnlockId.
    public string UnlockId => string.IsNullOrWhiteSpace(unlockId) ? name : unlockId;
}
