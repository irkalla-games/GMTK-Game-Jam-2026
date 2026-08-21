using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-shot wiring for the party portrait row and the Tab party sheet. Removes HeroInfoPanel (the
/// PlayerControlled SelectedCharacterPanel that used to occupy the bottom-left corner) and ManaCounter,
/// builds the HeroPortrait/PartySheetColumn/StatusDetailRow prefabs the first time this runs, and wires
/// a PartyPortraitPanel onto the main HUD Canvas plus a PartySheetPanel under RewardCanvas. Leaves
/// EnemyInfoPanel alone - it answers a different question this change does not touch.
///
/// A menu command rather than hand-edited scene YAML because the Editor holds Game.unity in memory while
/// it is open - anything written to that file underneath it is discarded the next time the scene is
/// saved. Going through SerializedObject also means private [SerializeField] fields are set the same way
/// the Inspector sets them, dirty flags and undo included. Same shape as CardPileWiring/BattleHudWiring.
///
/// Idempotent: every object is found by name (or loaded from its prefab path) before being created, so a
/// re-run refreshes wiring rather than duplicating anything. StatusIcons/Glossary/the StatusChip prefab
/// are copied off EnemyInfoPanel's own SelectedCharacterPanel rather than hardcoded, since that panel
/// keeps working after this runs and is guaranteed to already hold the right references.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - that script drives
/// Assembly-CSharp.csproj, which never lists Assets/Editor. Verify with the -IncludeEditor switch, or by
/// focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class PartyPortraitWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string SkipButtonPrefabPath = "Assets/Prefabs/UI/SkipButtonPrefab.prefab";
    private const string HeroPortraitPrefabPath = "Assets/Prefabs/UI/HeroPortrait.prefab";
    private const string SheetColumnPrefabPath = "Assets/Prefabs/UI/PartySheetColumn.prefab";
    private const string StatusDetailRowPrefabPath = "Assets/Prefabs/UI/StatusDetailRow.prefab";
    private const string StatusPipPrefabPath = "Assets/Prefabs/UI/StatusPip.prefab";

    private const string CanvasName = "Canvas";
    private const string HeroPanelName = "HeroInfoPanel";
    private const string EnemyPanelName = "EnemyInfoPanel";
    private const string ManaCounterName = "ManaCounter";

    private const string PortraitPanelName = "PartyPortraitPanel";
    private const string RowName = "Row";

    private const string SheetPanelName = "PartySheetPanel";
    private const string SheetBackdropName = "SheetBackdrop";
    private const string SheetTitleName = "SheetTitle";
    private const string SheetCloseName = "SheetCloseButton";
    private const string ColumnParentName = "ColumnParent";
    private const string StatusParentName = "StatusParent";

    // Where the vacated HeroInfoPanel + ManaCounter corner leaves room - see BattleHudWiring/
    // CardPileWiring for the neighbouring HUD elements this has to stay clear of (DeckPileButton sits
    // at x=520 bottom-left anchored). Tunable afterward in the Inspector like every other HUD position
    // this project wires up.
    //
    // Y keeps the active hero's full scaled height - root height x activeScale, currently
    // 285 x 1.25 = ~356, half ~178 - clear of the screen's own bottom edge (y=0), since Row sits at the
    // canvas's bottom-left corner and everything here hangs off that point. Kept close to that ~178
    // floor rather than given generous headroom, so the row still hugs the bottom of the screen.
    private static readonly Vector2 RowAnchoredPosition = new(200f, 190f);

    // Matches PartyPortraitPanel.cs's own field defaults (kept in step by hand, same as this file's own
    // HeroPortraitPrefabPath/HeroPortrait.prefab pairing) - pushed unconditionally below since the
    // scene's already-serialized instance would otherwise never see a .cs default change.
    private const float RestWidth = 72f;
    private const float ActiveWidth = 210f;
    private const float RowSpacing = 18f;
    private const float RestScale = 0.5f;

    [MenuItem("Tools/Battle HUD/Wire Party Portraits")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Party portrait wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        GameObject canvas = GameObject.Find(CanvasName);

        if (canvas == null)
        {
            Debug.LogError($"Party portrait wiring: no {CanvasName} in {ScenePath} - nowhere to put the row.");
            return;
        }

        if (!ReadEnemyPanelAssets(out StatusIcons icons, out Glossary glossary, out StatusChip chipPrefab))
        {
            return;
        }

        RemoveHeroPanel(canvas.transform);
        RemoveManaCounter(canvas.transform);

        HeroPortrait heroPortraitPrefab = EnsureHeroPortraitPrefab(chipPrefab);

        if (heroPortraitPrefab != null)
        {
            WirePartyPortraitPanel(canvas.transform, heroPortraitPrefab, icons, glossary);
        }

        WirePartySheetPanel(icons, glossary);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Party portrait wiring: done - scene and assets saved.");
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    // ------------------------------------------------------------------------------------------
    // Shared art/data - read off the enemy panel rather than hardcoded, so both panels always agree
    // ------------------------------------------------------------------------------------------

    private static bool ReadEnemyPanelAssets(out StatusIcons icons, out Glossary glossary, out StatusChip chipPrefab)
    {
        icons = null;
        glossary = null;
        chipPrefab = null;

        SelectedCharacterPanel[] panels = Object.FindObjectsByType<SelectedCharacterPanel>(FindObjectsInactive.Include);
        SelectedCharacterPanel enemyPanel = System.Array.Find(panels, p => p.name == EnemyPanelName);

        if (enemyPanel == null)
        {
            Debug.LogError($"Party portrait wiring: no {EnemyPanelName} in {ScenePath} - run Battle HUD "
                           + "wiring first, it is where StatusIcons/Glossary/the StatusChip prefab live.");
            return false;
        }

        SerializedObject so = new(enemyPanel);
        icons = so.FindProperty("icons").objectReferenceValue as StatusIcons;
        glossary = so.FindProperty("glossary").objectReferenceValue as Glossary;
        chipPrefab = so.FindProperty("chipPrefab").objectReferenceValue as StatusChip;

        if (icons == null || glossary == null || chipPrefab == null)
        {
            Debug.LogWarning($"Party portrait wiring: {EnemyPanelName} is missing icons/glossary/chipPrefab - "
                             + "the new panels will carry the same gaps until it is fixed.");
        }

        return true;
    }

    // ------------------------------------------------------------------------------------------
    // Removing what the row replaces
    // ------------------------------------------------------------------------------------------

    private static void RemoveHeroPanel(Transform canvas)
    {
        Transform hero = canvas.Find(HeroPanelName);

        if (hero == null) { return; }

        Object.DestroyImmediate(hero.gameObject);
        Debug.Log($"Party portrait wiring: removed {HeroPanelName}.");
    }

    private static void RemoveManaCounter(Transform canvas)
    {
        Transform mana = canvas.Find(ManaCounterName);

        if (mana == null) { return; }

        Object.DestroyImmediate(mana.gameObject);
        Debug.Log($"Party portrait wiring: removed {ManaCounterName}.");
    }

    // ------------------------------------------------------------------------------------------
    // HeroPortrait prefab - built once, the first time this ever runs
    // ------------------------------------------------------------------------------------------

    private static HeroPortrait EnsureHeroPortraitPrefab(StatusChip chipPrefab)
    {
        HeroPortrait existing = AssetDatabase.LoadAssetAtPath<HeroPortrait>(HeroPortraitPrefabPath);

        if (existing != null) { return existing; }

        GameObject host = new("HeroPortrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform hostRect = host.GetComponent<RectTransform>();
        Centre(hostRect);
        hostRect.sizeDelta = new Vector2(96f, 190f);

        // Transparent, not a white plate - the portrait sits directly against whatever renders behind
        // the HUD. Still the Button's targetGraphic, so raycasts and SetHighlighted's colour swap both
        // keep working; there is just nothing to see at alpha 0 until the two colours in HeroPortrait
        // are given some visible tint to swap between.
        Image frame = host.GetComponent<Image>();
        frame.color = new Color(1f, 1f, 1f, 0f);

        Button button = host.AddComponent<Button>();
        button.targetGraphic = frame;

        Image portraitImage = CreateImage(host.transform, "PortraitImage", new Vector2(0f, 30f), new Vector2(80f, 80f));
        portraitImage.preserveAspect = true;

        TMP_Text nameLabel = CreateLabel(host.transform, "NameLabel", new Vector2(0f, -16f), new Vector2(92f, 20f), 16f);

        Image healthBg = CreateImage(host.transform, "HealthBarBg", new Vector2(0f, -36f), new Vector2(86f, 10f));
        healthBg.color = new Color(0f, 0f, 0f, 0.6f);

        Image healthFill = CreateFillImage(healthBg.transform, "HealthFill", new Color(0.2f, 0.8f, 0.3f, 1f));
        Image shieldFill = CreateFillImage(healthBg.transform, "ShieldFill", new Color(0.4f, 0.7f, 1f, 1f));
        shieldFill.enabled = false;

        TMP_Text healthText = CreateLabel(host.transform, "HealthText", new Vector2(0f, -50f), new Vector2(92f, 16f), 12f);

        RectTransform pipParent = CreateAnchor(host.transform, "PipParent", new Vector2(0f, -68f));
        RectTransform chipParent = CreateAnchor(host.transform, "ChipParent", new Vector2(0f, -90f));

        Image pipPrefab = EnsurePipPrefab();

        HeroPortrait portrait = host.AddComponent<HeroPortrait>();

        SerializedObject so = new(portrait);
        so.FindProperty("button").objectReferenceValue = button;
        so.FindProperty("portraitImage").objectReferenceValue = portraitImage;
        so.FindProperty("frame").objectReferenceValue = frame;
        so.FindProperty("nameLabel").objectReferenceValue = nameLabel;
        so.FindProperty("healthFill").objectReferenceValue = healthFill;
        so.FindProperty("shieldFill").objectReferenceValue = shieldFill;
        so.FindProperty("healthText").objectReferenceValue = healthText;
        so.FindProperty("pipParent").objectReferenceValue = pipParent;
        so.FindProperty("pipPrefab").objectReferenceValue = pipPrefab;
        so.FindProperty("chipParent").objectReferenceValue = chipParent;
        so.FindProperty("chipPrefab").objectReferenceValue = chipPrefab;
        so.ApplyModifiedProperties();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(host, HeroPortraitPrefabPath);
        Object.DestroyImmediate(host);

        Debug.Log($"Party portrait wiring: created {HeroPortraitPrefabPath}.");

        return saved.GetComponent<HeroPortrait>();
    }

    /// <summary>
    /// A standalone prefab, not a child left nested inside HeroPortrait's own hierarchy - the same
    /// reasoning StatusChip.prefab is its own asset rather than a template hidden inside
    /// SelectedCharacterPanel. HeroPortrait.RefreshPips instantiates a fresh clone under pipParent per
    /// pip; a pip nested inside the portrait prefab itself would be saved as a permanently visible extra
    /// child of every portrait instance instead.
    /// </summary>
    private static Image EnsurePipPrefab()
    {
        Image existing = AssetDatabase.LoadAssetAtPath<Image>(StatusPipPrefabPath);

        if (existing != null) { return existing; }

        GameObject host = new("StatusPip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        RectTransform rect = host.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(14f, 14f);

        Image image = host.GetComponent<Image>();
        image.color = Color.white;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(host, StatusPipPrefabPath);
        Object.DestroyImmediate(host);

        Debug.Log($"Party portrait wiring: created {StatusPipPrefabPath}.");

        return saved.GetComponent<Image>();
    }

    // ------------------------------------------------------------------------------------------
    // PartyPortraitPanel - the row itself
    // ------------------------------------------------------------------------------------------

    private static void WirePartyPortraitPanel(Transform canvas, HeroPortrait portraitPrefab, StatusIcons icons, Glossary glossary)
    {
        Transform existing = canvas.Find(PortraitPanelName);
        PartyPortraitPanel panel;
        RectTransform row;

        GameObject rowGo;

        if (existing != null)
        {
            panel = existing.GetComponent<PartyPortraitPanel>();
            rowGo = existing.Find(RowName).gameObject;
        }
        else
        {
            GameObject host = new(PortraitPanelName, typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            host.layer = canvas.gameObject.layer;

            RectTransform hostRect = host.GetComponent<RectTransform>();
            hostRect.anchorMin = Vector2.zero;
            hostRect.anchorMax = Vector2.zero;
            hostRect.pivot = Vector2.zero;
            hostRect.anchoredPosition = Vector2.zero;
            hostRect.sizeDelta = Vector2.zero;

            panel = host.AddComponent<PartyPortraitPanel>();

            rowGo = new GameObject(RowName, typeof(RectTransform));
            rowGo.transform.SetParent(host.transform, false);
            rowGo.layer = host.layer;

            Debug.Log($"Party portrait wiring: created {PortraitPanelName}.");
        }

        // Re-applied every run, existing row or not - a prior run's position is exactly what a re-run
        // after tuning RowAnchoredPosition is meant to fix (see HeroPortrait.prefab's resize, which is
        // what pushed this off the bottom of the screen at the old value).
        row = rowGo.GetComponent<RectTransform>();
        row.anchorMin = Vector2.zero;
        row.anchorMax = Vector2.zero;
        row.pivot = new Vector2(0.5f, 0.5f);
        row.anchoredPosition = RowAnchoredPosition;
        row.sizeDelta = Vector2.zero;

        SerializedObject so = new(panel);
        so.FindProperty("portraitPrefab").objectReferenceValue = portraitPrefab;
        so.FindProperty("row").objectReferenceValue = row;

        if (icons != null) { so.FindProperty("icons").objectReferenceValue = icons; }
        if (glossary != null) { so.FindProperty("glossary").objectReferenceValue = glossary; }

        // Unconditional, like icons/glossary above - a scene object's already-serialized float does not
        // pick up a new .cs default just because PartyPortraitPanel.cs changed, so re-running this is
        // the only way a resize of HeroPortrait.prefab (HeroPortraitResizeWiring) actually reaches the
        // row layout that has to match it.
        so.FindProperty("restWidth").floatValue = RestWidth;
        so.FindProperty("activeWidth").floatValue = ActiveWidth;
        so.FindProperty("spacing").floatValue = RowSpacing;
        so.FindProperty("restScale").floatValue = RestScale;

        so.ApplyModifiedProperties();

        Debug.Log($"Party portrait wiring: {PortraitPanelName} fields set.");
    }

    // ------------------------------------------------------------------------------------------
    // PartySheetPanel - the Tab screen, under the same canvas CardPilePanel already uses
    // ------------------------------------------------------------------------------------------

    private static void WirePartySheetPanel(StatusIcons icons, Glossary glossary)
    {
        // Same canvas CardPilePanel was given, and for the same reason - it inherits the reward UI's
        // scaler and camera rather than keeping a second canvas in step with CameraFrame by hand. Not
        // fatal if missing: portrait wiring above still stands on its own.
        CardRemovalPanel removalPanel = Object.FindAnyObjectByType<CardRemovalPanel>(FindObjectsInactive.Include);

        if (removalPanel == null)
        {
            Debug.LogWarning("Party portrait wiring: no CardRemovalPanel found - run Level Reward wiring "
                             + "first to get a canvas for PartySheetPanel. Skipping the Tab screen for now.");
            return;
        }

        StatusDetailRow rowPrefab = EnsureStatusDetailRowPrefab();
        PartySheetColumn columnPrefab = EnsureSheetColumnPrefab(rowPrefab);

        if (rowPrefab == null || columnPrefab == null) { return; }

        PartySheetPanel panel = Object.FindAnyObjectByType<PartySheetPanel>(FindObjectsInactive.Include);

        if (panel == null)
        {
            GameObject host = new(SheetPanelName, typeof(RectTransform));
            host.transform.SetParent(removalPanel.transform.parent, false);
            host.layer = removalPanel.gameObject.layer;

            Stretch(host.GetComponent<RectTransform>());

            panel = host.AddComponent<PartySheetPanel>();

            Debug.Log($"Party portrait wiring: created {SheetPanelName}.");
        }

        GameObject backdrop = EnsureSheetBackdrop(panel.transform);
        RectTransform columnParent = EnsureColumnParent(backdrop.transform);

        SerializedObject so = new(panel);
        SetIfEmpty(so, "root", backdrop);
        SetIfEmpty(so, "closeButton", EnsureSheetCloseButton(backdrop.transform));
        SetIfEmpty(so, "columnPrefab", columnPrefab);
        SetIfEmpty(so, "columnParent", columnParent);

        // Unconditional (not SetIfEmpty) like WirePartyPortraitPanel's own icons/glossary assignment -
        // these two mirror EnemyInfoPanel on every run so the three panels can never quietly drift apart,
        // the same role chipSprite plays in CardPileWiring.
        if (icons != null) { so.FindProperty("icons").objectReferenceValue = icons; }
        if (glossary != null) { so.FindProperty("glossary").objectReferenceValue = glossary; }

        so.ApplyModifiedProperties();

        // Title is created but never wired to a field - PartySheetPanel has no titleLabel, unlike
        // CardPilePanel, since the sheet always shows the whole party rather than one named pile.
        EnsureSheetTitle(backdrop.transform);

        Debug.Log($"Party portrait wiring: {SheetPanelName} fields set.");
    }

    private static GameObject EnsureSheetBackdrop(Transform parent)
    {
        Transform existing = parent.Find(SheetBackdropName);

        if (existing != null) { return existing.gameObject; }

        GameObject backdrop = new(SheetBackdropName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backdrop.transform.SetParent(parent, false);
        backdrop.layer = parent.gameObject.layer;

        Stretch(backdrop.GetComponent<RectTransform>());
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        // Awake forces this off anyway - doing it here too keeps the Editor view uncluttered, same as
        // CardPilePanel's own EnsureBackdrop.
        backdrop.SetActive(false);

        return backdrop;
    }

    /// Top-anchored rather than the Centre-anchored CreateLabel default - a title sitting at the
    /// backdrop's vertical centre would overlap the column content instead of heading it. Same position
    /// CardPileWiring.EnsureTitle uses for CardPilePanel's own title, on the same canvas.
    private static void EnsureSheetTitle(Transform parent)
    {
        if (parent.Find(SheetTitleName) != null) { return; }

        TMP_Text text = CreateLabel(parent, SheetTitleName, Vector2.zero, new Vector2(1200f, 90f), 56f);
        text.text = "Party";

        RectTransform rect = (RectTransform)text.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -60f);
    }

    private static RectTransform EnsureColumnParent(Transform parent)
    {
        Transform existing = parent.Find(ColumnParentName);

        if (existing != null) { return (RectTransform)existing; }

        GameObject go = new(ColumnParentName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = new Vector2(0f, -20f);
        rect.sizeDelta = Vector2.zero;

        return rect;
    }

    /// Reuses the same skip button prefab CardRemovalPanel's own Cancel button and CardPilePanel's own
    /// Close button are built from, so this matches the rest of the reward UI's chrome.
    private static Object EnsureSheetCloseButton(Transform parent)
    {
        Transform existing = parent.Find(SheetCloseName);

        if (existing != null) { return existing.GetComponent<Button>(); }

        Button prefab = AssetDatabase.LoadAssetAtPath<Button>(SkipButtonPrefabPath);

        if (prefab == null)
        {
            Debug.LogWarning($"Party portrait wiring: {SkipButtonPrefabPath} not found - Close button not "
                             + "created, assign one by hand.");
            return null;
        }

        Button button = (Button)PrefabUtility.InstantiatePrefab(prefab, parent);
        button.name = SheetCloseName;

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 120f);

        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        if (label != null) { label.text = "Close"; }

        return button;
    }

    // ------------------------------------------------------------------------------------------
    // StatusDetailRow prefab - built once
    // ------------------------------------------------------------------------------------------

    private static StatusDetailRow EnsureStatusDetailRowPrefab()
    {
        StatusDetailRow existing = AssetDatabase.LoadAssetAtPath<StatusDetailRow>(StatusDetailRowPrefabPath);

        if (existing != null) { return existing; }

        GameObject host = new("StatusDetailRow", typeof(RectTransform));
        RectTransform hostRect = host.GetComponent<RectTransform>();
        hostRect.sizeDelta = new Vector2(220f, 56f);

        Image icon = CreateImage(host.transform, "Icon", Vector2.zero, new Vector2(36f, 36f));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(4f, 0f);

        TMP_Text title = CreateLeftLabel(host.transform, "TitleLabel", new Vector2(56f, -2f), new Vector2(160f, 20f), 18f);
        title.fontStyle = FontStyles.Bold;

        TMP_Text body = CreateLeftLabel(host.transform, "BodyLabel", new Vector2(56f, -22f), new Vector2(160f, 32f), 13f);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.fontStyle = FontStyles.Normal;

        StatusDetailRow row = host.AddComponent<StatusDetailRow>();

        SerializedObject so = new(row);
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("titleLabel").objectReferenceValue = title;
        so.FindProperty("bodyLabel").objectReferenceValue = body;
        so.ApplyModifiedProperties();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(host, StatusDetailRowPrefabPath);
        Object.DestroyImmediate(host);

        Debug.Log($"Party portrait wiring: created {StatusDetailRowPrefabPath}.");

        return saved.GetComponent<StatusDetailRow>();
    }

    // ------------------------------------------------------------------------------------------
    // PartySheetColumn prefab - built once
    // ------------------------------------------------------------------------------------------

    private static PartySheetColumn EnsureSheetColumnPrefab(StatusDetailRow rowPrefab)
    {
        PartySheetColumn existing = AssetDatabase.LoadAssetAtPath<PartySheetColumn>(SheetColumnPrefabPath);

        if (existing != null) { return existing; }

        if (rowPrefab == null)
        {
            Debug.LogError("Party portrait wiring: StatusDetailRow prefab missing - cannot build PartySheetColumn.");
            return null;
        }

        GameObject host = new("PartySheetColumn", typeof(RectTransform));
        RectTransform hostRect = host.GetComponent<RectTransform>();
        hostRect.sizeDelta = new Vector2(240f, 640f);

        Image portraitImage = CreateImage(host.transform, "PortraitImage", new Vector2(0f, -60f), new Vector2(140f, 140f));
        portraitImage.preserveAspect = true;

        TMP_Text nameLabel = CreateLabel(host.transform, "NameLabel", new Vector2(0f, -140f), new Vector2(220f, 30f), 28f);

        Image healthBg = CreateImage(host.transform, "HealthBarBg", new Vector2(0f, -180f), new Vector2(220f, 20f));
        healthBg.color = new Color(0f, 0f, 0f, 0.6f);

        Image healthFill = CreateFillImage(healthBg.transform, "HealthFill", new Color(0.2f, 0.8f, 0.3f, 1f));
        Image shieldFill = CreateFillImage(healthBg.transform, "ShieldFill", new Color(0.4f, 0.7f, 1f, 1f));
        shieldFill.enabled = false;

        TMP_Text healthText = CreateLabel(host.transform, "HealthText", new Vector2(0f, -205f), new Vector2(220f, 24f), 18f);
        TMP_Text energyText = CreateLabel(host.transform, "EnergyText", new Vector2(0f, -232f), new Vector2(220f, 24f), 18f);

        GameObject statusParentGo = new(StatusParentName, typeof(RectTransform));
        statusParentGo.transform.SetParent(host.transform, false);
        statusParentGo.layer = host.layer;

        RectTransform statusParent = statusParentGo.GetComponent<RectTransform>();
        statusParent.anchorMin = new Vector2(0f, 1f);
        statusParent.anchorMax = new Vector2(1f, 1f);
        statusParent.pivot = new Vector2(0.5f, 1f);
        statusParent.anchoredPosition = new Vector2(0f, -260f);
        statusParent.sizeDelta = new Vector2(0f, 0f);

        PartySheetColumn column = host.AddComponent<PartySheetColumn>();

        SerializedObject so = new(column);
        so.FindProperty("portraitImage").objectReferenceValue = portraitImage;
        so.FindProperty("nameLabel").objectReferenceValue = nameLabel;
        so.FindProperty("healthFill").objectReferenceValue = healthFill;
        so.FindProperty("shieldFill").objectReferenceValue = shieldFill;
        so.FindProperty("healthText").objectReferenceValue = healthText;
        so.FindProperty("energyText").objectReferenceValue = energyText;
        so.FindProperty("statusParent").objectReferenceValue = statusParent;
        so.FindProperty("rowPrefab").objectReferenceValue = rowPrefab;
        so.ApplyModifiedProperties();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(host, SheetColumnPrefabPath);
        Object.DestroyImmediate(host);

        Debug.Log($"Party portrait wiring: created {SheetColumnPrefabPath}.");

        return saved.GetComponent<PartySheetColumn>();
    }

    // ------------------------------------------------------------------------------------------
    // Shared helpers - deliberately duplicated rather than shared with the other *Wiring scripts,
    // matching how CharacterSelectWiring documents that same choice for its own Centre/CreateLabel pair.
    // ------------------------------------------------------------------------------------------

    private static Image CreateImage(Transform parent, string objectName, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        return go.GetComponent<Image>();
    }

    /// Stretched to fill its parent and set up as a horizontal fill bar - HealthBarFill only ever writes
    /// fillAmount (and, defensively, shieldFill's origin), so type/method/origin have to be right from
    /// authoring, unlike the five character prefabs HealthBarFill.cs's own comment warns are backwards.
    private static Image CreateFillImage(Transform parent, string objectName, Color color)
    {
        GameObject go = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        Stretch(rect);

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.fillAmount = 1f;

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

    /// Same as CreateLabel but left-anchored/left-aligned - StatusDetailRow's title and body sit beside
    /// an icon rather than centred over one, and the body in particular needs to wrap.
    private static TMP_Text CreateLeftLabel(Transform parent, string objectName, Vector2 anchoredPosition, Vector2 size, float fontSize)
    {
        GameObject go = new(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;

        ApplySampleFont(text);

        return text;
    }

    /// An empty point-anchor RectTransform - HeroPortrait pools pips/chips as its children at runtime, so
    /// this only has to mark where that row starts, not contain anything itself.
    private static RectTransform CreateAnchor(Transform parent, string objectName, Vector2 anchoredPosition)
    {
        GameObject go = new(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = Vector2.zero;

        return rect;
    }

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

    /// Assigns only an unset field, so a value tuned by hand survives a re-run - same as
    /// CardPileWiring.SetIfEmpty.
    private static void SetIfEmpty(SerializedObject so, string property, Object value)
    {
        SerializedProperty field = so.FindProperty(property);

        if (field.objectReferenceValue == null && value != null) { field.objectReferenceValue = value; }
    }
}
