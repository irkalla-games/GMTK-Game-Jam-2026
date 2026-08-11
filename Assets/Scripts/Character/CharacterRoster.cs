using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every character the select screen may offer, and how large a party it allows. Not a field on
/// RunData: RunData describes a run - which levels, who you start as - and is heading toward being
/// built at runtime by a level generator, while a roster is authored content about who exists at all.
/// A generator has no business producing "who exists"; it only ever picks from it.
/// </summary>
[CreateAssetMenu(menuName = "Character/Character Roster")]
public class CharacterRoster : ScriptableObject
{
    [SerializeField] private List<CharacterOption> characters = new();

    [SerializeField] private int minPartySize = 2;

    [SerializeField] private int maxPartySize = 4;

    public IReadOnlyList<CharacterOption> Characters => characters;

    public int MinPartySize => Mathf.Max(1, minPartySize);

    public int MaxPartySize => Mathf.Max(MinPartySize, maxPartySize);
}
