using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New Card", menuName = "Card Data")]
public class CardData : ScriptableObject
{

    [SerializeField] public string cardName;
    [SerializeField] public int cost;
    [SerializeField] public string description;
    [SerializeField] public Sprite image;
    [SerializeField] public List<GameAction> gameActions;


}
