# Builds the headless balance-bot player (Builds/BotPlayer/EscapeTheSpireBot.exe) without touching the
# project you have open.
#
#   powershell -ExecutionPolicy Bypass -File Tools/BotRunner/Build-BotPlayer.ps1
#
# Why a mirror: building a Windows player from the open Editor would switch its active platform (a full
# reimport) and write into the committed Web build profile. Instead this copies Assets, Packages and
# ProjectSettings into a mirror project under %LOCALAPPDATA% and runs a second, batch-mode Unity there,
# which can run while your Editor stays open. The mirror keeps its own Library, so only the first build
# imports everything (slow, and a few GB of disk); later builds reimport just what changed.
#
# Rebuild whenever content or code changed and you want a headless batch to see it - the player plays
# what was built, not what is in Assets now. Batches in the Editor (Tools/Bot/Bot Runner) always use the
# live assets.
[CmdletBinding()]
param(
    [string]$Mirror = (Join-Path $env:LOCALAPPDATA 'GMTK-BotBuild\Project'),
    [string]$Out
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Out) { $Out = Join-Path $repo 'Builds\BotPlayer' }

$Mirror = [System.IO.Path]::GetFullPath($Mirror)
$Out = [System.IO.Path]::GetFullPath($Out)

# robocopy /MIR deletes whatever the source lacks - never let it point into the real project.
if ($Mirror.StartsWith($repo, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The mirror ($Mirror) must live outside the repository ($repo)."
}

$versionLine = Select-String -Path (Join-Path $repo 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion:\s*(.+)$'
$unityVersion = $versionLine.Matches[0].Groups[1].Value.Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"

if (-not (Test-Path $unity)) { throw "Unity $unityVersion not found at $unity" }

New-Item -ItemType Directory -Force -Path $Mirror | Out-Null
New-Item -ItemType Directory -Force -Path $Out | Out-Null

foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Write-Host "Mirroring $folder..."
    & robocopy (Join-Path $repo $folder) (Join-Path $Mirror $folder) /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null

    # robocopy: 0-7 are success (8+ means something failed to copy).
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed mirroring $folder (exit $LASTEXITCODE)" }
}

Set-Content -Path (Join-Path $Mirror 'BotBuildMirror.txt') -Value "Mirror of $repo for Tools/BotRunner/Build-BotPlayer.ps1 - safe to delete." -Encoding utf8

$log = Join-Path $Out 'build.log'
$arguments = @(
    '-batchmode', '-nographics',
    '-projectPath', "`"$Mirror`"",
    '-buildTarget', 'Win64',
    '-executeMethod', 'BotPlayerBuild.BuildFromCommandLine',
    '-botBuildOut', "`"$Out`"",
    '-logFile', "`"$log`""
)

Write-Host "Building with Unity $unityVersion in batch mode (log: $log)..."
$process = Start-Process -FilePath $unity -ArgumentList $arguments -Wait -PassThru -NoNewWindow

if ($process.ExitCode -ne 0) {
    Write-Host "BUILD FAILED (exit $($process.ExitCode)) - see $log"
    exit $process.ExitCode
}

Write-Host "BUILD OK: $(Join-Path $Out 'EscapeTheSpireBot.exe')"
exit 0
