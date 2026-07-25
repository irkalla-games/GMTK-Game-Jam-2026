using System;
using Unity.VisualScripting;
using UnityEngine;
using DG.Tweening;

public class CreateCardViewer : Singleton<CreateCardViewer>
{
    [SerializeField] private CardViewer cardPrefab;
    public CardViewer CreateCard(Card card, Vector3 positon, Quaternion rotation)
    {
        CardViewer cardViewer = Instantiate(cardPrefab, positon, rotation);
        cardViewer.Setup(card);
        cardViewer.transform.localScale = Vector3.zero;
        cardViewer.transform.DOScale(Vector3.one, .15f);

        return cardViewer;
    } 
}
