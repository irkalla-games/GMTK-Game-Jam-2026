"""Bot profiles as the JSON a job carries, read from the BotProfile assets in the lab's FROZEN source.

    vet = stock(lab.source, 'Veteran')            # Assets/Data/Bots/Veteran.asset, as frozen
    exp = explore(vet)                            # uniform-random drafting - what ratings are measured with
    rated = with_ratings(vet, ratings, 'Veteran8')
    p = tweak(vet, 'Deep', searchDepth=8, **{'w.kill': 6, 'r.takeCardsMinValue': 4})

Unity YAML stores bools as 0/1 and floats as written; the job JSON needs true/false. Types come from
profile-template.json - a profile exported by the Editor - so a field the template does not know raises
ProfileSchemaChanged instead of silently writing the wrong type. Refresh the template through the live
Editor (unity command eval_file):  return JsonUtility.ToJson(BotRunnerEditor.Profiles("Veteran")[0]);
"""
import copy
import json
import os
import re

from .paths import PROFILE_TEMPLATE


class ProfileSchemaChanged(Exception):
    pass


# ---------------------------------------------------------------------- the YAML subset BotProfile assets use
def _lines(path):
    out, inside = [], False
    with open(path, encoding='utf-8') as f:
        for raw in f:
            raw = raw.rstrip('\r\n')
            if not raw.strip():
                continue
            indent = len(raw) - len(raw.lstrip(' '))
            if raw == '  data:':
                inside = True
                continue
            if inside:
                if indent <= 2:
                    break
                out.append((indent, raw.strip()))
    if not out:
        raise ProfileSchemaChanged(f'{path}: no "data:" block')
    return out


_KEY = re.compile(r'^([A-Za-z_][A-Za-z0-9_]*):(?: (.*)|)$')


def _scalar(lines, i, first, indent):
    """A scalar starting with `first`; continuation lines are any deeper than `indent`. Returns (str, i)."""
    parts = [first]
    while i < len(lines) and lines[i][0] > indent:
        parts.append(lines[i][1])
        i += 1
    text = ' '.join(parts)
    if text.startswith("'"):
        if not text.endswith("'"):
            raise ProfileSchemaChanged(f'unterminated quoted scalar: {text[:60]}')
        text = text[1:-1].replace("''", "'")
    elif text.startswith('"'):
        text = json.loads(text)
    return text, i


def _map(lines, i, indent):
    out = {}
    while i < len(lines) and lines[i][0] == indent and not lines[i][1].startswith('- '):
        m = _KEY.match(lines[i][1])
        if not m:
            raise ProfileSchemaChanged(f'unreadable line: {lines[i][1]!r}')
        key, rest = m.group(1), (m.group(2) or '').strip()
        i += 1
        if rest == '[]':
            out[key] = []
        elif rest:
            out[key], i = _scalar(lines, i, rest, indent)
        elif i < len(lines) and lines[i][1].startswith('- ') and lines[i][0] >= indent:
            out[key], i = _list(lines, i, lines[i][0])
        elif i < len(lines) and lines[i][0] > indent:
            out[key], i = _map(lines, i, lines[i][0])
        else:
            out[key] = None
    return out, i


def _list(lines, i, indent):
    out = []
    while i < len(lines) and lines[i][0] == indent and lines[i][1].startswith('- '):
        body = lines[i][1][2:]
        i += 1
        if _KEY.match(body):
            sub = [(indent + 2, body)]
            while i < len(lines) and lines[i][0] > indent:
                sub.append(lines[i])
                i += 1
            item, k = _map(sub, 0, indent + 2)
            if k != len(sub):
                raise ProfileSchemaChanged(f'unreadable list item near {body!r}')
            out.append(item)
        else:
            value, i = _scalar(lines, i, body, indent)
            out.append(value)
    return out, i


def parse_asset(path):
    lines = _lines(path)
    data, i = _map(lines, 0, lines[0][0])
    if i != len(lines):
        raise ProfileSchemaChanged(f'{path}: unread line {lines[i][1]!r}')
    return data


# ---------------------------------------------------------------------- typing against the template
def _typed(value, tmpl, where):
    if isinstance(tmpl, bool):
        if value not in ('0', '1'):
            raise ProfileSchemaChanged(f'{where}: expected 0/1 for a bool, got {value!r}')
        return value == '1'
    if isinstance(tmpl, (int, float)):
        if value is None:
            return 0
        return int(value) if re.match(r'^-?\d+$', value) else float(value)
    if isinstance(tmpl, str):
        return '' if value is None else str(value)
    if isinstance(tmpl, list):
        if value is None:
            return []
        elem = tmpl[0] if tmpl else ''
        return [_typed(v, elem, f'{where}[{k}]') for k, v in enumerate(value)]
    if isinstance(tmpl, dict):
        value = value or {}
        out = {}
        for key, v in value.items():
            if key not in tmpl:
                raise ProfileSchemaChanged(f'{where}.{key} is not in profile-template.json - export a fresh '
                                           f'template from the Editor (see labkit/profiles.py)')
            out[key] = _typed(v, tmpl[key], f'{where}.{key}')
        return out
    raise ProfileSchemaChanged(f'{where}: template type {type(tmpl).__name__} unsupported')


def template():
    with open(PROFILE_TEMPLATE, encoding='utf-8') as f:
        return json.load(f)


def stock(root, name):
    """The profile asset Assets/Data/Bots/<name>.asset under root (a lab's frozen source), as job JSON."""
    return _typed(parse_asset(os.path.join(root, 'Assets', 'Data', 'Bots', f'{name}.asset')), template(), name)


# ---------------------------------------------------------------------- variants
def tweak(p, name=None, **kw):
    """A copy with fields changed. Keys: a profile field, or 'w.<f>' early weights, 'lw.<f>' late weights,
    'bw.<f>' both, 'r.<f>' rewards. Unknown keys are allowed - mirror-only bot fields are new by design."""
    p = copy.deepcopy(p)
    if name:
        p['name'] = name
    for k, v in kw.items():
        if k.startswith('w.'):
            p['weights'][k[2:]] = v
        elif k.startswith('lw.'):
            p['lateWeights'][k[3:]] = v
        elif k.startswith('bw.'):
            p['weights'][k[3:]] = v
            p['lateWeights'][k[3:]] = v
        elif k.startswith('r.'):
            p['rewards'][k[2:]] = v
        else:
            p[k] = v
    return p


def explore(p, name='Explore'):
    """Uniform-random drafting (rewards.exploreRewards = 1): taking vs passing becomes causal, which is
    what card ratings are measured from."""
    return tweak(p, name, **{'r.exploreRewards': 1.0})


def with_ratings(p, ratings, name):
    """A copy drafting by measured ratings ([{card, bonus}], e.g. from ratings.card_ratings)."""
    return tweak(p, name, **{'r.cardRatings': [{'card': r['card'], 'bonus': r['bonus']} for r in ratings]})


def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


def save(p, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(p, f, indent=1, ensure_ascii=False)
