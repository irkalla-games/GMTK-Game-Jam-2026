/// <summary>
/// Which character a card belongs to. Knight cards are useless in the Mage's hand and vice versa.
///
/// Any = 0 so every card asset authored before this field existed deserializes as unrestricted -
/// the same load-bearing-int rule TargetRange/RangeShape already documents. Getting that default
/// backwards would silently make every existing card Knight-only.
///
/// Not checked in Card.Refusal. That answers "may this card be played on this tile" and is asked
/// once per click *and* against every tile on the board whenever a card is selected; class is static
/// for the life of the card, so re-deriving it thirty times per selection is waste. Worse, it would
/// produce a card sitting in your hand that highlights nothing and refuses every click. A Mage should
/// never be holding a Knight card in the first place, so the check lives where cards enter a deck.
/// </summary>
public enum CharacterClass
{
    Any = 0,
    Knight = 1,
    Mage = 2,
}
