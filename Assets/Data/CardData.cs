using UnityEngine;

[CreateAssetMenu(fileName = "New Card", menuName = "Card Data")]
public class CardData : ScriptableObject
{

    [SerializeField] public string cardName;
    [SerializeField] public int cost;
    [SerializeField] public string description;
    [SerializeField] public Sprite image;

}
