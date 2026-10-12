"""Card and equipment ratings from uniform-random drafting (profiles.explore) - Tools/BotRunner/Rate-Rewards.py's
method, plus a standard error and a per-profile filter.

    table = rate([out], profile='Explore')      # {name: {offers, takes, diff, se, bonus}}
    pooled = pool_rounds([t_r1, t_r2])          # same card measured in several rounds, inverse-variance
    vet8 = profiles.with_ratings(vet, card_ratings(pooled), 'Veteran8')

Every reward screen is one trial per card on offer: did taking it go with the run getting further than
passing on it? after = levels cleared after the screen's own level. diff = mean(after | taken) -
mean(after | passed); bonus = diff * takes / (takes + 30), exactly as Rate-Rewards.py. se is on diff and
optimistic (offers within one run are not independent).

Ratings describe the content they were measured on - re-measure after any balance change.
"""
import collections
import math

from .batch import runs as batch_runs

PRIOR = 30.0


def offered(reward):
    out = []
    for part in reward.get('offered', '').split(' / '):
        part = part.strip()
        if part and not part.startswith(('Skip', 'Remove', 'Upgrade')):
            out.append(part)
    return out


def rate(batches, profile=None, min_offers=20):
    took, passed = collections.defaultdict(list), collections.defaultdict(list)
    for b in batches:
        for r in batch_runs(b):
            if profile is not None and r['profile'] != profile:
                continue
            for rw in r.get('rewards', []):
                if rw.get('kind') not in ('card', 'equipment', 'skip', 'decline'):
                    continue
                after = r['levelsCleared'] - (rw['level'] - 1)
                for name in offered(rw):
                    (took if name == rw.get('choice') else passed)[name].append(after)
    out = {}
    for name in set(took) | set(passed):
        t, p = took.get(name, []), passed.get(name, [])
        if len(t) < 2 or len(p) < 2 or len(t) + len(p) < min_offers:
            continue
        mt, mp = sum(t) / len(t), sum(p) / len(p)
        vt = sum((x - mt) ** 2 for x in t) / (len(t) - 1)
        vp = sum((x - mp) ** 2 for x in p) / (len(p) - 1)
        out[name] = {'offers': len(t) + len(p), 'takes': len(t), 'diff': round(mt - mp, 3),
                     'se': round(math.sqrt(vt / len(t) + vp / len(p)), 3),
                     'bonus': round((mt - mp) * len(t) / (len(t) + PRIOR), 3)}
    return out


def pool_rounds(tables):
    """One rating per card across rounds that measured the SAME version of it (pass only those tables)."""
    out = {}
    for name in set().union(*[set(t) for t in tables]):
        rs = [t[name] for t in tables if name in t and t[name]['se'] > 0]
        if not rs:
            continue
        w = [1 / r['se'] ** 2 for r in rs]
        diff = sum(wi * r['diff'] for wi, r in zip(w, rs)) / sum(w)
        takes = sum(r['takes'] for r in rs)
        out[name] = {'diff': round(diff, 3), 'se': round((1 / sum(w)) ** 0.5, 3),
                     'bonus': round(diff * takes / (takes + PRIOR), 3), 'takes': takes,
                     'offers': sum(r['offers'] for r in rs), 'rounds': len(rs)}
    return out


def card_ratings(table):
    """BotRewardPrefs.cardRatings' shape, best first."""
    return sorted(({'card': n, 'bonus': r['bonus']} for n, r in table.items()), key=lambda x: -x['bonus'])
