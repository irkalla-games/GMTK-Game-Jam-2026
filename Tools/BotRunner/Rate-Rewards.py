"""
Measures what each reward (card or equipment) is worth to a run, from balance-bot batches.

    python Tools/BotRunner/Rate-Rewards.py BotRuns/<batch> [BotRuns/<batch> ...] [--json ratings.json] [--csv out.csv]

Every reward screen a run met is one trial: for each card or item on offer, did taking it go with the run
getting further than passing on it? "Further" is levels cleared after the screen's own level, so an offer
on level 1 and an offer on level 5 are both judged by what came next, not by how far the run had already
got. A batch whose profile has exploreRewards = 1 picks uniformly at random, which is what makes the
difference causal rather than "the bot takes X when it is already winning" - use one for ratings.

Output, per name: offers, takes, the mean progress after taking vs passing, and the difference shrunk
towards 0 by n / (n + prior) so a card seen five times cannot top the table. The --json list is
BotRewardPrefs.cardRatings' shape ([{"card": name, "bonus": levels}]); a profile's ratingWeight turns
levels into pick value.
"""
import argparse
import csv
import glob
import json
import os
import sys
from collections import defaultdict


def runs_in(batch):
    for path in glob.glob(os.path.join(batch, "runs", "*", "run.json")):
        with open(path, encoding="utf-8-sig") as f:
            run = json.load(f)
        if run.get("baseline") or run.get("outcome") not in ("Won", "Died"):
            continue
        yield run


def offered_names(reward):
    names = []
    for part in reward.get("offered", "").split(" / "):
        part = part.strip()
        # Skip buttons ("Skip - Heal 5", "Remove a card") are not things to rate.
        if not part or part.startswith("Skip") or part.startswith("Remove") or part.startswith("Upgrade"):
            continue
        names.append(part)
    return names


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("batches", nargs="+")
    parser.add_argument("--json", help="write cardRatings-shaped JSON here")
    parser.add_argument("--csv", help="write the full table here")
    parser.add_argument("--prior", type=float, default=30.0, help="shrinkage: offers worth of evidence for 0")
    parser.add_argument("--min-offers", type=int, default=20)
    args = parser.parse_args()

    took = defaultdict(list)
    passed = defaultdict(list)
    runs = 0

    for batch in args.batches:
        for run in runs_in(batch):
            runs += 1
            cleared = run["levelsCleared"]
            for reward in run.get("rewards", []):
                if reward.get("kind") not in ("card", "equipment", "skip", "decline"):
                    continue
                names = offered_names(reward)
                if not names:
                    continue
                after = cleared - (reward["level"] - 1)
                for name in names:
                    (took if name == reward.get("choice") else passed)[name].append(after)

    if runs == 0:
        sys.exit("No finished runs found in " + ", ".join(args.batches))

    rows = []
    for name in set(took) | set(passed):
        t, p = took.get(name, []), passed.get(name, [])
        n = len(t) + len(p)
        if n < args.min_offers or not t or not p:
            continue
        mt, mp = sum(t) / len(t), sum(p) / len(p)
        shrink = len(t) / (len(t) + args.prior)
        rows.append({"card": name, "offers": n, "takes": len(t), "after_take": round(mt, 3),
                     "after_pass": round(mp, 3), "diff": round(mt - mp, 3), "bonus": round((mt - mp) * shrink, 3)})

    rows.sort(key=lambda r: -r["bonus"])

    print(f"{runs} runs. bonus = (progress after taking - after passing), shrunk by takes/(takes+{args.prior:g})")
    print(f"{'reward':26s} {'offers':>6s} {'takes':>6s} {'take':>6s} {'pass':>6s} {'diff':>6s} {'bonus':>6s}")
    for r in rows:
        print(f"{r['card']:26s} {r['offers']:6d} {r['takes']:6d} {r['after_take']:6.2f} {r['after_pass']:6.2f} "
              f"{r['diff']:+6.2f} {r['bonus']:+6.2f}")

    if args.csv:
        with open(args.csv, "w", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            writer.writerows(rows)

    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump([{"card": r["card"], "bonus": r["bonus"]} for r in rows], f, indent=1)


if __name__ == "__main__":
    main()
