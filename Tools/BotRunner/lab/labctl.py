"""Command line for the steps /balance-lab runs as single commands. Everything else is labkit from a lab script.

    python Tools/BotRunner/lab/labctl.py preflight
    python Tools/BotRunner/lab/labctl.py new <slug> --topic "Alone cards" --deadline "2026-10-13 09:00" --phases cards
    python Tools/BotRunner/lab/labctl.py freeze <lab> [--head]
    python Tools/BotRunner/lab/labctl.py build <lab> <player> [--definition 07-card-lab-4] [key=value ...]
    python Tools/BotRunner/lab/labctl.py smoke <lab> <player> [--runs 8] [--procs 8]
    python Tools/BotRunner/lab/labctl.py check-defs [--root <folder>]
    python Tools/BotRunner/lab/labctl.py status <lab> | plan <lab> [--tier N] | index
    python Tools/BotRunner/lab/labctl.py cleanup <lab> [--apply] [--force]
    python Tools/BotRunner/lab/labctl.py release <lab>
    python Tools/BotRunner/lab/labctl.py wait <lab>        (queued behind another lab: blocks, then takes the lock)
    python Tools/BotRunner/lab/labctl.py wait-tree <lab> [--quiet 15] [--give-up 6]
                                                           (another session is still editing: blocks until the
                                                            tree is quiet and compiles; exit 1 on give-up)
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding='utf-8')

import labkit as K  # noqa: E402
from labkit.lab import git  # noqa: E402
from labkit.paths import MIRROR_LOCK, SOURCE_FOLDERS  # noqa: E402

DISK_WARN_GB = 20


def preflight(_):
    free = K.disk_free_gb()
    held = K.active_lab()
    procs = K.bot_processes()
    changed = [line for line in git('status', '--porcelain', '--', *SOURCE_FOLDERS).splitlines() if line.strip()]
    prev = K.Lab.previous_brief()
    labs = K.Lab.all()
    print(f'disk free        {free:.1f} GB' + ('   ** UNDER 20 GB - warn the user before starting **' if free < DISK_WARN_GB else ''))
    print(f'running lab      {held["lab"] + " until " + held["deadline"] if held else "none"}')
    print(f'bot players      {len(procs)} running' + ('   ** something is already using the machine **' if procs else ''))
    print(f'mirror lock      {"held: " + open(MIRROR_LOCK).read() if os.path.exists(MIRROR_LOCK) else "free"}')
    print(f'HEAD             {git("rev-parse", "--short", "HEAD").strip()} on {git("branch", "--show-current").strip()}')
    print(f'uncommitted      {len(changed)} file(s) under {", ".join(SOURCE_FOLDERS)}')
    for line in changed[:25]:
        print(f'                 {line}')
    print(f'previous brief   {prev.brief_path if prev else "none"}')
    print(f'next lab number  {max([lab.number for lab in labs] + [0]) + 1:02d}')
    print(f'index page       {K.index.index_url() or "not published yet"}')


def new(a):
    lab = K.Lab.new(a.slug, a.topic, a.deadline, a.phases.split(','), a.reserve)
    try:
        lab.acquire()
    except K.LabBusy as e:
        print(f'WAITING: {e}')
    print(json.dumps({'id': lab.id, 'title': lab.meta['title'], 'topic': lab.meta['topic'],
                      'definitions': lab.def_dir, 'data': lab.data_dir,
                      'brief': lab.brief_path, 'reserveFrom': lab.reserve_start.strftime('%Y-%m-%d %H:%M')}, indent=1))


def freeze(a):
    print(json.dumps(K.freeze(K.Lab.open(a.lab), head=a.head), indent=1))


def build(a):
    cfg = dict(kv.split('=', 1) for kv in a.config)
    print(K.build(K.Lab.open(a.lab), a.player, a.definition, **cfg))


def smoke(a):
    lab = K.Lab.open(a.lab)
    vet = K.profiles.stock(lab.source, 'Veteran')
    out = K.run(lab, f'smoke-{a.player}', [vet, K.profiles.explore(vet)], runs=a.runs, seed=lab.seed('smoke'),
                player=lab.player(a.player), procs=a.procs, timeout_minutes=30)
    K.summary(out)
    entry = next(b for b in lab.ledger()['batches'] if b['name'] == f'smoke-{a.player}')
    rough = entry['runsPerHour'] * 15 / entry['procs'] if entry.get('runsPerHour') else None
    print(f'smoke rate      {rough:.0f} runs/hour at 15 processes (rough: a tiny batch pays start-up)' if rough else '')
    print(f'planning rate   {K.runs_per_hour(lab):.0f} runs/hour at 15 processes (batches of 20+ runs, else 800)')


def check_defs(a):
    bad = 0
    for lab in K.Lab.all():
        if os.path.exists(os.path.join(lab.def_dir, 'definition.py')):
            try:
                print(f'{lab.id:24s} ok, {K.check(lab.id, a.root)} file(s) patch cleanly')
            except (K.PatchFailed, SystemExit) as e:
                bad += 1
                print(f'{lab.id:24s} FAILS: {e}')
    sys.exit(1 if bad else 0)


def status(a):
    path = os.path.join(K.Lab.open(a.lab).data_dir, 'status.md')
    print(open(path, encoding='utf-8').read() if os.path.exists(path) else 'no status.md yet')


def plan(a):
    lab = K.Lab.open(a.lab)
    print(f'reserve from     {lab.reserve_start:%Y-%m-%d %H:%M} (deadline {lab.meta["deadline"]})')
    print(f'throughput       {K.runs_per_hour(lab, a.tier):.0f} runs/hour at 15 processes')
    print(f'fits before it   {K.runs_that_fit(lab, a.tier)} runs')


def index(_):
    print(K.index.build())
    print(f'publish to: {K.index.index_url() or "a new artifact, then save its url with labkit.index.set_index_url"}')


def cleanup(a):
    print(json.dumps(K.cleanup(K.Lab.open(a.lab), dry_run=not a.apply, force=a.force), indent=1))


def release(a):
    K.Lab.open(a.lab).release()
    print('released')


def wait(a):
    """Blocks until no other lab holds the machine, then takes the lock. For a lab queued behind another."""
    import time
    lab = K.Lab.open(a.lab)
    while True:
        held = K.active_lab()
        if not held or held['lab'] == lab.id:
            lab.acquire()
            print(f'{K.now():%H:%M} acquired - {lab.id} may start')
            return
        lab.status('Queued', f'waiting for {held["lab"]} (until {held["deadline"]})')
        time.sleep(60)


def _tree_fingerprint():
    """Every uncommitted path under SOURCE_FOLDERS with its size and mtime - changes whenever anyone edits."""
    out = []
    for line in git('status', '--porcelain', '--', *SOURCE_FOLDERS).splitlines():
        path = line[3:].strip().strip('"').split(' -> ')[-1]
        full = os.path.join(K.REPO, path)
        try:
            st = os.stat(full)
            out.append((path, st.st_size, int(st.st_mtime)))
        except OSError:
            out.append((path, -1, 0))
    return tuple(sorted(out))


def wait_tree(a):
    """Blocks until the working tree has not changed for --quiet minutes AND compiles (Tools/compile-check.ps1),
    for a lab whose source is still being edited by another session. A failing compile restarts the quiet
    clock - the editor may still be mid-change. Exits 1 after --give-up hours, so the brief's pre-decided
    fallback (freeze HEAD, the last compiling state, or stuck + notify) takes over."""
    import subprocess
    import time
    lab = K.Lab.open(a.lab)
    start, last, since = time.time(), _tree_fingerprint(), time.time()
    while True:
        if time.time() - start > a.give_up * 3600:
            lab.status('Stuck', 'waiting for the tree', stuck=f'tree never went quiet and compiling within {a.give_up} h')
            print(f'{K.now():%H:%M} gave up after {a.give_up} h - apply the brief\'s fallback')
            sys.exit(1)
        time.sleep(60)
        fp = _tree_fingerprint()
        if fp != last:
            last, since = fp, time.time()
            lab.status('Queued', f'tree still being edited ({len(fp)} uncommitted file(s)); waiting for '
                                 f'{a.quiet} quiet minutes')
            continue
        if time.time() - since < a.quiet * 60:
            continue
        r = subprocess.run(['powershell', '-ExecutionPolicy', 'Bypass', '-File',
                            os.path.join(K.REPO, 'Tools', 'compile-check.ps1')],
                           capture_output=True, text=True, encoding='utf-8', errors='replace')
        if r.returncode == 0:
            print(f'{K.now():%H:%M} tree quiet for {a.quiet} min and compiles - {lab.id} may freeze')
            return
        tail = '\n'.join((r.stdout + r.stderr).strip().splitlines()[-8:])
        lab.status('Queued', f'tree quiet but does not compile; waiting for another change\n{tail}')
        print(f'{K.now():%H:%M} quiet but not compiling:\n{tail}')
        since = time.time()


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    s = p.add_subparsers(dest='cmd', required=True)
    s.add_parser('preflight').set_defaults(f=preflight)
    n = s.add_parser('new')
    n.add_argument('slug'); n.add_argument('--topic', required=True); n.add_argument('--deadline', required=True)
    n.add_argument('--phases', required=True); n.add_argument('--reserve', type=float, default=2.0)
    n.set_defaults(f=new)
    f = s.add_parser('freeze'); f.add_argument('lab'); f.add_argument('--head', action='store_true'); f.set_defaults(f=freeze)
    b = s.add_parser('build'); b.add_argument('lab'); b.add_argument('player'); b.add_argument('--definition')
    b.add_argument('config', nargs='*'); b.set_defaults(f=build)
    m = s.add_parser('smoke'); m.add_argument('lab'); m.add_argument('player')
    m.add_argument('--runs', type=int, default=8); m.add_argument('--procs', type=int, default=8); m.set_defaults(f=smoke)
    c = s.add_parser('check-defs'); c.add_argument('--root', default=K.REPO); c.set_defaults(f=check_defs)
    st = s.add_parser('status'); st.add_argument('lab'); st.set_defaults(f=status)
    pl = s.add_parser('plan'); pl.add_argument('lab'); pl.add_argument('--tier', type=int); pl.set_defaults(f=plan)
    s.add_parser('index').set_defaults(f=index)
    cl = s.add_parser('cleanup'); cl.add_argument('lab'); cl.add_argument('--apply', action='store_true')
    cl.add_argument('--force', action='store_true'); cl.set_defaults(f=cleanup)
    r = s.add_parser('release'); r.add_argument('lab'); r.set_defaults(f=release)
    w = s.add_parser('wait'); w.add_argument('lab'); w.set_defaults(f=wait)
    wt = s.add_parser('wait-tree'); wt.add_argument('lab'); wt.add_argument('--quiet', type=int, default=15)
    wt.add_argument('--give-up', type=float, default=6.0); wt.set_defaults(f=wait_tree)
    a = p.parse_args()
    a.f(a)


if __name__ == '__main__':
    main()
