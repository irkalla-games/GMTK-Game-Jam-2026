using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The yellow "you are about to lose this much" segment layered behind a character's overhead health
/// fill. HealthBarFill owns the arithmetic - what fraction of the bar this should cover - and this
/// owns everything visual: colour and the fade. The same split HealthBarFill's own doc comment draws
/// between itself and the two viewers that call it.
///
/// Built at runtime by cloning the authored HealthFill Image and slotting the clone one sibling index
/// behind it, so it draws underneath - the same "wrap what's already there, no prefab edit" trick
/// IntentRoll.Build uses for the intent icon, and for the same reason: this hierarchy exists on no
/// prefab today, and the Editor holds every character prefab and Game.unity in memory, so it cannot be
/// authored into the YAML directly. HealthBarFill's own comment about forcing ShieldFill's fillOrigin
/// at runtime is the existing precedent for that constraint.
///
/// Cloning rather than building an Image from scratch keeps this in sync with whatever the health fill
/// happens to be authored as - same sprite, same fillMethod, same anchors - with no assumptions of its
/// own to drift out of date.
/// </summary>
public class DamagePreviewFill : MonoBehaviour
{
    [SerializeField] private Color previewColor = new(1f, 0.85f, 0.15f, 1f);

    [Tooltip("Colour when the previewed loss would drop the character to zero.")]
    [SerializeField] private Color lethalColor = new(0.9f, 0.1f, 0.1f, 1f);

    [Tooltip("Seconds for one full fade out and back in.")]
    [SerializeField] private float pulsePeriod = 1.2f;

    private Image fill;

    /// <summary>
    /// Wraps `authoredHealthFill` - the Image CharacterOverheadViewer already exposes in the Inspector
    /// - with a clone slotted one sibling index behind it. Call once, from Awake.
    /// </summary>
    public static DamagePreviewFill Build(Image authoredHealthFill)
    {
        Image clone = Instantiate(authoredHealthFill, authoredHealthFill.transform.parent);
        clone.name = "DamagePreviewFill";
        clone.raycastTarget = false;

        // Behind, not on top: inserting at the original's own index shoves the original (and
        // everything after it) one slot later, so the clone ends up drawn first.
        clone.transform.SetSiblingIndex(authoredHealthFill.transform.GetSiblingIndex());

        DamagePreviewFill component = clone.gameObject.AddComponent<DamagePreviewFill>();
        component.fill = clone;
        component.fill.enabled = false;

        return component;
    }

    /// <summary>
    /// Shows the preview at `fillAmount` (already Health/Max - HealthBarFill's job, not this class's),
    /// coloured for a lethal hit or an ordinary one. Alpha is left alone here; Update pulses it every
    /// frame regardless of what this call last set it to.
    /// </summary>
    public void Show(float fillAmount, bool lethal)
    {
        if (fill == null) { return; }

        fill.fillAmount = Mathf.Clamp01(fillAmount);
        fill.color = lethal ? lethalColor : previewColor;
        fill.enabled = true;
    }

    public void Hide()
    {
        if (fill != null) { fill.enabled = false; }
    }

    /// Full 0-1 fade, on the shared Time.time clock rather than a per-instance one, so every character
    /// caught in one AoE preview pulses in phase - the same reasoning AuraPulse.RingAlpha documents for
    /// keeping same-coloured totem auras from drawing a visible seam where their regions meet.
    private void Update()
    {
        if (fill == null || !fill.enabled) { return; }

        float cycle = Mathf.Max(pulsePeriod, 0.01f);

        Color c = fill.color;
        c.a = Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI / cycle));
        fill.color = c;
    }
}
