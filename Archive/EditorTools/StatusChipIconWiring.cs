using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Centres StatusChip.prefab's Icon in the chip instead of leaving it in the top-left quadrant.
///
/// The Icon child was anchored (0, 0.5)-(0.5, 1) with a zero sizeDelta, which is exactly the chip's
/// top-left quarter: on a 44-unit chip the glyph drew at 20x20 in that corner while the Count badge sat
/// alone in the opposite one. Both callers size only the chip *root* at runtime - SelectedCharacterPanel
/// .Place at chipSize 44, HeroPortrait.Place at 42 - and neither touches the Icon, so the quadrant was
/// the whole story. The chip read as an icon shoved into a corner rather than a centred icon wearing a
/// number.
///
/// The Icon now stretches to the chip's full rect, inset by the frame it sits inside. SharpSkinWiring
/// skins the chip root with SharpSkin.Frame (square_button_neutral, a 30px 9-slice border) at
/// pixelsPerUnitMultiplier 4, so that border draws 30/4 = 7.5 units thick; a 7-unit inset keeps the
/// glyph in the dark centre instead of running under the warm frame. Absolute insets rather than
/// fractional anchors, so the margin reads the same at either chip size.
///
/// Count is deliberately left alone. It is already anchored and pivoted to the chip's bottom-right, and
/// it is second in the root's child order, which is what makes it draw *over* the glyph's corner rather
/// than displace it - the overlap is the intended look, not a collision.
///
/// A separate command from SharpSkinWiring's own StatusChip pass: that one owns the root's frame Image
/// and says outright that it leaves the runtime-assigned Icon alone. This is the Icon's layout, not its
/// art, so it does not belong there.
///
/// Idempotent: every value this owns is re-applied unconditionally on every run, so a re-run after
/// tuning FrameInset actually moves the icon rather than only filling in what was missing.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class StatusChipIconWiring
{
    private const string StatusChipPrefabPath = "Assets/Prefabs/UI/StatusChip.prefab";

    private const string IconName = "Icon";

    /// The frame draws 7.5 units thick (a 30px 9-slice border at pixelsPerUnitMultiplier 4). 7 sits the
    /// glyph just inside it without spending another half unit of an already small icon.
    private const float FrameInset = 7f;

    [MenuItem("Tools/Battle HUD/Center Status Chip Icon")]
    public static void Center()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Status chip icon wiring: exit Play Mode first - prefab edits made in play do not persist.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(StatusChipPrefabPath) == null)
        {
            Debug.LogError($"Status chip icon wiring: {StatusChipPrefabPath} not found - nothing to centre.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(StatusChipPrefabPath);
        Transform icon = root.transform.Find(IconName);

        if (icon == null)
        {
            Debug.LogError($"Status chip icon wiring: no {IconName} child under {StatusChipPrefabPath} - "
                + "skipping. StatusChip.Show assigns its sprite by reference, so the child is expected to "
                + "already exist.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        RectTransform rect = (RectTransform)icon;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(FrameInset, FrameInset);
        rect.offsetMax = new Vector2(-FrameInset, -FrameInset);

        // Square art in a square box, so this changes nothing today - it is what keeps a future
        // non-square status glyph from being stretched to fit rather than fitted inside the chip.
        Image image = icon.GetComponent<Image>();

        if (image != null) { image.preserveAspect = true; }

        PrefabUtility.SaveAsPrefabAsset(root, StatusChipPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        AssetDatabase.SaveAssets();

        Debug.Log($"Status chip icon wiring: done - {IconName} now fills the chip inset by {FrameInset} "
            + "units, with Count still overlapping its bottom-right corner. Prefab saved.");
    }
}
