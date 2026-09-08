<#
.SYNOPSIS
    Builds Docs/BossDesign.xlsx from the boss prefabs under Assets/Prefabs/Bosses and their decks.

.DESCRIPTION
    A forwarder. All the logic lives in Tools/EnemySheet/Export-EnemySheet.ps1, which builds either
    roster workbook depending on the -Domain it is given; everything that differs between them is one
    entry in Get-RosterDomain (Tools/EnemySheet/EnemySheet.Common.psm1). Bosses and ordinary enemies
    have an identical schema - the same Character component, the same tab layout, the same merge
    columns - so a second copy of that ~2,600 lines would only be a second place for a bug to be fixed.

    What this tool DOES own is its own baseline.json, which is the whole point of the split: the boss
    workbook and the enemy workbook record where they last agreed with Unity independently, so a boss
    retune and an enemy retune can never conflict with or overwrite each other.

    Two things are boss-specific and worth knowing about:

      - The PowerLevel tab is a read-only mirror of Docs/EnemySheets.xlsx's. Boss power is only
        meaningful on the same scale as enemy power, so the weights are authored in one place.
      - Seven of the eight bosses summon a plain enemy, whose tab belongs to the enemy workbook. Those
        appear on a read-only Summoned tab here, carrying only the pow_* cell a boss tab's Summon Power
        formula needs. They deliberately have no GUID row, so this tool can never write them.

.PARAMETER WorkbookPath
    Defaults to Docs/BossDesign.xlsx at the repo root.

.PARAMETER NoCsvMirror
    Skip writing Docs/BossDesign/*.csv.

.PARAMETER WriteBaseline
    Record the state both sides now agree on into Tools/BossSheet/baseline.json.

.PARAMETER Force
    Rebuild the sheet even though it holds edits that have not reached the prefabs, discarding them.

.PARAMETER SeedPreservedFrom
    One-time migration: also pull Brandon's Power Level and Notes for the boss tabs out of the given
    workbook. Used once when the bosses were split out of Docs/EnemySheets.xlsx.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/BossSheet/Export-BossSheet.ps1
#>
[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [switch]$NoCsvMirror,
    [switch]$WriteBaseline,
    [switch]$Force,
    [string]$SeedPreservedFrom
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$forward = @{} + $PSBoundParameters
$forward['Domain'] = 'Bosses'

& (Join-Path $PSScriptRoot '..\EnemySheet\Export-EnemySheet.ps1') @forward
