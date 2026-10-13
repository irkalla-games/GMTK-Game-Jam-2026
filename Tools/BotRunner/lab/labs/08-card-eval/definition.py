"""Spire Lab 08 (2026-10-12): the game as frozen, with the 47 new Knight/Cleric cards taken out of rewards.

Lab 08 measures the tree as it stands once the card session finished (freeze 2026-10-12 03:05, ed2880d8 plus
uncommitted). That tree already holds the 47 cards built from Card Labs II-IV and Card Lab II's L4 pool
(Docs/CHANGELOG.md, 2026-10-12). The base player needs no definition. This one builds the comparison arm,
"nonew": the same game with every one of those cards set excludeFromRewards, so the paired difference is
what the set adds to a run and nothing else (the L4 pool settings, enemy edits and bot code stay).

The list is the one the card session reported. The spec is Archive/EditorTools/CardLabContentBuilder.cs.
"""
import glob
import os

NEW_CARDS = [
    # Card Lab II
    'Shield Bash', 'Totem Guard', 'Communion', 'Prayer Circle', 'Bulwark Stance', 'Rally the Line',
    'Lasting Renewal', 'Sanctified Guard', 'Shield Strike', 'Tower Shield', 'Blessed Shield', 'Chapel Totem',
    'Sweeping Blow', 'Brothers in Arms', 'Warhorn Totem', 'Rejuvenate', 'Shrine Blessing', 'Holy Smite',
    'Totem Strike', 'Radiant Smite', 'Shield Wall', 'Shield Slam', 'Guard Up', 'Mending Light', 'Ward Blessing',
    'Spring Totem',
    # Card Lab III
    'Smite Together', 'Sanctified Ground', 'Laying on of Hands', 'Shield of Faith', 'Close Prayer',
    'Guiding Smite', 'Benediction', 'Shoulder to Shoulder', "Vanguard's Oath", 'Phalanx', 'Hold the Line',
    'Covering Strike', 'Flanking Strike',
    # Card Lab IV
    'Lone Stand', 'Solitary Guard', 'Last Bastion', "Hermit's Smite", 'Distant Grace', 'Far Mend', 'Vigil',
    'Judgement',
]
assert len(NEW_CARDS) == 47, len(NEW_CARDS)

FIELD = '  <excludeFromRewards>k__BackingField: '


def card_paths(root):
    """{card name: relative asset path} for every new card, found under Knight/ and Cleric/ in `root`."""
    out = {}
    for name in NEW_CARDS:
        hits = []
        for hero in ('Knight', 'Cleric'):
            hits += glob.glob(os.path.join(root, 'Assets', 'Data', 'CardData', hero, '**', name + '.asset'),
                              recursive=True)
        if len(hits) != 1:
            raise SystemExit(f'{name}: {len(hits)} assets found under {root}')
        out[name] = os.path.relpath(hits[0], root).replace(os.sep, '/')
    return out


def patches(root):
    return {rel: [(FIELD + '0', FIELD + '1')] for rel in card_paths(root).values()}
