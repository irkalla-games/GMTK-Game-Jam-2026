using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One party slot on the character-select screen: a portrait, name, and chosen starting deck, each
/// with a pair of arrow buttons to cycle through what is available. Purely a view, the same convention
/// RewardPanel and CardRemovalPanel already use - it fires an event when an arrow is clicked and shows
/// whatever CharacterSelectPanel.Refresh hands back, but never decides what "next" resolves to or
/// whether a deck is legal to offer. CharacterSelectPanel owns every rule about that.
///
/// Spawned fresh under CharacterSelectPanel.slotParent whenever the party size changes and destroyed
/// with the rest on the next change - no pooling, same as RewardPanel.SpawnCards.
/// </summary>
public class CharacterSelectSlot : MonoBehaviour
{
    [SerializeField] private Image portraitImage;

    [SerializeField] private TMP_Text nameLabel;

    [SerializeField] private TMP_Text deckLabel;

    [SerializeField] private Button previousCharacterButton;

    [SerializeField] private Button nextCharacterButton;

    [SerializeField] private Button previousDeckButton;

    [SerializeField] private Button nextDeckButton;

    public event System.Action PreviousCharacterClicked;

    public event System.Action NextCharacterClicked;

    public event System.Action PreviousDeckClicked;

    public event System.Action NextDeckClicked;

    private void Awake()
    {
        if (previousCharacterButton != null)
        {
            previousCharacterButton.onClick.AddListener(() => PreviousCharacterClicked?.Invoke());
        }

        if (nextCharacterButton != null)
        {
            nextCharacterButton.onClick.AddListener(() => NextCharacterClicked?.Invoke());
        }

        if (previousDeckButton != null)
        {
            previousDeckButton.onClick.AddListener(() => PreviousDeckClicked?.Invoke());
        }

        if (nextDeckButton != null)
        {
            nextDeckButton.onClick.AddListener(() => NextDeckClicked?.Invoke());
        }
    }

    /// <summary>
    /// Shows the current pick. Both locked flags are asked for separately rather than read off
    /// `character`/`deck` here - the Unlocks lookups need the same assets the panel already has in
    /// hand, and a view shouldn't reach into a save-state lookup on its own.
    ///
    /// A locked hero is greyed and labelled exactly as a locked deck already was, and stays *visible*
    /// while cycled to rather than being skipped - so a player can see what there is to earn without
    /// being allowed to take it. CharacterSelectPanel is what refuses to start the run.
    /// </summary>
    public void Refresh(CharacterOption character, DeckData deck, bool deckLocked, bool characterLocked)
    {
        if (portraitImage != null)
        {
            portraitImage.sprite = character != null ? character.Portrait : null;
            portraitImage.enabled = portraitImage.sprite != null;

            // Dimmed rather than hidden - the silhouette is the point of showing a locked hero at all.
            portraitImage.color = characterLocked ? Color.gray : Color.white;
        }

        if (nameLabel != null)
        {
            string characterName = character != null ? character.DisplayName : "-";

            nameLabel.text = characterLocked ? $"{characterName} (Locked)" : characterName;
            nameLabel.color = characterLocked ? Color.gray : Color.white;
        }

        if (deckLabel == null) { return; }

        string deckName = deck != null ? deck.DisplayName : "Default";
        deckLabel.text = deckLocked ? $"{deckName} (Locked)" : deckName;
        deckLabel.color = deckLocked ? Color.gray : Color.white;
    }
}
