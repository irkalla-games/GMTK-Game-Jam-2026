using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lets you work through every player card and assign it art from the packs under `Assets/Card Art`,
/// at click-click-click speed: pick a card on the left, click a sprite on the right, the window jumps
/// to the next unassigned card automatically.
///
/// Scoped to the 101 player cards (Knight/Mage/Rogue/Cleric/Generic) - Enemy and Debug cards are
/// excluded, matching CLAUDE.md's convention of not touching a class of asset the request didn't ask
/// about. The centre preview mirrors the card face's real proportions and palette (see
/// CardFaceV2Builder) and fits art through the same contain-fit maths CardViewer applies at runtime
/// via CardArt.ContainScale (see this file's ContainRect) - what you see here is what the card will
/// actually look like.
///
/// Three bulk operations sit in the toolbar beside the per-card clicking: "Auto-fill unassigned"
/// (seed anything still blank from a pack mapped to the card's class), "Fill summon art" (give every
/// summon card the art of the thing it summons - a totem's own gem), and "Clear art in filter". All
/// three respect the current filter, and all three are undoable.
///
/// The write path is CardSheetImporter.WriteCard's: `image` is a `[field: SerializeField]`
/// auto-property, so it can only be set from an editor script through SerializedObject and the mangled
/// `&lt;image&gt;k__BackingField` name. Sync With Sheet never touches this field (see
/// EquipmentSheetImporter's parallel note about equipment icons), so running a sheet sync after
/// assigning art cannot undo it.
///
/// Requires Tools/Cards/1 - Normalise Card Art Imports to have been run first - see
/// CardArtImportNormaliser for why the packs' native import settings hide half of them from
/// AssetDatabase.LoadAssetAtPath&lt;Sprite&gt;.
///
/// The project's first EditorWindow. IMGUI throughout, matching CardLibraryEditor's house style -
/// there is no UIToolkit anywhere else in this project to be consistent with instead.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public class CardArtAssigner : EditorWindow
{
    private const string CardDataFolder = "Assets/Data/CardData";

    private static readonly string[] ClassFilters = { "All", "Knight", "Mage", "Rogue", "Cleric", "Generic" };

    // The card face's own palette - CardFaceV2Builder's Body/Plate/GlyphInk/Ink/Pip colours - so the
    // mock reads as "this card", not a generic rectangle.
    private static readonly Color BodyColor = new(0.6588f, 0.5451f, 0.3098f);   // #A88B4F
    private static readonly Color PlateColor = new(0.3882f, 0.2784f, 0.0627f);  // #634710
    private static readonly Color BorderColor = new(0.0902f, 0.0706f, 0.0392f); // #17120A
    private static readonly Color InkColor = new(0.9647f, 0.9451f, 0.9020f);    // #F6F1E6
    private static readonly Color PipColor = new(0.2196f, 0.5137f, 0.8627f);    // #3883DC
    private static readonly Color HatchColor = new(0f, 0f, 0f, 0.34f);

    // Card proportions read straight from CardFaceV2.prefab's collider (1.6478488 x 2.5447304), and
    // the layout fractions CardFaceV2Builder.cs authors as consts (Pad, Gap, TopRow, NameRow, ArtRow,
    // PlateInsetX) - kept in sync by hand since the builder's consts are private.
    private const float CardAspect = 1.6478488f / 2.5447304f;
    private const float Pad = 0.030f;
    private const float Gap = 0.021f;
    private const float TopRowFraction = 0.106f;
    private const float NameRowFraction = 0.092f;
    private const float ArtRowFraction = 0.315f;
    private const float PlateInsetXFraction = 0.030f;

    /// How much narrower than the other plates the art window is - kept in sync by hand with
    /// CardArtWindowResize.ArtNarrowing, which applies the same cut to the real prefab.
    private const float ArtNarrowing = 0.85f;

    private struct SpritePack
    {
        public string label;
        public string folder;
    }

    private static readonly SpritePack[] Packs =
    {
        new() { label = "Barbarian", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/Barbarian/Icons" },
        new() { label = "BloodMage", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/BloodMage/Icons" },
        new() { label = "Druid", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/Druid/Icons" },
        new() { label = "EarthMage", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/EarthMage/Icons" },
        new() { label = "Engineer", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/Engineer/Icons" },
        new() { label = "FireMage", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/FireMage/Icons" },
        new() { label = "FrostMage", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/FrostMage/Icons" },
        new() { label = "Hunter", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/Hunter/Icons" },
        new() { label = "Monk", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/Monk/Icons" },
        new() { label = "Necromancer", folder = "Assets/Card Art/CaptainCatSparrow/SpellIconsVolume_2/Necromancer/Icons" },
        new() { label = "Cleric (detailed)", folder = "Assets/Card Art/Free_FantasySkillIcon/SkillIcon/cleric" },
        new() { label = "Assassin (detailed)", folder = "Assets/Card Art/Free_FantasySkillIcon/SkillIcon/assassin" },
        new() { label = "Cleric (simplified)", folder = "Assets/Card Art/Free_FantasySkillIcon/SkillIcon_Simplified/cleric" },
        new() { label = "Assassin (simplified)", folder = "Assets/Card Art/Free_FantasySkillIcon/SkillIcon_Simplified/assassin" },
    };

    /// Which pack(s) "Auto-fill unassigned" draws from for each card class - a card cycles through
    /// every pack listed for its class rather than exhausting the first one and stalling on the rest.
    private static readonly Dictionary<string, string[]> AutoFillPacks = new()
    {
        ["Knight"] = new[] { "Barbarian" },
        ["Mage"] = new[] { "FireMage", "FrostMage", "EarthMage" },
        ["Rogue"] = new[] { "Assassin (detailed)", "Assassin (simplified)" },
        ["Cleric"] = new[] { "Cleric (detailed)", "Cleric (simplified)" },
        ["Generic"] = new[] { "Monk" },
    };

    private class CardEntry
    {
        public string path;
        public CardData data;
        public string className;
    }

    private readonly List<CardEntry> cards = new();
    private readonly Dictionary<string, Sprite[]> packSpriteCache = new();

    private int selectedIndex = -1;
    private string classFilter = "All";
    private bool unassignedOnly;
    private string cardSearch = "";

    private int packIndex;
    private string spriteSearch = "";
    private float thumbSize = 48f;

    private Vector2 leftScroll;
    private Vector2 rightScroll;

    private GUIStyle centeredInkLabel;
    private GUIStyle centeredWhiteLabel;
    private GUIStyle wrappedDescLabel;

    [MenuItem("Tools/Cards/2 - Card Art Assigner")]
    private static void Open()
    {
        CardArtAssigner window = GetWindow<CardArtAssigner>("Card Art Assigner");
        window.minSize = new Vector2(900f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        ReloadCards();
    }

    // Ctrl+S and clicking away from the window both persist without a save call on every single click.
    private void OnLostFocus() { AssetDatabase.SaveAssets(); }
    private void OnDisable() { AssetDatabase.SaveAssets(); }

    private void ReloadCards()
    {
        cards.Clear();
        packSpriteCache.Clear();

        foreach (string guid in AssetDatabase.FindAssets("t:CardData", new[] { CardDataFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // Enemy and Debug cards are out of scope - see the class doc comment.
            if (path.Contains("/Enemy/") || path.Contains("/Debug/")) { continue; }

            CardData data = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (data == null) { continue; }

            // Assets/Data/CardData/<Class>/... - the folder a card sits in is already the class split
            // the sheet tools and CLAUDE.md's own folder layout use, so there is no need to duplicate
            // that decision by reading CharacterClass bits back out of the asset.
            string[] parts = path.Split('/');
            string className = parts.Length > 3 ? parts[3] : "Unknown";

            cards.Add(new CardEntry { path = path, data = data, className = className });
        }

        cards.Sort((a, b) => string.Compare(a.path, b.path, StringComparison.Ordinal));

        if (selectedIndex >= cards.Count) { selectedIndex = cards.Count > 0 ? 0 : -1; }
    }

    private IEnumerable<CardEntry> FilteredCards()
    {
        foreach (CardEntry entry in cards)
        {
            if (classFilter != "All" && entry.className != classFilter) { continue; }
            if (unassignedOnly && entry.data.image != null) { continue; }

            if (!string.IsNullOrEmpty(cardSearch)
                && entry.data.cardName.IndexOf(cardSearch, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            yield return entry;
        }
    }

    private void EnsureStyles()
    {
        if (centeredInkLabel != null) { return; }

        centeredInkLabel = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = InkColor },
        };

        centeredWhiteLabel = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };

        wrappedDescLabel = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
        {
            normal = { textColor = InkColor },
        };
    }

    private void OnGUI()
    {
        EnsureStyles();
        DrawToolbar();

        EditorGUILayout.BeginHorizontal();
        DrawCardList();
        DrawPreview();
        DrawSpriteGrid();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        int assigned = cards.Count(c => c.data.image != null);
        GUILayout.Label($"{assigned} / {cards.Count} assigned", EditorStyles.toolbarButton, GUILayout.Width(120));

        GUILayout.Space(8f);

        foreach (string filter in ClassFilters)
        {
            bool active = classFilter == filter;

            if (GUILayout.Toggle(active, filter, EditorStyles.toolbarButton, GUILayout.Width(60)) && !active)
            {
                classFilter = filter;
            }
        }

        GUILayout.Space(8f);

        unassignedOnly = GUILayout.Toggle(unassignedOnly, "Unassigned only", EditorStyles.toolbarButton, GUILayout.Width(110));
        cardSearch = EditorGUILayout.TextField(cardSearch, EditorStyles.toolbarSearchField, GUILayout.Width(150));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Auto-fill unassigned", EditorStyles.toolbarButton, GUILayout.Width(150)))
        {
            AutoFillUnassigned();
        }

        if (GUILayout.Button("Fill summon art", EditorStyles.toolbarButton, GUILayout.Width(120)))
        {
            if (EditorUtility.DisplayDialog("Fill Summon Art",
                    "Give every summon card in the current filter the art of the thing it summons - "
                    + "a totem's own gem, an ally's own sprite?\n\nThis overwrites art already on those "
                    + "cards. Cards that summon nothing are left alone. Undo with Ctrl+Z.",
                    "Fill", "Cancel"))
            {
                FillSummonArt();
            }
        }

        if (GUILayout.Button("Clear art in filter", EditorStyles.toolbarButton, GUILayout.Width(130)))
        {
            if (EditorUtility.DisplayDialog("Clear Card Art",
                    "Remove art from every card in the current filter? This can be undone with Ctrl+Z.",
                    "Clear", "Cancel"))
            {
                ClearFiltered();
            }
        }

        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            ReloadCards();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawCardList()
    {
        const float rowHeight = 34f;

        EditorGUILayout.BeginVertical(GUILayout.Width(300f));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        foreach (CardEntry entry in FilteredCards())
        {
            int index = cards.IndexOf(entry);
            bool selected = index == selectedIndex;

            Rect rowRect = GUILayoutUtility.GetRect(0f, rowHeight, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                if (selected) { EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.48f, 0.90f, 0.35f)); }

                Rect thumbRect = new(rowRect.x + 2f, rowRect.y + 2f, rowHeight - 4f, rowHeight - 4f);
                DrawSpriteThumb(thumbRect, entry.data.image);

                Rect labelRect = new(thumbRect.xMax + 6f, rowRect.y + 2f,
                    rowRect.width - thumbRect.width - 12f, rowHeight - 4f);

                GUI.Label(new Rect(labelRect.x, labelRect.y, labelRect.width, labelRect.height * 0.55f),
                    entry.data.cardName, EditorStyles.boldLabel);
                GUI.Label(new Rect(labelRect.x, labelRect.y + labelRect.height * 0.55f,
                        labelRect.width, labelRect.height * 0.45f),
                    entry.className, EditorStyles.miniLabel);
            }

            if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
            {
                selectedIndex = index;
                GUI.FocusControl(null);
                Event.current.Use();
                Repaint();
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawPreview()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(300f));
        GUILayout.Label("Preview", EditorStyles.boldLabel);

        if (selectedIndex < 0 || selectedIndex >= cards.Count)
        {
            GUILayout.Label("Select a card on the left.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
            return;
        }

        CardData data = cards[selectedIndex].data;

        float previewHeight = 380f;
        float previewWidth = previewHeight * CardAspect;

        GUILayout.Space(8f);
        Rect cardRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.ExpandWidth(false));

        if (Event.current.type == EventType.Repaint) { DrawMockCard(cardRect, data); }

        GUILayout.Space(6f);

        if (GUILayout.Button("Clear this card's art"))
        {
            Undo.RecordObject(data, "Clear Card Art");
            WriteImage(data, null);
        }

        EditorGUILayout.EndVertical();
    }

    /// A live mock of the real card face - CardFaceV2Builder's own proportions and palette, so what
    /// this window shows is what CardViewer will actually draw, not a generic placeholder rectangle.
    private void DrawMockCard(Rect cardRect, CardData data)
    {
        EditorGUI.DrawRect(cardRect, BorderColor);

        Rect inner = new(cardRect.x + 4f, cardRect.y + 4f, cardRect.width - 8f, cardRect.height - 8f);
        EditorGUI.DrawRect(inner, BodyColor);

        float pad = inner.height * Pad;
        float gap = inner.height * Gap;
        float plateInsetX = inner.width * PlateInsetXFraction;
        float plateW = inner.width - plateInsetX * 2f;

        float y = inner.y + pad;

        float topRowH = inner.height * TopRowFraction;
        Rect pipRect = new(inner.x + plateInsetX, y, topRowH, topRowH);
        EditorGUI.DrawRect(pipRect, PipColor);
        GUI.Label(pipRect, data.cost.ToString(), centeredWhiteLabel);
        y += topRowH + gap;

        float nameH = inner.height * NameRowFraction;
        Rect nameRect = new(inner.x + plateInsetX, y, plateW, nameH);
        EditorGUI.DrawRect(nameRect, PlateColor);
        GUI.Label(nameRect, data.cardName, centeredInkLabel);
        y += nameH + gap;

        float artH = inner.height * ArtRowFraction;
        float artW = plateW * ArtNarrowing;
        float artMarginX = (inner.width - artW) * 0.5f;
        Rect artRect = new(inner.x + artMarginX, y, artW, artH);

        if (data.image != null && data.image.texture != null)
        {
            // Contain-fit, not cropped: matches CardViewer.ApplyArt's Simple-mode scale-to-fit, so the
            // margin this leaves on the window's long axis is exactly what the real card will show -
            // BodyColor already fills `inner` behind it, same as the card body showing through.
            Rect fitted = ContainRect(artRect, data.image);
            GUI.DrawTextureWithTexCoords(fitted, data.image.texture, SpriteUV(data.image));
        }
        else
        {
            EditorGUI.DrawRect(artRect, HatchColor);
        }

        y += artH + gap;

        float descH = Mathf.Max(inner.yMax - pad - y, inner.height * 0.14f);
        Rect descRect = new(inner.x + plateInsetX, y, plateW, descH);
        EditorGUI.DrawRect(descRect, PlateColor);
        GUI.Label(new Rect(descRect.x + 4f, descRect.y + 4f, descRect.width - 8f, descRect.height - 8f),
            data.description, wrappedDescLabel);
    }

    private void DrawSpriteGrid()
    {
        EditorGUILayout.BeginVertical();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        string[] labels = Packs.Select(p => p.label).ToArray();
        packIndex = EditorGUILayout.Popup(packIndex, labels, EditorStyles.toolbarPopup, GUILayout.Width(170));
        spriteSearch = EditorGUILayout.TextField(spriteSearch, EditorStyles.toolbarSearchField, GUILayout.Width(140));
        GUILayout.Label("Size", GUILayout.Width(32));
        thumbSize = GUILayout.HorizontalSlider(thumbSize, 32f, 96f, GUILayout.Width(100));

        EditorGUILayout.EndHorizontal();

        Sprite[] sprites = GetPackSprites(Packs[packIndex].folder);

        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        float availableWidth = Mathf.Max(200f, position.width - 620f);
        int columns = Mathf.Max(1, Mathf.FloorToInt(availableWidth / (thumbSize + 6f)));

        int column = 0;
        EditorGUILayout.BeginHorizontal();

        foreach (Sprite sprite in sprites)
        {
            if (!string.IsNullOrEmpty(spriteSearch)
                && sprite.name.IndexOf(spriteSearch, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            Rect thumbRect = GUILayoutUtility.GetRect(thumbSize, thumbSize,
                GUILayout.Width(thumbSize), GUILayout.Height(thumbSize));

            DrawSpriteThumb(thumbRect, sprite);

            if (Event.current.type == EventType.MouseDown && thumbRect.Contains(Event.current.mousePosition))
            {
                AssignArt(sprite);
                Event.current.Use();
            }

            column++;

            if (column >= columns)
            {
                column = 0;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private Sprite[] GetPackSprites(string folder)
    {
        if (packSpriteCache.TryGetValue(folder, out Sprite[] cached)) { return cached; }

        List<Sprite> sprites = new();

        foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
            if (sprite != null) { sprites.Add(sprite); }
        }

        sprites.Sort((a, b) => NaturalCompare(a.name, b.name));

        Sprite[] array = sprites.ToArray();
        packSpriteCache[folder] = array;
        return array;
    }

    /// "FireMage_2" before "FireMage_10" - an ordinal sort puts every "_1x" name before "_2" and reads
    /// as shuffled once a pack passes nine icons.
    private static int NaturalCompare(string a, string b)
    {
        int ia = 0, ib = 0;

        while (ia < a.Length && ib < b.Length)
        {
            if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
            {
                int startA = ia, startB = ib;
                while (ia < a.Length && char.IsDigit(a[ia])) { ia++; }
                while (ib < b.Length && char.IsDigit(b[ib])) { ib++; }

                int numA = int.Parse(a.Substring(startA, ia - startA));
                int numB = int.Parse(b.Substring(startB, ib - startB));

                if (numA != numB) { return numA.CompareTo(numB); }
            }
            else
            {
                if (a[ia] != b[ib]) { return a[ia].CompareTo(b[ib]); }
                ia++;
                ib++;
            }
        }

        return a.Length - b.Length;
    }

    private static void DrawSpriteThumb(Rect rect, Sprite sprite)
    {
        if (Event.current.type != EventType.Repaint) { return; }

        if (sprite == null || sprite.texture == null)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.15f));
            return;
        }

        GUI.DrawTextureWithTexCoords(rect, sprite.texture, SpriteUV(sprite));
    }

    /// The normalized (0-1) UV rect a sprite occupies on its own texture - every icon here imports as
    /// its own single-sprite texture (no atlas packing), so this is just the pixel rect divided down,
    /// but going through `sprite.rect` rather than assuming (0,0,1,1) keeps this correct if that ever
    /// changes.
    private static Rect SpriteUV(Sprite sprite)
    {
        Rect r = sprite.rect;
        Texture2D texture = sprite.texture;

        return new Rect(r.x / texture.width, r.y / texture.height, r.width / texture.width, r.height / texture.height);
    }

    /// The centred sub-rect of `outer` that shows `sprite` at its own aspect ratio with no cropping -
    /// the Editor-preview equivalent of CardArt.ContainScale, expressed as a rect (for GUI drawing)
    /// rather than a transform scale.
    private static Rect ContainRect(Rect outer, Sprite sprite)
    {
        Vector2 native = sprite.rect.size;

        if (native.x <= 0f || native.y <= 0f) { return outer; }

        float spriteAspect = native.x / native.y;
        float outerAspect = outer.width / outer.height;

        float width, height;

        if (spriteAspect > outerAspect)
        {
            width = outer.width;
            height = width / spriteAspect;
        }
        else
        {
            height = outer.height;
            width = height * spriteAspect;
        }

        float x = outer.x + (outer.width - width) * 0.5f;
        float y = outer.y + (outer.height - height) * 0.5f;

        return new Rect(x, y, width, height);
    }

    /// Assigns `sprite` to the selected card, then jumps to the next card in the *current* filter -
    /// captured before the write, since assigning art can itself remove the just-assigned card from an
    /// "unassigned only" filter and leave nothing to find it by afterwards.
    private void AssignArt(Sprite sprite)
    {
        if (selectedIndex < 0 || selectedIndex >= cards.Count) { return; }

        List<CardEntry> filtered = FilteredCards().ToList();
        string currentPath = cards[selectedIndex].path;
        int currentPos = filtered.FindIndex(e => e.path == currentPath);

        Undo.RecordObject(cards[selectedIndex].data, "Assign Card Art");
        WriteImage(cards[selectedIndex].data, sprite);

        if (currentPos >= 0 && currentPos + 1 < filtered.Count)
        {
            selectedIndex = cards.IndexOf(filtered[currentPos + 1]);
        }

        Repaint();
    }

    /// Walks the current filter and hands every still-unassigned card the next icon from its class's
    /// pack list, wrapping around - duplicates are fine, an artless card is worse than a repeated one.
    /// Never touches a card that already has art, so a hand-picked choice can never be overwritten by
    /// running this again.
    private void AutoFillUnassigned()
    {
        Dictionary<string, Sprite[]> pools = new();
        Dictionary<string, int> nextIndex = new();
        int filled = 0;

        foreach (CardEntry entry in FilteredCards())
        {
            if (entry.data.image != null) { continue; }
            if (!AutoFillPacks.TryGetValue(entry.className, out string[] packLabels)) { continue; }

            if (!pools.TryGetValue(entry.className, out Sprite[] pool))
            {
                List<Sprite> combined = new();

                foreach (string label in packLabels)
                {
                    SpritePack pack = Packs.First(p => p.label == label);
                    combined.AddRange(GetPackSprites(pack.folder));
                }

                pool = combined.ToArray();
                pools[entry.className] = pool;
            }

            if (pool.Length == 0) { continue; }

            int index = nextIndex.TryGetValue(entry.className, out int n) ? n : 0;
            nextIndex[entry.className] = index + 1;

            Undo.RecordObject(entry.data, "Auto-fill Card Art");
            WriteImage(entry.data, pool[index % pool.Length]);
            filled++;
        }

        AssetDatabase.SaveAssets();
        Repaint();
        Debug.Log($"Card art assigner: auto-filled {filled} unassigned card(s) in the current filter.");
    }

    /// <summary>
    /// Gives every summon card in the current filter the art of the thing it actually summons - a
    /// totem card shows its own totem's gem, the skeleton card shows the skeleton.
    ///
    /// Overwrites art already on those cards, unlike AutoFillUnassigned: a totem's own gem is the
    /// right answer for that card, so whatever generic pack icon it was given first is exactly what
    /// this is meant to replace. Cards with no SummonEffect (the wall cards, which resolve through a
    /// Tile effect instead) are left completely alone.
    /// </summary>
    private void FillSummonArt()
    {
        int filled = 0;
        int missingSprite = 0;

        foreach (CardEntry entry in FilteredCards().ToList())
        {
            SummonEffect summon = FindSummon(entry.data);

            if (summon == null) { continue; }

            Sprite sprite = SummonSprite(summon.SummonedObject);

            if (sprite == null)
            {
                Debug.LogWarning($"Card art assigner: {entry.data.cardName} summons "
                                  + $"{summon.SummonedObject.name}, which has no sprite on a \"Sprite\" "
                                  + "child or on its own root - leaving that card's art alone.");
                missingSprite++;
                continue;
            }

            Undo.RecordObject(entry.data, "Fill Summon Art");
            WriteImage(entry.data, sprite);
            filled++;
        }

        AssetDatabase.SaveAssets();
        Repaint();

        string skipped = missingSprite > 0 ? $" {missingSprite} summon card(s) had no usable sprite." : "";
        Debug.Log($"Card art assigner: filled {filled} summon card(s) from what they summon.{skipped}");
    }

    /// The summon this card resolves through, or null if it has none - the same lookup DebugPanel.
    /// FindSummon does, including the legacy `effects` list, since a card migrated from it still plays
    /// from whichever list actually holds its effect.
    private static SummonEffect FindSummon(CardData card)
    {
        foreach (CardEffectEntry entry in card.effectEntries)
        {
            if (entry.effect is SummonEffect summon && summon.SummonedObject != null) { return summon; }
        }

        if (card.effects == null) { return null; }

        foreach (CardEffect effect in card.effects)
        {
            if (effect is SummonEffect summon && summon.SummonedObject != null) { return summon; }
        }

        return null;
    }

    /// <summary>
    /// The sprite that represents a summoned prefab on the board.
    ///
    /// Deliberately only two places, in this order, rather than "the first SpriteRenderer found": a
    /// totem carries its gem on a child called "Sprite" (all 48 of them do), while a Character like
    /// SkeletonAlly carries its own renderer on the root. Every one of these prefabs *also* has
    /// SpriteRenderers for its health bar (BarBack/HealthFill/ShieldFill) and overhead UI, so a
    /// GetComponentInChildren sweep would happily hand back a health bar for some of them.
    /// </summary>
    private static Sprite SummonSprite(GameObject prefab)
    {
        if (prefab == null) { return null; }

        Transform spriteChild = prefab.transform.Find("Sprite");

        if (spriteChild != null)
        {
            SpriteRenderer childRenderer = spriteChild.GetComponent<SpriteRenderer>();

            if (childRenderer != null && childRenderer.sprite != null) { return childRenderer.sprite; }
        }

        SpriteRenderer rootRenderer = prefab.GetComponent<SpriteRenderer>();

        if (rootRenderer != null && rootRenderer.sprite != null) { return rootRenderer.sprite; }

        return null;
    }

    private void ClearFiltered()
    {
        int cleared = 0;

        foreach (CardEntry entry in FilteredCards().ToList())
        {
            if (entry.data.image == null) { continue; }

            Undo.RecordObject(entry.data, "Clear Card Art");
            WriteImage(entry.data, null);
            cleared++;
        }

        AssetDatabase.SaveAssets();
        Repaint();
        Debug.Log($"Card art assigner: cleared art from {cleared} card(s) in the current filter.");
    }

    /// The only supported way to set a [field: SerializeField] auto-property from an editor script -
    /// see CardSheetImporter.WriteCard, the pattern this is lifted from.
    private static void WriteImage(CardData data, Sprite sprite)
    {
        SerializedObject so = new(data);
        so.FindProperty("<image>k__BackingField").objectReferenceValue = sprite;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(data);
    }
}
