/// <summary>
/// A status a Totem projects onto whoever is standing in its range, as opposed to a StatusEffect,
/// which the character carries itself.
///
/// Same capabilities as a StatusEffect - every hook is forwarded, so an aura can poison, freeze, root,
/// buff or mitigate exactly like a card-applied status. Character.ActiveStatuses puts auras ahead of
/// carried statuses, so they always resolve first. Ownership and lifetime are the only real
/// differences, and both follow from the totem owning this rather than the character:
///
///   - No duration. An aura lasts exactly as long as you stand in range; turnsRemaining is Indefinite
///     and never ages, because nothing on the character is tracking it.
///   - Never merged. Two totems projecting Strength give you two separate auras, not one doubled
///     entry, which is what lets each withdraw independently by you simply walking out of its range.
///   - Charges do not really deplete. Totem builds these fresh on every query, so a hook that spends a
///     stack is mutating a throwaway. That is the right reading of a *maintained* effect, but it makes
///     Block, Parry and Double Attack far stronger as auras than as cards.
///
/// Implemented by wrapping the StatusEffect that carries the rule rather than by reimplementing it.
/// A StrengthAura duplicating StrengthStatus's arithmetic is exactly the kind of second answer to one
/// question this refactor exists to delete.
/// </summary>
public class Aura : Status
{
    /// Which totem is projecting this. Kept so a UI or a log can say where a buff is coming from -
    /// "Strength 1 (Cleric)" rather than an unexplained bonus.
    public readonly Totem sourceTotem;

    /// The rule this aura is projecting. Every hook forwards here.
    private readonly StatusEffect projectedEffect;

    public Aura(Totem sourceTotem, StatusEffect projectedEffect)
    {
        this.sourceTotem = sourceTotem;
        this.projectedEffect = projectedEffect;
    }

    public override StatusType type => projectedEffect.type;

    public override int stacks
    {
        get => projectedEffect.stacks;
        set => projectedEffect.stacks = value;
    }

    /// Always Indefinite, and the setter is deliberately inert: an aura is maintained by its totem, so
    /// Character.OnTurnEnd ageing it would be ageing something the character does not own.
    public override int turnsRemaining
    {
        get => Indefinite;
        set { }
    }

    public override string ActRefusal(Character carrier) => projectedEffect.ActRefusal(carrier);

    public override string MoveRefusal(Character carrier, GridTile destination) =>
        projectedEffect.MoveRefusal(carrier, destination);

    public override DamageInfo OnDealDamage(DamageInfo info) => projectedEffect.OnDealDamage(info);

    public override DamageInfo OnTakeDamage(DamageInfo info) => projectedEffect.OnTakeDamage(info);

    public override void OnTurnStart(Character carrier) => projectedEffect.OnTurnStart(carrier);

    public override void OnTurnEnd(Character carrier) => projectedEffect.OnTurnEnd(carrier);

    public override string Describe() => projectedEffect.Describe();

    /// Forwarded like every other hook, so a totem's projected Block fills a tooltip's {amount} with
    /// the same arithmetic a carried one does rather than a second copy of it.
    public override string Describe(string template) => projectedEffect.Describe(template);
}
