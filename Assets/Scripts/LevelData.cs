using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One enemy to place when the battle starts.
///
/// Stats and deck live on the prefab's own Character rather than being repeated here - there is
/// one obvious place to tune a goblin, and it is the goblin. The deck override exists only so the
/// same prefab can turn up twice with different cards without needing a second prefab.
/// </summary>
[System.Serializable]
public struct EnemyPlacement
{
    [Tooltip("Prefab with a Character on it. Health, brain, damage and reach all come from there.")]
    public Character prefab;

    [Tooltip("Grid cell it starts on.")]
    public Vector2Int cell;

    [Tooltip("Leave empty to use the prefab's own deck.")]
    public List<CardData> deckOverride;
}

/// <summary>
/// One level's worth of board setup: which enemies stand where, and where the party is placed.
///
/// Deliberately does not say *who* is in the party - RunState owns the roster because it is chosen
/// once at the start of a run and carries across every level in that run, while a placement is
/// specific to this one level. A level that said "the party is Knight and Mage" would have to be
/// re-authored the moment the roster changes; a level that only says "party member 0 starts at
/// (2,1)" never has to change no matter who runs it.
/// </summary>
[CreateAssetMenu(menuName = "Level Data")]
public class LevelData : ScriptableObject
{
    [Tooltip("Enemies spawned when the battle starts.")]
    [SerializeField] private List<EnemyPlacement> enemies = new();

    [Tooltip("Where the run's party is placed, index for index against RunState's roster. A party "
             + "with more members than this list has spawn cells for is short the extras - they are "
             + "logged and left unplaced rather than guessed at.")]
    [SerializeField] private List<Vector2Int> partySpawnCells = new();

    [Tooltip("Turns the player has to survive. Reaching 0 is the win.")]
    [SerializeField] private int turnsToSurvive = 10;

    [Tooltip("Hand is topped back up to this at the start of each turn - unplayed cards carry over.")]
    [SerializeField] private int handSize = 5;

    public IReadOnlyList<EnemyPlacement> Enemies => enemies;

    public IReadOnlyList<Vector2Int> PartySpawnCells => partySpawnCells;

    public int TurnsToSurvive => turnsToSurvive;

    public int HandSize => handSize;
}
