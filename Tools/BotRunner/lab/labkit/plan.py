"""Sizing rounds against the deadline.

    rate = runs_per_hour(lab, tier=3)        # measured from this lab's finished batches
    ok, eta = can_start(lab, runs=1500, tier=3)
    if not ok: go to the reserve - replicate and write the report

The last reserveHours (default 2) before the deadline belong to the fresh-seed replication and the report.
A batch starts only if it is expected to finish before the reserve begins, padded by 15% - a batch cut short
by the deadline cannot be quoted, so starting one is pure waste.
"""
import datetime as dt
import statistics

from .lab import now

PAD = 1.15
# Card Lab IV, 15 processes, Knight + Cleric: 811-1447 runs/hour (short batches lose more to start-up; Hard III
# was no slower). The low end, used only until this lab's own smoke batch has been measured.
FALLBACK_RUNS_PER_HOUR = 800.0


def runs_per_hour(lab, tier=None, procs=15):
    """Median measured throughput, scaled to `procs`. Prefers batches of the same tier: harder tiers are
    slower per run. Falls back to any tier, then to FALLBACK_RUNS_PER_HOUR."""
    done = [b for b in lab.ledger().get('batches', []) if b.get('complete') and b.get('runsPerHour')
            and b.get('runs', 0) >= 20]
    same = [b for b in done if tier is None or b.get('tier') == tier]
    pick = same or done
    if not pick:
        return FALLBACK_RUNS_PER_HOUR * procs / 15
    return statistics.median(b['runsPerHour'] * procs / b['procs'] for b in pick)


def estimate_hours(lab, runs, tier=None, procs=15):
    return runs / runs_per_hour(lab, tier, procs) * PAD


def can_start(lab, runs, tier=None, procs=15):
    """(ok, expected finish). ok is False when the batch would run into the reserve."""
    eta = now() + dt.timedelta(hours=estimate_hours(lab, runs, tier, procs))
    return eta <= lab.reserve_start, eta


def runs_that_fit(lab, tier=None, procs=15):
    """How many runs fit between now and the reserve."""
    hours = (lab.reserve_start - now()).total_seconds() / 3600
    return max(0, int(hours * runs_per_hour(lab, tier, procs) / PAD))
