using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The next-wave preview strip at the top-left of the HUD: one WaveCircle per enemy in the soonest
/// EnemyWave still to come, left to right, so the player can see reinforcements arriving before they
/// do. See LevelData.TryNextWave for how "soonest" is worked out, and WaveCircle for what hovering one
/// shows.
///
/// Pools its circles rather than destroying them between refreshes - the same reasoning
/// PartyPortraitPanel documents for HeroPortrait, and the reason WaveCircle's own Hide has to clear its
/// tooltip and board pulse by hand rather than relying on OnPointerExit.
/// </summary>
public class NextWavePanel : MonoBehaviour
{
    [SerializeField] private WaveCircle circlePrefab;

    [Tooltip("Parent for the circles. Also what gets hidden entirely once no wave is left to preview - "
             + "a wave-less level, or the last wave already spawned.")]
    [SerializeField] private RectTransform row;

    /// <summary>
    /// Exposed for the tutorial spotlight, which lights the circles themselves while explaining what
    /// this strip shows.
    ///
    /// An array of the circles rather than row's own rect: BuildCircle positions each circle by
    /// anchoredPosition, not a Layout Group, so row's rect is whatever size it happened to be authored
    /// at and is never grown to bound its children - anchoring to it lit a small, wrongly placed square
    /// instead of the circles actually on screen. See TooltipAnchor.Of(RectTransform[], ...).
    /// </summary>
    public RectTransform[] ActiveCircleRects()
    {
        List<RectTransform> active = new();

        foreach (WaveCircle circle in circles)
        {
            if (circle != null && circle.gameObject.activeSelf) { active.Add((RectTransform)circle.transform); }
        }

        return active.ToArray();
    }

    [SerializeField] private float spacing = 84f;

    /// Pooled left to right - circles[0] is always the leftmost slot, so refreshing in place never
    /// reorders anything already on screen.
    private readonly List<WaveCircle> circles = new();

    private Canvas canvas;

    private void Start()
    {
        canvas = GetComponentInParent<Canvas>();

        BattleManager battle = BattleManager.Instance;

        if (battle == null || circlePrefab == null || row == null)
        {
            Debug.LogError($"{name}: NextWavePanel is missing BattleManager, circlePrefab or row - "
                + "nothing to preview a wave with");
            return;
        }

        battle.TurnAdvanced += Refresh;

        Refresh();
    }

    private void OnDestroy()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle != null) { battle.TurnAdvanced -= Refresh; }
    }

    /// <summary>
    /// Rebuilds the strip from LevelData.TryNextWave. Called once at Start and again on every
    /// TurnAdvanced - which fires after BattleManager.TurnStart has already called SpawnDueWaves, so
    /// "next" is always genuinely the next wave rather than the one that just landed.
    /// </summary>
    private void Refresh()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null || row == null) { return; }

        LevelData level = battle.CurrentLevel;

        // The out variable's definite assignment has to stay tied to this one condition for the
        // compiler to trust it below - splitting "is there a wave" into its own bool first loses that,
        // since a later unrelated `if` can't see back into how it was computed.
        if (level == null || !level.TryNextWave(battle.TurnsElapsed, out _, out List<EnemyPlacement> enemies))
        {
            row.gameObject.SetActive(false);
            foreach (WaveCircle circle in circles) { circle.Hide(); }
            return;
        }

        row.gameObject.SetActive(true);

        for (int i = 0; i < circles.Count; i++)
        {
            if (i < enemies.Count) { circles[i].Bind(enemies[i], canvas); }
            else { circles[i].Hide(); }
        }

        for (int i = circles.Count; i < enemies.Count; i++)
        {
            circles.Add(BuildCircle(i, enemies[i]));
        }
    }

    private WaveCircle BuildCircle(int index, EnemyPlacement placement)
    {
        WaveCircle circle = Instantiate(circlePrefab, row);
        ((RectTransform)circle.transform).anchoredPosition = new Vector2(index * spacing, 0f);

        circle.Bind(placement, canvas);

        return circle;
    }
}
