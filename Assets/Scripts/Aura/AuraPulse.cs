using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws every totem's reach on the board: a wave rippling outward from the totem's own tile, one
/// distance-ring at a time, out to the edge of its aura and then round again.
///
/// Rebuilt from scratch each frame off Totem.Active, in the same pulled spirit as the auras it draws.
/// Nothing registers and nothing unregisters, so a totem that dies, is picked up or walks away simply
/// stops turning up in the walk and its region goes dark on the next frame - there is no cleanup event
/// here to forget to fire, exactly as the Totem class comment argues for the statuses themselves.
///
/// The footprint comes from GridManager.GetTilesInRange fed with the totem's own TargetRange, so the
/// tiles that glow and the tiles the aura actually reaches are answers to a single question.
///
/// Self-creating, from Totem.OnEnable: there is nothing to configure and nothing to place, so an
/// instance somebody forgot to drop into the scene would be a bug with no cause to point at.
/// </summary>
public class AuraPulse : Singleton<AuraPulse>
{
    [Tooltip("Alpha between crests. Holds the region faintly visible rather than letting it blink out.")]
    [SerializeField, Range(0f, 1f)] private float restAlpha = 0.05f;

    [Tooltip("Alpha at the crest of the wave. Kept well under 1 so the aura reads as a wash laid over "
             + "the board rather than as a coloured tile replacing it.")]
    [SerializeField, Range(0f, 1f)] private float peakAlpha = 0.65f;

    [Tooltip("Seconds for one full cycle, after which the wave restarts at the totem.")]
    [SerializeField] private float period = 4f;

    [Tooltip("Seconds each ring crests after the ring inside it. This is what makes it travel.")]
    [SerializeField] private float ringDelay = 0.16f;

    [Tooltip("Seconds a single ring spends lit. Clamped to Period so a ring can always go dark again.")]
    [SerializeField] private float pulseWidth = 0.5f;

    /// Two colours count as the same aura within this much per channel. Well below anything the eye
    /// separates, but enough that two totems authored the same dark blue always group.
    private const float SameColorEpsilon = 0.01f;

    /// One totem's claim on one tile for one frame.
    private readonly struct Contribution
    {
        public readonly Color color;

        public readonly float alpha;

        public Contribution(Color color, float alpha)
        {
            this.color = color;
            this.alpha = alpha;
        }
    }

    private readonly Dictionary<GridTile, TileAuraOverlay> overlays = new();

    /// Kept across frames and cleared rather than rebuilt, so the per-tile lists keep their capacity
    /// and a permanent effect running every frame settles down to no allocation at all.
    private readonly Dictionary<GridTile, List<Contribution>> contributions = new();

    private readonly List<Contribution> groups = new();

    private readonly List<GridTile> stale = new();

    private HashSet<GridTile> litLastFrame = new();

    private HashSet<GridTile> litThisFrame = new();

    public static void Ensure()
    {
        if (Instance != null) { return; }

        new GameObject(nameof(AuraPulse)).AddComponent<AuraPulse>();
    }

    private void Update()
    {
        if (GridManager.Instance == null) { return; }

        Prune();
        Gather();
        Apply();
    }

    /// <summary>
    /// Empties last frame's claims, and forgets any tile that has since been destroyed.
    ///
    /// GridManager.BuildGrid tears the whole board down and builds a new one between levels, so these
    /// dictionaries would otherwise collect a dead generation of keys per level. Nothing announces a
    /// rebuild - noticing one is cheaper than being told, and it keeps GridManager from having to know
    /// a board visual exists. The overlay objects themselves need no cleanup: they are children of the
    /// tiles, so they are destroyed along with them.
    /// </summary>
    private void Prune()
    {
        stale.Clear();

        foreach (KeyValuePair<GridTile, List<Contribution>> entry in contributions)
        {
            if (entry.Key == null) { stale.Add(entry.Key); continue; }

            entry.Value.Clear();
        }

        foreach (GridTile tile in stale)
        {
            contributions.Remove(tile);
            overlays.Remove(tile);
        }
    }

    /// Walks the totems and records what each is claiming on each tile it reaches this frame.
    private void Gather()
    {
        // Indexed rather than foreach: Totem.Active is exposed as IReadOnlyList, and enumerating an
        // interface boxes an enumerator every frame for no reason.
        for (int i = 0; i < Totem.Active.Count; i++)
        {
            Totem totem = Totem.Active[i];

            if (totem == null || !totem.IsProjecting) { continue; }

            GridTile origin = totem.OriginTile;
            TargetRange range = totem.Range;
            Color color = totem.AuraColor;

            foreach (GridTile tile in GridManager.Instance.GetTilesInRange(origin, range))
            {
                int ring = range.Distance(origin, tile);

                if (ring < 0) { continue; }

                ContributionsFor(tile).Add(new Contribution(color, RingAlpha(ring)));
            }
        }
    }

    /// Resolves each covered tile to one colour, then darkens whatever was lit last frame and is not
    /// lit now. That second half is what makes a dead totem's region disappear on its own.
    private void Apply()
    {
        litThisFrame.Clear();

        foreach (KeyValuePair<GridTile, List<Contribution>> entry in contributions)
        {
            if (entry.Value.Count == 0) { continue; }

            TileAuraOverlay overlay = OverlayFor(entry.Key);

            if (overlay == null) { continue; }

            overlay.SetTint(Resolve(entry.Value));
            litThisFrame.Add(entry.Key);
        }

        foreach (GridTile tile in litLastFrame)
        {
            if (litThisFrame.Contains(tile)) { continue; }

            if (overlays.TryGetValue(tile, out TileAuraOverlay overlay) && overlay != null) { overlay.Clear(); }
        }

        (litLastFrame, litThisFrame) = (litThisFrame, litLastFrame);
    }

    /// <summary>
    /// Where a ring sits in the wave right now.
    ///
    /// One shared Time.time clock rather than a per-totem one, so two totems of the same colour stay
    /// in phase. They have to: their overlap takes the stronger of the two alphas, and out-of-phase
    /// crests would draw a visible seam along the line where their regions meet.
    /// </summary>
    private float RingAlpha(int ring)
    {
        float cycle = Mathf.Max(period, 0.01f);
        float width = Mathf.Clamp(pulseWidth, 0.01f, cycle);

        float t = Mathf.Repeat(Time.time - (ring * ringDelay), cycle);
        float x = t / width;

        // A half sine over the lit stretch: smoothly out of the resting alpha, up to the crest and
        // back, then flat until the cycle comes round again.
        float bump = x < 1f ? Mathf.Sin(x * Mathf.PI) : 0f;

        return Mathf.Lerp(restAlpha, peakAlpha, bump);
    }

    /// <summary>
    /// Folds one tile's claims into the colour it should actually be.
    ///
    /// Same-coloured totems take the strongest alpha rather than summing, so two dark blues overlapping
    /// look exactly like one. Distinct colours then blend by weight while the alpha stays a maximum:
    /// an overlap should read as a different hue, never as a darker or more solid patch, which is what
    /// keeps three auras on one tile from going muddy.
    /// </summary>
    private Color Resolve(List<Contribution> list)
    {
        groups.Clear();

        foreach (Contribution claim in list)
        {
            bool merged = false;

            for (int i = 0; i < groups.Count; i++)
            {
                if (!SameColor(groups[i].color, claim.color)) { continue; }

                if (claim.alpha > groups[i].alpha) { groups[i] = claim; }

                merged = true;
                break;
            }

            if (!merged) { groups.Add(claim); }
        }

        Color blended = Color.clear;
        float weight = 0f;
        float alpha = 0f;

        foreach (Contribution group in groups)
        {
            blended += group.color * group.alpha;
            weight += group.alpha;
            alpha = Mathf.Max(alpha, group.alpha);
        }

        if (weight <= 0f) { return Color.clear; }

        blended /= weight;
        blended.a = alpha;

        return blended;
    }

    private static bool SameColor(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < SameColorEpsilon
        && Mathf.Abs(a.g - b.g) < SameColorEpsilon
        && Mathf.Abs(a.b - b.b) < SameColorEpsilon;

    private List<Contribution> ContributionsFor(GridTile tile)
    {
        if (contributions.TryGetValue(tile, out List<Contribution> list)) { return list; }

        list = new List<Contribution>();
        contributions[tile] = list;

        return list;
    }

    private TileAuraOverlay OverlayFor(GridTile tile)
    {
        if (overlays.TryGetValue(tile, out TileAuraOverlay cached))
        {
            // ReferenceEquals, not ==, for the second half: a real null means AttachTo already turned
            // this tile down and must not be retried every frame, while Unity's fake-null means the
            // overlay was destroyed under us and does want rebuilding.
            if (cached != null || ReferenceEquals(cached, null)) { return cached; }
        }

        TileAuraOverlay overlay = TileAuraOverlay.AttachTo(tile);
        overlays[tile] = overlay;

        return overlay;
    }
}
