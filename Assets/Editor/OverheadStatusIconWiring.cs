using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires CharacterOverheadViewer's statusIconParent/statusIconPrefab/statusIcons row onto every base
/// prefab that owns the component - not just heroes and enemies, but every totem too, since each of the
/// 15 files under Assets/Prefabs/Totem/ is a fully independent prefab with its own serialized
/// CharacterOverheadViewer rather than a Prefab Variant of Totem.prefab. An explicit path list, not a
/// directory sweep, so a future unrelated prefab can never be silently caught by this.
///
/// The Overhead RectTransform is found by walking healthFill's own parent rather than assuming a child
/// name - totem/enemy/hero hierarchies do not share identical object names, only the same wiring
/// convention (BarBack/HealthFill/ShieldFill/IntentIcon as direct children of one small canvas).
///
/// The row sits to the left of the bar, growing further left as more stack up, and low enough to clear
/// the bar vertically - see RowOffset below for the exact numbers this was tuned against.
///
/// Idempotent, and repairs rather than skips: an existing StatusIconRow/OverheadStatusIcon.prefab is
/// reused rather than duplicated, but every value - position, size, prefab structure - is re-applied
/// unconditionally on every run, same as PartySheetStyling.cs's own restyle pass. That is what lets a
/// re-run actually fix a prior run's numbers rather than only ever filling in what was missing.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class OverheadStatusIconWiring
{
    private const string StatusIconPrefabPath = "Assets/Prefabs/UI/OverheadStatusIcon.prefab";
    private const string StatusIconsAssetPath = "Assets/Scripts/Statuses/StatusIconData/StatusIcons.asset";
    private const string RowName = "StatusIconRow";

    // The bar fills Overhead's full 160-wide, 8-tall rect, i.e. x in [-80, 80] and y in [-4, 4]. The
    // row's anchor lands exactly on the bar's own left edge - CharacterOverheadViewer.PlaceIcon pivots
    // each icon on its own left edge and grows rightward from here, so the row reads as flush with the
    // bar rather than floating off to one side of it. Y keeps a clear gap below the bar's bottom edge.
    private const float RowOffsetX = -80f;
    private const float RowOffsetY = -20f;

    private const float BackgroundSize = 22f;
    private const float GlyphSize = 18f;
    private const float StatusIconSpacing = 4f;

    private static readonly string[] TargetPrefabPaths =
    {
        "Assets/Prefabs/Player/PlayerKnight.prefab",
        "Assets/Prefabs/Player/PlayerMage.prefab",
        "Assets/Prefabs/Player/PlayerRogue.prefab",
        "Assets/Prefabs/Enemies/SkeletonWarrior.prefab",
        "Assets/Prefabs/Enemies/EnemyRanger.prefab",
        "Assets/Prefabs/Allies/SkeletonAlly.prefab",
        "Assets/Prefabs/Totem/Totem.prefab",
        "Assets/Prefabs/Totem/AegisTotem.prefab",
        "Assets/Prefabs/Totem/BastionTotem.prefab",
        "Assets/Prefabs/Totem/BulwarkTotem.prefab",
        "Assets/Prefabs/Totem/RampartTotem.prefab",
        "Assets/Prefabs/Totem/WarcryTotem.prefab",
        "Assets/Prefabs/Totem/WarlordTotem.prefab",
        "Assets/Prefabs/Totem/CurseTotem.prefab",
        "Assets/Prefabs/Totem/EnfeebleTotem.prefab",
        "Assets/Prefabs/Totem/FractureTotem.prefab",
        "Assets/Prefabs/Totem/HexTotem.prefab",
        "Assets/Prefabs/Totem/RuinTotem.prefab",
        "Assets/Prefabs/Totem/SapTotem.prefab",
        "Assets/Prefabs/Totem/VenomTotem.prefab",
        "Assets/Prefabs/Totem/WeakenTotem.prefab",
    };

    [MenuItem("Tools/Battle HUD/Wire Overhead Status Icons")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Overhead status icon wiring: exit Play Mode first - prefab edits made in play do not persist.");
            return;
        }

        StatusIcons icons = AssetDatabase.LoadAssetAtPath<StatusIcons>(StatusIconsAssetPath);

        if (icons == null)
        {
            Debug.LogError($"Overhead status icon wiring: {StatusIconsAssetPath} not found - nowhere to pull icon art from.");
            return;
        }

        OverheadStatusIcon iconPrefab = EnsureIconPrefab();
        int wired = 0;

        foreach (string path in TargetPrefabPaths)
        {
            if (WireOne(path, iconPrefab, icons)) { wired++; }
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"Overhead status icon wiring: done - {wired}/{TargetPrefabPaths.Length} prefab(s) wired.");
    }

    private static bool WireOne(string prefabPath, OverheadStatusIcon iconPrefab, StatusIcons icons)
    {
        GameObject existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (existingAsset == null)
        {
            Debug.LogError($"Overhead status icon wiring: {prefabPath} not found - skipping.");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        CharacterOverheadViewer viewer = root.GetComponentInChildren<CharacterOverheadViewer>(true);

        if (viewer == null)
        {
            Debug.LogError($"Overhead status icon wiring: {prefabPath} has no CharacterOverheadViewer - skipping.");
            PrefabUtility.UnloadPrefabContents(root);
            return false;
        }

        SerializedObject so = new(viewer);
        Image healthFill = so.FindProperty("healthFill").objectReferenceValue as Image;

        if (healthFill == null)
        {
            Debug.LogError($"Overhead status icon wiring: {prefabPath}'s CharacterOverheadViewer has no "
                + "healthFill - cannot locate the Overhead canvas. Skipping.");
            PrefabUtility.UnloadPrefabContents(root);
            return false;
        }

        Transform overhead = healthFill.transform.parent;
        Transform existingRow = overhead.Find(RowName);
        GameObject rowGo;

        if (existingRow != null)
        {
            rowGo = existingRow.gameObject;
        }
        else
        {
            rowGo = new GameObject(RowName, typeof(RectTransform));
            rowGo.transform.SetParent(overhead, false);
            rowGo.layer = overhead.gameObject.layer;
        }

        // Re-applied every run, existing row or not - a prior run's position is exactly what a re-run
        // after tuning these constants is meant to fix.
        RectTransform rowRect = rowGo.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(RowOffsetX, RowOffsetY);
        rowRect.sizeDelta = Vector2.zero;

        so.FindProperty("statusIconParent").objectReferenceValue = rowRect;
        so.FindProperty("statusIconPrefab").objectReferenceValue = iconPrefab;
        so.FindProperty("statusIcons").objectReferenceValue = icons;
        so.FindProperty("statusIconSize").floatValue = BackgroundSize;
        so.FindProperty("statusIconSpacing").floatValue = StatusIconSpacing;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        return true;
    }

    // ------------------------------------------------------------------------------------------
    // OverheadStatusIcon.prefab - a dark backing plate (the root, matching IntentBg's own colour) with
    // a smaller glyph Image inset inside it. Deliberately not StatusChip (stack-count badge and hover
    // tooltip this row was asked not to have) or StatusPip (tinted blue for energy specifically).
    // Repairs an existing asset in place rather than skipping it, so a structural change here (like this
    // pass's background/icon split) actually reaches a prefab a previous run already created.
    // ------------------------------------------------------------------------------------------

    private static OverheadStatusIcon EnsureIconPrefab()
    {
        GameObject existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(StatusIconPrefabPath);
        bool isNew = existingAsset == null;

        GameObject root = isNew
            ? new GameObject("OverheadStatusIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
            : PrefabUtility.LoadPrefabContents(StatusIconPrefabPath);

        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = new Vector2(BackgroundSize, BackgroundSize);

        Image background = root.GetComponent<Image>();
        if (background == null) { background = root.AddComponent<Image>(); }
        background.color = new Color(0f, 0f, 0f, 0.6f);
        background.raycastTarget = false;

        Transform iconTransform = root.transform.Find("Icon");
        GameObject iconGo;

        if (iconTransform != null)
        {
            iconGo = iconTransform.gameObject;
        }
        else
        {
            iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(root.transform, false);
            iconGo.layer = root.layer;
        }

        RectTransform iconRect = (RectTransform)iconGo.transform;
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        float inset = (BackgroundSize - GlyphSize) / 2f;
        iconRect.offsetMin = new Vector2(inset, inset);
        iconRect.offsetMax = new Vector2(-inset, -inset);

        Image iconImage = iconGo.GetComponent<Image>();
        if (iconImage == null) { iconImage = iconGo.AddComponent<Image>(); }
        iconImage.color = Color.white;
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;

        OverheadStatusIcon component = root.GetComponent<OverheadStatusIcon>();
        if (component == null) { component = root.AddComponent<OverheadStatusIcon>(); }

        SerializedObject so = new(component);
        so.FindProperty("icon").objectReferenceValue = iconImage;
        so.ApplyModifiedProperties();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StatusIconPrefabPath);

        if (isNew) { Object.DestroyImmediate(root); }
        else { PrefabUtility.UnloadPrefabContents(root); }

        Debug.Log($"Overhead status icon wiring: {(isNew ? "created" : "updated")} {StatusIconPrefabPath}.");

        return saved.GetComponent<OverheadStatusIcon>();
    }
}
