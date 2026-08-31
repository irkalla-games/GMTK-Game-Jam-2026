using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-shot wiring for the Main Menu's character-select screen: builds the CharacterSelectPanel
/// hierarchy (and, the first time it runs, the CharacterSelectSlot prefab it spawns from) and hands
/// them to MainMenu's new fields. Also two small standalone tools that belong with this feature but
/// touch other assets entirely - see TopUpPartySpawnCells and the two Tools/Unlocks commands below.
///
/// A menu command rather than hand-edited scene YAML because the Editor holds MainMenu.unity in memory
/// while it is open - anything written to that file underneath it is discarded the next time the scene
/// is saved. Going through SerializedObject also means every private [SerializeField] field is set the
/// same way the Inspector sets it, dirty flags and undo included. Same shape as TutorialToggleWiring
/// and LevelRewardWiring.
///
/// Idempotent: every step checks whether it has already been done, so re-running after nudging
/// something by hand will not duplicate objects or stomp what you changed.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 - that script drives
/// Assembly-CSharp.csproj, which never lists Assets/Editor. Verify by focusing the Editor and checking
/// the console, per CLAUDE.md.
/// </summary>
public static class CharacterSelectWiring
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const string SkipButtonPrefabPath = "Assets/Prefabs/SkipButtonPrefab.prefab";
    private const string SlotPrefabPath = "Assets/Prefabs/CharacterSelectSlot.prefab";
    private const string DefaultRosterPath = "Assets/Data/Characters/DefaultRoster.asset";

    private const string PanelName = "CharacterSelectPanel";
    private const string BackdropName = "Backdrop";
    private const string TitleName = "Title";
    private const string SlotParentName = "SlotParent";
    private const string StartButtonName = "StartButton";
    private const string BackButtonName = "BackButton";
    private const string PortraitName = "Portrait";

    private static readonly string[] SizeButtonNames = { "PartySize2Button", "PartySize3Button", "PartySize4Button" };

    /// Existing objects Play used to show outright and now hides while the select screen is up - see
    /// MainMenu.menuButtons. Named rather than "every direct child of Canvas" so a future addition to
    /// the menu (a version label, say) is not silently swept in and hidden by this tool.
    private static readonly string[] MenuButtonNames = { "PlayButton", "SettingsButton", "ExitButton", "TutorialToggle" };

    /// Mirrors CharacterRoster's default MinPartySize/MaxPartySize (2..4). Not read from the roster
    /// asset itself - this tool has to work even before DefaultRoster.asset exists, and a level's spawn
    /// cells are board authoring, independent of whatever roster happens to be assigned at the menu.
    private const int MaxPartySize = 4;

    /// Distance between adjacent slot centres. 500 was tuned for 2 slots and crowded the screen once a
    /// 4-slot party (see EnsureSelectionCount's roster-order defaults) pushed the outer arrows toward
    /// the edge - see SetSlotSpacing.
    private const float SlotSpacing = 350f;

    [MenuItem("Tools/Main Menu/Wire Character Select")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Character select wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenMenuScene()) { return; }

        MainMenu menu = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);

        if (menu == null)
        {
            Debug.LogError($"Character select wiring: no MainMenu component in {ScenePath} - nothing to wire against.");
            return;
        }

        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);

        if (canvas == null)
        {
            Debug.LogError($"Character select wiring: no Canvas in {ScenePath} - nowhere to put the panel.");
            return;
        }

        CharacterSelectSlot slotPrefab = EnsureSlotPrefab();
        CharacterSelectPanel panel = EnsurePanel(canvas.transform, slotPrefab);

        WireMainMenu(menu, panel);
        WireMenuButtons(menu, canvas.transform);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Character select wiring: done - scene and assets saved.");
    }

    /// <summary>
    /// Adds cells to every LevelData short of MaxPartySize, so a party larger than a level's authored
    /// spawn points still has somewhere requested for each member - BattleManager.SpawnParty falls back
    /// to GridManager.NearestFreeSpawnTile for anyone past the authored list, so a cell landing near the
    /// edge of the board (or nudged off it) is not a bug, just a starting request the fallback resolves.
    /// A level author can always drag the new cells elsewhere by hand afterwards; the point is nobody
    /// loses a 3rd or 4th party member for want of a spawn cell existing at all.
    /// </summary>
    [MenuItem("Tools/Level/Top Up Party Spawn Cells")]
    public static void TopUpPartySpawnCells()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Character select wiring: exit Play Mode first - asset edits made in play are not reliable.");
            return;
        }

        int updated = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:LevelData"))
        {
            LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));

            if (level == null) { continue; }

            SerializedObject so = new(level);
            SerializedProperty cells = so.FindProperty("partySpawnCells");

            if (cells.arraySize >= MaxPartySize) { continue; }

            int previousCount = cells.arraySize;

            Vector2Int last = previousCount > 0
                ? cells.GetArrayElementAtIndex(previousCount - 1).vector2IntValue
                : Vector2Int.zero;

            cells.arraySize = MaxPartySize;

            for (int i = previousCount; i < MaxPartySize; i++)
            {
                cells.GetArrayElementAtIndex(i).vector2IntValue = last + new Vector2Int(i - previousCount + 1, 0);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(level);
            updated++;

            Debug.Log($"Character select wiring: {level.name} partySpawnCells {previousCount} -> {MaxPartySize}.");
        }

        AssetDatabase.SaveAssets();

        Debug.Log(updated > 0
            ? $"Character select wiring: topped up {updated} LevelData asset(s)."
            : "Character select wiring: every LevelData already has enough party spawn cells - nothing to do.");
    }

    /// Forces CharacterSelectPanel.slotSpacing to SlotSpacing - unlike Wire's SetIfEmpty fields, this one
    /// overwrites every run, since the point of the command is to apply whatever number is tuned into
    /// the constant below. Bump SlotSpacing and re-run to retune.
    [MenuItem("Tools/Main Menu/Set Character Select Slot Spacing")]
    public static void SetSlotSpacing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Character select wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenMenuScene()) { return; }

        CharacterSelectPanel panel = Object.FindAnyObjectByType<CharacterSelectPanel>(FindObjectsInactive.Include);

        if (panel == null)
        {
            Debug.LogError($"Character select wiring: no CharacterSelectPanel in {ScenePath} - run "
                           + "Wire Character Select first.");
            return;
        }

        SerializedObject so = new(panel);
        so.FindProperty("slotSpacing").floatValue = SlotSpacing;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log($"Character select wiring: slotSpacing -> {SlotSpacing}.");
    }

    /// Testing-only. Unlocks every DeckData.Locked deck for whoever's PlayerPrefs this Editor is
    /// running under, so the select screen's lock UI can be exercised without a real unlock hook.
    [MenuItem("Tools/Unlocks/Unlock All Decks")]
    public static void UnlockAllDecks() => ForEveryDeck(DeckUnlocks.Unlock, "unlocked");

    /// Re-locks everything Unlock All Decks unlocked, for the same testing reason.
    [MenuItem("Tools/Unlocks/Clear All Deck Unlocks")]
    public static void ClearAllDeckUnlocks() => ForEveryDeck(DeckUnlocks.Lock, "cleared the unlock state of");

    private static void ForEveryDeck(System.Action<DeckData> apply, string pastTenseVerb)
    {
        int count = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:DeckData"))
        {
            DeckData deck = AssetDatabase.LoadAssetAtPath<DeckData>(AssetDatabase.GUIDToAssetPath(guid));

            if (deck == null) { continue; }

            apply(deck);
            count++;
        }

        Debug.Log($"Character select wiring: {pastTenseVerb} {count} deck(s).");
    }

    private static bool OpenMenuScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    // ------------------------------------------------------------------------------------------
    // CharacterSelectPanel + its scene hierarchy
    // ------------------------------------------------------------------------------------------

    private static CharacterSelectPanel EnsurePanel(Transform canvas, CharacterSelectSlot slotPrefab)
    {
        Transform existing = canvas.Find(PanelName);
        GameObject host;
        CharacterSelectPanel panel;

        if (existing != null && existing.TryGetComponent(out panel))
        {
            host = existing.gameObject;
        }
        else
        {
            host = new GameObject(PanelName, typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            host.layer = canvas.gameObject.layer;
            Stretch(host.GetComponent<RectTransform>());

            panel = host.AddComponent<CharacterSelectPanel>();

            Debug.Log($"Character select wiring: created {PanelName}.");
        }

        GameObject backdrop = EnsureBackdrop(host.transform);
        EnsureTitle(backdrop.transform);
        List<Button> sizeButtons = EnsureSizeButtons(backdrop.transform);
        Transform slotParent = EnsureSlotParent(backdrop.transform);
        Button startButton = EnsureActionButton(backdrop.transform, StartButtonName, "Start", new Vector2(360f, -420f));
        Button backButton = EnsureActionButton(backdrop.transform, BackButtonName, "Back", new Vector2(-360f, -420f));

        SerializedObject so = new(panel);
        SetIfEmpty(so, "root", backdrop);
        SetIfEmpty(so, "slotParent", slotParent);

        if (slotPrefab != null) { SetIfEmpty(so, "slotPrefab", slotPrefab); }
        if (startButton != null) { SetIfEmpty(so, "startButton", startButton); }
        if (backButton != null) { SetIfEmpty(so, "backButton", backButton); }

        SerializedProperty sizeButtonsProp = so.FindProperty("sizeButtons");

        if (sizeButtonsProp.arraySize == 0 && sizeButtons.Count > 0)
        {
            sizeButtonsProp.arraySize = sizeButtons.Count;

            for (int i = 0; i < sizeButtons.Count; i++)
            {
                sizeButtonsProp.GetArrayElementAtIndex(i).objectReferenceValue = sizeButtons[i];
            }
        }

        so.ApplyModifiedProperties();

        Debug.Log("Character select wiring: CharacterSelectPanel fields set.");

        return panel;
    }

    /// The dim sheet Show/Hide toggles. A child, not the panel's own GameObject - CharacterSelectPanel
    /// has to stay active for its Awake to have already wired the Start/Back buttons by the time
    /// MainMenu.playButton calls Show, same reasoning CardRemovalPanel documents for its own root split.
    private static GameObject EnsureBackdrop(Transform parent)
    {
        Transform existing = parent.Find(BackdropName);

        if (existing != null) { return existing.gameObject; }

        GameObject backdrop = new(BackdropName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backdrop.transform.SetParent(parent, false);
        backdrop.layer = parent.gameObject.layer;

        Stretch(backdrop.GetComponent<RectTransform>());
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        // Awake forces this off anyway; doing it here too keeps the Editor view uncluttered.
        backdrop.SetActive(false);

        return backdrop;
    }

    private static void EnsureTitle(Transform parent)
    {
        if (parent.Find(TitleName) != null) { return; }

        GameObject go = new(TitleName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -60f);
        rect.sizeDelta = new Vector2(1600f, 120f);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = "Choose Your Party";
        text.fontSize = 80f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        ApplySampleFont(text);
    }

    private static List<Button> EnsureSizeButtons(Transform parent)
    {
        List<Button> buttons = new();
        Button source = AssetDatabase.LoadAssetAtPath<Button>(SkipButtonPrefabPath);

        if (source == null)
        {
            Debug.LogWarning($"Character select wiring: {SkipButtonPrefabPath} not found - size buttons not created.");
            return buttons;
        }

        float spacing = 220f;
        float startX = -(SizeButtonNames.Length - 1) * spacing / 2f;

        for (int i = 0; i < SizeButtonNames.Length; i++)
        {
            Transform existing = parent.Find(SizeButtonNames[i]);
            Button button;

            if (existing != null)
            {
                button = existing.GetComponent<Button>();
            }
            else
            {
                button = (Button)PrefabUtility.InstantiatePrefab(source, parent);
                button.name = SizeButtonNames[i];

                RectTransform rect = button.GetComponent<RectTransform>();
                Centre(rect);
                rect.anchoredPosition = new Vector2(startX + i * spacing, 220f);
                rect.sizeDelta = new Vector2(180f, 90f);

                TMP_Text text = button.GetComponentInChildren<TMP_Text>();
                if (text != null) { text.text = (2 + i).ToString(); }
            }

            buttons.Add(button);
        }

        return buttons;
    }

    private static Transform EnsureSlotParent(Transform parent)
    {
        Transform existing = parent.Find(SlotParentName);

        if (existing != null) { return existing; }

        GameObject go = new(SlotParentName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = new Vector2(0f, -20f);
        rect.sizeDelta = Vector2.zero;

        return go.transform;
    }

    private static Button EnsureActionButton(Transform parent, string objectName, string label, Vector2 anchoredPosition)
    {
        Transform existing = parent.Find(objectName);

        if (existing != null) { return existing.GetComponent<Button>(); }

        Button source = AssetDatabase.LoadAssetAtPath<Button>(SkipButtonPrefabPath);

        if (source == null)
        {
            Debug.LogWarning($"Character select wiring: {SkipButtonPrefabPath} not found - {objectName} not created.");
            return null;
        }

        Button button = (Button)PrefabUtility.InstantiatePrefab(source, parent);
        button.name = objectName;

        RectTransform rect = button.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = anchoredPosition;

        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        if (text != null) { text.text = label; }

        return button;
    }

    // ------------------------------------------------------------------------------------------
    // CharacterSelectSlot prefab - built once, the first time this ever runs
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Loads the slot prefab if it already exists, otherwise builds and saves it. The build happens in
    /// the currently open scene (MainMenu.unity, opened by Wire before this is called) because a new
    /// GameObject has to live somewhere to be saved as a prefab asset - it is destroyed again
    /// immediately after SaveAsPrefabAsset, so nothing is left behind for the scene save at the end of
    /// Wire to pick up.
    /// </summary>
    private static CharacterSelectSlot EnsureSlotPrefab()
    {
        CharacterSelectSlot existing = AssetDatabase.LoadAssetAtPath<CharacterSelectSlot>(SlotPrefabPath);

        if (existing != null) { return existing; }

        Button arrowSource = AssetDatabase.LoadAssetAtPath<Button>(SkipButtonPrefabPath);

        if (arrowSource == null)
        {
            Debug.LogError($"Character select wiring: {SkipButtonPrefabPath} not found - cannot build "
                           + $"{SlotPrefabPath}'s arrow buttons.");
            return null;
        }

        GameObject host = new(PanelName + "Slot", typeof(RectTransform));
        RectTransform hostRect = host.GetComponent<RectTransform>();
        Centre(hostRect);
        hostRect.sizeDelta = new Vector2(380f, 520f);

        Image portrait = CreatePortrait(host.transform);
        TMP_Text nameLabel = CreateLabel(host.transform, "NameLabel", new Vector2(0f, 40f), new Vector2(260f, 80f), 48f);
        TMP_Text deckLabel = CreateLabel(host.transform, "DeckLabel", new Vector2(0f, -100f), new Vector2(260f, 70f), 34f);

        Button previousCharacter = CreateArrow(host.transform, arrowSource, "PreviousCharacterButton", "<", new Vector2(-190f, 40f));
        Button nextCharacter = CreateArrow(host.transform, arrowSource, "NextCharacterButton", ">", new Vector2(190f, 40f));
        Button previousDeck = CreateArrow(host.transform, arrowSource, "PreviousDeckButton", "<", new Vector2(-190f, -100f));
        Button nextDeck = CreateArrow(host.transform, arrowSource, "NextDeckButton", ">", new Vector2(190f, -100f));

        CharacterSelectSlot slot = host.AddComponent<CharacterSelectSlot>();

        SerializedObject so = new(slot);
        so.FindProperty("portraitImage").objectReferenceValue = portrait;
        so.FindProperty("nameLabel").objectReferenceValue = nameLabel;
        so.FindProperty("deckLabel").objectReferenceValue = deckLabel;
        so.FindProperty("previousCharacterButton").objectReferenceValue = previousCharacter;
        so.FindProperty("nextCharacterButton").objectReferenceValue = nextCharacter;
        so.FindProperty("previousDeckButton").objectReferenceValue = previousDeck;
        so.FindProperty("nextDeckButton").objectReferenceValue = nextDeck;
        so.ApplyModifiedProperties();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(host, SlotPrefabPath);
        Object.DestroyImmediate(host);

        Debug.Log($"Character select wiring: created {SlotPrefabPath}.");

        return saved.GetComponent<CharacterSelectSlot>();
    }

    private static Image CreatePortrait(Transform parent)
    {
        GameObject go = new(PortraitName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = new Vector2(0f, 170f);
        rect.sizeDelta = new Vector2(280f, 280f);

        Image image = go.GetComponent<Image>();
        image.preserveAspect = true;

        return image;
    }

    private static TMP_Text CreateLabel(Transform parent, string objectName, Vector2 anchoredPosition, Vector2 size, float fontSize)
    {
        GameObject go = new(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        ApplySampleFont(text);

        return text;
    }

    private static Button CreateArrow(Transform parent, Button source, string objectName, string label, Vector2 anchoredPosition)
    {
        Button button = (Button)PrefabUtility.InstantiatePrefab(source, parent);
        button.name = objectName;

        RectTransform rect = button.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(70f, 70f);

        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        if (text != null) { text.text = label; }

        return button;
    }

    // ------------------------------------------------------------------------------------------
    // MainMenu field wiring
    // ------------------------------------------------------------------------------------------

    private static void WireMainMenu(MainMenu menu, CharacterSelectPanel panel)
    {
        SerializedObject so = new(menu);
        SetIfEmpty(so, "selectPanel", panel);

        CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(DefaultRosterPath);

        if (roster != null)
        {
            SetIfEmpty(so, "roster", roster);
        }
        else
        {
            Debug.LogWarning($"Character select wiring: {DefaultRosterPath} not found - MainMenu.roster "
                             + "left unset. Author the CharacterOption/CharacterRoster assets and re-run.");
        }

        so.ApplyModifiedProperties();
    }

    private static void WireMenuButtons(MainMenu menu, Transform canvas)
    {
        SerializedObject so = new(menu);
        SerializedProperty list = so.FindProperty("menuButtons");

        if (list.arraySize > 0) { return; }

        List<GameObject> found = new();

        foreach (string objectName in MenuButtonNames)
        {
            Transform child = canvas.Find(objectName);

            if (child != null) { found.Add(child.gameObject); }
        }

        list.arraySize = found.Count;

        for (int i = 0; i < found.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        so.ApplyModifiedProperties();

        Debug.Log($"Character select wiring: MainMenu.menuButtons -> [{string.Join(", ", MenuButtonNames)}].");
    }

    // ------------------------------------------------------------------------------------------
    // Shared helpers - deliberately duplicated rather than shared with TutorialToggleWiring /
    // LevelRewardWiring, matching how those two already duplicate the same handful of helpers rather
    // than factoring out a common base for three one-shot scripts.
    // ------------------------------------------------------------------------------------------

    private static void ApplySampleFont(TMP_Text text)
    {
        TMP_Text sample = Object.FindAnyObjectByType<TMP_Text>(FindObjectsInactive.Include);

        if (sample != null && sample.font != null) { text.font = sample.font; }
    }

    private static void Centre(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// Assigns only an unset field, so a value tuned by hand survives a re-run.
    private static void SetIfEmpty(SerializedObject so, string property, Object value)
    {
        SerializedProperty field = so.FindProperty(property);

        if (field.objectReferenceValue == null && value != null) { field.objectReferenceValue = value; }
    }
}
