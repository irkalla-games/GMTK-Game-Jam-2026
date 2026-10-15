"""Where everything lives. Every other labkit module takes its paths from here."""
import os
import re

KIT = os.path.dirname(os.path.abspath(__file__))                 # Tools/BotRunner/lab/labkit
TOOLS = os.path.dirname(KIT)                                     # Tools/BotRunner/lab (committed)
REPO = os.path.abspath(os.path.join(TOOLS, '..', '..', '..'))

LABS_DEF = os.path.join(TOOLS, 'labs')                           # committed: brief, lab.json, definitions
BOTRUNS = os.path.join(REPO, 'BotRuns')
LABS_DATA = os.path.join(BOTRUNS, 'labs')                        # gitignored: source, players, batches, status

BASE_JOB = os.path.join(TOOLS, 'base-job.json')
PROFILE_TEMPLATE = os.path.join(TOOLS, 'profile-template.json')
INDEX_JSON = os.path.join(TOOLS, 'index.json')
STYLE = os.path.join(KIT, 'report_style.html')
RUN_BATCH = os.path.join(REPO, 'Tools', 'BotRunner', 'Run-BotBatch.ps1')

ACTIVE_LOCK = os.path.join(LABS_DATA, 'active-lab.json')
MIRROR_LOCK = os.path.join(LABS_DATA, 'mirror.lock')
MIRROR = os.path.join(os.environ.get('LOCALAPPDATA', os.path.expanduser('~')), 'GMTK-BotBuild', 'Project')

# robocopy /MIR deletes whatever the source lacks - the mirror must never point into the real project.
assert not os.path.normcase(MIRROR).startswith(os.path.normcase(REPO)), 'mirror inside the repository'

SOURCE_FOLDERS = ('Assets', 'Packages', 'ProjectSettings')


def unity_exe(root=REPO):
    """The Unity editor matching root's ProjectVersion.txt."""
    text = open(os.path.join(root, 'ProjectSettings', 'ProjectVersion.txt'), encoding='utf-8').read()
    version = re.search(r'^m_EditorVersion:\s*(\S+)', text, re.M).group(1)
    exe = rf'C:\Program Files\Unity\Hub\Editor\{version}\Editor\Unity.exe'
    if not os.path.exists(exe):
        raise SystemExit(f'Unity {version} not found at {exe}')
    return exe
