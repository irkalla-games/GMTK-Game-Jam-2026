# Runs a balance-bot batch in the background with the headless player, several processes at once, then
# merges every run into one report.
#
#   powershell -ExecutionPolicy Bypass -File Tools/BotRunner/Run-BotBatch.ps1 -Job my.job.json -Processes 4
#
# The job file is what Tools/Bot/Bot Runner's "Save job..." writes: campaign, party, tier, profiles (by
# value), runs per profile, seed. Build the player first with Build-BotPlayer.ps1 - it plays the content
# it was built from.
#
# Each process plays every Nth run (-botShard i -botShards N). Seeds depend only on the base seed and the
# run index, so how a batch is split never changes its results. Re-running with the same -Out resumes:
# runs that already finished are kept.
#
# Exit codes: 0 all runs finished cleanly, 2 some run errored or stalled, 3 the job was rejected,
# 4 a player process crashed or timed out.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Job,
    [int]$Processes = 4,
    [string]$Out,
    [string]$Player,
    [int]$TimeoutMinutes = 240
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Player) { $Player = Join-Path $repo 'Builds\BotPlayer\EscapeTheSpireBot.exe' }
if (-not (Test-Path $Player)) { throw "No bot player at $Player - run Tools/BotRunner/Build-BotPlayer.ps1 first." }
if (-not (Test-Path $Job)) { throw "No job file at $Job" }

$jobData = Get-Content -Raw -Path $Job | ConvertFrom-Json

if (-not $Out) {
    $label = if ($jobData.label) { $jobData.label } else { 'headless' }
    $Out = Join-Path $repo ("BotRuns\" + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$label-headless")
}

$Out = [System.IO.Path]::GetFullPath($Out)
$logs = Join-Path $Out 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

# The batch's own copy of the job, stamped the way the Editor stamps one, so report.md can say what
# produced it and a replay can warn when the project has moved on.
$commit = (& git -C $repo rev-parse --short HEAD 2>$null)
$dirty = [bool](& git -C $repo status --porcelain 2>$null)
$jobData.batchId = Split-Path -Leaf $Out
$jobData.outputDir = $Out
$jobData.commit = if ($commit) { "$commit".Trim() } else { 'unknown' }
$jobData.dirtyTree = $dirty
$jobData.createdUtc = (Get-Date).ToUniversalTime().ToString('o')
$jobPath = Join-Path $Out 'job.json'
$jobData | ConvertTo-Json -Depth 20 | Set-Content -Path $jobPath -Encoding utf8

Write-Host "Batch: $Out"
Write-Host "Starting $Processes player process(es)..."

$running = @()

for ($i = 0; $i -lt $Processes; $i++) {
    $arguments = @(
        '-batchmode', '-nographics',
        '-botJob', "`"$jobPath`"",
        '-botOut', "`"$Out`"",
        '-botShard', "$i",
        '-botShards', "$Processes",
        '-logFile', "`"$(Join-Path $logs "shard-$i.log")`""
    )

    $running += Start-Process -FilePath $Player -ArgumentList $arguments -PassThru -WindowStyle Hidden
}

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$crashed = $false

while ($true) {
    Start-Sleep -Seconds 10

    $done = 0
    $total = 0

    foreach ($file in Get-ChildItem -Path $Out -Filter 'status-shard-*.json' -ErrorAction SilentlyContinue) {
        try {
            $status = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json
            $done += [int]$status.runsDone
            $total += [int]$status.runsTotal
        }
        catch { }
    }

    $alive = @($running | Where-Object { -not $_.HasExited })
    Write-Host ("  {0}/{1} runs finished, {2} process(es) running" -f $done, $total, $alive.Count)

    if ($alive.Count -eq 0) { break }

    if ((Get-Date) -gt $deadline) {
        Write-Host "Timed out after $TimeoutMinutes minutes - stopping the remaining players (finished runs are kept)."
        foreach ($p in $alive) { try { $p.Kill() } catch { } }
        $crashed = $true
        break
    }
}

$worst = 0

foreach ($p in $running) {
    $code = 0
    try { $code = $p.ExitCode } catch { $code = 4 }

    if ($code -eq 3) { Write-Host "A player rejected the job - see $logs"; exit 3 }
    if ($code -gt 3 -or $code -lt 0) { $crashed = $true }
    if ($code -gt $worst -and $code -le 3) { $worst = $code }
}

Write-Host "Merging runs into report.md..."
$report = Start-Process -FilePath $Player -ArgumentList @('-batchmode', '-nographics', '-botReport', "`"$Out`"", '-logFile', "`"$(Join-Path $logs 'report.log')`"") -Wait -PassThru -WindowStyle Hidden

if ($report.ExitCode -ne 0) { Write-Host "Report step failed (exit $($report.ExitCode)) - see $logs\report.log" }

Write-Host "Done: $(Join-Path $Out 'report.md')"

if ($crashed) { exit 4 }
exit $worst
