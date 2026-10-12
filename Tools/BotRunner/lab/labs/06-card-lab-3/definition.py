"""Card Lab III (2026-10-11): sixteen Knight/Cleric adjacency cards on Card Lab II's L4 pool, mirror only.
Copied verbatim from BotRuns/claude-tools/cardlab3.py and cardlab3/overlay (whose LabCost.cs replaces lab 2's).
The default numbers are round R3's, the version the evaluation arms used (L7).

Everything Card Lab II patched, plus:
  - CardEffectEntry gains labCondition/labBonus: a bonus while the caster meets the condition, or (no bonus) a gate.
    Card resolves, previews and lands entries through LabCost.EntryDelta/EntryLive.
  - The bot: Measure reads the same EntryDelta and scores LabAfterMoveEffect at the destination; a new weight
    `formation` (BotWeights/BotFeatures) prices what a move does to the party's adjacency payoff
    (LabCost.FormationPayoff after minus before) - 0 is the plain bot.
"""
import json
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))
from labkit.patches import merge, totem_hp  # noqa: E402
from labkit.definitions import load  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
OVERLAY = os.path.join(HERE, 'overlay')
CONFIG_PATH = 'Assets/Editor/CardLab/config.txt'
METHOD = 'CardLab3Builder.BuildFromCommandLine'
NL = '\r\n'
LAB2 = load('05-card-lab-2')

CODE3 = {
    'Assets/Scripts/Cards/CardData.cs': [
        ('    public int amountPercent;',
         '    public int amountPercent;' + NL + '    public int labCondition;' + NL + '    public int labBonus;'),
    ],
    'Assets/Scripts/Cards/Card.cs': [
        ('entry.effect.Resolve(new ActionContext(this, source, landed, aim, entry.amountDelta, entry.amountPercent));',
         'entry.effect.Resolve(new ActionContext(this, source, landed, aim, LabCost.EntryDelta(entry, source), entry.amountPercent));'),
        ('ActionContext ctx = new(this, source, Array.Empty<GridTile>(), aim, entry.amountDelta, entry.amountPercent);',
         'ActionContext ctx = new(this, source, Array.Empty<GridTile>(), aim, LabCost.EntryDelta(entry, source), entry.amountPercent);', 3),
        ('        CardEffectEntry entry, Character source, GridTile casterTile, GridTile aim, bool filterRefused)' + NL + '    {' + NL,
         '        CardEffectEntry entry, Character source, GridTile casterTile, GridTile aim, bool filterRefused)' + NL + '    {' + NL
         + '        if (!LabCost.EntryLive(entry, source)) { return new List<GridTile>(); }' + NL),
        ('            List<GridTile> landed = Landed(entry, source, casterTile, aim, filterRefused);',
         '            if (!LabCost.EntryLive(entry, source)) { continue; }' + NL + NL
         + '            List<GridTile> landed = Landed(entry, source, casterTile, aim, filterRefused);'),
        ('    private IEnumerable<GridTile> EntryFootprint(Character source, GridTile casterTile, GridTile aim, CardEffectEntry entry)' + NL + '    {' + NL,
         '    private IEnumerable<GridTile> EntryFootprint(Character source, GridTile casterTile, GridTile aim, CardEffectEntry entry)' + NL + '    {' + NL
         + '        if (!LabCost.EntryLive(entry, source)) { yield break; }' + NL),
    ],
    'Assets/Scripts/Bot/BotProfile.cs': [
        ('    public float hunt;',
         '    public float hunt;' + NL + NL
         + '    [Tooltip("CARD LAB 3: per point of adjacency payoff a move switches on (LabCost.FormationPayoff).")]' + NL
         + '    public float formation;'),
    ],
    'Assets/Scripts/Bot/BotPlanner.cs': [
        ('exposure, mitigationNeed, forecast, focus, hunt;', 'exposure, mitigationNeed, forecast, focus, hunt, formation;'),
        ('+ forecast * w.forecast + focus * w.focus + hunt * w.hunt - cost * w.costPenalty;',
         '+ forecast * w.forecast + focus * w.focus + hunt * w.hunt + formation * w.formation - cost * w.costPenalty;'),
        ('        Term("hunt", hunt, w.hunt);', '        Term("hunt", hunt, w.hunt);' + NL + '        Term("formation", formation, w.formation);'),
        ('            if (effect == null || effect is DamageEffect || effect is AnimateEffect) { continue; }',
         '            if (effect == null || effect is DamageEffect || effect is AnimateEffect) { continue; }' + NL
         + '            if (!LabCost.EntryLive(entry, hero)) { continue; }'),
        ('ActionContext ctx = new(card, hero, landed, tile, entry.amountDelta, entry.amountPercent);',
         'ActionContext ctx = new(card, hero, landed, tile, LabCost.EntryDelta(entry, hero), entry.amountPercent);'),
        ('                case MoveEffect:' + NL + '                    moves = true;',
         '                case LabAfterMoveEffect after:' + NL
         + '                    foreach (Character ally in after.Recipients(hero, tile))' + NL
         + '                    {' + NL
         + '                        int n = ctx.Amount(after.Amount);' + NL
         + '                        m.Ops.Add(after.Kind == LabAfterMoveEffect.Heal ? new BotOp(BotOp.Kind.Heal, ally, n)' + NL
         + '                            : after.Kind == LabAfterMoveEffect.GainBlock ? new BotOp(BotOp.Kind.Charges, ally, n, BlockStatus.AmountPerHit)' + NL
         + '                            : new BotOp(BotOp.Kind.Shield, ally, n));' + NL
         + '                    }' + NL + NL
         + '                    f.draw += after.Draw;' + NL
         + '                    break;' + NL
         + '                case MoveEffect:' + NL + '                    moves = true;'),
        ('            MoveLookahead(model, scoring, candidates);',
         '            if (!Mathf.Approximately(weights.formation, 0f)) { Formation(model, scoring, candidates); }' + NL
         + '            MoveLookahead(model, scoring, candidates);'),
        ('    private static void MoveLookahead(BotBoardModel model, Scoring scoring, List<BotCandidate> sorted)',
         '    /// CARD LAB 3: every move, priced by what it does to the party\'s adjacency payoff (relocate and restore).' + NL
         + '    private static void Formation(BotBoardModel model, Scoring scoring, List<BotCandidate> candidates)' + NL
         + '    {' + NL
         + '        float before = LabCost.FormationPayoff(model.Heroes);' + NL + NL
         + '        foreach (BotCandidate candidate in candidates)' + NL
         + '        {' + NL
         + '            if (!candidate.Card.HasEffect<MoveEffect>() || candidate.Tile.Occupant != null) { continue; }' + NL + NL
         + '            Character hero = candidate.Hero;' + NL
         + '            GridTile origin = hero.Tile;' + NL + NL
         + '            if (origin == null || candidate.Tile == origin) { continue; }' + NL + NL
         + '            float after;' + NL + NL
         + '            try' + NL
         + '            {' + NL
         + '                hero.MoveTo(candidate.Tile);' + NL
         + '                after = LabCost.FormationPayoff(model.Heroes);' + NL
         + '            }' + NL
         + '            finally' + NL
         + '            {' + NL
         + '                hero.MoveTo(origin);' + NL
         + '            }' + NL + NL
         + '            candidate.Measure.Fixed.formation += after - before;' + NL
         + '            candidate.Features.formation += after - before;' + NL
         + '            candidate.Score = candidate.Features.Score(scoring.W);' + NL
         + '        }' + NL
         + '    }' + NL + NL
         + '    private static void MoveLookahead(BotBoardModel model, Scoring scoring, List<BotCandidate> sorted)'),
    ],
    'Assets/Scripts/Bot/BotCardInfo.cs': [
        ('                case MoveEffect:' + NL + '                    info.Mark(BotPlayCategory.Move);',
         '                case LabAfterMoveEffect after:' + NL
         + '                    info.Mark(after.Kind == LabAfterMoveEffect.Heal ? BotPlayCategory.Heal : BotPlayCategory.Defense);' + NL
         + '                    break;' + NL
         + '                case MoveEffect:' + NL + '                    info.Mark(BotPlayCategory.Move);'),
        ('ActionContext ctx = new(card, null, Array.Empty<GridTile>(), null, entry.amountDelta, entry.amountPercent);',
         'ActionContext ctx = new(card, null, Array.Empty<GridTile>(), null, entry.amountDelta + entry.labBonus / 2, entry.amountPercent);'),
        ('                case MoveEffect:' + NL + '                    raw += 3f * Mathf.Max(w.moveSetup, w.moveSafety);',
         '                case LabAfterMoveEffect after:' + NL
         + '                    raw += 1.5f * ctx.Amount(after.Amount) * (after.Kind == LabAfterMoveEffect.Heal ? w.heal' + NL
         + '                        : after.Kind == LabAfterMoveEffect.GainBlock ? BlockStatus.AmountPerHit * w.mitigation * 0.8f : w.mitigation * 0.8f)' + NL
         + '                        + after.Draw * w.draw;' + NL
         + '                    break;' + NL
         + '                case MoveEffect:' + NL + '                    raw += 3f * Mathf.Max(w.moveSetup, w.moveSafety);'),
    ],
}

BASE_CONFIG = {'wave': '5', 'clear': '40,50,10', 'skip': 'Shield Wall|Heavy Slash'}
with open(os.path.join(HERE, 'lab3-numbers.json'), encoding='utf-8') as _f:
    FINAL_NUMBERS = json.load(_f)['R3']


def config(numbers=None, **over):
    """The builder's config.txt: pool settings, then n.<Card>.<field>=<value> card numbers."""
    cfg = dict(BASE_CONFIG)
    cfg.update({k: str(v) for k, v in over.items()})
    lines = [f'{k}={v}' for k, v in cfg.items()]
    lines += [f'n.{k}={v}' for k, v in (FINAL_NUMBERS if numbers is None else numbers).items()]
    return '\n'.join(lines) + '\n'


def patches(root):
    return merge(LAB2.CODE, CODE3, totem_hp(root, LAB2.RARE_TOTEMS, 10))
