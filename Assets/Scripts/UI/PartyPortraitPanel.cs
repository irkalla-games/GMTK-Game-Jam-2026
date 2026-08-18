using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The row of hero portraits at the bottom-left of the battle HUD. Replaces HeroInfoPanel - the
/// PlayerControlled SelectedCharacterPanel that described whichever hero was last inspected - with a
/// row showing the whole living party at once, and is also how you switch who is active: click a
/// portrait, press 1-4, or click the hero's own tile on the board, all three landing in Activate below.
///
/// The enemy-side SelectedCharacterPanel is untouched. It answers a different question - one inspected
/// target plus an intent icon - that a party row cannot cover, so it keeps doing that job on its own.
/// </summary>
public class PartyPortraitPanel : MonoBehaviour
{
    [SerializeField] private HeroPortrait portraitPrefab;

    [SerializeField] private RectTransform row;

    [SerializeField] private StatusIcons icons;

    [SerializeField] private Glossary glossary;

    [Header("Layout")]
    [Tooltip("Width of a portrait's slot when it is not the active one.")]
    [SerializeField] private float restWidth = 96f;

    [Tooltip("Width of the active hero's slot - wider, so the row spreads to make room for it rather " +
        "than just scaling a portrait up in place.")]
    [SerializeField] private float activeWidth = 140f;

    [SerializeField] private float spacing = 12f;

    [SerializeField] private float activeScale = 1.25f;

    [SerializeField] private float layoutDuration = 0.15f;

    [Tooltip("Status chips a portrait shows before the rest collapse into a '+N' overflow badge.")]
    [SerializeField] private int maxChips = 4;

    /// In roster order, which is also hotkey order - portraits[0] is hero 1, and so on. That is what
    /// lets the digit keys below index straight into this list with no separate mapping to keep honest.
    private readonly List<HeroPortrait> portraits = new();

    /// Held so per-hero StatsChanged subscriptions can be detached on rebuild and OnDestroy - the same
    /// swap CardPileHud.ShowFor uses for its own follow-the-active-character subscription.
    private readonly List<Character> subscribed = new();

    private Canvas canvas;

    private void Start()
    {
        canvas = GetComponentInParent<Canvas>();

        BattleManager battle = BattleManager.Instance;

        if (battle == null)
        {
            Debug.LogError($"{name}: no BattleManager in the scene - nothing to show a party for");
            return;
        }

        battle.CharacterJoined += OnRosterChanged;
        battle.CharacterLeft += OnRosterChanged;
        battle.ActiveCharacterChanged += OnActiveChanged;
        battle.TurnAdvanced += RefreshAll;

        Rebuild();
    }

    private void OnDestroy()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle != null)
        {
            battle.CharacterJoined -= OnRosterChanged;
            battle.CharacterLeft -= OnRosterChanged;
            battle.ActiveCharacterChanged -= OnActiveChanged;
            battle.TurnAdvanced -= RefreshAll;
        }

        DetachHeroes();
        DetachPortraits();
    }

    /// The panel owns the digit keys rather than BattleManager, so "who is #2" has exactly one answer -
    /// portraits[1] - and the row's display order is the hotkey order by construction.
    private void Update()
    {
        if (Keyboard.current == null || portraits.Count == 0) { return; }

        if (Keyboard.current.digit1Key.wasPressedThisFrame) { ActivateAt(0); }
        else if (Keyboard.current.digit2Key.wasPressedThisFrame) { ActivateAt(1); }
        else if (Keyboard.current.digit3Key.wasPressedThisFrame) { ActivateAt(2); }
        else if (Keyboard.current.digit4Key.wasPressedThisFrame) { ActivateAt(3); }
    }

    /// <summary>
    /// Where this hero's portrait currently sits, or null if they have none - they are dead, or not
    /// player-controlled. Looked up live rather than cached by the caller because Rebuild destroys and
    /// re-creates the whole row whenever the roster changes, so any held reference goes stale.
    ///
    /// Exposed for the tutorial spotlight, which lights the portrait the player is being told to press.
    /// </summary>
    public RectTransform PortraitRectFor(Character hero)
    {
        if (hero == null) { return null; }

        foreach (HeroPortrait portrait in portraits)
        {
            if (portrait != null && portrait.Hero == hero) { return (RectTransform)portrait.transform; }
        }

        return null;
    }

    private void OnRosterChanged(Character _) => Rebuild();

    private void OnActiveChanged(Character _) => Layout();

    private void OnHeroStatsChanged(Character character) => RefreshOne(character);

    private void OnPortraitClicked(HeroPortrait portrait) => Activate(portrait);

    private void ActivateAt(int index)
    {
        if (index < 0 || index >= portraits.Count) { return; }

        Activate(portraits[index]);
    }

    /// <summary>
    /// The one door every way of switching heroes goes through - a portrait click, a digit key, or
    /// (unchanged) clicking the hero's own tile via BattleManager.OnTileClicked. Mirrors that method's
    /// own two gates, since this is the same decision reached a different way and must not be able to
    /// do anything a tile click could not.
    /// </summary>
    private void Activate(HeroPortrait portrait)
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null || portrait == null) { return; }

        Character hero = portrait.Hero;

        if (hero == null || hero.IsDead || !hero.IsPlayerControlled) { return; }

        if (battle.InputLocked) { return; }

        if (battle.Phase != BattlePhase.PlayerActing && battle.Phase != BattlePhase.NotStarted) { return; }

        // Here rather than in Update so it covers a portrait click too, not just the digit keys - this
        // method is already the one door every way of switching heroes goes through.
        string tutorialRefusal = TutorialDirector.RefuseActivate(hero);

        if (tutorialRefusal != null)
        {
            Debug.Log($"hero activated: {hero.name} - ignored, {tutorialRefusal}");
            return;
        }

        // Before the switch: the selected card belongs to a hand that is about to leave the screen, and
        // leaving it selected would keep tiles highlighted for a card nobody can see any more.
        if (CardPlayManager.Instance != null) { CardPlayManager.Instance.ClearSelection(); }

        battle.SetSelectedCharacter(hero);
        battle.SetActiveCharacter(hero);
    }

    /// <summary>
    /// Rebuilds the row from BattleManager.Characters, kept to living player-controlled heroes in roster
    /// order. A summon is PlayableCharacter.Ally rather than AllyPlayable, so it never joins this row -
    /// see Character.IsPlayerControlled. Unsubscribe does not remove a dead character from
    /// BattleManager's own list, but Unity's == reports a destroyed Character as equal to null, which is
    /// what the IsDead/null check below relies on to drop the corpse.
    /// </summary>
    private void Rebuild()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null || row == null || portraitPrefab == null) { return; }

        DetachHeroes();
        DetachPortraits();

        foreach (HeroPortrait portrait in portraits)
        {
            if (portrait != null) { Destroy(portrait.gameObject); }
        }

        portraits.Clear();

        foreach (Character character in battle.Characters)
        {
            if (character == null || !character.IsPlayerControlled || character.IsDead) { continue; }

            HeroPortrait portrait = Instantiate(portraitPrefab, row);
            portrait.Bind(character);
            portrait.Clicked += OnPortraitClicked;
            portraits.Add(portrait);

            character.StatsChanged += OnHeroStatsChanged;
            subscribed.Add(character);
        }

        RefreshAll();
        Layout();
    }

    private void DetachHeroes()
    {
        foreach (Character character in subscribed)
        {
            if (character != null) { character.StatsChanged -= OnHeroStatsChanged; }
        }

        subscribed.Clear();
    }

    private void DetachPortraits()
    {
        foreach (HeroPortrait portrait in portraits)
        {
            if (portrait != null) { portrait.Clicked -= OnPortraitClicked; }
        }
    }

    private void RefreshAll()
    {
        foreach (HeroPortrait portrait in portraits) { portrait.Refresh(icons, glossary, maxChips, canvas); }
    }

    private void RefreshOne(Character character)
    {
        foreach (HeroPortrait portrait in portraits)
        {
            if (portrait.Hero == character)
            {
                portrait.Refresh(icons, glossary, maxChips, canvas);
                return;
            }
        }
    }

    /// <summary>
    /// Spreads the row so the active hero's portrait claims a wider slot and every other portrait's
    /// position shifts outward to make room for it, then tweens each one to its new position and scale.
    /// Run whenever who is active changes and whenever the roster itself does - a death or a rebuild
    /// changes how many portraits are sharing the row even when nobody's turn just started.
    /// </summary>
    private void Layout()
    {
        BattleManager battle = BattleManager.Instance;
        Character active = battle != null ? battle.ActiveCharacter : null;

        float total = spacing * Mathf.Max(0, portraits.Count - 1);

        foreach (HeroPortrait portrait in portraits)
        {
            total += portrait.Hero == active ? activeWidth : restWidth;
        }

        float x = -total / 2f;

        foreach (HeroPortrait portrait in portraits)
        {
            bool isActive = portrait.Hero == active;
            float width = isActive ? activeWidth : restWidth;
            float target = x + width / 2f;

            x += width + spacing;

            portrait.SetHighlighted(isActive);
            portrait.SetLayoutTarget(new Vector2(target, 0f), isActive ? activeScale : 1f, layoutDuration);
        }
    }
}
