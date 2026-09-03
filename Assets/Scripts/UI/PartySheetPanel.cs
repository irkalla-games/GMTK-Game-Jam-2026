using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The Tab screen: every living hero side by side, health and energy as plain numbers instead of a
/// small bar, and every active status written out in full via StatusDetailRow instead of an icon you
/// have to point at one at a time. PartyPortraitPanel's row answers "who is active and roughly how are
/// they doing"; this answers "let me actually read everything" - the two exist side by side rather than
/// one panel trying to do both jobs at every size.
///
/// Contract copied from CardPilePanel: hidden regardless of authored scene state, SetInputLocked while
/// open, Tab closes it as well as opening it. InputLocked is a single
/// bool, so Show refuses outright while a reward panel or any other modal already holds it rather than
/// fighting it for the lock.
/// </summary>
public class PartySheetPanel : Singleton<PartySheetPanel>
{
    [SerializeField] private GameObject root;

    [SerializeField] private Button closeButton;

    [SerializeField] private PartySheetColumn columnPrefab;

    [SerializeField] private RectTransform columnParent;

    [SerializeField] private StatusIcons icons;

    [SerializeField] private Glossary glossary;

    /// <summary>
    /// Column width and gap, read straight off PanelPalette rather than serialized here.
    ///
    /// These were [SerializeField] floats, and that was a real bug rather than a style preference:
    /// PartySheetColumn.prefab sizes its own root from PanelPalette.ColumnWidth, while Refresh below
    /// rewrites every column's sizeDelta from this value on every single refresh. Two copies of one
    /// number, with the scene's copy silently winning at runtime - so widening the palette constant
    /// moved the prefab, looked correct in the Project window, and changed nothing in play, because
    /// Game.unity still held the old width and stamped it back over the prefab the moment the sheet
    /// opened. Exactly the drift PanelPalette's own header says it exists to prevent.
    ///
    /// Now there is one number. Changing PanelPalette.ColumnWidth is the whole edit - no wiring
    /// command, no scene save, nothing to keep in step. The stale columnWidth/columnSpacing keys left
    /// in Game.unity are inert (Unity drops YAML keys with no matching field) and disappear the next
    /// time the scene is saved.
    /// </summary>
    private static float ColumnWidth => PanelPalette.ColumnWidth;

    private static float ColumnSpacing => PanelPalette.ColumnSpacing;

    /// Grown on demand and reused, never destroyed - same pooling contract every other pooled view in
    /// this codebase uses.
    private readonly List<PartySheetColumn> columns = new();

    /// Reused per Refresh so filtering the roster down to living heroes does not allocate a list every
    /// time an action resolves while the sheet is open.
    private readonly List<Character> livingHeroes = new();

    public bool IsOpen { get; private set; }

    /// Starts hidden regardless of the scene's authored state - same reasoning as CardPilePanel.Awake.
    protected override void Awake()
    {
        base.Awake();

        if (root != null) { root.SetActive(false); }

        if (closeButton != null) { closeButton.onClick.AddListener(Close); }
    }

    private void Start()
    {
        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved += OnActionResolved; }

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced += OnTurnAdvanced; }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved -= OnActionResolved; }

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced -= OnTurnAdvanced; }
    }

    private void Update()
    {
        if (Keyboard.current == null) { return; }

        if (Keyboard.current.tabKey.wasPressedThisFrame)
        {
            // A pile panel closing on this same press has already spoken for it - see
            // CardPilePanel.TabConsumedThisFrame.
            if (CardPilePanel.TabConsumedThisFrame) { return; }

            if (IsOpen) { Close(); } else { Show(); }

            return;
        }
    }

    public void Show()
    {
        if (IsOpen) { return; }

        if (BattleManager.Instance != null && BattleManager.Instance.InputLocked) { return; }

        IsOpen = true;

        if (root != null) { root.SetActive(true); }

        // Columns are pooled and reused across opens, so a column left on the equipment page last time
        // would still be there now. The sheet always opens on statuses - see PartySheetColumn.
        foreach (PartySheetColumn column in columns) { column.ResetToFirstPage(); }

        Refresh();

        // Same lock CardPilePanel takes while open - stops the board, the hand and any tooltip from
        // responding underneath this modal.
        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(true); }
    }

    public void Close()
    {
        if (!IsOpen) { return; }

        IsOpen = false;

        if (root != null) { root.SetActive(false); }

        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(false); }
    }

    private void OnActionResolved(GameAction action, ActionContext ctx)
    {
        if (IsOpen) { Refresh(); }
    }

    /// Every OnTurnStart/OnTurnEnd hook has run by the time this fires without going through an action
    /// at all - Poison biting and Shield wiping itself, same reasoning SelectedCharacterPanel's own
    /// TurnAdvanced subscription documents.
    private void OnTurnAdvanced()
    {
        if (IsOpen) { Refresh(); }
    }

    /// <summary>
    /// Rebuilds every column from BattleManager.Characters, kept to living player-controlled heroes in
    /// roster order - the same filter PartyPortraitPanel.Rebuild uses, so the two screens never disagree
    /// about who counts as "in the party" right now.
    /// </summary>
    private void Refresh()
    {
        if (!IsOpen || columnPrefab == null || columnParent == null) { return; }

        BattleManager battle = BattleManager.Instance;

        if (battle == null) { return; }

        livingHeroes.Clear();

        foreach (Character character in battle.Characters)
        {
            if (character != null && character.IsPlayerControlled && !character.IsDead)
            {
                livingHeroes.Add(character);
            }
        }

        float total = livingHeroes.Count * ColumnWidth
            + Mathf.Max(0, livingHeroes.Count - 1) * ColumnSpacing;

        float startX = -total / 2f + ColumnWidth / 2f;

        for (int i = 0; i < livingHeroes.Count; i++)
        {
            PartySheetColumn column = ColumnAt(i);
            column.Refresh(livingHeroes[i], icons, glossary);

            RectTransform rect = column.Rect;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ColumnWidth, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(startX + i * (ColumnWidth + ColumnSpacing), 0f);
        }

        for (int i = livingHeroes.Count; i < columns.Count; i++) { columns[i].gameObject.SetActive(false); }
    }

    private PartySheetColumn ColumnAt(int index)
    {
        while (columns.Count <= index) { columns.Add(Instantiate(columnPrefab, columnParent)); }

        columns[index].gameObject.SetActive(true);

        return columns[index];
    }
}
