"""Card and equipment ratings from uniform-random drafting (profiles.explore) - Tools/BotRunner/Rate-Rewards.py's
method, plus a standard error and a per-profile filter.

    table = rate([out], profile='Explore')      # {name: {offers, takes, diff, se, bonus}}
    pooled = pool_rounds([t_r1, t_r2])          # same card measured in several rounds, inverse-variance
    check = agreement(t_r1, t_r2, names=new)    # do the cards differ at all? before naming any winner
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


def agreement(t_a, t_b, names=None, k=1.5):
    """Do the cards actually differ, or is a ranking just noise? Two rounds on separate seed blocks, same
    card versions. Run this before naming any per-card winner or loser.

        a = agreement(t_r1, t_r2, names=new_cards)
        # {'n': 47, 'r': 0.17, 'meanZ2': 0.90, 'trueSd': 0.07, 'typicalSe': 0.11, 'differ': False, 'clear': [...]}

    r: correlation of the two rounds' diffs. meanZ2: mean of (d_a - d_b)^2 / (se_a^2 + se_b^2) - about 1 when
    se is honest, above 1 when it is optimistic. trueSd: the spread left once noise is removed,
    sqrt(var(pooled diff) - mean(pooled se^2)). differ: trueSd clearly above typicalSe; when False, report
    the set-level number and only the `clear` cards - pooled rating k se clear of zero AND the same sign in
    both rounds. Lab 08: r 0.17, trueSd 0.07 in an 86-card pool; its one-round top and bottom were noise.
    """
    both = sorted((set(t_a) & set(t_b)) if names is None else [n for n in names if n in t_a and n in t_b])
    both = [n for n in both if t_a[n]['se'] > 0 and t_b[n]['se'] > 0]
    if len(both) < 3:
        raise ValueError(f'agreement: need 3+ cards rated in both rounds, got {len(both)}')
    xa, xb = [t_a[n]['diff'] for n in both], [t_b[n]['diff'] for n in both]
    ma, mb = sum(xa) / len(xa), sum(xb) / len(xb)
    cov = sum((a - ma) * (b - mb) for a, b in zip(xa, xb))
    va, vb = sum((a - ma) ** 2 for a in xa), sum((b - mb) ** 2 for b in xb)
    r = cov / math.sqrt(va * vb) if va > 0 and vb > 0 else 0.0
    z2 = sum((t_a[n]['diff'] - t_b[n]['diff']) ** 2 / (t_a[n]['se'] ** 2 + t_b[n]['se'] ** 2)
             for n in both) / len(both)
    pooled = pool_rounds([{n: t_a[n] for n in both}, {n: t_b[n] for n in both}])
    pd = [pooled[n]['diff'] for n in both]
    mp = sum(pd) / len(pd)
    var_p = sum((d - mp) ** 2 for d in pd) / (len(pd) - 1)
    se2 = sum(pooled[n]['se'] ** 2 for n in both) / len(both)
    true_sd = math.sqrt(max(0.0, var_p - se2))
    typical = math.sqrt(se2)
    clear = [n for n in both if abs(pooled[n]['diff']) >= k * pooled[n]['se']
             and (t_a[n]['diff'] > 0) == (t_b[n]['diff'] > 0)]
    return {'n': len(both), 'r': round(r, 2), 'meanZ2': round(z2, 2), 'trueSd': round(true_sd, 3),
            'typicalSe': round(typical, 3), 'differ': true_sd > typical,
            'clear': sorted(clear, key=lambda n: -pooled[n]['diff'])}


def card_ratings(table):
    """BotRewardPrefs.cardRatings' shape, best first."""
    return sorted(({'card': n, 'bonus': r['bonus']} for n, r in table.items()), key=lambda x: -x['bonus'])
