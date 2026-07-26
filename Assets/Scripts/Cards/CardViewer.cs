using TMPro;
using UnityEngine;

public class CardViewer : MonoBehaviour
{
    [SerializeField] private TMP_Text cardName;

    [SerializeField] private TMP_Text description;

    [SerializeField] private TMP_Text cost;

    [SerializeField] private SpriteRenderer image;

    [SerializeField] private GameObject wrapper;

    public Card card { get; private set; }

    public bool isSelected { get; private set; }

    /// True once the card has been played and is tweening away - hover must stop touching it.
    private bool isPlaying;

    public void Setup(Card newCard)
    {
        this.card = newCard;
        cardName.text = card.cardName;
        description.text = card.description;
        cost.text = card.cost.ToString();
        image.sprite = card.image;
    }

    public void SetSelected(bool value)
    {
        isSelected = value;
        // The hover preview swaps this off; restore it here so a card can't get stuck invisible when
        // selection suppresses the OnMouseExit that would normally put it back.
        wrapper.SetActive(true);
    }

    public void BeginPlay()
    {
        isPlaying = true;
        isSelected = false;
    }

    private bool HoverSuppressed =>
        isPlaying || isSelected || (CardPlayManager.Instance != null && CardPlayManager.Instance.HasSelection);

    public void OnMouseEnter()
    {
        if (HoverSuppressed) { return; }
        wrapper.SetActive(false);
        Vector3 pos = new Vector3(transform.position.x, 3, 0);
        ActiveHandViewer.Instance.ShowLargeCard(card, pos);
    }

    public void OnMouseExit()
    {
        if (isPlaying) { return; }
        ActiveHandViewer.Instance.HideLargeCard();
        wrapper.SetActive(true);
    }

    public void OnMouseDown()
    {
        if (CardPlayManager.Instance != null)
        {
            CardPlayManager.Instance.OnCardClicked(this);
        }
    }
}
