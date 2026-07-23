using NUnit.Framework;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    //public List<CardData> deck;

    private CardData cardData;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Card card = new(cardData);
            //CardViewer cardViwer = CreateCardViewer.Instance.CreateCard(card, transform.position, Quaternion.identity);
        }
    }
}
