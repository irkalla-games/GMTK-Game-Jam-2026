"""Card Lab II (2026-10-10): a stronger Knight/Cleric reward pool, mirror only. Copied verbatim from
BotRuns/claude-tools/cardlab2.py and cardlab2/overlay. The default config is the L4 pool (L3 without Rally the Line
and Communion) - the pool Card Labs III and IV started from.

  - CardData gains labCondition/labDiscount; Card.cost becomes a property that takes the discount off while
    the holder meets the condition (Character stamps itself as each card's holder; CostModifier writes the
    base cost so an equipment discount never bakes a condition discount in).
  - Every Rare Knight/Cleric totem goes to 10 health (the new lab totems are built at 10 by the builder).
"""
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))
from labkit.patches import merge, totem_hp  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
OVERLAY = os.path.join(HERE, 'overlay')
CONFIG_PATH = 'Assets/Editor/CardLab/config.txt'
METHOD = 'CardLab2Builder.BuildFromCommandLine'
NL = '\n'

CODE = {
    'Assets/Scripts/Cards/CardData.cs': [
        ('    [field: SerializeField] public bool excludeFromRewards { get; private set; }',
         '    [field: SerializeField] public bool excludeFromRewards { get; private set; }' + NL
         + '    [field: SerializeField] public int labCondition { get; private set; }' + NL
         + '    [field: SerializeField] public int labDiscount { get; private set; }'),
    ],
    'Assets/Scripts/Cards/Card.cs': [
        ('    public int cost;',
         '    public int baseCost;' + NL + '    public Character Holder;' + NL
         + '    public int cost { get => Mathf.Max(0, baseCost - LabCost.Discount(Holder, data)); set => baseCost = value; }'),
    ],
    'Assets/Scripts/Cards/Modifiers/CostModifier.cs': [
        ('card.cost = Mathf.Max(0, card.cost + delta);', 'card.baseCost = Mathf.Max(0, card.baseCost + delta);'),
    ],
    'Assets/Scripts/Character/Character.cs': [
        ('        Card card = new(data);', '        Card card = new(data) { Holder = this };'),
    ],
}

RARE_TOTEMS = ['BloodmoonTotem', 'LodestoneTotem', 'Totem', 'TwinTotem', 'AegisTotem', 'ReinforceTotem', 'WarcryTotem']

BASE_CONFIG = {'wave': '3', 'clear': '40,50,10', 'skip': 'Shield Wall|Heavy Slash|Rally the Line|Communion'}
FINAL_NUMBERS = {}


def config(numbers=None, **over):
    """The builder's config.txt: pool settings, then n.<Card>.<field>=<value> card numbers."""
    cfg = dict(BASE_CONFIG)
    cfg.update({k: str(v) for k, v in over.items()})
    lines = [f'{k}={v}' for k, v in cfg.items()]
    lines += [f'n.{k}={v}' for k, v in (FINAL_NUMBERS if numbers is None else numbers).items()]
    return '\n'.join(lines) + '\n'


def patches(root):
    return merge(CODE, totem_hp(root, RARE_TOTEMS, 10))
