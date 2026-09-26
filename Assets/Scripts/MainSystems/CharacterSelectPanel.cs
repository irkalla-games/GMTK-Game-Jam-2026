using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The party-select screen MainMenu.playButton opens instead of starting a run outright: a character
/// and a starting deck for each slot. Duplicates are allowed - each slot is independent.
///
/// The party size is only choosable in a Debug build (BuildMode.DebugTools), across
/// CharacterRoster.MinPartySize..MaxPartySize. A Playable build hides the size buttons and always
/// plays at CharacterRoster.PlayablePartySize - the size the game is balanced for - and nothing else on
/// the screen moves, so the two builds lay out identically apart from the missing buttons.
///
/// Model is `selections`, one (CharacterOption, DeckData) pair per slot; the CharacterSelectSlot views
/// are rebuilt from it on every party-size change and refreshed in place on every cycle, so resizing
/// the party keeps whatever picks still fit rather than resetting the screen. This mirrors RewardPanel
/// and CardRemovalPanel's "purely a view" split - CharacterSelectSlot fires an event per arrow click
/// and shows whatever Refresh hands it, every rule about what a slot may legally show lives here.
///
/// Owns starting the run: on Start it builds a runtime RunData (RunData.CreateRuntimeFrom) from the
/// chosen party plus the levels/carry-damage authored on `campaign`, exactly as MainMenu.playButton
/// used to build the whole run itself before this screen existed.
/// </summary>
public class CharacterSelectPanel : MonoBehaviour
{
    [Tooltip("Backdrop root, toggled on Show/Hide. Same convention as RewardPanel.root.")]
    [SerializeField] private GameObject root;

    [Tooltip("One button per legal party size, in order (index 0 is CharacterRoster.MinPartySize). "
             + "Built by Tools/Main Menu/Wire Character Select to match whatever roster is assigned "
             + "there - resizing MinPartySize/MaxPartySize on the roster needs a re-run. Hidden in a "
             + "Playable build, which has no size to choose.")]
    [SerializeField] private List<Button> sizeButtons = new();

    [SerializeField] private Transform slotParent;

    [SerializeField] private CharacterSelectSlot slotPrefab;

    [Tooltip("Horizontal gap between slot centres. Arithmetic in code rather than a HorizontalLayoutGroup "
             + "- same reasoning as SelectedCharacterPanel.Place: the row stays a live number that "
             + "re-flows on every party-size change, with no ContentSizeFitter/LayoutGroup interplay to "
             + "depend on.")]
    [SerializeField] private float slotSpacing = 500f;

    [SerializeField] private Button startButton;

    [SerializeField] private Button backButton;

    [Tooltip("The whole difficulty row, hidden outright until a second rung has been earned - a "
             + "first-time player has no choice to make and should not be shown one. Built by "
             + "Tools/Main Menu/Wire Character Select.")]
    [SerializeField] private GameObject difficultyRow;

    [SerializeField] private TMP_Text difficultyLabel;

    [SerializeField] private Button previousDifficultyButton;

    [SerializeField] private Button nextDifficultyButton;

    private readonly List<(CharacterOption character, DeckData deck)> selections = new();

    private readonly List<CharacterSelectSlot> slotViews = new();

    private RunData campaign;

    private CharacterRoster roster;

    private int currentSize;

    /// Fired when Back is clicked, after this panel has already hidden itself - MainMenu is what
    /// decides that means its own buttons come back, this screen has no idea such a thing exists.
    public event System.Action BackClicked;

    /// Whether the party size is the player's to pick - see the class comment. BuildMode rather than
    /// GameSettings.DebugRunEnabled: that setting sends Play straight to the testbed, skipping this
    /// screen, so gating on it would hide the buttons every time this screen is actually reached.
    private static bool SizeIsChoosable => BuildMode.DebugTools;

    /// Starts hidden regardless of the scene's authored state - same reasoning as RewardPanel.Awake.
    private void Awake()
    {
        if (root != null) { root.SetActive(false); }
        if (backButton != null) { backButton.onClick.AddListener(OnBackButtonClicked); }
        if (startButton != null) { startButton.onClick.AddListener(OnStartButtonClicked); }

        if (!SizeIsChoosable)
        {
            foreach (Button button in sizeButtons)
            {
                if (button != null) { button.gameObject.SetActive(false); }
            }
        }

        if (previousDifficultyButton != null)
        {
            previousDifficultyButton.onClick.AddListener(() => CycleDifficulty(-1));
        }

        if (nextDifficultyButton != null)
        {
            nextDifficultyButton.onClick.AddListener(() => CycleDifficulty(1));
        }
    }

    /// <summary>
    /// Opens the screen. `campaign` supplies the levels and carry-damage rule the eventual run reads -
    /// the same RunData MainMenu used to hand straight to RunManager.StartRun - and `roster` is who may
    /// be offered. Safe to call more than once per session (Play, Back, Play again): the picks from the
    /// previous visit are kept as long as they still fit the same roster, and so is a Debug build's
    /// party size. The first visit opens at the roster's PlayablePartySize in either build.
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

        int size = SizeIsChoosable && currentSize != 0 ? currentSize : roster.PlayablePartySize;
        currentSize = Mathf.Clamp(size, roster.MinPartySize, roster.MaxPartySize);

        EnsureSelectionCount(currentSize);
        WireSizeButtons();
        RebuildSlots();
        UpdateSizeButtonStates();
        RefreshDifficulty();
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

        // CreateRuntimeFrom rather than CreateRuntime, so every campaign-wide setting - levels, carry
        // damage, the ladder, whether clearing it awards anything - carries across in one call and
        // this screen cannot drift from MainMenu's tutorial hand-over about which of them matter.
        RunData run = RunData.CreateRuntimeFrom(campaign, entries);

        // Never with the tutorial. It is its own prologue run now, with its own fixed party and its own
        // scripted level - see MainMenu.TryStartTutorial, which is the only path that ever reaches it.
        // Passing the setting through here would point the director at whatever level 1 of the real
        // campaign happens to be, and it would script a board it knows nothing about.
        RunManager.StartRun(run, showTutorial: false, difficultyTier: SelectedTier());

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
    /// the slots that were dropped rather than remembering what they used to hold. A newly added slot
    /// defaults to the roster entry at its own index (slot 2 -> roster.Characters[2]) rather than always
    /// the first character, so growing the party introduces each hero in roster order.
    private void EnsureSelectionCount(int size)
    {
        while (selections.Count < size)
        {
            CharacterOption defaultCharacter = DefaultCharacterAt(selections.Count);
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
        bool deckLocked = deck != null && !DeckUnlocks.IsUnlocked(deck);
        bool characterLocked = !CharacterUnlocks.IsUnlocked(character);

        slotViews[index].Refresh(character, deck, deckLocked, characterLocked);
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
    /// Start is withheld until every slot holds an unlocked character on an unlocked deck - either
    /// kind of locked pick stays visible while cycled to (see CharacterSelectSlot.Refresh's "(Locked)"
    /// label) rather than being skipped outright, so a player can see what there is to earn without
    /// being allowed to take it.
    /// </summary>
    private void UpdateStartInteractable()
    {
        if (startButton == null) { return; }

        bool allReady = selections.Count > 0;

        foreach ((CharacterOption character, DeckData deck) in selections)
        {
            if (character == null
                || !CharacterUnlocks.IsUnlocked(character)
                || (deck != null && !DeckUnlocks.IsUnlocked(deck)))
            {
                allReady = false;
                break;
            }
        }

        startButton.interactable = allReady;
    }

    // ---- Difficulty ----------------------------------------------------------------------------

    /// <summary>
    /// The hardest rung that may be picked right now: what the player has earned, but never past the
    /// end of the campaign's ladder. Both halves matter - progress is stored independently of any one
    /// ladder, so a save from a longer ladder must not offer a rung this campaign cannot resolve.
    /// </summary>
    private int MaxSelectableTier()
    {
        if (campaign == null || campaign.Ladder == null) { return 0; }

        return Mathf.Clamp(DifficultyProgress.HighestUnlocked, 0, campaign.Ladder.Tiers.Count - 1);
    }

    private int SelectedTier() => Mathf.Clamp(DifficultyProgress.Selected, 0, MaxSelectableTier());

    /// <summary>
    /// Shows the chosen rung, or hides the row outright while there is only one to choose from - a
    /// first-time player has no decision to make here, and the row appearing is itself the reward for
    /// clearing the game once.
    /// </summary>
    private void RefreshDifficulty()
    {
        int max = MaxSelectableTier();

        if (difficultyRow != null) { difficultyRow.SetActive(max > 0); }

        if (max <= 0 || difficultyLabel == null) { return; }

        difficultyLabel.text = campaign.Ladder.NameAt(SelectedTier());
    }

    /// Steps one rung, clamped rather than wrapped: the ends of a difficulty ladder are meaningful in
    /// a way a character list's are not, and wrapping from Normal to the hardest rung is exactly the
    /// misclick a player would not forgive.
    private void CycleDifficulty(int direction)
    {
        DifficultyProgress.Selected = Mathf.Clamp(SelectedTier() + direction, 0, MaxSelectableTier());

        RefreshDifficulty();
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

    /// <summary>
    /// Who a newly added slot starts on: the roster entry at its own index, so growing the party
    /// introduces each hero in roster order - but skipped past anything still locked, walking forward
    /// and then wrapping.
    ///
    /// The skip is the whole point. Without it a fresh screen opens on a hero the player has not
    /// earned, with Start greyed out and nothing on screen explaining why. Falls back to the entry at
    /// its own index only when every hero on the roster is locked, which a shipped roster never is.
    ///
    /// Same shape, and same reason, as DefaultDeckFor below.
    /// </summary>
    private CharacterOption DefaultCharacterAt(int slotIndex)
    {
        IReadOnlyList<CharacterOption> characters = roster.Characters;

        if (characters.Count == 0) { return null; }

        int start = Mathf.Clamp(slotIndex, 0, characters.Count - 1);

        for (int step = 0; step < characters.Count; step++)
        {
            CharacterOption candidate = characters[Wrap(start + step, characters.Count)];

            if (CharacterUnlocks.IsUnlocked(candidate)) { return candidate; }
        }

        return characters[start];
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
