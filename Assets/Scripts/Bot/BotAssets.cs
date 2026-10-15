#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// Which side of the fight a body is on, from the party's point of view.
public enum BotSide
{
    Hero = 0,
    Ally = 1,
    Enemy = 2,
    Neutral = 3,
}

/// <summary>
/// Name-based lookups and identities for the balance bot.
///
/// Jobs name assets rather than referencing them, so a headless player - which has no AssetDatabase and
/// only the assets its scenes pull in - can still resolve them. MainMenu references the campaign and the
/// roster, and through the roster every hero option and deck, so all of them are loaded by the time the
/// bot looks. The Editor also injects an AssetDatabase fallback (BotRunnerEditor) for anything not yet
/// loaded.
/// </summary>
public static class BotAssets
{
    /// Set by the Editor bridge; null in a player.
    public static Func<Type, string, UnityEngine.Object> EditorLookup;

    public static T Find<T>(string assetName) where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(assetName)) { return null; }

        foreach (T candidate in Resources.FindObjectsOfTypeAll<T>())
        {
            if (candidate != null && candidate.name == assetName) { return candidate; }
        }

        return EditorLookup != null ? EditorLookup(typeof(T), assetName) as T : null;
    }

    /// The deck an option starts with: the one named "...Starter" if there is one, else the first.
    public static DeckData StarterDeckOf(CharacterOption option)
    {
        if (option == null) { return null; }

        DeckData first = null;

        foreach (DeckData deck in option.Decks)
        {
            if (deck == null) { continue; }
            if (first == null) { first = deck; }
            if (deck.name.IndexOf("Starter", StringComparison.OrdinalIgnoreCase) >= 0) { return deck; }
        }

        return first;
    }

    /// BattleManager.Spawn names enemies "<prefab> x,y" and a summon keeps Unity's "(Clone)" - both
    /// stripped, leaving the prefab name the design sheets already use.
    private static readonly Regex SpawnSuffix = new(@"\s+-?\d+,-?\d+$", RegexOptions.Compiled);

    /// <summary>
    /// What kind of body this is, for grouping stats: the prefab name for enemies, summons and totems
    /// ("Goblin", "AegisTotem"); the display name for heroes ("Knight", or "Knight 2" in a party that
    /// has two).
    /// </summary>
    public static string TypeKey(Character character)
    {
        if (character == null) { return "unknown"; }

        if (character.IsPlayerControlled) { return character.DisplayName; }

        string key = character.name.Replace("(Clone)", string.Empty).Trim();
        key = SpawnSuffix.Replace(key, string.Empty);

        return key.Length > 0 ? key : "unknown";
    }

    public static BotSide SideOf(Character character)
    {
        if (character == null) { return BotSide.Neutral; }
        if (character.IsPlayerControlled) { return BotSide.Hero; }
        if (character.IsHostileToParty) { return BotSide.Enemy; }

        return Character.AreAllies(character.Affiliation, PlayableCharacter.AllyPlayable)
            ? BotSide.Ally
            : BotSide.Neutral;
    }

    /// Hero, or a friendly summon or totem.
    public static bool IsOurSide(Character character)
    {
        BotSide side = SideOf(character);
        return side == BotSide.Hero || side == BotSide.Ally;
    }

    public static bool IsLiving(Character character) =>
        character != null && !character.IsDead && character.Tile != null;

    /// <summary>
    /// The run record each living hero came from, in the order BattleManager.SpawnParty placed them -
    /// which is the roster's order, one hero per record whose prefab it was instantiated from. Heroes
    /// keep their prefab's name (SpawnParty sets it), so two Knights pair off with two Knight records
    /// in turn.
    /// </summary>
    public static PartyMember RecordFor(Character hero)
    {
        RunManager run = RunManager.Instance;
        BattleManager battle = BattleManager.Instance;

        if (hero == null || run == null || battle == null) { return null; }

        List<PartyMember> unclaimed = new();

        foreach (PartyMember member in run.Party)
        {
            if (member != null && member.prefab != null) { unclaimed.Add(member); }
        }

        foreach (Character character in battle.Characters)
        {
            if (character == null || !character.IsPlayerControlled) { continue; }

            int match = unclaimed.FindIndex(m => m.prefab.name == character.name);

            if (match < 0) { continue; }

            if (character == hero) { return unclaimed[match]; }

            unclaimed.RemoveAt(match);
        }

        return null;
    }
}
#endif
