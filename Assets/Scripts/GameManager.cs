using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    public List<CardData> deck;

    [SerializeField] private HandViewer handViewer;

    [SerializeField] private CardData cardData;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Card card = new(cardData);
            CardViewer cardViewer = CreateCardViewer.Instance.CreateCard(card, transform.position, Quaternion.identity);
            StartCoroutine(handViewer.AddCard(cardViewer));
        }
    }
}
