"""A lab: one balance session from kickoff to report.

Two folders per lab, both named NN-slug:
  Tools/BotRunner/lab/labs/NN-slug/   committed  - lab.json, brief.md, definition.py + overlay/, profiles, ratings
  BotRuns/labs/NN-slug/               gitignored - source/ (frozen copy), players/, batches/, jobs/, scripts,
                                                   status.md, ledger.json

Only one lab runs at a time: BotRuns/labs/active-lab.json says which. A lock is stale - and is cleared by
the next kickoff - once its lab is past deadline + 1 hour with no bot player running, so a session that
died does not block the next one forever.
"""
import ctypes
import datetime as dt
import json
import os
import re
import shutil
import subprocess

from .paths import LABS_DEF, LABS_DATA, ACTIVE_LOCK, REPO

TIME = '%Y-%m-%d %H:%M'


def now():
    return dt.datetime.now().replace(second=0, microsecond=0)


def parse_time(s):
    return dt.datetime.strptime(s, TIME)


def _read(path, default=None):
    if not os.path.exists(path):
        return default
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


def _write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
    os.replace(tmp, path)


class LabBusy(Exception):
    pass


class Lab:
    def __init__(self, lab_id):
        if not re.match(r'^\d\d-[a-z0-9-]+$', lab_id):
            raise ValueError(f'lab id must be NN-slug, got {lab_id!r}')
        self.id = lab_id
        self.number = int(lab_id[:2])
        self.def_dir = os.path.join(LABS_DEF, lab_id)
        self.data_dir = os.path.join(LABS_DATA, lab_id)

    def __repr__(self):
        return f'Lab({self.id!r})'

    # ------------------------------------------------------------------ creation and lookup
    @staticmethod
    def new(slug, topic, deadline, phases, reserve_hours=2.0):
        """A new lab numbered after every lab so far. deadline is local 'YYYY-MM-DD HH:MM'."""
        parse_time(deadline)
        number = max([int(d[:2]) for d in os.listdir(LABS_DEF) if re.match(r'^\d\d-', d)] + [0]) + 1
        lab = Lab(f'{number:02d}-{slug}')
        os.makedirs(lab.def_dir)
        for sub in ('batches', 'jobs', 'players', 'scripts'):
            os.makedirs(os.path.join(lab.data_dir, sub), exist_ok=True)
        lab.meta = {'id': lab.id, 'number': number, 'title': f'Spire Lab {number:02d}: {topic}',
                    'created': now().strftime(TIME), 'deadline': deadline, 'reserveHours': reserve_hours,
                    'phases': list(phases), 'state': 'running', 'url': None, 'headline': {}, 'freeze': None}
        return lab

    @staticmethod
    def open(lab_id):
        lab = Lab(lab_id)
        if not os.path.exists(os.path.join(lab.def_dir, 'lab.json')):
            raise SystemExit(f'no lab {lab_id} (looked for {lab.def_dir}\\lab.json)')
        return lab

    @staticmethod
    def all():
        return [Lab(d) for d in sorted(os.listdir(LABS_DEF)) if re.match(r'^\d\d-', d)
                and os.path.exists(os.path.join(LABS_DEF, d, 'lab.json'))]

    @staticmethod
    def previous_brief():
        """The most recent lab that has a brief.md - the source of the "(Same as last time)" answers."""
        for lab in reversed(Lab.all()):
            if os.path.exists(lab.brief_path):
                return lab
        return None

    # ------------------------------------------------------------------ files
    @property
    def meta(self):
        return _read(os.path.join(self.def_dir, 'lab.json'), {})

    @meta.setter
    def meta(self, value):
        _write(os.path.join(self.def_dir, 'lab.json'), value)

    def update(self, **fields):
        m = self.meta
        m.update(fields)
        self.meta = m
        return m

    @property
    def brief_path(self):
        return os.path.join(self.def_dir, 'brief.md')

    @property
    def source(self):
        return os.path.join(self.data_dir, 'source')

    def player(self, name):
        return os.path.join(self.data_dir, 'players', name, 'EscapeTheSpireBot.exe')

    def batch_dir(self, label):
        return os.path.join(self.data_dir, 'batches', label)

    # ------------------------------------------------------------------ deadline
    @property
    def deadline(self):
        return parse_time(self.meta['deadline'])

    @property
    def reserve_start(self):
        """When the reserve begins: replication and the report only, no new rounds."""
        return self.deadline - dt.timedelta(hours=self.meta.get('reserveHours', 2.0))

    # ------------------------------------------------------------------ seeds
    def seed(self, block):
        """The base seed for a named block. The same name always gives the same seed (pair arms on it);
        a new name gives a base seed no earlier block used (replicate on it). A run's seed is a hash of
        (base seed, run index) - BotSeeds.RunSeed - so distinct base seeds never share runs. Lab NN's blocks
        are NN * 100000 + 1000 * k, so labs never reuse each other's either (past labs used 1000-61000)."""
        ledger = self.ledger()
        blocks = ledger.setdefault('seedBlocks', {})
        if block not in blocks:
            blocks[block] = self.number * 100000 + 1000 * len(blocks)
            self._save_ledger(ledger)
        return blocks[block]

    # ------------------------------------------------------------------ ledger and status
    def ledger(self):
        return _read(os.path.join(self.data_dir, 'ledger.json'), {'builds': [], 'batches': [], 'seedBlocks': {}})

    def _save_ledger(self, ledger):
        _write(os.path.join(self.data_dir, 'ledger.json'), ledger)

    def record(self, kind, entry):
        ledger = self.ledger()
        items = ledger.setdefault(kind, [])
        items[:] = [e for e in items if e.get('name') != entry.get('name')] + [entry]
        self._save_ledger(ledger)

    def status(self, phase, doing, done=(), next_up=None, eta=None, stuck=None):
        """Overwrites status.md - what a check-in from your phone reads."""
        lines = [f'# {self.meta.get("title", self.id)}', '',
                 f'Updated {now().strftime(TIME)} · deadline {self.meta.get("deadline")} · '
                 f'reserve from {self.reserve_start.strftime("%H:%M")}', '']
        if stuck:
            lines += [f'**STUCK:** {stuck}', '']
        lines += [f'**Phase:** {phase}', f'**Now:** {doing}']
        if eta:
            lines.append(f'**Finishes about:** {eta.strftime("%H:%M") if hasattr(eta, "strftime") else eta}')
        if next_up:
            lines.append(f'**Next:** {next_up}')
        if done:
            lines += ['', '## Done', *[f'- {d}' for d in done]]
        os.makedirs(self.data_dir, exist_ok=True)
        with open(os.path.join(self.data_dir, 'status.md'), 'w', encoding='utf-8') as f:
            f.write('\n'.join(lines) + '\n')

    # ------------------------------------------------------------------ the one-lab-at-a-time lock
    def acquire(self):
        held = active_lab()
        if held and held['lab'] != self.id:
            raise LabBusy(f"{held['lab']} is running until {held['deadline']}; this lab waits for it")
        _write(ACTIVE_LOCK, {'lab': self.id, 'deadline': self.meta['deadline'], 'since': now().strftime(TIME)})

    def release(self):
        held = _read(ACTIVE_LOCK)
        if held and held.get('lab') == self.id:
            os.remove(ACTIVE_LOCK)
        self.update(state='done')
        self.status('Done', 'lab finished' + (f' - report {self.meta["url"]}' if self.meta.get('url') else ''))


def active_lab():
    """The running lab's lock, or None. A stale lock (deadline + 1h passed, no bot player running) is
    removed and reported as None."""
    held = _read(ACTIVE_LOCK)
    if not held:
        return None
    if now() > parse_time(held['deadline']) + dt.timedelta(hours=1) and not bot_processes():
        os.remove(ACTIVE_LOCK)
        return None
    return held


# ---------------------------------------------------------------------- machine state
def bot_processes():
    """PIDs of running headless bot players (any lab, or a batch started by hand)."""
    out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq EscapeTheSpireBot.exe', '/FO', 'CSV', '/NH'],
                         capture_output=True, text=True).stdout
    return [int(line.split('","')[1]) for line in out.splitlines() if line.startswith('"EscapeTheSpireBot')]


def pid_alive(pid):
    """True while the process exists. Never os.kill(pid, 0) on Windows - that terminates it."""
    PROCESS_QUERY_LIMITED_INFORMATION, STILL_ACTIVE = 0x1000, 259
    k = ctypes.windll.kernel32
    h = k.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, int(pid))
    if not h:
        return False
    try:
        code = ctypes.c_ulong()
        return bool(k.GetExitCodeProcess(h, ctypes.byref(code))) and code.value == STILL_ACTIVE
    finally:
        k.CloseHandle(h)


def disk_free_gb(path=REPO):
    return shutil.disk_usage(path).free / 1024 ** 3


def git(*args):
    r = subprocess.run(['git', '-C', REPO, *args], capture_output=True, text=True, encoding='utf-8')
    if r.returncode != 0:
        raise SystemExit(f'git {" ".join(args)} failed: {r.stderr.strip()}')
    return r.stdout
