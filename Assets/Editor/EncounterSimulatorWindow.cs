using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Slot in a LevelData, press Simulate, and see what that level rolls: example battles, how often each
/// enemy turns up, the most and least common openings and waves, pacing, and checks for budgets that
/// cannot do what they look like they do. Open it from Tools/Level/Encounter Simulator, from a
/// LevelData's Inspector menu (the ⋮ in its header), or by right-clicking a LevelData in the Project.
///
/// Purely fields and buttons - EncounterSimulation is the engine, and it rolls every battle through
/// EncounterRoller.RollFor, the same call BattleManager makes, so the report is the game's behaviour
/// rather than a model of it.
///
/// Reads everything fresh on each Simulate: edit the level's Encounter Budget or any enemy's Power
/// Level, press Simulate again, and the report reflects it. Nothing here writes to any asset.
/// </summary>
public class EncounterSimulatorWindow : EditorWindow
{
    [SerializeField] private LevelData level;
    [SerializeField] private EnemyRegistry registry;
    [SerializeField] private DifficultyLadder ladder;
    [SerializeField] private int tierIndex;
    [SerializeField] private int battles = 5000;
    [SerializeField] private int examples = 10;
    [SerializeField] private int seed = 12345;

    /// One per report section, so a collapsed section stays collapsed across re-runs of the same level.
    [SerializeField] private List<bool> expanded = new();

    /// Not serialized: rebuilt by pressing Simulate, and a report surviving a domain reload would
    /// describe numbers that may have changed underneath it.
    private EncounterSimulation.Report report;

    private Vector2 scroll;
    private Font monoFont;
    private GUIStyle monoStyle;

    [MenuItem("Tools/Level/Encounter Simulator")]
    public static void Open() => ShowFor(null, simulate: false);

    [MenuItem("CONTEXT/LevelData/Simulate Encounters")]
    private static void OpenFromInspector(MenuCommand command) => ShowFor(command.context as LevelData, simulate: true);

    [MenuItem("Assets/Simulate Encounters", false, 2000)]
    private static void OpenFromProject() => ShowFor(Selection.activeObject as LevelData, simulate: true);

    [MenuItem("Assets/Simulate Encounters", true)]
    private static bool CanOpenFromProject() => Selection.activeObject as LevelData != null;

    private static void ShowFor(LevelData chosen, bool simulate)
    {
        EncounterSimulatorWindow window = GetWindow<EncounterSimulatorWindow>("Encounter Simulator");
        window.minSize = new Vector2(820f, 500f);

        // `!= null` rather than `?.` - a UnityEngine.Object must be null-tested with Unity's own ==.
        if (chosen != null) { window.level = chosen; }

        window.FindDefaults();

        if (simulate && window.level != null) { window.Simulate(); }
    }

    private void OnEnable() => FindDefaults();

    /// Fills the registry and ladder with the project's own assets when nothing is assigned, so the
    /// common case is "slot in a level, press Simulate". Either can still be swapped by hand.
    private void FindDefaults()
    {
        if (registry == null) { registry = FindFirst<EnemyRegistry>(); }
        if (ladder == null) { ladder = FindFirst<DifficultyLadder>(); }
    }

    private static T FindFirst<T>() where T : Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

            if (asset != null) { return asset; }
        }

        return null;
    }

    private void EnsureStyles()
    {
        // `== null`, not `??`: a Font is a UnityEngine.Object and can be destroyed by a domain reload
        // while this reference still looks non-null to C#. See CLAUDE.md.
        if (monoFont == null)
        {
            monoFont = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "Courier New" }, 12);
            monoStyle = null;
        }

        if (monoStyle == null)
        {
            monoStyle = new GUIStyle(EditorStyles.label)
            {
                font = monoFont,
                fontSize = 12,
                wordWrap = false,
                richText = false,
                alignment = TextAnchor.UpperLeft,
            };
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        DrawSettings();

        EditorGUILayout.Space(4f);

        if (report == null)
        {
            EditorGUILayout.HelpBox(level == null
                ? "Slot in a Level, then press Simulate."
                : "Press Simulate to roll this level.", MessageType.Info);
            return;
        }

        DrawReport();
    }

    private void DrawSettings()
    {
        EditorGUILayout.Space(4f);

        level = (LevelData)EditorGUILayout.ObjectField("Level", level, typeof(LevelData), false);
        registry = (EnemyRegistry)EditorGUILayout.ObjectField("Enemy Registry", registry, typeof(EnemyRegistry), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            ladder = (DifficultyLadder)EditorGUILayout.ObjectField("Difficulty Ladder", ladder, typeof(DifficultyLadder), false);

            string[] tiers = TierNames();
            tierIndex = Mathf.Clamp(tierIndex, 0, tiers.Length - 1);
            tierIndex = EditorGUILayout.Popup(tierIndex, tiers, GUILayout.Width(120f));
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            battles = Mathf.Clamp(EditorGUILayout.IntField("Battles", battles), 1, 100000);
            examples = Mathf.Clamp(EditorGUILayout.IntField("Examples", examples, GUILayout.MinWidth(200f)), 0, 100);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            seed = EditorGUILayout.IntField(new GUIContent("Seed",
                "Battle N uses seed + N. Keep it fixed to compare an edit like-for-like; change it to see "
                + "a different sample."), seed);

            if (GUILayout.Button("New seed", GUILayout.Width(80f)))
            {
                seed = Random.Range(0, int.MaxValue / 2);
            }
        }

        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(level == null || registry == null))
            {
                if (GUILayout.Button("Simulate", GUILayout.Height(28f))) { Simulate(); }
            }

            using (new EditorGUI.DisabledScope(report == null))
            {
                if (GUILayout.Button("Copy report", GUILayout.Height(28f), GUILayout.Width(110f)))
                {
                    EditorGUIUtility.systemCopyBuffer = report.ToText();
                    ShowNotification(new GUIContent("Report copied"));
                }
            }

            using (new EditorGUI.DisabledScope(level == null))
            {
                if (GUILayout.Button("Select level", GUILayout.Height(28f), GUILayout.Width(100f)))
                {
                    Selection.activeObject = level;
                    EditorGUIUtility.PingObject(level);
                }
            }
        }

        if (registry == null)
        {
            EditorGUILayout.HelpBox("No EnemyRegistry found - run Tools > Enemies > Rebuild Enemy Registry.",
                MessageType.Warning);
        }
    }

    private string[] TierNames()
    {
        if (ladder == null || ladder.Tiers.Count == 0) { return new[] { "Normal" }; }

        string[] names = new string[ladder.Tiers.Count];

        for (int i = 0; i < names.Length; i++) { names[i] = ladder.NameAt(i); }

        return names;
    }

    private void DrawReport()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        for (int i = 0; i < report.sections.Count; i++)
        {
            EncounterSimulation.Section section = report.sections[i];

            while (expanded.Count <= i) { expanded.Add(true); }

            expanded[i] = EditorGUILayout.Foldout(expanded[i], section.title, true, EditorStyles.foldoutHeader);

            if (!expanded[i]) { continue; }

            // Sized to the text so the scroll view scrolls sideways for wide tables rather than
            // wrapping them into nonsense. SelectableLabel so any part can be copied on its own.
            string body = section.body.TrimEnd();
            Vector2 size = monoStyle.CalcSize(new GUIContent(body));

            EditorGUILayout.SelectableLabel(body, monoStyle,
                GUILayout.Width(Mathf.Max(size.x + 12f, 200f)), GUILayout.Height(size.y + 6f));

            EditorGUILayout.Space(6f);
        }

        EditorGUILayout.EndScrollView();
    }

    private void Simulate()
    {
        if (level == null || registry == null) { return; }

        EncounterSimulation.Settings settings = new()
        {
            level = level,
            registry = registry,
            ladder = ladder,
            tierIndex = tierIndex,
            battles = battles,
            examples = examples,
            seed = seed,
        };

        EncounterSimulation.Report result;

        try
        {
            result = EncounterSimulation.Run(settings, progress =>
                EditorUtility.DisplayCancelableProgressBar("Encounter Simulator",
                    $"Rolling {level.name}: {Mathf.RoundToInt(progress * battles)} / {battles} battles", progress));
        }
        finally
        {
            // In a finally so an exception mid-run cannot leave a progress bar stuck over the Editor.
            EditorUtility.ClearProgressBar();
        }

        if (result == null)
        {
            ShowNotification(new GUIContent("Cancelled - previous report kept"));
            return;
        }

        report = result;
        Repaint();
    }
}
