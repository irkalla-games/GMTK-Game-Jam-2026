using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Makes individual words inside a run of text hoverable.
///
/// Sits on any TMP_Text - world-space like a card's description, or uGUI - and watches for the cursor
/// crossing a &lt;link&gt; tag that Glossary.Tag wrote. On a hit it shows that term's entry at
/// TooltipPriority.Nested, which outranks whatever tooltip the surrounding object is already showing;
/// on the way out it drops only its own request, so the card's keyword box comes straight back.
///
/// Deliberately generic - it knows about links and the glossary, not about cards. An enemy intent
/// readout or a relic description reuses it by tagging its own text and calling BeginPolling.
///
/// Polls rather than using OnMouseEnter, because there is nothing to enter: a link is a range of
/// glyphs inside one renderer, not a collider. It only polls between BeginPolling and EndPolling, so
/// the eight other cards in hand cost nothing.
/// </summary>
public class TooltipLinkText : MonoBehaviour
{
    private TMP_Text text;

    private Glossary glossary;

    /// Where the box sits while a word here is hovered. Supplied by the owner rather than derived from
    /// the word itself - a tooltip that jumped from word to word as you read would be unusable, and the
    /// card it belongs to is the thing the player is looking at.
    private TooltipAnchor anchor;

    private bool polling;

    /// The link currently under the cursor, so the tooltip is only rebuilt when it actually changes
    /// rather than on every frame of a hover.
    private int shownLink = -1;

    /// Resolved once. Graphic.canvas re-walks the parents every call when there is no canvas to find,
    /// which is exactly the case for a world-space card description and would run every frame.
    private Canvas textCanvas;

    private Camera textCamera;

    private void Awake()
    {
        text = GetComponent<TMP_Text>();
        textCanvas = GetComponentInParent<Canvas>();
    }

    /// <summary>
    /// Start watching. Called by whatever owns this text when it becomes hovered.
    /// </summary>
    public void BeginPolling(Glossary newGlossary, TooltipAnchor newAnchor)
    {
        glossary = newGlossary;
        anchor = newAnchor;
        polling = glossary != null && text != null;
    }

    /// Stop watching and drop any word tooltip. Safe to call when nothing was ever shown, so every exit
    /// path can call it unconditionally.
    public void EndPolling()
    {
        polling = false;
        Clear();
    }

    private void OnDisable() => EndPolling();

    private void Update()
    {
        if (!polling) { return; }

        Mouse mouse = Mouse.current;

        if (mouse == null) { return; }

        Vector2 screenPosition = mouse.position.ReadValue();

        int link = TMP_TextUtilities.FindIntersectingLink(text, screenPosition, TextCamera());

        if (link == shownLink) { return; }

        shownLink = link;

        if (link < 0)
        {
            Clear();
            return;
        }

        Show(text.textInfo.linkInfo[link].GetLinkID());
    }

    private void Show(string linkId)
    {
        if (TooltipManager.Instance == null) { return; }

        BattleManager battle = BattleManager.Instance;

        // The active character is who the reader is asking about - it is their hand these cards are in,
        // and their Block the sentence should be quoting.
        Character context = battle != null ? battle.ActiveCharacter : null;

        TooltipManager.Instance.Show(this, glossary.ContentForLink(linkId, context), anchor,
            TooltipPriority.Nested);
    }

    private void Clear()
    {
        shownLink = -1;

        if (TooltipManager.Instance != null) { TooltipManager.Instance.Hide(this); }
    }

    /// <summary>
    /// Which camera FindIntersectingLink should project through.
    ///
    /// Overlay canvases want null and everything else wants a real camera - passing the wrong one puts
    /// the hit test a whole viewport away from the cursor, which reads as "links just do not work".
    /// </summary>
    private Camera TextCamera()
    {
        if (textCanvas == null)
        {
            if (textCamera == null) { textCamera = SceneCameras.For(gameObject); }

            return textCamera;
        }

        return textCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : textCanvas.worldCamera;
    }
}
