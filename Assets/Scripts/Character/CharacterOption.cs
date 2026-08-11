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

    [Tooltip("Shown on the select screen. Character has no portrait field of its own - identity there "
             + "is a sprite rig, not a single image - so this is new, select-screen-only art.")]
    [SerializeField] private Sprite portrait;

    [Tooltip("Prefab this option builds an instance from. Needs a Character component.")]
    [SerializeField] private GameObject prefab;

    [Tooltip("Starting decks this character may be given. Leave empty to fall back to the prefab's own "
             + "authored deck (Character.AuthoredDeck) with no select-screen deck choice at all.")]
    [SerializeField] private List<DeckData> decks = new();

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    public Sprite Portrait => portrait;

    public GameObject Prefab => prefab;

    public IReadOnlyList<DeckData> Decks => decks;
}
