using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Resizes HeroPortrait.prefab ~1.5x - rebudgeting the bottom section from scratch rather than blindly
/// scaling it, since the chip row already overflowed past the panel's own bottom edge before this and a
/// uniform ×1.5 of that would only make it worse - and assigns each player class prefab's
/// Character.portrait: White Cross for the Knight, White Witch Hat for the Mage, White Mask for the
/// Rogue. Character.Portrait is read generically by HeroPortrait, PartySheetColumn and CharacterOption,
/// so setting it once here is what the Tab panel and character-select screen also pick up - no separate
/// art needed for either.
///
/// Edits existing prefabs via PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset rather than deleting
/// and recreating them, which is what keeps their file GUIDs - and every scene/prefab reference to them
/// - intact. Same technique PartySheetStyling.cs already established for this project. A separate
/// command from Tools/Battle HUD/Wire Party Portraits, not a change to it:
/// PartyPortraitWiring.EnsureHeroPortraitPrefab early-returns once the prefab exists, which is the right
/// answer to "does this exist at all" and the wrong one to "does this match the current spec."
///
/// Idempotent: every value is re-applied unconditionally on every run.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class HeroPortraitResizeWiring
{
    private const string HeroPortraitPrefabPath = "Assets/Prefabs/UI/HeroPortrait.prefab";
    private const string PlayerKnightPrefabPath = "Assets/Prefabs/Player/PlayerKnight.prefab";
    private const string PlayerMagePrefabPath = "Assets/Prefabs/Player/PlayerMage.prefab";
    private const string PlayerRoguePrefabPath = "Assets/Prefabs/Player/PlayerRogue.prefab";

    private const string KnightPortraitPath = "Assets/Extra Assets/Dark UI/New Icons/White Cross.png";
    private const string MagePortraitPath = "Assets/Extra Assets/Dark UI/New Icons/White Witch Hat.png";
    private const string RoguePortraitPath = "Assets/Extra Assets/Dark UI/New Icons/White Mask.png";

    [MenuItem("Tools/Battle HUD/Resize Hero Portrait")]
    public static void Resize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Hero portrait resize: exit Play Mode first - prefab edits made in play do not persist.");
            return;
        }

        if (!ResizeHeroPortrait())
        {
            Debug.LogError($"Hero portrait resize: {HeroPortraitPrefabPath} not found - run Tools > Battle HUD > "
                + "Wire Party Portraits first to create it.");
            return;
        }

        AssignPortrait(PlayerKnightPrefabPath, KnightPortraitPath);
        AssignPortrait(PlayerMagePrefabPath, MagePortraitPath);
        AssignPortrait(PlayerRoguePrefabPath, RoguePortraitPath);

        AssetDatabase.SaveAssets();

        Debug.Log("Hero portrait resize: done - prefabs saved. Re-run Tools > Battle HUD > Wire Party "
            + "Portraits afterward so PartyPortraitPanel's row-slot widths match the new size.");
    }

    // ------------------------------------------------------------------------------------------
    // HeroPortrait.prefab - a fresh vertical budget for the new 144x285 size, not a scale of the old
    // (buggy) one. See the plan this was authored from for the numbers' derivation.
    // ------------------------------------------------------------------------------------------

    private static bool ResizeHeroPortrait()
    {
        HeroPortrait existing = AssetDatabase.LoadAssetAtPath<HeroPortrait>(HeroPortraitPrefabPath);

        if (existing == null) { return false; }

        GameObject root = PrefabUtility.LoadPrefabContents(HeroPortraitPrefabPath);
        RectTransform rootRect = (RectTransform)root.transform;

        rootRect.sizeDelta = new Vector2(144f, 285f);

        SetRect(rootRect, "PortraitImage", new Vector2(0f, 76.5f), new Vector2(120f, 120f));
        SetRect(rootRect, "NameLabel", new Vector2(0f, -1.5f), new Vector2(138f, 30f));
        SetRect(rootRect, "HealthBarBg", new Vector2(0f, -27f), new Vector2(129f, 15f));
        SetRect(rootRect, "HealthText", new Vector2(0f, -49.5f), new Vector2(138f, 24f));
        SetRect(rootRect, "PipParent", new Vector2(0f, -76f), Vector2.zero);
        SetRect(rootRect, "ChipParent", new Vector2(0f, -90.5f), Vector2.zero);

        SetFontSize(rootRect, "NameLabel", 24f);
        SetFontSize(rootRect, "HealthText", 18f);

        HeroPortrait portrait = root.GetComponent<HeroPortrait>();
        SerializedObject so = new(portrait);
        so.FindProperty("pipSize").floatValue = 21f;
        so.FindProperty("pipSpacing").floatValue = 4.5f;
        so.FindProperty("chipSize").floatValue = 42f;
        so.FindProperty("chipSpacing").floatValue = 3f;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, HeroPortraitPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        return true;
    }

    private static void SetRect(Transform root, string childName, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        Transform child = root.Find(childName);

        if (child == null)
        {
            Debug.LogError($"Hero portrait resize: {childName} not found under HeroPortrait.prefab - skipping it.");
            return;
        }

        RectTransform rect = (RectTransform)child;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private static void SetFontSize(Transform root, string childName, float fontSize)
    {
        Transform child = root.Find(childName);

        if (child == null) { return; }

        TMP_Text text = child.GetComponent<TMP_Text>();

        if (text != null) { text.fontSize = fontSize; }
    }

    // ------------------------------------------------------------------------------------------
    // Player class portraits - one Character.portrait assignment each, read generically by every
    // screen that shows one.
    // ------------------------------------------------------------------------------------------

    private static void AssignPortrait(string prefabPath, string spritePath)
    {
        GameObject existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (existingAsset == null)
        {
            Debug.LogError($"Hero portrait resize: {prefabPath} not found - skipping its portrait assignment.");
            return;
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);

        if (sprite == null)
        {
            Debug.LogError($"Hero portrait resize: portrait sprite not found at {spritePath} - skipping.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        Character character = root.GetComponent<Character>();

        if (character == null)
        {
            Debug.LogError($"Hero portrait resize: {prefabPath} has no Character component - skipping.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        SerializedObject so = new(character);
        so.FindProperty("portrait").objectReferenceValue = sprite;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log($"Hero portrait resize: {prefabPath} portrait set to {spritePath}.");
    }
}
