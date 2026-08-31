using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates and updates CardData assets from the design workbook, via the cards.json work order that
/// Tools/CardSheet/Import-CardSheet.ps1 writes.
///
/// The split is deliberate. PowerShell reads the .xlsx and does the diffing, because Unity's batch mode
/// refuses to run while the Editor holds Temp/UnityLockfile. Unity does every write, because GUIDs,
/// fileID cross-references and the packed-hex encoding of List&lt;enum&gt; are its business - the same
/// argument StarterCharacterAuthoring.cs makes in its header. Hand-writing .asset YAML would mean
/// minting .meta GUIDs by hand and getting the tag blob right, for no gain.
///
/// The write path is lifted from AreaOfEffectExampleContent.WriteCard: SerializedObject plus
/// FindProperty("&lt;name&gt;k__BackingField"), which is the only supported way to set a
/// [field: SerializeField] auto-property from an editor script.
///
/// Never deletes. A card that exists as an asset but has no row in the sheet is reported and left
/// alone - the sheet is not the authority on what should stop existing.
///
/// AllCards.asset needs no step here: CardLibraryEditor's AssetPostprocessor rescans
/// Assets/Data/CardData on every CardData import and rebuilds the library itself.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 - that script drives Assembly-CSharp.csproj,
/// which never lists Assets/Editor. Verify by focusing the Editor and reading the console.
/// </summary>
public static class CardSheetImporter
{
    private const string JsonPath = "Tools/CardSheet/cards.json";
    private const string CardRoot = "Assets/Data/CardData";
    private const string EffectRoot = "Assets/Data/EffectData";

    // ------------------------------------------------------------------------------------------
    // The cards.json shape. Flat and List-based because JsonUtility handles nothing else.
    // ------------------------------------------------------------------------------------------

    [Serializable]
    private class Payload
    {
        public string generatedUtc;
        public string source;
        public List<CardSpec> cards = new();
        public List<EffectSpec> newEffects = new();
        public List<GlossarySpec> glossary = new();
        public List<ConflictSpec> conflicts = new();
        public List<ConflictSpec> unresolved = new();
        public List<string> removedRows = new();
        public List<string> newInUnity = new();
        public List<string> problems = new();
    }

    [Serializable]
    private class ConflictSpec
    {
        public string key;
        public string tab;
        public List<ConflictColumn> columns = new();
    }

    [Serializable]
    private class ConflictColumn
    {
        public string column;
        public string unity;
        public string sheet;
        public string wasLast;
    }

    [Serializable]
    private class GlossarySpec
    {
        public string kind;      // "Status" or "Keyword" - which list on the Glossary asset
        public string type;      // StatusType or CardKeywordType name
        public string title;
        public string body;
        public List<string> terms = new();
        public int defaultStacks;
        public int defaultAmount;
        public string action;
        public List<string> changed = new();
    }

    [Serializable]
    private class CardSpec
    {
        public string key;
        public string assetPath;      // where the asset is now
        public string newAssetPath;   // where a rename/move should put it; empty otherwise
        public string guid;
        public string action;         // create | update | move
        public string cardName;
        public int cost;
        public int requiredClass;
        public int rarity;
        public List<int> tags = new();
        public int rangeShape;
        public int rangeMin;
        public int rangeMax;
        public bool excludeFromRewards;
        public bool rotatableAim;
        public string description;
        public string animation;
        public List<KeywordSpec> keywords = new();
        public List<EntrySpec> effectEntries = new();
        public string sourceTab;
        public List<string> changed = new();
    }

    [Serializable]
    private class KeywordSpec
    {
        public int type;
        public int magnitude;
    }

    [Serializable]
    private class EntrySpec
    {
        public string effectName;
        public int aimsAt;
        public int areaKind;
        public int areaShape;
        public int areaMin;
        public int areaMax;
        public string areaPattern;
    }

    [Serializable]
    private class EffectSpec
    {
        public string name;
        public string kind;
        public int amount;
        public string folder;
        public string status;
    }

    private const string ImportScript = "Tools/CardSheet/Import-CardSheet.ps1";
    private const string ExportScript = "Tools/CardSheet/Export-CardSheet.ps1";
    private const int ScriptTimeoutMs = 180_000;

    // ------------------------------------------------------------------------------------------
    // The button
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The everyday action: reconcile the workbook and the assets in both directions.
    ///
    /// Sheet edits are applied to the assets, then the workbook is rebuilt so anything authored in the
    /// Inspector shows up as a row. Safe to press whatever you changed and wherever - the merge decides
    /// per column which side moved, and refuses to guess when both did.
    ///
    /// Runs as three steps because neither tool can do the other's job: PowerShell owns the .xlsx (no
    /// Excel interop in Unity), Unity owns the asset writes (GUIDs, serialization), and Unity's own
    /// batch mode cannot run while the Editor holds Temp/UnityLockfile.
    ///
    /// Reads Docs/CardDesign.xlsx, and keeps reading it even while Excel has the workbook open - Excel
    /// marks ownership with a ~$ file rather than holding the .xlsx exclusively, so a saved edit is
    /// picked up without closing anything. Only a workbook that genuinely cannot be read falls back to
    /// the Docs/CardDesign/*.csv mirror, and Import-CardSheet.ps1 says so in the console when it does.
    /// The export step at the end is the part that does need Excel closed, since it rewrites the file
    /// wholesale; it fails with a plain-language message rather than half-writing it.
    /// </summary>
    /// Top level and first in the menu on purpose - this is the one you press constantly, and burying
    /// the everyday action a submenu deep costs a click every time. The large negative priority floats
    /// it above everything else in Tools; the gap to the next item also gives it its own separator.
    [MenuItem("Tools/Sync With Sheet", priority = -1000)]
    public static void SyncWithSheet()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Card sheet sync: exit Play Mode first.");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Sync With Sheet", "Reading the workbook...", 0.1f);
            if (!RunPowerShell(ImportScript, string.Empty)) { return; }

            EditorUtility.DisplayProgressBar("Sync With Sheet", "Applying changes to assets...", 0.45f);
            if (!Apply(out int conflicts, out int failures)) { return; }

            if (conflicts > 0 || failures > 0)
            {
                // Deliberately do not refresh the workbook. The affected rows still hold the wording you
                // typed, and rebuilding the sheet from the assets now would overwrite it with the Unity
                // side - silently reverting your edit, which is the one thing this whole design exists to
                // prevent. A row that FAILED to apply needs this exactly as much as one that conflicted:
                // either way the asset does not carry your change, so exporting from it would eat it.
                List<string> reasons = new();
                if (conflicts > 0) { reasons.Add($"{conflicts} row(s) changed on both sides"); }
                if (failures > 0) { reasons.Add($"{failures} row(s) could not be applied - see the errors above"); }

                Debug.LogWarning($"Card sheet sync: stopped after applying everything else - {string.Join(", ", reasons)}. "
                                 + "The sheet was NOT refreshed, so your edits are still there. Fix the cause and sync again.");
                return;
            }

            EditorUtility.DisplayProgressBar("Sync With Sheet", "Refreshing the workbook...", 0.8f);
            RunPowerShell(ExportScript, "-WriteBaseline");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>
    /// Escape hatch: throw away whatever is in the sheet and rebuild it from the assets. Only for when
    /// the sheet is known to be wrong - the normal path is Sync With Sheet, which keeps both sides.
    /// </summary>
    [MenuItem("Tools/Cards/Refresh Sheet From Unity (discards sheet edits)")]
    public static void RefreshSheetFromUnity()
    {
        if (!EditorUtility.DisplayDialog(
                "Refresh Sheet From Unity",
                "This rebuilds Docs/CardDesign.xlsx from the assets and DISCARDS any edit in the sheet "
                + "that has not been synced.\n\nSync With Sheet keeps both sides. Use that unless the "
                + "sheet is known to be wrong.",
                "Discard sheet edits", "Cancel"))
        {
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Refresh Sheet", "Rebuilding the workbook...", 0.5f);
            RunPowerShell(ExportScript, "-Force -WriteBaseline");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>
    /// Runs one of the CardSheet PowerShell scripts and pipes its output into the Unity console.
    ///
    /// The project's only shell-out from an Editor script, kept in one place on purpose. Both streams
    /// are drained asynchronously rather than with two ReadToEnd calls, which deadlock as soon as one
    /// pipe's buffer fills while the other is being waited on.
    /// </summary>
    private static bool RunPowerShell(string scriptRelativePath, string arguments)
    {
        string root = Directory.GetCurrentDirectory();
        string script = Path.Combine(root, scriptRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(script))
        {
            Debug.LogError($"Card sheet sync: {scriptRelativePath} not found.");
            return false;
        }

        System.Text.StringBuilder output = new();
        System.Text.StringBuilder errors = new();

        System.Diagnostics.ProcessStartInfo psi = new()
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" {arguments}",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using System.Diagnostics.Process process = new() { StartInfo = psi };

            process.OutputDataReceived += (_, e) => { if (e.Data != null) { output.AppendLine(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { errors.AppendLine(e.Data); } };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(ScriptTimeoutMs))
            {
                try { process.Kill(); } catch { /* already gone */ }
                Debug.LogError($"Card sheet sync: {scriptRelativePath} did not finish within "
                               + $"{ScriptTimeoutMs / 1000}s and was stopped.");
                return false;
            }

            string log = output.ToString().TrimEnd();
            string err = errors.ToString().TrimEnd();

            if (process.ExitCode != 0)
            {
                // The script's own message is far more useful than the exit code, so lead with it.
                Debug.LogError($"Card sheet sync: {scriptRelativePath} failed.\n"
                               + (string.IsNullOrEmpty(err) ? log : err));
                return false;
            }

            if (!string.IsNullOrEmpty(log)) { Debug.Log($"[{Path.GetFileName(script)}]\n{log}"); }
            if (!string.IsNullOrEmpty(err)) { Debug.LogWarning($"[{Path.GetFileName(script)}]\n{err}"); }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Card sheet sync: could not run {scriptRelativePath} - {e.Message}. "
                           + "PowerShell must be on PATH.");
            return false;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Applying the plan
    // ------------------------------------------------------------------------------------------

    private static bool Apply(out int conflictCount, out int failureCount)
    {
        conflictCount = 0;
        failureCount = 0;

        string full = Path.Combine(Directory.GetCurrentDirectory(), JsonPath);
        if (!File.Exists(full))
        {
            Debug.LogError($"Card sheet sync: {JsonPath} not found.");
            return false;
        }

        Payload payload;
        try
        {
            // PowerShell's Set-Content -Encoding utf8 emits a BOM on Windows PowerShell 5.1. ReadAllText
            // normally strips it, but trimming defensively costs nothing and JsonUtility will not
            // tolerate one if it ever survives.
            string text = File.ReadAllText(full).TrimStart('﻿', '​');
            payload = JsonUtility.FromJson<Payload>(text);
        }
        catch (Exception e)
        {
            Debug.LogError($"Card sheet sync: could not read {JsonPath} - {e.Message}");
            return false;
        }

        if (payload == null)
        {
            Debug.LogError($"Card sheet sync: {JsonPath} was empty or malformed.");
            return false;
        }

        foreach (string problem in payload.problems)
        {
            Debug.LogWarning($"Card sheet: {problem}");
        }

        conflictCount = payload.conflicts.Count + payload.unresolved.Count;
        ReportConflicts(payload);

        if (payload.cards.Count == 0 && payload.newEffects.Count == 0 && payload.glossary.Count == 0)
        {
            Debug.Log("Card sheet sync: no sheet edits to apply.");
            return true;
        }

        try
        {
            // Deliberately not wrapped in StartAssetEditing/StopAssetEditing. Batching would defer the
            // imports, and both CreateFolder and the FindAssets lookups below need the database
            // current - a folder created inside the batch is not yet valid, and a just-created effect
            // is not yet findable. The volume here is a handful of assets, so there is nothing to win.
            Dictionary<string, CardEffect> effects = BuildEffectIndex();

            int madeEffects = 0;
            foreach (EffectSpec spec in payload.newEffects)
            {
                CardEffect created = CreateEffect(spec);
                if (created == null) { continue; }

                effects[spec.name] = created;
                madeEffects++;
            }

            int createdCards = 0;
            int updated = 0;
            int moved = 0;
            int failed = 0;

            foreach (CardSpec card in payload.cards)
            {
                // Move before writing fields, so the SerializedObject is opened on the asset at its
                // final path rather than one Unity is about to relocate underneath it.
                if (card.action == "move" && !MoveCard(card))
                {
                    failed++;
                    continue;
                }

                if (!WriteCard(card, effects))
                {
                    failed++;
                    continue;
                }

                if (card.action == "move") { moved++; }
                else if (card.action == "update") { updated++; }
                else { createdCards++; }
            }

            int glossaryWritten = WriteGlossary(payload.glossary, out int glossaryFailed);
            failed += glossaryFailed;

            // Reported to the caller, not just logged: a row that failed to apply leaves the asset
            // WITHOUT the change, so letting the export pass run would rebuild the sheet from that
            // asset and throw the typed wording away - the same damage a conflict does.
            failureCount = failed;

            Debug.Log($"Card sheet sync: {createdCards} created, {updated} updated, {moved} moved, "
                      + $"{madeEffects} new effect assets, {glossaryWritten} tooltip(s) reworded"
                      + (failed > 0 ? $", {failed} FAILED - see errors above." : "."));
        }
        finally
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (payload.removedRows.Count > 0)
        {
            Debug.LogWarning($"Card sheet: {payload.removedRows.Count} row(s) were deleted from the sheet. The "
                             + "assets were KEPT - delete them in Unity if that was the intent: "
                             + string.Join(", ", payload.removedRows));
        }

        if (payload.newInUnity.Count > 0)
        {
            Debug.Log($"Card sheet: {payload.newInUnity.Count} card(s) authored in Unity will be added to the "
                      + $"sheet - {string.Join(", ", payload.newInUnity)}");
        }

        return true;
    }

    private static void ReportConflicts(Payload payload)
    {
        foreach (ConflictSpec c in payload.conflicts)
        {
            System.Text.StringBuilder sb = new();
            sb.AppendLine($"Card sheet CONFLICT: '{c.key}' changed in BOTH Unity and the sheet since the last "
                          + "sync. Nothing was written to it on either side.");

            foreach (ConflictColumn col in c.columns)
            {
                sb.AppendLine($"    {col.column}:");
                sb.AppendLine($"        was    '{col.wasLast}'");
                sb.AppendLine($"        Unity  '{col.unity}'");
                sb.AppendLine($"        sheet  '{col.sheet}'");
            }

            sb.Append("    Make both sides agree, or change only one of them, then sync again.");
            Debug.LogWarning(sb.ToString());
        }

        foreach (ConflictSpec c in payload.unresolved)
        {
            Debug.LogWarning($"Card sheet: '{c.key}' differs between Unity and the sheet, but there is no "
                             + "baseline recording which side moved, so neither was written. Use "
                             + "Tools > Cards > Refresh Sheet From Unity if the assets are correct.");
        }
    }

    /// <summary>
    /// Renames and/or refiles a card because its Key or Folder changed in the sheet.
    ///
    /// AssetDatabase.MoveAsset rather than a file move: it carries the .meta with the asset, so the GUID
    /// survives and every DeckData still points at the card. A plain file move risks Unity reimporting
    /// the pair separately and minting a fresh GUID, which silently empties the decks referencing it.
    /// </summary>
    private static bool MoveCard(CardSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.newAssetPath) || spec.newAssetPath == spec.assetPath) { return true; }

        if (AssetDatabase.LoadAssetAtPath<CardData>(spec.assetPath) == null)
        {
            Debug.LogError($"Card sheet: cannot move '{spec.key}' - nothing at {spec.assetPath}.");
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<CardData>(spec.newAssetPath) != null)
        {
            Debug.LogError($"Card sheet: cannot move '{spec.key}' to {spec.newAssetPath} - a card is already there.");
            return false;
        }

        EnsureFolder(Path.GetDirectoryName(spec.newAssetPath).Replace('\\', '/'));

        string error = AssetDatabase.MoveAsset(spec.assetPath, spec.newAssetPath);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"Card sheet: could not move {spec.assetPath} -> {spec.newAssetPath}: {error}");
            return false;
        }

        Debug.Log($"Card sheet: moved {spec.assetPath}  ->  {spec.newAssetPath}");

        // Everything downstream writes through the new path.
        spec.assetPath = spec.newAssetPath;
        return true;
    }

    /// <summary>
    /// Rewrites tooltip wording on the Glossary asset from the sheet's Glossary tab.
    ///
    /// Entries are matched on their enum value, never on list position - the sheet is sorted for
    /// reading and its row order carries no meaning. An entry the asset does not have yet is appended.
    ///
    /// Nothing is ever removed: a row cleared in the sheet is skipped upstream rather than deleting the
    /// tooltip, on the same "the importer never deletes" rule the card path follows.
    /// </summary>
    private static int WriteGlossary(List<GlossarySpec> specs, out int failed)
    {
        failed = 0;

        if (specs == null || specs.Count == 0) { return 0; }

        string[] guids = AssetDatabase.FindAssets("t:Glossary");
        if (guids.Length == 0)
        {
            Debug.LogError("Card sheet: no Glossary asset found - tooltip changes skipped.");
            failed = specs.Count;
            return 0;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        Glossary glossary = AssetDatabase.LoadAssetAtPath<Glossary>(path);
        if (glossary == null)
        {
            Debug.LogError($"Card sheet: the Glossary asset at {path} could not be loaded - tooltip changes skipped.");
            failed = specs.Count;
            return 0;
        }

        SerializedObject so = new(glossary);
        SerializedProperty statuses = so.FindProperty("statuses");
        SerializedProperty keywords = so.FindProperty("keywords");

        int written = 0;

        foreach (GlossarySpec spec in specs)
        {
            bool isStatus = spec.kind == "Status";
            SerializedProperty list = isStatus ? statuses : keywords;

            int typeValue;
            if (isStatus)
            {
                if (!Enum.TryParse(spec.type, out StatusType status))
                {
                    Debug.LogError($"Card sheet: '{spec.type}' is not a StatusType - tooltip skipped. If this "
                                   + "reads Unknown(N), the name table in Tools/CardSheet/CardSheet.Common.psm1 "
                                   + "is missing that value - append it to match StatusType.cs.");
                    failed++;
                    continue;
                }
                typeValue = (int)status;
            }
            else
            {
                if (!Enum.TryParse(spec.type, out CardKeywordType keyword))
                {
                    Debug.LogError($"Card sheet: '{spec.type}' is not a CardKeywordType - tooltip skipped. If this "
                                   + "reads Unknown(N), the name table in Tools/CardSheet/CardSheet.Common.psm1 "
                                   + "is missing that value - append it to match CardKeywordType.cs.");
                    failed++;
                    continue;
                }
                typeValue = (int)keyword;
            }

            SerializedProperty entry = null;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty candidate = list.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("type").intValue == typeValue)
                {
                    entry = candidate;
                    break;
                }
            }

            if (entry == null)
            {
                list.arraySize++;
                entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("type").intValue = typeValue;
            }

            entry.FindPropertyRelative("title").stringValue = spec.title;
            entry.FindPropertyRelative("body").stringValue = spec.body;
            entry.FindPropertyRelative("defaultStacks").intValue = spec.defaultStacks;
            entry.FindPropertyRelative("defaultAmount").intValue = spec.defaultAmount;

            SerializedProperty terms = entry.FindPropertyRelative("terms");
            terms.arraySize = spec.terms.Count;
            for (int i = 0; i < spec.terms.Count; i++)
            {
                terms.GetArrayElementAtIndex(i).stringValue = spec.terms[i];
            }

            string what = spec.action == "create" ? "added" : string.Join(", ", spec.changed);
            Debug.Log($"Card sheet: glossary {spec.kind} {spec.type} - {what}");
            written++;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(glossary);

        return written;
    }

    private static Dictionary<string, CardEffect> BuildEffectIndex()
    {
        Dictionary<string, CardEffect> index = new();

        foreach (string guid in AssetDatabase.FindAssets("t:CardEffect", new[] { EffectRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CardEffect effect = AssetDatabase.LoadAssetAtPath<CardEffect>(path);
            if (effect != null) { index[effect.name] = effect; }
        }

        return index;
    }

    /// <summary>
    /// Creates one shared effect asset from a name the sheet used but no asset matched, following the
    /// "&lt;Kind&gt; &lt;Amount&gt;" convention the existing assets already use.
    /// </summary>
    private static CardEffect CreateEffect(EffectSpec spec)
    {
        string folder = $"{EffectRoot}/{spec.folder}";
        EnsureFolder(folder);

        string path = $"{folder}/{spec.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<CardEffect>(path) != null)
        {
            return AssetDatabase.LoadAssetAtPath<CardEffect>(path);
        }

        CardEffect effect;
        SerializedObject so;

        switch (spec.kind)
        {
            case "Damage":
                effect = ScriptableObject.CreateInstance<DamageEffect>();
                AssetDatabase.CreateAsset(effect, path);
                so = new SerializedObject(effect);
                so.FindProperty("damageAmount").intValue = spec.amount;
                break;

            case "Heal":
                effect = ScriptableObject.CreateInstance<HealEffect>();
                AssetDatabase.CreateAsset(effect, path);
                so = new SerializedObject(effect);
                so.FindProperty("healAmount").intValue = spec.amount;
                break;

            case "Shield":
                effect = ScriptableObject.CreateInstance<ShieldEffect>();
                AssetDatabase.CreateAsset(effect, path);
                so = new SerializedObject(effect);
                so.FindProperty("shieldAmount").intValue = spec.amount;
                break;

            case "Block":
                effect = ScriptableObject.CreateInstance<BlockEffect>();
                AssetDatabase.CreateAsset(effect, path);
                so = new SerializedObject(effect);
                so.FindProperty("blockCount").intValue = spec.amount;
                break;

            case "Parry":
                effect = ScriptableObject.CreateInstance<ParryEffect>();
                AssetDatabase.CreateAsset(effect, path);
                so = new SerializedObject(effect);
                so.FindProperty("parryCharges").intValue = spec.amount;
                break;

            case "Draw":
                effect = ScriptableObject.CreateInstance<DrawEffect>();
                AssetDatabase.CreateAsset(effect, path);
                so = new SerializedObject(effect);
                so.FindProperty("drawAmount").intValue = spec.amount;
                break;

            case "Status":
                if (!Enum.TryParse(spec.status, out StatusType statusType))
                {
                    Debug.LogError($"Card sheet: '{spec.name}' names status '{spec.status}', which is not a StatusType.");
                    return null;
                }

                effect = ScriptableObject.CreateInstance<ApplyStatusEffect>();
                AssetDatabase.CreateAsset(effect, path);
                so = new SerializedObject(effect);
                so.FindProperty("status").intValue = (int)statusType;
                so.FindProperty("stacks").intValue = spec.amount;
                // Hostile statuses land on enemies; the friendly ones are self-buffs. Matches how the
                // hand-authored Status assets are set up today.
                bool hostile = statusType is StatusType.Poison or StatusType.Frozen or StatusType.Rooted
                               or StatusType.Weaken or StatusType.Taunt or StatusType.Vulnerable;
                SerializedProperty alliesOnly = so.FindProperty("alliesOnly");
                if (alliesOnly != null) { alliesOnly.boolValue = !hostile; }
                break;

            default:
                Debug.LogError($"Card sheet: do not know how to create effect '{spec.name}' of kind '{spec.kind}'.");
                return null;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(effect);
        Debug.Log($"Card sheet: created effect {path}");

        return effect;
    }

    private static bool WriteCard(CardSpec spec, Dictionary<string, CardEffect> effects)
    {
        // Resolve every effect before creating anything, so a typo cannot leave a half-built card.
        List<CardEffect> resolved = new();
        foreach (EntrySpec entry in spec.effectEntries)
        {
            if (!effects.TryGetValue(entry.effectName, out CardEffect effect) || effect == null)
            {
                Debug.LogError($"Card sheet: '{spec.key}' references effect '{entry.effectName}', which does not exist.");
                return false;
            }
            resolved.Add(effect);
        }

        string path = spec.assetPath;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

        CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);
        if (card == null)
        {
            card = ScriptableObject.CreateInstance<CardData>();
            AssetDatabase.CreateAsset(card, path);
        }

        SerializedObject so = new(card);

        so.FindProperty("<cardName>k__BackingField").stringValue = spec.cardName;
        so.FindProperty("<cost>k__BackingField").intValue = spec.cost;
        so.FindProperty("<requiredClass>k__BackingField").intValue = spec.requiredClass;
        so.FindProperty("<rarity>k__BackingField").intValue = spec.rarity;
        so.FindProperty("<excludeFromRewards>k__BackingField").boolValue = spec.excludeFromRewards;
        so.FindProperty("<rotatableAim>k__BackingField").boolValue = spec.rotatableAim;
        so.FindProperty("<description>k__BackingField").stringValue = spec.description;

        SerializedProperty range = so.FindProperty("<range>k__BackingField");
        range.FindPropertyRelative("shape").intValue = spec.rangeShape;
        range.FindPropertyRelative("minDistance").intValue = spec.rangeMin;
        range.FindPropertyRelative("maxDistance").intValue = spec.rangeMax;

        SerializedProperty tags = so.FindProperty("<tags>k__BackingField");
        tags.arraySize = spec.tags.Count;
        for (int i = 0; i < spec.tags.Count; i++)
        {
            tags.GetArrayElementAtIndex(i).intValue = spec.tags[i];
        }

        SerializedProperty keywords = so.FindProperty("<keywords>k__BackingField");
        keywords.arraySize = spec.keywords.Count;
        for (int i = 0; i < spec.keywords.Count; i++)
        {
            SerializedProperty k = keywords.GetArrayElementAtIndex(i);
            k.FindPropertyRelative("type").intValue = spec.keywords[i].type;
            k.FindPropertyRelative("magnitude").intValue = spec.keywords[i].magnitude;
        }

        SerializedProperty animation = so.FindProperty("<animation>k__BackingField");
        animation.objectReferenceValue = string.IsNullOrWhiteSpace(spec.animation)
            ? null
            : FindByName<CardAnimation>(spec.animation);

        SerializedProperty entries = so.FindProperty("<effectEntries>k__BackingField");
        entries.arraySize = spec.effectEntries.Count;
        for (int i = 0; i < spec.effectEntries.Count; i++)
        {
            EntrySpec source = spec.effectEntries[i];
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);

            entry.FindPropertyRelative("effect").objectReferenceValue = resolved[i];
            entry.FindPropertyRelative("aimsAt").intValue = source.aimsAt;

            SerializedProperty area = entry.FindPropertyRelative("area");
            area.FindPropertyRelative("kind").intValue = source.areaKind;

            SerializedProperty radius = area.FindPropertyRelative("radius");
            radius.FindPropertyRelative("shape").intValue = source.areaShape;
            radius.FindPropertyRelative("minDistance").intValue = source.areaMin;
            radius.FindPropertyRelative("maxDistance").intValue = source.areaMax;

            area.FindPropertyRelative("pattern").objectReferenceValue =
                string.IsNullOrWhiteSpace(source.areaPattern) ? null : FindByName<EffectPattern>(source.areaPattern);
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);

        Debug.Log($"Card sheet: {(spec.action == "update" ? "updated" : "created")} {path}"
                  + (spec.changed is { Count: > 0 } ? $" ({string.Join("; ", spec.changed)})" : ""));

        return true;
    }

    private static T FindByName<T>(string name) where T : UnityEngine.Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null && asset.name == name) { return asset; }
        }

        Debug.LogWarning($"Card sheet: no {typeof(T).Name} named '{name}' - left empty.");
        return null;
    }

    private static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) { return; }

        string parent = path[..path.LastIndexOf('/')];
        string leaf = path[(path.LastIndexOf('/') + 1)..];

        if (!AssetDatabase.IsValidFolder(parent)) { EnsureFolder(parent); }

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
