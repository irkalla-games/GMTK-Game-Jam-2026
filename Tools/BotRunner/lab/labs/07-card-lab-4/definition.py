"""Card Lab IV (2026-10-11): Knight taunts and "alone" cards on the L4 pool minus every next-to card, mirror only.
Copied verbatim from BotRuns/claude-tools/cardlab4.py and cardlab4/overlay. Defaults are the tuned version:
alone = 3 (every other hero at least 3 tiles away, Chebyshev) and the T round's numbers.

On top of Card Lab III's patches:
  - Card.ResolveEffects logs LABPLAY|card|met|nearest for every positional card (LabCost.LogPlay), read
    back from the shard logs to measure how often the condition held.
  - The bot's after-move payoff only counts its draw when the destination meets the condition.
"""
import copy
import json
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))
from labkit.patches import merge, totem_hp  # noqa: E402
from labkit.definitions import load  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
OVERLAY = os.path.join(HERE, 'overlay')
CONFIG_PATH = 'Assets/Editor/CardLab/config.txt'
METHOD = 'CardLab4Builder.BuildFromCommandLine'
LAB2 = load('05-card-lab-2')
LAB3 = load('06-card-lab-3')
CODE3 = LAB3.CODE3

NEXT_TO = 'Rally the Line|Communion|Brothers in Arms|Prayer Circle|Mending Light|Ward Blessing'
BASE_CONFIG = {'wave': '3', 'clear': '40,50,10', 'skip': 'Shield Wall|Heavy Slash|' + NEXT_TO, 'alone': '3'}
with open(os.path.join(HERE, 'lab4-numbers.json'), encoding='utf-8') as _f:
    FINAL_NUMBERS = json.load(_f)['T']


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


def config(numbers=None, **over):
    """The builder's config.txt: pool settings, then n.<Card>.<field>=<value> card numbers."""
    cfg = dict(BASE_CONFIG)
    cfg.update({k: str(v) for k, v in over.items()})
    lines = [f'{k}={v}' for k, v in cfg.items()]
    lines += [f'n.{k}={v}' for k, v in (FINAL_NUMBERS if numbers is None else numbers).items()]
    return '\n'.join(lines) + '\n'


def patches(root):
    return merge(LAB2.CODE, code4(), totem_hp(root, LAB2.RARE_TOTEMS, 10))
