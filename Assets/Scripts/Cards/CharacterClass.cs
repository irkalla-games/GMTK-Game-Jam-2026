/// <summary>
/// Which character(s) a card belongs to. Knight cards are useless in the Mage's hand and vice versa,
/// but a card can now belong to more than one class at once - a Rogue reusing a Knight's Quick Attack,
/// say - so this is a [Flags] mask rather than a plain count.
///
/// Any = 0 so every card asset authored before this field existed deserializes as unrestricted -
/// the same load-bearing-int rule TargetRange/RangeShape already documents. Getting that default
/// backwards would silently make every existing card Knight-only.
///
/// Knight = 1 and Mage = 2 were already single, non-overlapping bits before this became a flags
/// enum, so every card asset on disk (storing a raw int) keeps meaning exactly what it always meant -
/// no migration. That is also why the next class has to be 1 &lt;&lt; 2 = 4, not 3: 3 is
/// Knight | Mage, already spoken for as "either of the first two", not a free value. Add further
/// classes the same way - 8, 16, 32... - and never reorder or reuse a bit once a card asset may have
/// it written into it.
///
/// Room for ~8 classes comfortably: the underlying type is int (32 bits), Any reserves one value, so
/// 31 single-bit classes fit before CharacterClass would need to become a long - and Unity's
/// serializer writes enums as int regardless of the declared underlying type, so a long would have its
/// top bits silently truncated on save. Don't equality-check against a hand-built "all classes"
/// constant either: Unity's Inspector mask stores -1 for "Everything", not the sum of the declared
/// flags, and the & test below already handles that correctly.
///
/// Not checked in Card.Refusal. That answers "may this card be played on this tile" and is asked
/// once per click *and* against every tile on the board whenever a card is selected; class is static
/// for the life of the card, so re-deriving it thirty times per selection is waste. Worse, it would
/// produce a card sitting in your hand that highlights nothing and refuses every click. A Mage should
/// never be holding a Knight card in the first place, so the check lives where cards enter a deck.
///
/// A CardData's requiredClass is authored as this mask directly (Unity's default multi-select
/// dropdown). A Character's own class is exactly one value, never a combination - see
/// SingleClassAttribute on Character.characterClass, which restricts the Inspector to single-bit
/// choices only.
/// </summary>
[System.Flags]
public enum CharacterClass
{
    Any = 0,
    Knight = 1 << 0,
    Mage = 1 << 1,
    Rogue = 1 << 2,
    Cleric = 1 << 3,
}
