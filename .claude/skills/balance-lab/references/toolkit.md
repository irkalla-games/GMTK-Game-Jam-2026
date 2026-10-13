# Toolkit: labkit and labctl

`Tools/BotRunner/lab/` (committed). Each module's docstring is its full manual; this page is the map.

## Folders

```
Tools/BotRunner/lab/
  labctl.py                 single steps: preflight, new, freeze, build, smoke, check-defs, status, plan, index, cleanup, release, wait, wait-tree
  labkit/                   the library lab scripts import
  base-job.json             job defaults (campaign, limits); batch.make_job fills in the rest
  profile-template.json     an Editor-exported profile: the type map profiles.stock() checks against
  index.json                the index page's published URL
  labs/NN-slug/             COMMITTED per lab: lab.json, brief.md, definition.py, overlay/, ratings and profiles
BotRuns/labs/               gitignored
  active-lab.json           the one-lab-at-a-time lock
  mirror.lock               held while a build uses the shared mirror
  index.html                the rendered index page
  NN-slug/
    source/                 the frozen copy (about 1.8 GB, deleted by cleanup)
    freeze.patch            the uncommitted diff that was frozen
    players/<name>/         bot players (deleted by cleanup)
    batches/<label>/        batch output: report.md, CSVs, runs/*/run.json (+ turns.log, events.jsonl)
    jobs/  scripts/         job files; this lab's round scripts and their .out logs
    status.md  ledger.json  report.html
```

## Lab scripts

Copy `assets/chain-template.py` to `BotRuns/labs/NN-slug/scripts/<round>.py`. It puts labkit on the path from
the script's own location, opens the lab, prints one timestamped line per step, and ends with `ROUND DONE`, or
with `STUCK` (after `lab.status(..., stuck=...)`) and exit 1. Launch and watch:

```
Bash (run_in_background):  python BotRuns/labs/NN-slug/scripts/r1.py > BotRuns/labs/NN-slug/scripts/r1.out 2>&1
Monitor (optional):         tail -f r1.out | grep --line-buffered -E "STUCK|Traceback|ROUND DONE"
```

The harness re-invokes you when the background command exits. Read the `.out`, then plan the next round.

## API by job

| Job | Call |
|---|---|
| Open the lab | `lab = Lab.open('08-alone-cards')`; `lab.meta`, `lab.update(**fields)`, `lab.source`, `lab.player(name)` |
| Seeds | `lab.seed('eval-t0')`: same name gives the same seed; a new name gives a fresh one |
| Status | `lab.status(phase, doing, done=[...], next_up=..., eta=..., stuck=None)` overwrites status.md |
| Profiles | `vet = profiles.stock(lab.source, 'Veteran')`, `profiles.explore(vet)`, `profiles.with_ratings(vet, rows, 'Veteran08')`, `profiles.tweak(vet, 'Name', **{'w.kill': 6, 'r.takeCardsMinValue': 4, 'searchDepth': 8})`, `profiles.save/load` |
| Build | `exe = build(lab, 'name', definition=None or 'NN-slug' or module, extra=patches, **config)` raises `BuildFailed` with the compiler errors |
| Patches | `merge(...)`, `enemy_patch(lab.source, 'Skull', health=12, power=2.5)`, `totem_hp(lab.source, names, hp)`, `one_hp_totems_to(lab.source, hp)` |
| Check a definition | `check('07-card-lab-4', lab.source)` applies its patches in memory and raises `PatchFailed` on any mismatch |
| Bot or content patch for the handoff | `export_patch(defn, lab.source, path, prefixes=('Assets/Scripts/Bot/',))`, then `git apply --check path` |
| Run a batch | `out = run(lab, 'label', [profiles], runs=250, seed=lab.seed('eval-t0'), party=KC, tier=0, player=exe, procs=15)` blocks until complete; a finished label returns at once; it resumes once after a crash |
| Is it safe to start | `ok, eta = can_start(lab, runs, tier)`; `runs_that_fit(lab, tier)`; `runs_per_hour(lab, tier)` |
| Read results | `levels(out)` gives {profile: n, levels, se, byRun}; `paired(a, pa, b, pb)` gives (diff, se, n); `pool((m, se), (m, se))`; `summary(out)` prints a table |
| Ratings | `rate([outs], profile='Explore')`, `pool_rounds([tables])`, `agreement(t_a, t_b, names=)` (do the cards differ? before naming winners), `card_ratings(table)` |
| Raw runs | `runs(out)` (Won/Died only) yields run.json dicts: `rewards`, `cards`, `enemies`, `levels`, `falls`, `killers`, ... and `_dir` for its folder's events.jsonl |
| Report | `page(lab, lede=, tiles=, verdict=, decisions=, assumptions=, sections=[section(...)], method=)` with `table`, `cards`, `dots`, `paras`, `est`, `chip`; `report_text(path)` for the read-back before publishing |
| Index | `index.build()`, `index.index_url()`, `index.set_index_url(url)` |
| Cleanup | `cleanup(lab, dry_run=True)` returns what it would delete; `dry_run=False` deletes and gzips the shard logs |

## Definitions

`Tools/BotRunner/lab/labs/NN-slug/definition.py` holds a lab's mirror-only content: `OVERLAY` (files copied over
the mirror), `METHOD` (the build entry, e.g. a card builder), `CONFIG_PATH` + `config(**over)` (the builder's
settings), and `patches(root)` (exact-match text patches against the frozen source). Past labs are loadable:

| Lab | What `definition.config()` builds by default |
|---|---|
| `05-card-lab-2` | Card Lab II's **L4** pool: wave 3, clears 40/50/10, Shield Wall, Heavy Slash, Rally the Line and Communion out, Rare totems at 10 HP |
| `06-card-lab-3` | L4 plus 16 adjacency cards at round R3's numbers (wave 5) with Rally the Line and Communion back in, plus the `formation` bot weight |
| `07-card-lab-4` | L4 minus every next-to card, plus 4 taunts and 12 "alone" cards at alone = 3 and the T round's numbers |

They patch commit `da64f785` cleanly. `labctl.py check-defs` tells you whether they still apply to the working
tree, or to `--root <a lab's source>`. Run it at kickoff whenever the brief starts from a past lab. Once the
real game gains a mechanic a definition patches in (the conditional card cost, for instance), its patches stop
matching. They fail loudly, which is the point. The lab then either freezes from a commit where they still
apply, or writes its own definition against the new code: often simpler, because the mechanic now exists and
only the cards need building. Each definition's `original/` folder keeps the claude-tools scripts it was
copied from.

## Failure modes already met

- **A patch fails** with "occurs 0 times": the code moved since the definition was written. Fix the patch's
  `old` text against `lab.source`, never by editing the frozen copy.
- **The build fails** with no `error CS` lines: open `players/<name>/build.log`. Unity 6000.5 sometimes
  crashes on exit *after* a good build; `build()` already trusts the `BotPlayerBuild: Succeeded` line over
  the exit code.
- **A batch exits 4** (a player crashed or timed out): `run()` resumes once. If the batch is still incomplete,
  it raises `BatchFailed`. The finished runs are kept, and re-running the same label resumes again.
- **A profile asset has a new field**: `ProfileSchemaChanged`. Re-export `profile-template.json` from the
  Editor (the command is in the `labkit/profiles.py` docstring), and only while the user is there.
- **PowerShell from Python**: `run()` calls `Run-BotBatch.ps1` through `powershell -ExecutionPolicy Bypass`,
  and its output goes to `batches/<label>/runner.log`.
