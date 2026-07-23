using UnityEngine;

public class Card
{
    public int cost {  get; private set; }
    public string description { get; private set; }
    public string cardName => data.cardName;
    public Sprite image => data.image;

    private readonly CardData data;
    public Card(CardData newData)
    {
        this.data = newData;
        this.cost = newData.cost;
        this.description = newData.description;
    }
}
