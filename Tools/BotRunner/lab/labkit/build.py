"""Builds a headless bot player from the lab's frozen source, in the one shared mirror, under a lock.

    exe = build(lab, 'base')                                   # the frozen game as it is
    exe = build(lab, 'I3', '07-card-lab-4', alone='3')         # a definition, with config overrides
    exe = build(lab, 'v1', extra=enemy_patch(lab.source, 'Skull', power=2.5))

Each build re-syncs the mirror from lab.source (robocopy /MIR, which also undoes the previous build's
overlay and patches), copies the definition's overlay, writes its config, applies its patches, then runs a
batch-mode Unity there. The player lands in BotRuns/labs/NN-slug/players/<name>/. A failed build raises
BuildFailed carrying the first compiler errors from the log.

check(definition, root) applies a definition's patches in memory only - no mirror, no Unity - to prove
they still match the code before an overnight build depends on them.
"""
import json
import os
import re
import subprocess
import time

from . import definitions
from .lab import pid_alive, now, TIME
from .patches import merge, apply, PatchFailed
from .paths import MIRROR, MIRROR_LOCK, SOURCE_FOLDERS, unity_exe

DEFAULT_METHOD = 'BotPlayerBuild.BuildFromCommandLine'


class BuildFailed(Exception):
    pass


class _MirrorLock:
    def __init__(self, lab, wait_minutes=60):
        self.lab, self.wait = lab, wait_minutes

    def __enter__(self):
        deadline = time.time() + self.wait * 60
        while True:
            held = None
            if os.path.exists(MIRROR_LOCK):
                try:
                    held = json.load(open(MIRROR_LOCK, encoding='utf-8'))
                except (OSError, ValueError):
                    held = None
            if not held or not pid_alive(held.get('pid', 0)):
                break
            if time.time() > deadline:
                raise BuildFailed(f"mirror held by {held.get('lab')} (pid {held.get('pid')}) for over {self.wait} min")
            time.sleep(30)
        os.makedirs(os.path.dirname(MIRROR_LOCK), exist_ok=True)
        with open(MIRROR_LOCK, 'w', encoding='utf-8') as f:
            json.dump({'pid': os.getpid(), 'lab': self.lab.id, 'since': now().strftime(TIME)}, f)

    def __exit__(self, *exc):
        try:
            os.remove(MIRROR_LOCK)
        except OSError:
            pass


def _robocopy(src, dst, mirror):
    rc = subprocess.run(['robocopy', src, dst, '/MIR' if mirror else '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP',
                         '/R:2', '/W:2'], stdout=subprocess.DEVNULL).returncode
    if rc >= 8:
        raise BuildFailed(f'robocopy {src} -> {dst} failed ({rc})')


def _patch_set(defn, root, extra):
    return merge(defn.patches(root) if defn is not None and hasattr(defn, 'patches') else {}, extra or {})


def build(lab, name, definition=None, extra=None, **config):
    defn = definitions.resolve(definition)
    if not os.path.isdir(os.path.join(lab.source, 'Assets')):
        raise BuildFailed(f'{lab.id} has no frozen source - freeze(lab) first')
    out = os.path.join(lab.data_dir, 'players', name)
    os.makedirs(out, exist_ok=True)
    method = getattr(defn, 'METHOD', DEFAULT_METHOD) if defn is not None else DEFAULT_METHOD
    patches = _patch_set(defn, lab.source, extra)
    started = time.time()

    with _MirrorLock(lab):
        for folder in SOURCE_FOLDERS:
            _robocopy(os.path.join(lab.source, folder), os.path.join(MIRROR, folder), mirror=True)
        with open(os.path.join(MIRROR, 'BotBuildMirror.txt'), 'w', encoding='utf-8') as f:
            f.write(f'Bot build mirror, last synced from {lab.source} - safe to delete.\n')
        overlay = getattr(defn, 'OVERLAY', None) if defn is not None else None
        if overlay:
            _robocopy(overlay, MIRROR, mirror=False)
        if defn is not None and getattr(defn, 'CONFIG_PATH', None):
            with open(os.path.join(MIRROR, defn.CONFIG_PATH), 'w', encoding='utf-8', newline='\n') as f:
                f.write(defn.config(**config))
        elif config:
            raise BuildFailed(f'config {sorted(config)} given but the definition has no CONFIG_PATH')

        def read(rel):
            with open(os.path.join(MIRROR, rel), encoding='utf-8', newline='') as f:
                return f.read()

        def write(rel, text):
            with open(os.path.join(MIRROR, rel), 'w', encoding='utf-8', newline='') as f:
                f.write(text)

        try:
            apply(patches, read, write)
        except PatchFailed as e:
            raise BuildFailed(f'build {name}: a patch no longer matches the frozen code - {e}') from e
        log = os.path.join(out, 'build.log')
        subprocess.run([unity_exe(lab.source), '-batchmode', '-nographics', '-projectPath', MIRROR,
                        '-buildTarget', 'Win64', '-executeMethod', method, '-botBuildOut', out, '-logFile', log])

    text = open(log, encoding='utf-8', errors='replace').read() if os.path.exists(log) else ''
    minutes = round((time.time() - started) / 60, 1)
    ok = 'BotPlayerBuild: Succeeded' in text and os.path.exists(os.path.join(out, 'EscapeTheSpireBot.exe'))
    entry = {'name': name, 'definition': getattr(defn, '__name__', None), 'config': config, 'method': method,
             'patchedFiles': len(patches), 'minutes': minutes, 'ok': ok, 'at': now().strftime(TIME)}
    lab.record('builds', entry)
    if not ok:
        errors = sorted(set(re.findall(r'^.*error CS\d+.*$', text, re.M)))[:10]
        raise BuildFailed(f'build {name} failed ({minutes} min), see {log}' +
                          ('\n' + '\n'.join(errors) if errors else ''))
    with open(os.path.join(out, 'variant.txt'), 'w', encoding='utf-8') as f:
        f.write(json.dumps({k: v for k, v in entry.items() if k != 'ok'}, indent=1) + '\n')
        for rel, subs in patches.items():
            for sub in subs:
                f.write(f'{rel}: {sub[0][:120]!r} -> {sub[1][:120]!r}\n')
    return os.path.join(out, 'EscapeTheSpireBot.exe')


def export_patch(definition, root, out_path, prefixes=('Assets/',), extra=None):
    """Writes what a definition changes under `prefixes` - patched files and new overlay files - as a git
    patch against root (the lab's frozen source), for the handoff or `git apply` on the real tree. Default
    prefixes take everything under Assets/; pass ('Assets/Scripts/Bot/',) for just the bot. Returns the
    files it covers. Check it with: git apply --check <out_path>."""
    import difflib
    defn = definitions.resolve(definition)
    overlay = getattr(defn, 'OVERLAY', None)
    after = {}

    def read(rel):
        if rel in after:
            return after[rel]
        for base in ([overlay] if overlay else []) + [root]:
            path = os.path.join(base, rel)
            if os.path.exists(path):
                with open(path, encoding='utf-8', newline='') as f:
                    return f.read()
        raise PatchFailed(f'patch target missing: {rel}')

    apply(_patch_set(defn, root, extra), read, lambda rel, text: after.__setitem__(rel, text))
    if overlay:
        for d, _, fs in os.walk(overlay):
            for name in fs:
                rel = os.path.relpath(os.path.join(d, name), overlay).replace(os.sep, '/')
                if rel not in after and not rel.endswith('config.txt'):
                    with open(os.path.join(d, name), encoding='utf-8', newline='') as f:
                        after[rel] = f.read()
    chunks, files = [], []
    for rel in sorted(after):
        if not rel.startswith(tuple(prefixes)):
            continue
        base_path = os.path.join(root, rel)
        before = open(base_path, encoding='utf-8', newline='').read() if os.path.exists(base_path) else None
        if before == after[rel]:
            continue
        old = (before or '').splitlines(keepends=True)
        new = after[rel].splitlines(keepends=True)
        head = f'diff --git a/{rel} b/{rel}\n' + ('new file mode 100644\n' if before is None else '')
        body = []
        for line in difflib.unified_diff(old, new, '/dev/null' if before is None else f'a/{rel}', f'b/{rel}'):
            body.append(line if line.endswith('\n') else line + '\n\\ No newline at end of file\n')
        chunks.append(head + ''.join(body))
        files.append(rel)
    with open(out_path, 'w', encoding='utf-8', newline='') as f:
        f.write(''.join(chunks))
    return files


def check(definition, root, extra=None):
    """Applies a definition's patches to an in-memory copy of root (overlay files win), touching nothing.
    Returns the number of files patched; raises on any patch that no longer matches."""
    defn = definitions.resolve(definition)
    overlay = getattr(defn, 'OVERLAY', None)

    def read(rel):
        for base in ([overlay] if overlay else []) + [root]:
            path = os.path.join(base, rel)
            if os.path.exists(path):
                with open(path, encoding='utf-8', newline='') as f:
                    return f.read()
        raise PatchFailed(f'patch target missing: {rel}')

    patches = _patch_set(defn, root, extra)
    apply(patches, read, lambda rel, text: None)
    return len(patches)
