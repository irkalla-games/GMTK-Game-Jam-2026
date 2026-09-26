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

    [Tooltip("The smallest party a Debug build's size buttons offer.")]
    [SerializeField] private int minPartySize = 2;

    [Tooltip("The largest party a Debug build's size buttons offer.")]
    [SerializeField] private int maxPartySize = 4;

    [Tooltip("The party size the game is balanced for - the only one a Playable build offers, with the "
             + "size buttons hidden, and the one a Debug build opens at. Change this, not Min/Max, to "
             + "move the whole game to another size.")]
    [SerializeField] private int playablePartySize = 2;

    public IReadOnlyList<CharacterOption> Characters => characters;

    public int MinPartySize => Mathf.Max(1, minPartySize);

    public int MaxPartySize => Mathf.Max(MinPartySize, maxPartySize);

    /// Clamped into MinPartySize..MaxPartySize, so a value typed outside that range - 0, or 5 on a 2..4
    /// roster - still names a party the Debug size buttons could have offered.
    public int PlayablePartySize => Mathf.Clamp(playablePartySize, MinPartySize, MaxPartySize);
}
