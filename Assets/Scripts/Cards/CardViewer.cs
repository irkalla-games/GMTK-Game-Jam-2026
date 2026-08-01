using TMPro;
using UnityEngine;
using DG.Tweening;

public class CardViewer : MonoBehaviour
{
    [SerializeField] private TMP_Text cardName;

    [SerializeField] private TMP_Text description;

    [SerializeField] private TMP_Text cost;

    [SerializeField] private SpriteRenderer image;

    [SerializeField] private SpriteRenderer rangeIndicator;

    [SerializeField] private Sprite bowIcon;

    [SerializeField] private Sprite swordIcon;

    [SerializeField] private float hoverScale = 1.5f;

    [SerializeField] private float hoverDuration = 0.1f;



    public Card card { get; private set; }

    public bool isSelected { get; private set; }

    public bool isHovered { get; private set; }

    /// True once the card has been played and is tweening away - hover must stop touching it.
    private bool isPlaying;

    // Each ever holds at most one live tween, killed by direct reference (never DOTween's id/target
    // search) before being replaced - so hover, selection, layout, spawn and discard can never stack
    // competing tweens on the same transform.
    private Tweener scaleTween;
    private Tweener positionTween;
    private Tweener rotationTween;

    public void Setup(Card newCard)
    {
        this.card = newCard;
        cardName.text = card.cardName;
        description.text = card.description;
        cost.text = card.cost.ToString();
        image.sprite = card.image;
        if (card.range.MaxDistance > 1)
        {
            rangeIndicator.sprite = bowIcon;
        } else if (card.range.MaxDistance == 1){
            rangeIndicator.sprite = swordIcon;
        } else
        {
            rangeIndicator.sprite = null;
        }
    }

    /// Grows the card from nothing - called once, when it is first dealt into a hand.
    public void PlaySpawnIn(float duration)
    {
        transform.localScale = Vector3.zero;
        scaleTween = transform.DOScale(1f, duration);
    }

    /// Where the hand layout wants this card right now. Called on every relayout, whether or not this
    /// particular card's target actually changed.
    public void SetLayoutTarget(Vector3 position, Quaternion rotation, float duration)
    {
        positionTween?.Kill();
        rotationTween?.Kill();
        positionTween = transform.DOMove(position, duration);
        rotationTween = transform.DORotate(rotation.eulerAngles, duration);
    }

    /// Flies the card to the discard anchor and shrinks it away.
    public void PlayDiscard(Vector3 target, float duration)
    {
        scaleTween?.Kill();
        positionTween?.Kill();
        rotationTween?.Kill();
        scaleTween = transform.DOScale(Vector3.zero, duration);
        positionTween = transform.DOMove(target, duration);
    }

    public void SetSelected(bool value)
    {
        isSelected = value;

        if (value)
        {
            // A card can be clicked without the mouse ever leaving it, which would never fire
            // OnMouseExit - so selection has to clear the hover pop itself.
            isHovered = false;
            scaleTween?.Kill();
            scaleTween = transform.DOScale(1f, hoverDuration);
        }
    }

    public void BeginPlay()
    {
        isPlaying = true;
        isSelected = false;
        isHovered = false;
        scaleTween?.Kill();
        positionTween?.Kill();
        rotationTween?.Kill();
    }

    private bool HoverSuppressed =>
        isPlaying || isSelected || (CardPlayManager.Instance != null && CardPlayManager.Instance.HasSelection);

    public void OnMouseEnter()
    {
        if (HoverSuppressed) { return; }
        isHovered = true;
        scaleTween?.Kill();
        scaleTween = transform.DOScale(hoverScale, hoverDuration);
        StartCoroutine(ActiveHandViewer.Instance.Relayout());
    }

    public void OnMouseExit()
    {
        if (isPlaying) { return; }
        isHovered = false;
        scaleTween?.Kill();
        scaleTween = transform.DOScale(1f, hoverDuration);
        StartCoroutine(ActiveHandViewer.Instance.Relayout());
    }

    public void OnMouseDown()
    {
        if (CardPlayManager.Instance != null)
        {
            CardPlayManager.Instance.OnCardClicked(this);
        }
    }
}
