[CmdletBinding()]
param(
  # This repo is the C# track alone (US-T69); -Track stays so older command lines keep working.
  [ValidateSet('CSharp')][string]$Track = 'CSharp',
  # How many runs (each a fresh process); the write-up takes medians.
  [int]$Runs = 3,
  # Where the results go: <Out>/<track>-<n>.json.
  [string]$Out = '',
  # Run the editor binaries (UnrealEditor -game) instead of the package in Builds/<Track>.
  [switch]$Editor,
  # The packaged game's folder (default Builds/<Track>/Windows, the last tools/package.ps1 output).
  [string]$PackageDir = '',
  [int]$ResX = 1600, [int]$ResY = 900,
  [string]$EngineVersion = '5.8'
)

<#
.SYNOPSIS
The race's in-game run (US-T46, docs/RACE.md): starts the packaged game (Builds/<Track>/Windows/Unreal.SpaceGod.<Track>.exe)
with -SpaceGodRace, a scripted tour at a fixed start and seed (New Game, Sol idle, a staged battle, three saves and loads,
a year at the highest warp, the trip to Alpha Centauri), every frame 1/60 s of game time (-UseFixedTimeStep -FPS=60)
with no frame cap or VSync, and the shared frame sampler (SpaceGodRender's SpaceGodRace.cpp) writing one JSON per run.
The script waits for a quiet machine (no compiler, UE build or other UE game running) before each run and records what
else was running, and the time from launching the exe to the process ending.

.EXAMPLE
  ./tools/race.ps1 -Track Cpp -Runs 3
  ./tools/race.ps1 -Track CSharp -Runs 3 -Out Builds/Race/game
#>

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/engine.ps1"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $repo 'Builds\Race\game' }
# US-T60: the game writes -SpaceGodRaceOut relative to its own exe folder, so a relative -Out recorded nothing; resolve it
# against this shell's current directory first.
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
New-Item -ItemType Directory -Force $Out | Out-Null
$heavy = '^(cl|link|UnrealEditor.*|UnrealBuildTool|ShaderCompileWorker|MSBuild|UnrealPak|UnrealSpaceGod.*|Unreal\.SpaceGod.*|Bench\..*)$'
$gameArgs = @('-windowed', "-ResX=$ResX", "-ResY=$ResY", '-nosound', '-unattended', '-nosplash', '-UseFixedTimeStep', '-FPS=60', '-SpaceGodRace')

# The rest of the machine's CPU over a few seconds, % of all logical CPUs (other agents share this machine).
function Get-OthersCpuPct([int]$Seconds = 5) {
  $a = @{}; foreach ($p in Get-Process) { if ($p.CPU) { $a[$p.Id] = $p.CPU } }
  Start-Sleep -Seconds $Seconds
  $sum = 0.0
  foreach ($p in Get-Process) { if ($p.Id -ne $PID -and $p.CPU) { $sum += $p.CPU - $(if ($a.ContainsKey($p.Id)) { $a[$p.Id] } else { 0 }) } }
  [Math]::Round(100.0 * $sum / $Seconds / [Environment]::ProcessorCount, 1)
}

for ($n = 1; $n -le $Runs; $n++) {
  # A quiet machine first: the rest of it under 8% of the CPUs (at most 10 minutes of waiting).
  $t0 = Get-Date
  do { $others = Get-OthersCpuPct } while ($others -ge 8 -and ((Get-Date) - $t0).TotalMinutes -lt 10)
  $busy = "others $others% after $([int]((Get-Date) - $t0).TotalSeconds) s; " + ((Get-Process | Where-Object { $_.ProcessName -match $heavy } | ForEach-Object ProcessName | Sort-Object -Unique) -join ',')
  $t0 = Get-Date
  $file = Join-Path $Out ("{0}-{1}{2}.json" -f $Track.ToLowerInvariant(), $(if ($Editor) { 'editor-' } else { '' }), $n)
  Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
  $runArgs = $gameArgs + @("-SpaceGodRaceOut=$file")
  if ($Editor) {
    $project = Get-ChildItem -LiteralPath (Join-Path $repo $Track) -Filter '*.uproject' | Select-Object -First 1
    $exe = Join-Path (Find-UnrealEngine $EngineVersion) 'Engine\Binaries\Win64\UnrealEditor.exe'
    $runArgs = @("`"$($project.FullName)`"", '/Engine/Maps/Entry', '-game', '-log') + $runArgs
  } else {
    $dir = if ($PackageDir) { $PackageDir } else { Join-Path $repo "Builds\$Track\Windows" }
    $exe = Join-Path $dir "Unreal.SpaceGod.$Track.exe"
  }
  if ($Track -eq 'CSharp' -and $Editor) {
    . "$PSScriptRoot/dotnet-sdk.ps1"
    Use-UnrealSharpDotNet -PrivateRoot (Join-Path $project.DirectoryName 'Intermediate\DotNet')
  }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Start-Process -FilePath $exe -ArgumentList $runArgs -PassThru
  # The launcher starts the real exe and returns; wait for every process of the package (or the editor) to end.
  $deadline = (Get-Date).AddMinutes(30)
  Start-Sleep -Seconds 3
  [long]$peakWs = 0
  do {
    Start-Sleep -Milliseconds 500
    $game = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(UnrealSpaceGod|UnrealSpaceGodCSharp)(-Win64-Shipping)?$|^UnrealEditor$' -and $_.StartTime -ge $t0 })
    foreach ($g in $game) { $peakWs = [Math]::Max($peakWs, $g.PeakWorkingSet64) }
  } while (($game.Count -gt 0 -or -not $p.HasExited) -and (Get-Date) -lt $deadline)
  $wall = $sw.Elapsed.TotalSeconds
  if (-not (Test-Path -LiteralPath $file)) { Write-Warning "run $n wrote no results ($file)"; continue }
  $j = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
  $j | Add-Member -NotePropertyName harness -NotePropertyValue ([ordered]@{
      launchToExitSeconds = [Math]::Round($wall, 2); busyBefore = $busy; peakWorkingSetBytesExternal = $peakWs; startedUtc = $t0.ToUniversalTime().ToString('o') })
  $j | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $file -Encoding utf8
  "{0} run {1}: {2:N1} s, {3} phases -> {4}" -f $Track, $n, $wall, @($j.phases).Count, $file
}
