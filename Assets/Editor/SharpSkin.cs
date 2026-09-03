using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Sharp GUI art, addressed by path, plus the appliers every UI wiring command shares.
///
/// Paths live here rather than in PanelPalette because resolving one needs AssetDatabase, and
/// PanelPalette is runtime code that must never reference UnityEditor - see CLAUDE.md on why a
/// `using UnityEditor;` in a runtime script breaks player builds. The split is the same one this
/// codebase already draws between authoring and runtime everywhere else.
///
/// Only the sprites survived the import - the package's own scripts, prefabs, demo scenes and test
/// libraries were deleted, because every one of its runtime components depends on UniRx, which this
/// project does not have. Nothing here loads any of that; these are plain PNGs.
/// </summary>
public static class SharpSkin
{
    private const string Root = "Assets/SharpUI/Textures/";

    // ---- Chrome -------------------------------------------------------------------------------
    /// Near-black fill, thin copper edge, rounded corners. 13px border - the panel workhorse.
    public const string Panel = Root + "info_box.png";

    /// Sharper corners, 1px copper edge, 5px border. For the tooltip, where Panel's corner radius
    /// reads as a blob at that size.
    public const string PanelTight = Root + "tooltip_box.png";

    public const string Header = Root + "dialog_header_background.png";

    /// Thick warm frame, dark centre, 30px border. For chips and portrait frames, not panels.
    public const string Frame = Root + "square_button_neutral.png";

    public const string FrameSelected = Root + "square_button_selected.png";

    // ---- Controls -----------------------------------------------------------------------------
    public const string Button = Root + "rect_button.png";
    public const string ButtonRound = Root + "round_button.png";
    public const string Row = Root + "list_item_background.png";
    public const string RowSelected = Root + "list_item_background_selected.png";
    public const string Input = Root + "rect_input.png";

    public const string SliderTrack = Root + "scroll_background.png";
    public const string SliderFill = Root + "scroll_fill.png";
    public const string SliderKnob = Root + "scroll_handle.png";

    public const string Checkbox = Root + "checkbox.png";
    public const string CheckMark = Root + "check.png";

    public const string BarFrame = Root + "resourc_bar_frame.png";
    public const string BarFill = Root + "resource_bar_fill.png";

    // ---- Icons --------------------------------------------------------------------------------
    public const string IconClose = Root + "icon_close.png";
    public const string IconSettings = Root + "icon_settings_grayscale.png";
    public const string IconInfo = Root + "icon_info.png";
    public const string Arrow = Root + "page_arrow.png";

    public const string FontPath =
        "Assets/Extra Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    /// <summary>
    /// Loads one of the constants above, logging rather than throwing so a wiring command can report
    /// which asset is missing and carry on styling everything else.
    /// </summary>
    public static Sprite Load(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        if (sprite == null)
        {
            Debug.LogError($"SharpSkin: no sprite at {path} - was Assets/SharpUI/Textures pruned too far?");
        }

        return sprite;
    }

    public static TMP_FontAsset LoadFont()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        if (font == null)
        {
            Debug.LogError($"SharpSkin: font not found at {FontPath} - check the path still matches the "
                           + "project's TMP install.");
        }

        return font;
    }

    /// <summary>
    /// Points an Image at one of the skin's sprites, white-tinted so the art shows as authored.
    ///
    /// Falls back to Simple for a sprite with no 9-slice border: Sliced on a borderless sprite draws
    /// nothing but a centre stretch and warns about it. Several of the package's sprites
    /// (list_item_background, scroll_fill, dialog_header_background) are borderless by design - they
    /// are meant to stretch.
    /// </summary>
    public static void ApplySliced(Image image, string path, float pixelsPerUnitMultiplier = 1f)
    {
        ApplySliced(image, path, Color.white, pixelsPerUnitMultiplier);
    }

    public static void ApplySliced(Image image, string path, Color tint, float pixelsPerUnitMultiplier = 1f)
    {
        if (image == null) { return; }

        Sprite sprite = Load(path);

        if (sprite == null) { return; }

        image.sprite = sprite;
        image.color = tint;
        image.type = sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        image.fillCenter = true;

        EditorUtility.SetDirty(image);
    }

    /// <summary>
    /// Skins a button and hands it a SharpButtonTint wired to its own Image and label.
    ///
    /// Turns the Button's own transition OFF: SharpButtonTint writes background.color on every state
    /// change and Unity's ColorTint writes that same field, so leaving both on makes the resting
    /// colour depend on which ran last. See SharpButtonTint's doc comment.
    /// </summary>
    public static void ApplyButton(UnityEngine.UI.Button button, string path = Button)
    {
        if (button == null) { return; }

        Image background = button.GetComponent<Image>();

        if (background == null) { background = button.gameObject.AddComponent<Image>(); }

        ApplySliced(background, path);

        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(includeInactive: true);

        SharpButtonTint tint = Ensure<SharpButtonTint>(button.gameObject);
        tint.SetGraphics(background, label);
        tint.Apply();

        EditorUtility.SetDirty(button);
        EditorUtility.SetDirty(tint);
    }

    /// <summary>
    /// GetComponent-else-AddComponent, written longhand because `GetComponent&lt;T&gt;() ?? Add...`
    /// silently never adds anything - GetComponent returns a fake-null that is null by == but a real
    /// reference to ??. CLAUDE.md documents this as a project-wide rule.
    ///
    /// Public where TutorialWiring's identical helper is private, so the newer wiring commands share
    /// one copy rather than each writing their own.
    /// </summary>
    public static T Ensure<T>(GameObject go) where T : Component
    {
        T existing = go.GetComponent<T>();

        if (existing == null) { existing = go.AddComponent<T>(); }

        return existing;
    }

    /// <summary>

    /// <summary>

    /// <summary>
    /// Recomputes a freshly-built hierarchy's layout immediately, instead of leaving it to the
    /// end-of-frame rebuild.
    ///
    /// uGUI defers layout to CanvasUpdateRegistry. A wiring command creates and configures dozens of
    /// objects inside a single frame, so that deferred pass runs against half-built intermediate state -
    /// and because RectTransform width and height are SERIALIZED, whatever it computed is what gets
    /// saved into the scene.
    ///
    /// The failure this fixes is nastier than a wrong number: re-running does not correct it. Every
    /// LayoutElement and LayoutGroup value already equals what the code sets, so nothing changes,
    /// nothing calls SetDirty, and no rebuild is ever requested again. The scene keeps a row serialized
    /// at 118.5 tall whose own LayoutElement says 40, forever.
    ///
    /// Called bottom-up: a parent's size depends on its children's, so the innermost group has to settle
    /// before the one above it is measured.
    /// </summary>
    public static void RebuildLayout(RectTransform root)
    {
        if (root == null) { return; }

        RectTransform[] all = root.GetComponentsInChildren<RectTransform>(includeInactive: true);

        // Reverse of hierarchy order, which GetComponentsInChildren returns parent-first - so this
        // walks the deepest rects before the ones containing them.
        for (int i = all.Length - 1; i >= 0; i--)
        {
            if (all[i] == root) { continue; }

            LayoutRebuilder.ForceRebuildLayoutImmediate(all[i]);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
    }

    /// Destroys any child of `parent` whose name is not in `keep`.
    ///
    /// EnsureChild makes a command idempotent for things it still builds, but says nothing about things
    /// it USED to build. When the Grant row changed from a captioned ButtonRow to a caption-less
    /// ButtonBar, re-running left the old "Label" and "Button" sitting beside the new "Button0" - three
    /// children in a horizontal row, which is what squeezed the button into a vertical letter stack.
    ///
    /// Every row builder prunes to exactly the children it creates, so a row's contents are whatever the
    /// current code says and not the union of every version that ever ran.
    /// </summary>
    public static void PruneChildren(RectTransform parent, params string[] keep)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);

            bool wanted = false;

            foreach (string name in keep)
            {
                if (child.name != name) { continue; }

                wanted = true;
                break;
            }

            if (!wanted) { Object.DestroyImmediate(child.gameObject); }
        }
    }

    /// Find-by-name-else-create, the idempotency rule every wiring command here follows so a second
    /// run repairs rather than duplicates.
    /// </summary>
    public static RectTransform EnsureChild(RectTransform parent, string childName)
    {
        Transform existing = parent.Find(childName);

        if (existing != null) { return (RectTransform)existing; }

        GameObject created = new(childName, typeof(RectTransform));
        created.layer = parent.gameObject.layer;
        created.transform.SetParent(parent, worldPositionStays: false);

        return (RectTransform)created.transform;
    }
}
