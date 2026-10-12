"""Builds the card-lab-2 bot player: the working tree plus cardlab2/overlay (LabCost, CardLab2Builder) and the
patches below, IN THE BUILD MIRROR ONLY. The repository is never written (scratch tooling).

  - CardData gains labCondition/labDiscount; Card.cost becomes a property that takes the discount off while
    the holder meets the condition (Character stamps itself as each card's holder; CostModifier writes the
    base cost so an equipment discount never bakes a condition discount in).
  - Every Rare Knight/Cleric totem goes to 10 health (the new lab totems are built at 10 by the builder).
"""
import os, re
from variant import build_variant, merge
from botlab import REPO

HERE = os.path.dirname(os.path.abspath(__file__))
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


def totem_hp(names, health):
    out = {}
    for n in names:
        rel = f'Assets/Prefabs/Totem/{n}.prefab'
        text = open(os.path.join(REPO, rel), encoding='utf-8', newline='').read()
        nl = '\r\n' if '\r\n' in text else '\n'
        h = re.search(r'\n  maxHealth: (\S+)', text).group(1)
        out[rel] = [(f'{nl}  maxHealth: {h}{nl}', f'{nl}  maxHealth: {health}{nl}'),
                    (f'<Health>k__BackingField: {h}{nl}', f'<Health>k__BackingField: {health}{nl}')]
    return out


def build(out=r'Builds\BotPlayerL1', extra=None, config=''):
    with open(os.path.join(HERE, 'cardlab2', 'overlay', 'Assets', 'Editor', 'CardLab', 'config.txt'), 'w') as f:
        f.write(config)
    patches = merge(CODE, totem_hp(RARE_TOTEMS, 10), extra or {})
    return build_variant(out, patches, overlay=os.path.join(HERE, 'cardlab2', 'overlay'),
                         method='CardLab2Builder.BuildFromCommandLine')


if __name__ == '__main__':
    print(build())
