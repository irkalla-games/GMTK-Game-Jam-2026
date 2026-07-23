using TMPro;
using UnityEditor;
using UnityEngine;

public class CardViewer : MonoBehaviour
{
    [SerializeField] private TMP_Text cardName;

    [SerializeField] private TMP_Text description;

    [SerializeField] private TMP_Text cost;

    [SerializeField] private SpriteRenderer image;

    [SerializeField] private GameObject wrapper;

    public Card Card { get; private set; }

    public void Setup(Card card)
    {
        this.Card = card;
        cardName.text = card.cardName;
        description.text = card.description;
        cost.text = card.cost.ToString();
        image.sprite = card.image;
    }
}
