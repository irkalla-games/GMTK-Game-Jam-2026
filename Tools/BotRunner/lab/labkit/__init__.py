"""labkit - the shared toolkit behind /balance-lab (.claude/skills/balance-lab). Lab scripts start with:

    import sys; sys.path.insert(0, r'<repo>\\Tools\\BotRunner\\lab')
    from labkit import *

Modules: lab (folders, lock, status, seeds, ledger), freeze, build, patches, definitions, profiles, batch,
ratings, plan, report, index, cleanup. Each module's docstring is its manual.
"""
from .paths import REPO, LABS_DEF, LABS_DATA, MIRROR
from .lab import Lab, LabBusy, active_lab, bot_processes, disk_free_gb, now, parse_time
from .freeze import freeze
from .build import build, check, export_patch, BuildFailed
from .patches import merge, enemy_patch, totem_hp, one_hp_totems_to, PatchFailed
from .definitions import load as load_definition
from . import profiles
from .batch import (KC, run, runs, levels, paired, pool, summary, is_complete, progress, IncompleteBatch,
                    BatchFailed)
from .ratings import rate, pool_rounds, card_ratings
from .plan import runs_per_hour, estimate_hours, can_start, runs_that_fit
from .report import page, section, table, cards, dots, paras, est, chip, fmt, Raw
from . import index
from .cleanup import cleanup
