using TMPro;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

public class CardViewer : MonoBehaviour
{
    [SerializeField] private TMP_Text cardName;

    [SerializeField] private TMP_Text description;

    [SerializeField] private TMP_Text cost;

    [SerializeField] private SpriteRenderer image;

    [SerializeField] private GameObject wrapper;

    public Card card { get; private set; }

    public void Setup(Card newCard)
    {
        this.card = newCard;
        cardName.text = card.cardName;
        description.text = card.description;
        cost.text = card.cost.ToString();
        image.sprite = card.image;
    }

    public void OnMouseEnter()
    {
        wrapper.SetActive(false);
        Vector3 pos = new Vector3(transform.position.x, 3, 0);
        CardHoverManager.Instance.ShowLargeCard(card, pos);
    }

    public void OnMouseExit()
    {
        CardHoverManager.Instance.HideLargeCard();
        wrapper.SetActive(true);
    }
}
