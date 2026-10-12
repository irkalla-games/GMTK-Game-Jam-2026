"""Builds the card-lab-4 bot player: the working tree plus cardlab4/overlay (LabCost with "alone",
LabAfterMoveEffect with a condition, CardLab4Builder) and lab 3's patches, IN THE BUILD MIRROR ONLY. The
repository is never written (scratch tooling).

Pool: Card Lab II's L4 pool (wave=3) minus every "next to another hero" card (Rally the Line, Communion,
Brothers in Arms, Prayer Circle, Mending Light, Ward Blessing), plus lab 4's taunt and alone cards. alone=N sets
the distance: every other hero at least N tiles away (Chebyshev).

On top of lab 3's patches:
  - Card.ResolveEffects logs LABPLAY|card|met|nearest for every positional card (LabCost.LogPlay), read
    back from the shard logs to measure how often the condition held.
  - The bot's after-move payoff only counts its draw when the destination meets the condition.
"""
import os, copy
from variant import build_variant, merge
from cardlab2 import CODE as CODE2, totem_hp, RARE_TOTEMS
from cardlab3 import CODE3

HERE = os.path.dirname(os.path.abspath(__file__))

NEXT_TO = 'Rally the Line|Communion|Brothers in Arms|Prayer Circle|Mending Light|Ward Blessing'
BASE_CONFIG = {'wave': '3', 'clear': '40,50,10', 'skip': 'Shield Wall|Heavy Slash|' + NEXT_TO}


def code4():
    c = copy.deepcopy(CODE3)
    bp = c['Assets/Scripts/Bot/BotPlanner.cs']
    for i, (old, new, *rest) in enumerate(bp):
        if 'f.draw += after.Draw;' in new:
            bp[i] = (old, new.replace('f.draw += after.Draw;', 'f.draw += after.DrawAt(hero, tile);'), *rest)
            break
    else:
        raise SystemExit('after-move draw patch not found')
    c['Assets/Scripts/Cards/Card.cs'].append(
        ('        CardKeyword cooldown = Keyword(CardKeywordType.Cooldown);',
         '        LabCost.LogPlay(this, source);\r\n        CardKeyword cooldown = Keyword(CardKeywordType.Cooldown);'))
    return c


def config_text(numbers=None, **over):
    cfg = dict(BASE_CONFIG)
    cfg.update(over)
    lines = [f'{k}={v}' for k, v in cfg.items()]
    lines += [f'n.{k}={v}' for k, v in (numbers or {}).items()]
    return '\n'.join(lines) + '\n'


def build(out, alone=2, numbers=None, extra=None, **over):
    cfg_dir = os.path.join(HERE, 'cardlab4', 'overlay', 'Assets', 'Editor', 'CardLab')
    with open(os.path.join(cfg_dir, 'config.txt'), 'w') as f:
        f.write(config_text(numbers, alone=str(alone), **over))
    patches = merge(CODE2, code4(), totem_hp(RARE_TOTEMS, 10), extra or {})
    return build_variant(out, patches, overlay=os.path.join(HERE, 'cardlab4', 'overlay'),
                         method='CardLab4Builder.BuildFromCommandLine')


if __name__ == '__main__':
    import sys
    print(build(sys.argv[1], alone=int(sys.argv[2]) if len(sys.argv) > 2 else 2,
                **dict(a.split('=', 1) for a in sys.argv[3:])))
