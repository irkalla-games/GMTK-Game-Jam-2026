using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CARD LAB 3 (bot-mirror only). "Then you and heroes next to you gain N Shield / heal N / gain N Block" -
/// the payoff half of a move-into-formation card, entry 2 after a Move entry. Who it reaches is decided
/// when it resolves, not when the card is played: Resolve only queues LabAfterMoveAction behind the
/// MoveAction, which reads the caster's tile once the walk has finished.
///
/// Refuses every tile so Card.Refusal never lets it claim one - the Move entry alone decides where the
/// card may be played (an effect that refused nothing would make the caster's own tile playable).
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Lab After Move")]
public class LabAfterMoveEffect : CardEffect
{
    public const int GainShield = 0, Heal = 1, GainBlock = 2;

    [SerializeField] private int kind;
    [SerializeField] private int amount;
    [SerializeField] private bool includeSelf = true;
    [SerializeField] private int draw;
    [Tooltip("CARD LAB 4: a LabCost condition (an alone distance) the destination must meet; None = lab 3's rule.")]
    [SerializeField] private int condition;

    public int Kind => kind;
    public int Amount => amount;
    public bool IncludeSelf => includeSelf;
    public int Draw => draw;
    public int Condition => condition;

    /// CARD LAB 4: true when `source` standing on `at` meets the condition (always true without one).
    public bool MetAt(Character source, GridTile at) => condition == LabCost.None || LabCost.MetAt(source, at, condition);

    /// Cards drawn if `source` ends on `at`.
    public int DrawAt(Character source, GridTile at) => MetAt(source, at) ? draw : 0;

    public override bool ActsOnSource => true;

    public override bool SupportsArea => false;

    public override TargetAudience Audience => TargetAudience.Ally;

    public override string Refusal(Character source, GridTile target) => "lands after the move";

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new LabAfterMoveAction(this), ctx);
    }

    /// Who it reaches if `source` stands on `at`: the source itself (when includeSelf) and every hero next
    /// to that tile. `at` need not be where the source stands yet - the bot asks it of a destination.
    public List<Character> Recipients(Character source, GridTile at)
    {
        if (condition != LabCost.None)
        {
            List<Character> self = new();
            if (source != null && MetAt(source, at)) { self.Add(source); }
            return self;
        }

        List<Character> who = LabCost.HeroesNextTo(source, at);

        if (includeSelf && source != null) { who.Insert(0, source); }

        return who;
    }
}

public class LabAfterMoveAction : GameAction
{
    private readonly LabAfterMoveEffect effect;

    public LabAfterMoveAction(LabAfterMoveEffect effect)
    {
        this.effect = effect;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        Character source = ctx.source;

        if (source == null || source.IsDead || source.Tile == null) { yield break; }

        if (effect.Condition != LabCost.None)
        {
            LabCost.Record($"LABPLAY|{(ctx.card != null && ctx.card.Data != null ? ctx.card.Data.cardName : "?")}|{(effect.MetAt(source, source.Tile) ? 1 : 0)}|{LabCost.NearestHero(source)}");
        }

        int draws = effect.DrawAt(source, source.Tile);
        if (draws > 0) { source.DrawCards(draws); }

        List<GridTile> tiles = new();

        foreach (Character who in effect.Recipients(source, source.Tile))
        {
            if (who != null && who.Tile != null) { tiles.Add(who.Tile); }
        }

        if (tiles.Count == 0) { yield break; }

        ActionContext landed = new(ctx.card, source, tiles, source.Tile);
        int n = ctx.Amount(effect.Amount);

        GameAction inner = effect.Kind switch
        {
            LabAfterMoveEffect.Heal => new HealAction(n),
            LabAfterMoveEffect.GainBlock => new BlockAction(n),
            _ => new ShieldAction(n),
        };

        yield return inner.Execute(landed);
    }
}
