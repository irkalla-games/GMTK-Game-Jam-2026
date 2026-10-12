# Balance labs

The toolkit behind the `/balance-lab` skill (`.claude/skills/balance-lab/`). A lab is one multi-hour balance
session with the headless bot: questions at kickoff, unattended batches, a report.

- `labctl.py`: single steps (`preflight`, `new`, `freeze`, `build`, `smoke`, `check-defs`, `status`, `plan`,
  `index`, `cleanup`, `release`). Run it with `--help`.
- `labkit/`: the library lab scripts import. Each module's docstring is its manual.
- `labs/NN-slug/`: one folder per lab. It holds `lab.json` (title, report URL, headline numbers) and `brief.md`
  (the kickoff answers). A lab that made mirror-only content also has `definition.py` and `overlay/`, which a
  later lab can build on. Labs 01–07 ran before the skill existed and were backfilled. Card Labs II–IV's
  definitions are byte-identical to the scripts they came from.
- `base-job.json`, `profile-template.json`: job defaults, and the profile type map.
- `index.json`: the published URL of the index page listing every lab.

Run data lives in `BotRuns/labs/NN-slug/` (gitignored). Nothing here writes the real `Assets/`. Builds happen in
the mirror at `%LOCALAPPDATA%\GMTK-BotBuild\Project`, synced from each lab's frozen copy of the game.
