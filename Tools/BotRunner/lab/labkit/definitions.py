"""A lab's mirror-only content, committed so a later lab can start from it after BotRuns/ is cleaned.

Tools/BotRunner/lab/labs/NN-slug/definition.py is a plain module with:

    OVERLAY = os.path.join(HERE, 'overlay')   # optional: files copied over the mirror (new C#, a card builder)
    METHOD = 'CardLab4Builder.BuildFromCommandLine'   # optional: build entry, default BotPlayerBuild's
    CONFIG_PATH = 'Assets/Editor/CardLab/config.txt'  # optional: where config() is written in the mirror
    def config(**over) -> str                 # optional: the builder's config text
    def patches(root) -> dict                 # optional: text patches against the frozen source at `root`

A later lab builds on an earlier one by loading it - load('07-card-lab-4') - and merging its patches and
config into its own definition, exactly as each card lab has built on the last.
"""
import importlib.util
import os

from .paths import LABS_DEF

_cache = {}


def load(lab_id):
    if lab_id not in _cache:
        path = os.path.join(LABS_DEF, lab_id, 'definition.py')
        if not os.path.exists(path):
            raise SystemExit(f'{lab_id} has no definition.py ({path})')
        spec = importlib.util.spec_from_file_location('labdef_' + lab_id.replace('-', '_'), path)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        _cache[lab_id] = mod
    return _cache[lab_id]


def resolve(definition):
    """A definition given as a lab id, a module, or None."""
    return load(definition) if isinstance(definition, str) else definition
