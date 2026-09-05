---
description: Arm a one-shot timer that runs a task later in this same session
argument-hint: <duration> <task or /command>
allowed-tools: PowerShell, CronCreate
---

Arm a delayed task. **Do the minimum and stop.** The user is running low on session usage;
every token spent here is token they wanted spent on the task itself.

Input: `$ARGUMENTS` — a leading duration token (`3h`, `90m`, `2h30m`, `45`, `1d`), then the task.

## Do exactly this, in order

1. Split `$ARGUMENTS` at the first space: `<duration>` and `<task>`.

2. Run this once to turn the duration into a cron expression (substitute `<duration>` for `3h`):

```
$d='3h'; $m=0; if($d -match '(\d+)\s*d'){$m+=[int]$Matches[1]*1440}; if($d -match '(\d+)\s*h'){$m+=[int]$Matches[1]*60}; if($d -match '(\d+)\s*m'){$m+=[int]$Matches[1]}; if($d -match '^\d+$'){$m=[int]$d}; $t=(Get-Date).AddMinutes($m); '{0} {1} {2} {3} * | fires {4}' -f $t.Minute,$t.Hour,$t.Day,$t.Month,$t.ToString('ddd HH:mm')
```

3. Call `CronCreate` with the cron expression it printed, `recurring: false`, and `prompt` set to
   `<task>` **verbatim** — do not rewrite, expand, or add context to it. If `<task>` is a slash
   command, pass it exactly as typed.

4. Reply with **one line**: the task, the fire time, and the job id. Nothing else.

## Do not

- Do not read files, search the repo, plan the task, or start any part of it now. The whole point
  is that the work happens at fire time, not now.
- Do not ask clarifying questions about the task — it is not your task yet. The only thing that can
  block you is an unparseable duration; if the script prints nothing sane, say so in one line.
- Do not restate these instructions or explain how the timer works. The user already knows.
