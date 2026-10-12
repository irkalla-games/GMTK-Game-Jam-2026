"""Freeze at kickoff: the lab's own copy of the game, so a session editing the real project mid-lab can never
leak half-written work into a build.

    freeze(lab)               working tree, uncommitted edits included (the default)
    freeze(lab, head=True)    HEAD only - your uncommitted edits stay out

Every build in the lab syncs the mirror from lab.source, never from the live tree. What was frozen is
recorded in lab.json (commit, changed files, sha256 of the uncommitted diff) and the diff itself is saved as
BotRuns/labs/NN-slug/freeze.patch, so the lab can be rebuilt after cleanup deletes the 1.8 GB copy.
"""
import hashlib
import os
import subprocess
import tarfile

from .lab import git, now, TIME
from .paths import REPO, SOURCE_FOLDERS


def freeze(lab, head=False):
    if os.path.exists(lab.source) and os.listdir(lab.source):
        raise SystemExit(f'{lab.source} already exists - a lab freezes once. Delete it to re-freeze.')
    os.makedirs(lab.source, exist_ok=True)
    commit = git('rev-parse', '--short', 'HEAD').strip()
    changed = [line for line in git('status', '--porcelain', '--', *SOURCE_FOLDERS).splitlines() if line.strip()]

    if head:
        proc = subprocess.Popen(['git', '-C', REPO, 'archive', '--format=tar', 'HEAD', *SOURCE_FOLDERS],
                                stdout=subprocess.PIPE)
        with tarfile.open(fileobj=proc.stdout, mode='r|') as t:       # streamed: the archive is ~1.8 GB
            t.extractall(lab.source, filter='data')
        # tarfile stops at the end-of-archive marker, but git still writes its record padding after it.
        # Drain the pipe, or git blocks on a full pipe while we wait for it to exit - a silent deadlock.
        while proc.stdout.read(1 << 16):
            pass
        if proc.wait() != 0:
            raise SystemExit(f'git archive HEAD failed ({proc.returncode})')
        patch = b''
    else:
        for folder in SOURCE_FOLDERS:
            rc = subprocess.run(['robocopy', os.path.join(REPO, folder), os.path.join(lab.source, folder), '/MIR',
                                 '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:2', '/W:2'],
                                stdout=subprocess.DEVNULL).returncode
            if rc >= 8:
                raise SystemExit(f'robocopy {folder} failed ({rc})')
        patch = subprocess.run(['git', '-C', REPO, 'diff', '--binary', 'HEAD', '--', *SOURCE_FOLDERS],
                               capture_output=True, check=True).stdout

    with open(os.path.join(lab.data_dir, 'freeze.patch'), 'wb') as f:
        f.write(patch)
    files = sum(len(fs) for _, _, fs in os.walk(lab.source))
    info = {'mode': 'HEAD' if head else 'working tree', 'commit': commit, 'at': now().strftime(TIME),
            'uncommitted': changed, 'uncommittedIncluded': not head, 'patchSha256': hashlib.sha256(patch).hexdigest()[:16],
            'patchBytes': len(patch), 'files': files}
    lab.update(freeze=info)
    return info
