using System;
using Unity.VisualScripting;
using UnityEngine;

public class CreateCardViewer : MonoBehaviour
{
    [SerializeField] private CardViewer cardPrefab;
    public CardViewer CreateCard(Card card, Vector3 positon, Quaternion rotation)
    {
        CardViewer cardViewer = Instantiate(cardPrefab, positon, rotation);
        cardViewer.Setup(card);

        return cardViewer;
    } 
}
