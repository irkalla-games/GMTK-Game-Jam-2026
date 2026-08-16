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

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    /// Read off the prefab's own Character rather than authored twice here - the select screen and the
    /// battle portrait row are asking the same question, and two fields would drift.
    public Sprite Portrait => prefab != null && prefab.TryGetComponent(out Character character)
        ? character.Portrait
        : null;

    public GameObject Prefab => prefab;

    public IReadOnlyList<DeckData> Decks => decks;
}
