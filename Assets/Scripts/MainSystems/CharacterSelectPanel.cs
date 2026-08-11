using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The party-select screen MainMenu.playButton opens instead of starting a run outright: choose a
/// party size (CharacterRoster.MinPartySize..MaxPartySize), then a character and a starting deck for
/// each slot. Duplicates are allowed - each slot is independent.
///
/// Model is `selections`, one (CharacterOption, DeckData) pair per slot; the CharacterSelectSlot views
/// are rebuilt from it on every party-size change and refreshed in place on every cycle, so resizing
/// the party keeps whatever picks still fit rather than resetting the screen. This mirrors RewardPanel
/// and CardRemovalPanel's "purely a view" split - CharacterSelectSlot fires an event per arrow click
/// and shows whatever Refresh hands it, every rule about what a slot may legally show lives here.
///
/// Owns starting the run: on Start it builds a runtime RunData (RunData.CreateRuntime) from the
/// chosen party plus the levels/carry-damage authored on `campaign`, exactly as MainMenu.playButton
/// used to build the whole run itself before this screen existed.
/// </summary>
public class CharacterSelectPanel : MonoBehaviour
{
    [Tooltip("Backdrop root, toggled on Show/Hide. Same convention as RewardPanel.root.")]
    [SerializeField] private GameObject root;

    [Tooltip("One button per legal party size, in order (index 0 is CharacterRoster.MinPartySize). "
             + "Built by Tools/Main Menu/Wire Character Select to match whatever roster is assigned "
             + "there - resizing MinPartySize/MaxPartySize on the roster needs a re-run.")]
    [SerializeField] private List<Button> sizeButtons = new();

    [SerializeField] private Transform slotParent;

    [SerializeField] private CharacterSelectSlot slotPrefab;

    [Tooltip("Horizontal gap between slot centres. Arithmetic in code rather than a HorizontalLayoutGroup "
             + "- same reasoning as SelectedCharacterPanel.Place: the row stays a live number that "
             + "re-flows on every party-size change, with no ContentSizeFitter/LayoutGroup interplay to "
             + "depend on.")]
    [SerializeField] private float slotSpacing = 420f;

    [SerializeField] private Button startButton;

    [SerializeField] private Button backButton;

    private readonly List<(CharacterOption character, DeckData deck)> selections = new();

    private readonly List<CharacterSelectSlot> slotViews = new();

    private RunData campaign;

    private CharacterRoster roster;

    private int currentSize;

    /// Fired when Back is clicked, after this panel has already hidden itself - MainMenu is what
    /// decides that means its own buttons come back, this screen has no idea such a thing exists.
    public event System.Action BackClicked;

    /// Starts hidden regardless of the scene's authored state - same reasoning as RewardPanel.Awake.
    private void Awake()
    {
        if (root != null) { root.SetActive(false); }
        if (backButton != null) { backButton.onClick.AddListener(OnBackButtonClicked); }
        if (startButton != null) { startButton.onClick.AddListener(OnStartButtonClicked); }
    }

    /// <summary>
    /// Opens the screen. `campaign` supplies the levels and carry-damage rule the eventual run reads -
    /// the same RunData MainMenu used to hand straight to RunManager.StartRun - and `roster` is who may
    /// be offered. Safe to call more than once per session (Play, Back, Play again): the party size and
    /// picks from the previous visit are kept as long as they still fit the same roster.
    /// </summary>
    public void Show(RunData campaign, CharacterRoster roster)
    {
        this.campaign = campaign;
        this.roster = roster;

        if (root != null) { root.SetActive(true); }

        if (roster == null || roster.Characters.Count == 0)
        {
            Debug.LogError($"{name}: no CharacterRoster (or an empty one) assigned - nothing to offer.");
            return;
        }

        currentSize = Mathf.Clamp(
            currentSize == 0 ? roster.MinPartySize : currentSize, roster.MinPartySize, roster.MaxPartySize);

        EnsureSelectionCount(currentSize);
        WireSizeButtons();
        RebuildSlots();
        UpdateSizeButtonStates();
        UpdateStartInteractable();
    }

    public void Hide()
    {
        if (root != null) { root.SetActive(false); }

        ClearSlotViews();
    }

    private void OnBackButtonClicked()
    {
        Hide();
        BackClicked?.Invoke();
    }

    /// <summary>
    /// Builds the run and leaves for the Game scene - the same two calls MainMenu.playButton made
    /// directly before this screen existed, and in the same order for the same reason (StartRun before
    /// the load resets any previous run that ended in defeat before BattleManager could resume from it).
    /// </summary>
    private void OnStartButtonClicked()
    {
        if (campaign == null)
        {
            Debug.LogError($"{name}: no campaign RunData - there is nowhere to read levels from.");
            return;
        }

        List<PartyEntry> entries = new();

        foreach ((CharacterOption character, DeckData deck) in selections)
        {
            if (character == null || character.Prefab == null) { continue; }

            entries.Add(new PartyEntry { prefab = character.Prefab, deck = deck });
        }

        RunData run = RunData.CreateRuntime(campaign.Levels, entries, campaign.CarryDamageBetweenLevels);

        RunManager.StartRun(run, GameSettings.TutorialEnabled);

        SceneManager.LoadScene("Game");
    }

    private void WireSizeButtons()
    {
        for (int i = 0; i < sizeButtons.Count; i++)
        {
            Button button = sizeButtons[i];

            if (button == null) { continue; }

            int size = roster.MinPartySize + i;

            // Cleared first: Show can run more than once a session (Play, Back, Play again), and
            // AddListener without this would stack a duplicate call onto every previous visit's.
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SetPartySize(size));
        }
    }

    private void SetPartySize(int size)
    {
        currentSize = Mathf.Clamp(size, roster.MinPartySize, roster.MaxPartySize);

        EnsureSelectionCount(currentSize);
        RebuildSlots();
        UpdateSizeButtonStates();
        UpdateStartInteractable();
    }

    /// Grows or shrinks `selections` to exactly `size`, keeping every entry that still fits - shrinking
    /// from 4 to 2 leaves slots 0 and 1 exactly as chosen, growing back to 4 restores fresh defaults for
    /// the slots that were dropped rather than remembering what they used to hold.
    private void EnsureSelectionCount(int size)
    {
        while (selections.Count < size)
        {
            CharacterOption defaultCharacter = roster.Characters[0];
            selections.Add((defaultCharacter, DefaultDeckFor(defaultCharacter)));
        }

        while (selections.Count > size)
        {
            selections.RemoveAt(selections.Count - 1);
        }
    }

    private void RebuildSlots()
    {
        ClearSlotViews();

        if (slotPrefab == null || slotParent == null)
        {
            Debug.LogError($"{name}: slotPrefab or slotParent not set - character select cannot show slots.");
            return;
        }

        float startX = -(selections.Count - 1) * slotSpacing / 2f;

        for (int i = 0; i < selections.Count; i++)
        {
            // Captured per-iteration on purpose - same reasoning as RewardPanel.SpawnCards: `i` is
            // reassigned every loop, `slotIndex` is not.
            int slotIndex = i;

            CharacterSelectSlot view = Instantiate(slotPrefab, slotParent);

            if (view.TryGetComponent(out RectTransform rect))
            {
                rect.anchoredPosition = new Vector2(startX + i * slotSpacing, 0f);
            }

            view.PreviousCharacterClicked += () => CycleCharacter(slotIndex, -1);
            view.NextCharacterClicked += () => CycleCharacter(slotIndex, 1);
            view.PreviousDeckClicked += () => CycleDeck(slotIndex, -1);
            view.NextDeckClicked += () => CycleDeck(slotIndex, 1);

            slotViews.Add(view);
            RefreshSlot(slotIndex);
        }
    }

    private void ClearSlotViews()
    {
        foreach (CharacterSelectSlot view in slotViews)
        {
            if (view != null) { Destroy(view.gameObject); }
        }

        slotViews.Clear();
    }

    private void CycleCharacter(int slotIndex, int direction)
    {
        IReadOnlyList<CharacterOption> characters = roster.Characters;

        if (characters.Count == 0) { return; }

        (CharacterOption character, DeckData _) = selections[slotIndex];

        int current = character != null ? IndexOf(characters, character) : -1;
        int next = Wrap(current + direction, characters.Count);

        CharacterOption chosen = characters[next];
        selections[slotIndex] = (chosen, DefaultDeckFor(chosen));

        RefreshSlot(slotIndex);
        UpdateStartInteractable();
    }

    private void CycleDeck(int slotIndex, int direction)
    {
        (CharacterOption character, DeckData deck) = selections[slotIndex];

        List<DeckData> eligible = EligibleDecks(character);
        int current = eligible.IndexOf(deck);
        if (current < 0) { current = 0; }

        int next = Wrap(current + direction, eligible.Count);

        selections[slotIndex] = (character, eligible[next]);

        RefreshSlot(slotIndex);
        UpdateStartInteractable();
    }

    private void RefreshSlot(int index)
    {
        if (index >= slotViews.Count) { return; }

        (CharacterOption character, DeckData deck) = selections[index];
        bool locked = deck != null && !DeckUnlocks.IsUnlocked(deck);

        slotViews[index].Refresh(character, deck, locked);
    }

    private void UpdateSizeButtonStates()
    {
        for (int i = 0; i < sizeButtons.Count; i++)
        {
            if (sizeButtons[i] == null) { continue; }

            int size = roster.MinPartySize + i;

            // The button for the size already showing is disabled rather than hidden - a player still
            // sees every size on offer, just not one that would do nothing if clicked.
            sizeButtons[i].interactable = size != currentSize;
        }
    }

    /// <summary>
    /// Start is withheld until every slot holds a character and an unlocked deck - a locked deck stays
    /// visible while cycled to (see CharacterSelectSlot.Refresh's "(Locked)" label) rather than being
    /// skipped outright, so a player can see what there is to earn without being allowed to take it.
    /// </summary>
    private void UpdateStartInteractable()
    {
        if (startButton == null) { return; }

        bool allReady = selections.Count > 0;

        foreach ((CharacterOption character, DeckData deck) in selections)
        {
            if (character == null || (deck != null && !DeckUnlocks.IsUnlocked(deck)))
            {
                allReady = false;
                break;
            }
        }

        startButton.interactable = allReady;
    }

    /// <summary>
    /// Every deck `option` may start with, filtered to the ones its actual class can hold - a slot can
    /// never be handed a deck Character.BuildDeck would silently filter every card out of. Never empty:
    /// no compatible authored deck (or none authored at all) falls back to a single null entry, meaning
    /// "use the prefab's own authored deck" - the same meaning PartyEntry.deck == null already carries
    /// into RunManager.Begin.
    /// </summary>
    private static List<DeckData> EligibleDecks(CharacterOption option)
    {
        List<DeckData> eligible = new();

        if (option == null) { return eligible; }

        CharacterClass ownClass = ClassOf(option);

        foreach (DeckData deck in option.Decks)
        {
            if (deck != null && deck.AvailableTo(ownClass)) { eligible.Add(deck); }
        }

        if (eligible.Count == 0) { eligible.Add(null); }

        return eligible;
    }

    /// Prefers the first eligible deck that is already unlocked, so a fresh pick never lands on
    /// something the player cannot actually take - only falls back to a locked one if every eligible
    /// deck for this character is locked, which EligibleDecks guarantees is never zero of.
    private static DeckData DefaultDeckFor(CharacterOption option)
    {
        List<DeckData> eligible = EligibleDecks(option);

        foreach (DeckData deck in eligible)
        {
            if (DeckUnlocks.IsUnlocked(deck)) { return deck; }
        }

        return eligible.Count > 0 ? eligible[0] : null;
    }

    private static CharacterClass ClassOf(CharacterOption option)
    {
        if (option == null || option.Prefab == null) { return CharacterClass.Any; }

        Character character = option.Prefab.GetComponent<Character>();

        return character != null ? character.Class : CharacterClass.Any;
    }

    private static int IndexOf(IReadOnlyList<CharacterOption> characters, CharacterOption target)
    {
        for (int i = 0; i < characters.Count; i++)
        {
            if (characters[i] == target) { return i; }
        }

        return -1;
    }

    private static int Wrap(int index, int count) => count <= 0 ? 0 : ((index % count) + count) % count;
}
