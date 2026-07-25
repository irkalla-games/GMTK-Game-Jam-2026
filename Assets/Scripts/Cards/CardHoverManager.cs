using UnityEngine;

public class CardHoverManager : Singleton<CardHoverManager>
{
    [SerializeField] private CardViewer largeCardViewer;
    
    public void ShowLargeCard(Card card, Vector3 position)
    {
        largeCardViewer.gameObject.SetActive(true);
        largeCardViewer.Setup(card);
        largeCardViewer.transform.position = position;
    }

    public void HideLargeCard()
    {
        largeCardViewer.gameObject.SetActive(false);
    }

}
