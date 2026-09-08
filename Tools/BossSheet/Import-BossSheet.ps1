<#
.SYNOPSIS
    Diffs Docs/BossDesign.xlsx against the boss prefabs and writes Tools/BossSheet/bosses.json.

.DESCRIPTION
    A forwarder into Tools/EnemySheet/Import-EnemySheet.ps1 with -Domain Bosses - see
    Export-BossSheet.ps1's own comment for why the two rosters share one engine and what each tool
    still owns separately.

    Does NOT touch the Unity project. It only produces the work order; Assets/Editor/
    BossSheetImporter.cs reads that file and writes the prefabs.

.PARAMETER WorkbookPath
    Defaults to Docs/BossDesign.xlsx at the repo root.

.PARAMETER OutputPath
    Defaults to Tools/BossSheet/bosses.json.

.PARAMETER FromCsv
    Force reading the Docs/BossDesign/*.csv mirror even though the workbook is readable.

.PARAMETER OnlyPrefab
    Only consider prefabs whose name matches one of these wildcard patterns.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/BossSheet/Import-BossSheet.ps1
#>
[CmdletBinding()]
param(
    [string]$WorkbookPath,
    [string]$OutputPath,
    [switch]$FromCsv,
    [string[]]$OnlyPrefab
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$forward = @{} + $PSBoundParameters
$forward['Domain'] = 'Bosses'

& (Join-Path $PSScriptRoot '..\EnemySheet\Import-EnemySheet.ps1') @forward
