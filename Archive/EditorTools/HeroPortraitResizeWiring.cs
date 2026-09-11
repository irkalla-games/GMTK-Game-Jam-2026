using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Resizes HeroPortrait.prefab to its current spec - rebudgeting the whole layout from scratch rather
/// than blindly scaling it, same reasoning as the original ~1.5x pass this superseded. The energy pips
/// stand in a vertical column beside the health bar instead of a horizontal row below it, with the health
/// text next to the bar at the foot of that column and pips stacking upward from there - so the status
/// chip row is the only thing left below the bar. Fitting the side column cost the panel 24 units of
/// width (144 -> 168); PortraitImage, NameLabel, HealthBarBg and ChipParent all shift left by half that
/// (-12) to keep the bar/name/portrait column centred against the new panel width, and
/// HeroPortrait.PlacePip stacks pips upward from pipParent instead of rightward to match. Also assigns
/// each player class prefab's Character.portrait: White Cross for the
/// Knight, White Witch Hat for the Mage, White Mask for the Rogue. Character.Portrait is read generically
/// by HeroPortrait, PartySheetColumn and CharacterOption, so setting it once here is what the Tab panel
/// and character-select screen also pick up - no separate art needed for either.
///
/// Edits existing prefabs via PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset rather than deleting
/// and recreating them, which is what keeps their file GUIDs - and every scene/prefab reference to them
/// - intact. Same technique PartySheetStyling.cs already established for this project. A separate
/// command from Tools/Battle HUD/Wire Party Portraits, not a change to it:
/// PartyPortraitWiring.EnsureHeroPortraitPrefab early-returns once the prefab exists, which is the right
/// answer to "does this exist at all" and the wrong one to "does this match the current spec."
///
/// Idempotent: every value is re-applied unconditionally on every run. The panel's width change means
/// PartyPortraitPanel's restWidth/activeWidth (wired by PartyPortraitWiring) need to grow to match, or
/// rest-scaled portraits in the row will overlap - see PartyPortraitWiring.RestWidth/ActiveWidth's own
/// comment. Re-run Tools > Battle HUD > Wire Party Portraits after this.
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
    // HeroPortrait.prefab - a fresh layout budget for the current 168x285 size, not a scale of the
    // previous 144-wide one. The extra 24 units of width holds the pip column beside the bar; every
    // other core element (portrait/name/bar/chips) shifts left by half that (-12) to stay centred.
    // ------------------------------------------------------------------------------------------

    private static bool ResizeHeroPortrait()
    {
        HeroPortrait existing = AssetDatabase.LoadAssetAtPath<HeroPortrait>(HeroPortraitPrefabPath);

        if (existing == null) { return false; }

        GameObject root = PrefabUtility.LoadPrefabContents(HeroPortraitPrefabPath);
        RectTransform rootRect = (RectTransform)root.transform;

        rootRect.sizeDelta = new Vector2(168f, 285f);

        SetRect(rootRect, "PortraitImage", new Vector2(-12f, 76.5f), new Vector2(120f, 120f));
        SetRect(rootRect, "NameLabel", new Vector2(-12f, -1.5f), new Vector2(138f, 30f));
        SetRect(rootRect, "HealthBarBg", new Vector2(-12f, -27f), new Vector2(129f, 15f));

        // Pulled up tight against the bar's bottom edge (-34.5) - now that the pip/text column moved up
        // beside the bar instead of hanging below it, nothing else needs the space in between. Anchored
        // near the panel's own left edge (-84, so -80 leaves a small 4-unit margin) rather than under the
        // bar's centre - HeroPortrait.Place lays chips out left-to-right from this point in a single row
        // that never wraps (the chip-row counterpart to PlacePip above), so starting as far left as the
        // panel allows is what gives that row the most width to grow into before a chip has to spill past
        // the panel's right edge.
        SetRect(rootRect, "ChipParent", new Vector2(-80f, -44f), Vector2.zero);

        // Beside the bar rather than below it. HealthText sits level with HealthBarBg (bar centre y -27)
        // just past its right edge (52.5) - "right next to" the bar rather than tucked under it. PipParent
        // anchors the column's bottom just above the text, and pips stack upward from there - four pips
        // now span roughly y -15..51 at x 62..78 (pipSpacing 4 rather than 2, so an available pip at
        // availablePipScale 1.25 - a 13-unit pip renders 16.25 tall - clears its neighbour instead of
        // touching it), which is what makes the whole side column read noticeably higher than the bar it
        // sits beside instead of hanging below it, with room for roughly 10 pips before the column reaches
        // the panel top. Leaves the chip row as the only thing below the bar.
        SetRect(rootRect, "HealthText", new Vector2(70f, -27f), new Vector2(36f, 16f));
        SetRect(rootRect, "PipParent", new Vector2(70f, -15f), Vector2.zero);

        SetFontSize(rootRect, "NameLabel", 24f);
        SetFontSize(rootRect, "HealthText", 11f);

        HeroPortrait portrait = root.GetComponent<HeroPortrait>();
        SerializedObject so = new(portrait);
        so.FindProperty("pipSize").floatValue = 13f;
        so.FindProperty("pipSpacing").floatValue = 4f;
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
