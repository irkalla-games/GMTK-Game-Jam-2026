using System.Collections;
using TMPro;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// A single floating label that punches in, rises, fades and destroys itself - Projectile's structural
/// twin. Where Projectile takes a sprite and two points and knows nothing about cards, this takes a
/// string, a colour and a target size and knows nothing about damage, healing or Character at all;
/// FloatingTextManager is the thing that turns a game event into those three things.
///
/// Lives on a prefab (rather than being built in code like Projectile) because there is real art
/// direction here - font, outline, punch/fade feel - worth authoring in the Inspector instead of C#.
/// </summary>
public class FloatingText : MonoBehaviour
{
    /// Above Projectile's 100 (which sits above every character's SortingGroup) and still under Cards,
    /// so a hovered card still covers a popup rather than the other way round. Set here rather than
    /// left to prefab authoring so a restyle of the prefab cannot silently drop a label behind a body.
    private const int SortingOrder = 200;

    [SerializeField] private TMP_Text text;

    [Tooltip("World-space distance this rises over its lifetime.")]
    [SerializeField] private float riseDistance = 1f;

    [Tooltip("Horizontal scatter so two labels landing close together stay legible.")]
    [SerializeField] private float jitterX = 0.3f;

    [SerializeField] private float duration = 0.8f;

    [Tooltip("Share of duration spent at full alpha before the fade begins.")]
    [SerializeField] private float holdFraction = 0.4f;

    [Tooltip("Starting scale of the punch-in, as a multiple of the settled size.")]
    [SerializeField] private float punchScale = 1.4f;

    [SerializeField] private float punchDuration = 0.12f;

    /// <summary>
    /// Sets the text, colour, position and world-space cap height, then animates and destroys this
    /// popup once its lifetime elapses.
    ///
    /// `position` is a world position, not a local one - callers spawn this with no parent, so it is
    /// unaffected by whatever it is reporting on being destroyed mid-animation (a killing blow's body,
    /// for instance). `height` is the target cap height in world units; this class does not know or
    /// care what decided it.
    /// </summary>
    public void Play(string body, Color color, Vector3 position, float height)
    {
        text.SetText(body);
        text.color = color;

        float jitter = Random.Range(-1f, 1f) * jitterX;
        transform.position = position + new Vector3(jitter, 0f, 0f);

        Renderer meshRenderer = GetComponent<Renderer>();

        if (meshRenderer != null)
        {
            meshRenderer.sortingLayerName = SortingLayers.Characters;
            meshRenderer.sortingOrder = SortingOrder;
        }

        StartCoroutine(Animate(height));
    }

    private IEnumerator Animate(float height)
    {
        // Measured off the mesh rather than trusting the prefab's authored font size, so a future
        // restyle of the prefab cannot silently break the size a caller asked for. textBounds is the
        // glyph cap height at the text's current localScale (1 at spawn), identical for "5" and "12"
        // and for "Blocked!" - the scale this produces is whatever it takes to hit `height`.
        text.ForceMeshUpdate();
        float natural = text.textBounds.size.y;
        float scale = natural > 0f ? height / natural : 1f;

        transform.localScale = Vector3.one * scale * punchScale;
        transform.DOScale(scale, punchDuration);

        transform.DOMoveY(transform.position.y + riseDistance, duration).SetEase(Ease.OutCubic);

        // DOTween.To rather than the .DOFade/.DOColor shortcut - that extension only exists when
        // DOTween's optional module has been enabled via its setup utility, which this project has
        // not done. Same note as CharacterAnimator.PlayDeath.
        Color transparent = text.color;
        transparent.a = 0f;
        DOTween.To(() => text.color, c => text.color = c, transparent, duration * (1f - holdFraction))
               .SetDelay(duration * holdFraction);

        yield return new WaitForSeconds(duration);

        Destroy(gameObject);
    }
}
