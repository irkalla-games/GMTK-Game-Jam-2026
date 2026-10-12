"""Text patches applied IN THE BUILD MIRROR ONLY - the repository and the lab's frozen source are never written.

A patch set is {relative path: [(old, new), (old, new, count), ...]}. `old` must occur exactly `count` times
(default 1) in the file, so a patch that no longer matches the code fails loudly instead of building the
unpatched game. Content helpers read current values from the lab's frozen source (`root`), never the live
tree, so they patch exactly what was frozen.
"""
import glob
import os
import re


class PatchFailed(Exception):
    pass


def merge(*patch_sets):
    out = {}
    for p in patch_sets:
        for rel, subs in (p or {}).items():
            out.setdefault(rel, []).extend(subs)
    return out


def apply(patches, read, write):
    """Applies every patch through read(rel) -> text and write(rel, text). Raises on any count mismatch."""
    for rel, subs in patches.items():
        text = read(rel)
        for sub in subs:
            old, new, want = (tuple(sub) + (1,))[:3]
            k = text.count(old)
            if k != want:
                raise PatchFailed(f'{rel}: {old[:80]!r} occurs {k} times, expected {want}')
            text = text.replace(old, new)
        write(rel, text)


def _text(root, rel):
    with open(os.path.join(root, rel), encoding='utf-8', newline='') as f:
        return f.read()


def _health_subs(text, health):
    nl = '\r\n' if '\r\n' in text else '\n'
    h = re.search(r'\n  maxHealth: (\S+)', text).group(1)
    return [(f'{nl}  maxHealth: {h}{nl}', f'{nl}  maxHealth: {health}{nl}'),
            (f'<Health>k__BackingField: {h}{nl}', f'<Health>k__BackingField: {health}{nl}')]


def enemy_patch(root, prefab, health=None, power=None, folder='Enemies'):
    """An enemy (or boss: folder='Bosses') prefab's maxHealth - and its serialized Health copy - and/or
    powerLevel."""
    rel = f'Assets/Prefabs/{folder}/{prefab}.prefab'
    text = _text(root, rel)
    nl = '\r\n' if '\r\n' in text else '\n'
    subs = _health_subs(text, health) if health is not None else []
    if power is not None:
        p = re.search(r'\n  powerLevel: (\S+)', text).group(1)
        subs.append((f'{nl}  powerLevel: {p}{nl}', f'{nl}  powerLevel: {power}{nl}'))
    return {rel: subs}


def totem_hp(root, names, health):
    """Named totem prefabs (Assets/Prefabs/Totem/<name>.prefab) set to `health`."""
    return {f'Assets/Prefabs/Totem/{n}.prefab': _health_subs(_text(root, f'Assets/Prefabs/Totem/{n}.prefab'), health)
            for n in names}


def one_hp_totems_to(root, health):
    """Every totem prefab currently at 1 health raised to `health`."""
    out = {}
    for path in glob.glob(os.path.join(root, 'Assets', 'Prefabs', 'Totem', '*.prefab')):
        text = _text(root, os.path.relpath(path, root))
        nl = '\r\n' if '\r\n' in text else '\n'
        if f'{nl}  maxHealth: 1{nl}' in text:
            out[os.path.relpath(path, root).replace(os.sep, '/')] = _health_subs(text, health)
    return out
