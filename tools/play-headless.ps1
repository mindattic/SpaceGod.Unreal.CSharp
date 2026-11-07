<#
.SYNOPSIS
  Run this game headless (no window, no sound) for scripted/automated testing, through the shared MindAtticShared plugin.
.DESCRIPTION
  Calls ..\Unreal.Shared\tools\Start-Headless.ps1 (the shared launch contract: --headless -RenderOffScreen -unattended
  -nosplash -nosound) on the editor binaries. The plugin logs MINDATTIC_HEADLESS_READY <ProjectName> once the game is up.
  Extra arguments go to the game unchanged. Needs the editor target built (tools/build.ps1).
#>
[CmdletBinding()]
param([switch]$Wait, [Parameter(ValueFromRemainingArguments)][string[]]$GameArgs)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\CSharp\UnrealSpaceGodCSharp.uproject'
$shared = Join-Path $PSScriptRoot '..\..\Unreal.Shared\tools\Start-Headless.ps1'
if (-not (Test-Path -LiteralPath $shared)) { throw "Unreal.Shared is not cloned next to this repo ($shared)." }
# -PassThru by name, so the game's own -flags never bind to the launcher's parameters (-log would bind -LogPath).
& $shared -Project $project -Wait:$Wait -PassThru $GameArgs
