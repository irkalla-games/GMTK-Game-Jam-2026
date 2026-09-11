using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Skins the character-select screen with the Sharp GUI art: the backdrop panel, the "Choose Your
/// Party" heading, the party-size row, Start/Back, and the four arrow buttons inside the
/// CharacterSelectSlot prefab.
///
/// Separate from CharacterSelectWiring, which owns the screen's structure and layout, and from
/// MainMenuCanvasWiring, which owns the canvas and the menu column. One command, one job: a restyle
/// should not be able to reparent anything, and re-running the structural wiring should not be the
/// price of changing a colour.
///
/// Reaches into Assets/Prefabs/UI/CharacterSelectSlot.prefab as an asset rather than editing the
/// instances under SlotParent, because those instances are spawned at runtime from the prefab -
/// skinning what is currently in the scene would be undone the first time the panel rebuilds its row.
///
/// Idempotent and re-applies every value on every run, so a changed PanelPalette constant or a swapped
/// SharpSkin sprite is one re-run away from shipping.
/// </summary>
public static class CharacterSelectSkinWiring
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const string SlotPrefabPath = "Assets/Prefabs/UI/CharacterSelectSlot.prefab";

    private const string PanelName = "CharacterSelectPanel";
    private const string BackdropName = "Backdrop";
    private const string TitleName = "Title";

    private static readonly string[] ButtonNames =
    {
        "PartySize2Button", "PartySize3Button", "PartySize4Button", "StartButton", "BackButton",
    };

    private const float TitleFontSize = 44f;
    private const float ButtonLabelSize = 26f;
    private const float SlotLabelSize = 24f;

    /// Must exceed the frame sprite's own 9-slice border (square_button_neutral is 30px on every
    /// side) or the border renders underneath the portrait and only its outer edge shows.
    private const float FramePadding = 32f;

    [MenuItem("Tools/Main Menu/Skin Character Select")]
    public static void Skin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Character select skin: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenMenuScene()) { return; }

        CharacterSelectPanel panel = Object.FindAnyObjectByType<CharacterSelectPanel>(FindObjectsInactive.Include);

        if (panel == null)
        {
            Debug.LogError($"Character select skin: no {PanelName} in {ScenePath} - run "
                           + "Tools/Main Menu/Wire Character Select first.");
            return;
        }

        SkinBackdrop((RectTransform)panel.transform);
        SkinSlotPrefab();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Character select skin: done - scene and slot prefab saved.");
    }

    /// <summary>
    /// The Backdrop becomes a real panel instead of a flat rect, and everything named inside it gets
    /// the button treatment.
    ///
    /// Buttons are found by name under the Backdrop rather than by walking every Button in the panel,
    /// so a control added later is skinned deliberately rather than swept in - the same reasoning
    /// CharacterSelectWiring records for its own named MenuButtonNames list.
    /// </summary>
    private static void SkinBackdrop(RectTransform panel)
    {
        Transform backdropTransform = panel.Find(BackdropName);

        if (backdropTransform == null)
        {
            Debug.LogWarning($"Character select skin: no {BackdropName} under {PanelName} - skipped.");
            return;
        }

        RectTransform backdrop = (RectTransform)backdropTransform;

        Image backdropImage = backdrop.GetComponent<Image>();

        if (backdropImage == null) { backdropImage = backdrop.gameObject.AddComponent<Image>(); }

        SharpSkin.ApplySliced(backdropImage, SharpSkin.Panel);

        Transform titleTransform = backdrop.Find(TitleName);

        if (titleTransform != null)
        {
            TMP_Text title = titleTransform.GetComponentInChildren<TMP_Text>(includeInactive: true);

            if (title != null)
            {
                title.color = PanelPalette.Gold;
                title.fontSize = TitleFontSize;
                title.alignment = TextAlignmentOptions.Center;
                EditorUtility.SetDirty(title);
            }
        }
        else
        {
            Debug.LogWarning($"Character select skin: no {TitleName} under {BackdropName} - skipped heading.");
        }

        foreach (string name in ButtonNames)
        {
            Transform found = backdrop.Find(name);

            if (found == null)
            {
                Debug.LogWarning($"Character select skin: no {name} under {BackdropName} - skipped.");
                continue;
            }

            SkinButton(found.GetComponent<Button>(), name, ButtonLabelSize);
        }
    }

    /// <summary>
    /// Skins the slot prefab's four arrow buttons and its two labels, and frames the portrait.
    ///
    /// Loaded and saved as a prefab asset: the slots under SlotParent are spawned from this at runtime
    /// by CharacterSelectPanel, so styling the scene instances would last exactly until the next
    /// rebuild. Saved with SaveAsPrefabAsset rather than edited through a PrefabUtility edit scope
    /// because every change here is a plain component write with no structural change to record.
    /// </summary>
    private static void SkinSlotPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefabPath);

        if (prefab == null)
        {
            Debug.LogWarning($"Character select skin: {SlotPrefabPath} not found - slot arrows not skinned.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(SlotPrefabPath);

        CharacterSelectSlot slot = root.GetComponent<CharacterSelectSlot>();

        if (slot == null)
        {
            Debug.LogWarning($"Character select skin: {SlotPrefabPath} has no CharacterSelectSlot component.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        SerializedObject so = new(slot);

        SkinSerializedButton(so, "previousCharacterButton");
        SkinSerializedButton(so, "nextCharacterButton");
        SkinSerializedButton(so, "previousDeckButton");
        SkinSerializedButton(so, "nextDeckButton");

        SkinSerializedLabel(so, "nameLabel", PanelPalette.Ink);
        SkinSerializedLabel(so, "deckLabel", PanelPalette.BodyInk);

        // The portrait gets the heavy frame rather than the panel sprite - it is a picture in a border,
        // not a surface things sit on.
        if (so.FindProperty("portraitImage").objectReferenceValue is Image portrait)
        {
            Image backing = EnsurePortraitBacking(portrait);
            SharpSkin.ApplySliced(backing, SharpSkin.Frame);
        }

        PrefabUtility.SaveAsPrefabAsset(root, SlotPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static void SkinSerializedButton(SerializedObject so, string propertyName)
    {
        SerializedProperty property = so.FindProperty(propertyName);

        if (property == null)
        {
            Debug.LogWarning($"Character select skin: CharacterSelectSlot has no {propertyName} field.");
            return;
        }

        SkinButton(property.objectReferenceValue as Button, propertyName, SlotLabelSize);
    }

    private static void SkinSerializedLabel(SerializedObject so, string propertyName, Color colour)
    {
        SerializedProperty property = so.FindProperty(propertyName);

        if (property == null) { return; }

        if (property.objectReferenceValue is TMP_Text label)
        {
            label.color = colour;
            label.alignment = TextAlignmentOptions.Center;
            EditorUtility.SetDirty(label);
        }
    }

    /// <summary>
    /// Puts a frame behind the portrait rather than on it.
    ///
    /// Writing the frame sprite onto portraitImage itself would replace the character art with a
    /// border, so the frame goes on a sibling drawn underneath - built once and reused after that.
    /// Sized by stretching to the portrait's own rect, so the two cannot drift apart.
    /// </summary>
    private static Image EnsurePortraitBacking(Image portrait)
    {
        RectTransform portraitRect = portrait.rectTransform;
        Transform parent = portraitRect.parent;

        if (parent == null) { return portrait; }

        RectTransform backing = SharpSkin.EnsureChild((RectTransform)parent, "PortraitFrame");

        backing.anchorMin = portraitRect.anchorMin;
        backing.anchorMax = portraitRect.anchorMax;
        backing.pivot = portraitRect.pivot;
        backing.anchoredPosition = portraitRect.anchoredPosition;
        // Inflated past the portrait so the frame reads as a border around the art rather
        // than a rectangle hidden entirely behind it.
        backing.sizeDelta = portraitRect.sizeDelta + new Vector2(FramePadding * 2f, FramePadding * 2f);
        backing.localScale = Vector3.one;

        // Behind the portrait: uGUI draws siblings in hierarchy order, so index 0 is the back.
        backing.SetSiblingIndex(0);

        Image image = SharpSkin.Ensure<Image>(backing.gameObject);
        image.raycastTarget = false;

        return image;
    }

    /// <summary>
    /// Shared button treatment. Null-tolerant so a missing reference logs one warning and the rest of
    /// the screen still gets skinned.
    /// </summary>
    private static void SkinButton(Button button, string label, float fontSize)
    {
        if (button == null)
        {
            Debug.LogWarning($"Character select skin: {label} has no Button component - not skinned.");
            return;
        }

        SharpSkin.ApplyButton(button);

        TMP_Text text = button.GetComponentInChildren<TMP_Text>(includeInactive: true);

        if (text == null) { return; }

        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        textRect.localScale = Vector3.one;

        EditorUtility.SetDirty(text);
    }

    private static bool OpenMenuScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);

        return true;
    }
}
