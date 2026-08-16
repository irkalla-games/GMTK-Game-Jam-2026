using UnityEngine;

/// <summary>
/// Where an effect lands, relative to the tile the player clicked.
///
/// PlayedTile = 0 so every effect asset authored before this field existed keeps aiming where it
/// always did. Lives here rather than nowhere because AuraReaction and CardEffectEntry both need the
/// vocabulary, even though the aim choice itself now lives on CardEffectEntry, not on this asset - see
/// that struct's doc comment for why.
/// </summary>
public enum EffectTarget
{
    /// The tile the card was played on.
    PlayedTile = 0,

    /// The acting character's own tile, whatever they clicked. Steely Attack damages the enemy you
    /// picked but armors *you*.
    Source = 1,
}

/// <summary>
/// Who an effect is willing to land on.
///
/// The declared half of the rule <see cref="CardEffect.Refusal"/> already enforced by hand: rather
/// than each effect hard-coding a `wantAlly` argument nothing else can read, it names its audience
/// once and the shared refusal reads it. That is what lets a card face colour its stripe and a
/// tooltip write "Targets an enemy" without a second, parallel answer to the same question drifting
/// out of step with the first.
///
/// Lives here beside EffectTarget for the same reason that one does - it is vocabulary the effects,
/// the card and the card face all have to share.
///
/// Unrestricted is deliberately value 0, so an effect that never declares an audience behaves exactly
/// like the base CardEffect always did: no occupant rule at all.
/// </summary>
public enum TargetAudience
{
    /// No occupant rule of its own. Draw, Animate and the tile effects - a wall does not care who is
    /// standing where it lands.
    Unrestricted = 0,

    /// Somebody on your own side. Heals, shields, blocks, parries and buffs. A character counts as
    /// their own ally, so a self-aimed buff satisfies this.
    Ally = 1,

    /// Somebody on the other side. Damage, curses, Taunt.
    Enemy = 2,

    /// Anybody at all, but somebody - Swap does not care which side it displaces, only that there is
    /// a body there to displace.
    AnyCharacter = 3,

    /// Bare ground. Summons and movement need the tile itself, and refuse an occupied one.
    EmptyTile = 4,
}

public abstract class CardEffect : ScriptableObject
{
    /// <summary>
    /// Called when a card is played.
    /// The effect should queue one or more GameActions.
    /// </summary>
    public abstract void Resolve(ActionContext ctx);

    /// <summary>
    /// False for an effect that can only ever mean one tile - Move, principally, since MoveAction
    /// throws if it is handed more than one target. Card.ResolveEffects forces an entry back to
    /// AreaKind.Single before building its footprint when this is false, no matter what area the card
    /// was authored with, so a Move entry can never be handed a multi-tile ActionContext.
    /// </summary>
    public virtual bool SupportsArea => true;

    /// <summary>
    /// Who this effect is willing to land on. Declared once here rather than buried in a `wantAlly`
    /// argument inside Refusal, because two different readers need the answer: the refusal below
    /// enforces it, and the card face paints its audience stripe from it. Deriving both from this one
    /// property is what stops the stripe promising something a click would then refuse - the same
    /// bargain Card.Refusal and GridManager.ShowPlayableTiles already strike over range.
    ///
    /// Effects with a rule the audience cannot express - Heal's full-health check, Move's whole
    /// GridManager.MoveRefusal - still override Refusal, and call base first.
    /// </summary>
    public virtual TargetAudience Audience => TargetAudience.Unrestricted;

    /// <summary>
    /// Why this effect could not land on `target`, or null if it can. Asked before the card is paid
    /// for, so a refusal costs the player nothing.
    ///
    /// Range is not checked here - that is the card's rule, on CardData. This is for rules only the
    /// effect knows, like Move being unable to land on a tile somebody is already standing on.
    ///
    /// Note `target` is always the *played* tile, even for a Source-aimed effect. See Card.Refusal.
    /// </summary>
    public virtual string Refusal(Character source, GridTile target) =>
        RefuseByAudience(source, target, Audience);

    /// <summary>
    /// The shared "is there somebody there, and are they the right somebody" rule. Damage wants an
    /// enemy, heals and buffs want an ally, and hand-rolling that in each of them is four copies of
    /// one comparison waiting to disagree.
    ///
    /// Sides are just "player controlled or not" - there are no teams beyond that yet.
    /// </summary>
    protected static string RefuseByAudience(Character source, GridTile target, TargetAudience audience)
    {
        if (audience == TargetAudience.Unrestricted) { return null; }

        Character occupant = target != null ? target.Occupant : null;

        // Ground-seeking effects want the opposite of everything below: a body is the problem, not
        // the requirement. Mirrors GridTile.SummonObject's own occupancy check.
        if (audience == TargetAudience.EmptyTile)
        {
            return occupant != null ? $"{occupant.name} is already standing there" : null;
        }

        if (occupant == null) { return "there is nobody there"; }

        // A caster we cannot identify has no side to compare against, so anybody standing there will
        // do - the same permissive answer this gave before audiences existed.
        if (audience == TargetAudience.AnyCharacter || source == null) { return null; }

        bool ally = Character.AreAllies(occupant.Affiliation, source.Affiliation);

        if (audience == TargetAudience.Ally && !ally) { return $"{occupant.name} is not on your side"; }

        if (audience == TargetAudience.Enemy && ally) { return $"{occupant.name} is on your own side"; }

        return null;
    }
}
