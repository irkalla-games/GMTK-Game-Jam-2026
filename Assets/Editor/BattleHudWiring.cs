using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-shot wiring for splitting the battle HUD's single SelectedCharacterPanel into a hero panel
/// (bottom-left) and an enemy panel (top-left) - clones the panel already authored in Game.unity rather
/// than building one from scratch, so both copies share identical status-chip/health-bar wiring with no
/// manual re-pointing: Unity remaps a clone's intra-hierarchy references (panelRoot, nameText,
/// healthFill, shieldFill, healthText, chipParent) onto its own children automatically, while its
/// references to shared assets (icons, glossary, chipPrefab) keep pointing at the originals. Also moves
/// ManaCounter and HandViewer clear of the corner the hero panel now occupies.
///
/// A menu command rather than hand-edited scene YAML because the Editor holds Game.unity in memory
/// while it is open - anything written to that file underneath it is discarded the next time the scene
/// is saved. Going through SerializedObject also means private [SerializeField] fields are set the same
/// way the Inspector sets them, dirty flags and undo included. Same shape as LevelRewardWiring.
///
/// Idempotent: identifies the hero/enemy panels by name, so a re-run re-applies layout and audience
/// rather than cloning a third panel. RectTransform layout values are re-asserted unconditionally every
/// run - that is the whole point of a "move this to bottom-left" tool - but the enemy plate's tint is
/// only set the run it is created, so a hand-tuned colour survives a re-run.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 - that script drives
/// Assembly-CSharp.csproj, which never lists Assets/Editor. Verify with the -IncludeEditor switch, or by
/// focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class BattleHudWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string IntentIconsPath = "Assets/Data/Intent Icons/IntentIcon.asset";

    private const string HeroPanelName = "HeroInfoPanel";
    private const string EnemyPanelName = "EnemyInfoPanel";
    private const string RootName = "Root";
    private const string NameTextName = "NameText";
    private const string IntentIconName = "IntentIcon";
    private const string ManaCounterName = "ManaCounter";
    private const string HandViewerName = "HandViewer";

    private static readonly Color EnemyTint = new(0.14f, 0.05f, 0.06f, 0.88f);

    private static readonly Vector2 TopLeft = new(0f, 1f);
    private static readonly Vector2 BottomLeft = Vector2.zero;

    [MenuItem("Tools/Battle HUD/Wire Info Panels")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Battle HUD wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        SelectedCharacterPanel[] panels = Object.FindObjectsByType<SelectedCharacterPanel>(FindObjectsInactive.Include);

        if (panels.Length == 0)
        {
            Debug.LogError($"Battle HUD wiring: no SelectedCharacterPanel in {ScenePath} - nothing to wire against.");
            return;
        }

        SelectedCharacterPanel enemyPanel = System.Array.Find(panels, p => p.name == EnemyPanelName);

        // First run: the one panel authored in the scene has neither name yet, so it falls through to
        // "whichever one isn't the enemy panel" - there is nothing else it could be.
        SelectedCharacterPanel heroPanel =
            System.Array.Find(panels, p => p.name == HeroPanelName)
            ?? System.Array.Find(panels, p => p != enemyPanel);

        if (heroPanel == null)
        {
            Debug.LogError("Battle HUD wiring: could not identify a hero panel among the "
                           + $"{panels.Length} SelectedCharacterPanel object(s) found.");
            return;
        }

        ConfigureHeroPanel(heroPanel);

        bool enemyPanelCreated = enemyPanel == null;

        if (enemyPanelCreated) { enemyPanel = CloneEnemyPanel(heroPanel); }

        ConfigureEnemyPanel(enemyPanel, enemyPanelCreated);

        MoveManaCounter();
        MoveHandViewer();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Battle HUD wiring: done - scene and assets saved.");
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    // ------------------------------------------------------------------------------------------
    // Hero panel - bottom-left, unchanged content, just moved and named
    // ------------------------------------------------------------------------------------------

    private static void ConfigureHeroPanel(SelectedCharacterPanel panel)
    {
        panel.name = HeroPanelName;

        SetRect((RectTransform)panel.transform, BottomLeft, BottomLeft, BottomLeft, Vector2.zero, Vector2.zero);

        Transform root = panel.transform.Find(RootName);

        if (root != null)
        {
            // pivot.y = 0 keeps LayOutStatuses growing the plate upward off the bottom edge, matching
            // the Tooltip on SelectedCharacterPanel.panelRect.
            SetRect((RectTransform)root, BottomLeft, BottomLeft, BottomLeft, new Vector2(20f, 20f), new Vector2(320f, 170f));
        }

        SetAudience(panel, PanelAudience.PlayerControlled);
    }

    // ------------------------------------------------------------------------------------------
    // Enemy panel - top-left, cloned from the hero panel so its internal wiring is guaranteed identical
    // ------------------------------------------------------------------------------------------

    private static SelectedCharacterPanel CloneEnemyPanel(SelectedCharacterPanel heroPanel)
    {
        GameObject clone = Object.Instantiate(heroPanel.gameObject);
        clone.transform.SetParent(heroPanel.transform.parent, worldPositionStays: false);
        clone.name = EnemyPanelName;

        Debug.Log($"Battle HUD wiring: cloned {EnemyPanelName} from {HeroPanelName}.");

        return clone.GetComponent<SelectedCharacterPanel>();
    }

    private static void ConfigureEnemyPanel(SelectedCharacterPanel panel, bool created)
    {
        panel.name = EnemyPanelName;

        SetRect((RectTransform)panel.transform, TopLeft, TopLeft, TopLeft, Vector2.zero, Vector2.zero);

        Transform root = panel.transform.Find(RootName);
        RectTransform rootRect = root != null ? (RectTransform)root : null;

        if (rootRect != null)
        {
            // -230 clears TurnCounter (anchored top-left, centred (113,-113), 200x200 -> bottom edge at
            // y = -213) by 17px. pivot.y = 1 makes LayOutStatuses grow this plate downward instead of
            // off the top of the screen - the opposite of the hero plate, and both are correct for the
            // corner they sit in.
            SetRect(rootRect, TopLeft, TopLeft, TopLeft, new Vector2(20f, -230f), new Vector2(320f, 170f));

            // Only on creation - a colour tuned by hand afterward should survive a re-run, same as
            // LevelRewardWiring's EnsureBackdrop leaving an existing tint alone.
            if (created)
            {
                Image plate = rootRect.GetComponent<Image>();

                if (plate != null) { plate.color = EnemyTint; }
            }

            // The clone inherited the hero plate's NameText width (292), which ran almost edge-to-edge
            // and would sit under the intent icon this panel is about to gain. Only shrunk on the panel
            // that actually has an icon competing for the corner.
            Transform nameText = rootRect.Find(NameTextName);

            if (nameText is RectTransform nameRect) { nameRect.sizeDelta = new Vector2(250f, nameRect.sizeDelta.y); }
        }

        SetAudience(panel, PanelAudience.NotPlayerControlled);
        WireIntentIcon(panel, rootRect);
    }

    /// <summary>
    /// Builds the small icon in the plate's top-right corner and points SelectedCharacterPanel's
    /// intentIcon/intentIcons fields at it. Left null on the hero panel on purpose - see the class doc
    /// on SelectedCharacterPanel.intentIcon - so this is only ever called for the enemy copy.
    /// </summary>
    private static void WireIntentIcon(SelectedCharacterPanel panel, RectTransform root)
    {
        if (root == null) { return; }

        Transform existing = root.Find(IntentIconName);
        Image icon;

        if (existing != null)
        {
            icon = existing.GetComponent<Image>();
        }
        else
        {
            GameObject go = new(IntentIconName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(root, false);
            go.layer = root.gameObject.layer;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = TopLeft;
            rect.anchorMax = TopLeft;
            rect.pivot = TopLeft;
            rect.anchoredPosition = new Vector2(280f, -10f);
            rect.sizeDelta = new Vector2(32f, 32f);

            icon = go.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false; // SelectedCharacterPanel.RefreshIntent turns it on once there is a sprite

            Debug.Log($"Battle HUD wiring: created {IntentIconName} on {EnemyPanelName}.");
        }

        IntentIcons intentIcons = AssetDatabase.LoadAssetAtPath<IntentIcons>(IntentIconsPath);

        if (intentIcons == null)
        {
            Debug.LogWarning($"Battle HUD wiring: {IntentIconsPath} not found - EnemyInfoPanel.intentIcons left unset.");
        }

        SerializedObject so = new(panel);
        so.FindProperty("intentIcon").objectReferenceValue = icon;

        if (intentIcons != null) { so.FindProperty("intentIcons").objectReferenceValue = intentIcons; }

        so.ApplyModifiedProperties();
    }

    // ------------------------------------------------------------------------------------------
    // Everything else the new corner layout displaces
    // ------------------------------------------------------------------------------------------

    /// ManaCounter always shows ActiveCharacter's energy - hero information - and sat bottom-right,
    /// exactly where the card fan is moving to. Anchored beside the hero plate rather than above it,
    /// because that plate grows upward as its status row gains rows.
    private static void MoveManaCounter()
    {
        GameObject mana = GameObject.Find(ManaCounterName);

        if (mana == null)
        {
            Debug.LogWarning($"Battle HUD wiring: no {ManaCounterName} in {ScenePath} - not moved.");
            return;
        }

        RectTransform rect = mana.GetComponent<RectTransform>();

        if (rect == null) { return; }

        rect.anchorMin = BottomLeft;
        rect.anchorMax = BottomLeft;
        rect.anchoredPosition = new Vector2(400f, 66f);
    }

    /// HandViewer is world-space on a spline, not UI - transform position is the only knob (see
    /// ActiveHandViewer.UpdateCardPosition). ---VISUALS sits at world x 0.81334, so this puts the fan's
    /// centre at world x 4.0, clear of both the hero plate (ends at world x -5.4) and the relocated
    /// ManaCounter (ends at world x -4.3).
    private static void MoveHandViewer()
    {
        GameObject hand = GameObject.Find(HandViewerName);

        if (hand == null)
        {
            Debug.LogWarning($"Battle HUD wiring: no {HandViewerName} in {ScenePath} - not moved.");
            return;
        }

        Vector3 position = hand.transform.localPosition;
        hand.transform.localPosition = new Vector3(3.19f, position.y, position.z);
    }

    // ------------------------------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------------------------------

    private static void SetAudience(SelectedCharacterPanel panel, PanelAudience audience)
    {
        SerializedObject so = new(panel);
        so.FindProperty("audience").enumValueIndex = (int)audience;
        so.ApplyModifiedProperties();
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }
}
