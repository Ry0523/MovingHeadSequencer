<#
.SYNOPSIS
Compatibility launcher for the cross-platform MovingHeadSequencer .NET application.

.DESCRIPTION
Provides a PowerShell command surface while forwarding all work to the structured
.NET 8 console application in tools/MovingHeadSequencer.

.PARAMETER Target
Use All to generate every bundled sequence definition.

.PARAMETER SequencePath
Path to one supported .xsq sequence. The song metadata selects its choreography definition.
#>

[CmdletBinding()]
param(
    [ValidateSet('All')]
    [string]$Target,

    [string]$SequencePath,

    [ValidateSet(4, 6, 8)]
    [int]$HeadCount = 4,

    [string]$RgbEffectsPath,

    [string]$MovingHeadGroupName,

    [switch]$InspectLayout,

    [switch]$AllowLayoutWarnings,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $PSScriptRoot 'MovingHeadSequencer\MovingHeadSequencer.csproj'
$arguments = @(
    'run',
    '--project', $projectPath,
    '--',
    '--head-count', $HeadCount,
    '--workspace-root', $workspaceRoot
)

if (-not $InspectLayout) {
    $hasTarget = -not [string]::IsNullOrWhiteSpace($Target)
    $hasSequencePath = -not [string]::IsNullOrWhiteSpace($SequencePath)
    if ($hasTarget -eq $hasSequencePath) {
        throw 'Choose exactly one generation mode: -Target All or -SequencePath <file.xsq>.'
    }
}

if (-not [string]::IsNullOrWhiteSpace($Target)) {
    $arguments += @('--target', $Target)
}
if (-not [string]::IsNullOrWhiteSpace($SequencePath)) {
    $arguments += @('--sequence-path', $SequencePath)
}
if (-not [string]::IsNullOrWhiteSpace($RgbEffectsPath)) {
    $arguments += @('--rgb-effects-path', $RgbEffectsPath)
}
if (-not [string]::IsNullOrWhiteSpace($MovingHeadGroupName)) {
    $arguments += @('--moving-head-group-name', $MovingHeadGroupName)
}
if ($InspectLayout) {
    $arguments += '--inspect-layout'
}
if ($AllowLayoutWarnings) {
    $arguments += '--allow-layout-warnings'
}
if ($Force) {
    $arguments += '--force'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "MovingHeadSequencer exited with code $LASTEXITCODE."
}
