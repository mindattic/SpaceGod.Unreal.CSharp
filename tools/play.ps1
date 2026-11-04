[CmdletBinding()]
param(
  [ValidateSet('solo', 'host', 'join', 'smoke', 'shots', 'views', 'viewshot', 'demo', 'fidelity', 'coop-smoke', 'audit')][string]$Mode = 'solo',
  [string]$CaptureDir = '',
  [string]$Address = '127.0.0.1',
  # This repo is the C# track alone (US-T69); -Track stays so older command lines keep working.
  [ValidateSet('CSharp')][string]$Track = 'CSharp',
  [string]$EngineVersion = '5.8',
  # -Mode coop-smoke only (US-T64): run a packaged game's exe (tools/package.ps1's folder, e.g. Builds/CSharp/Windows, Development or
  # Shipping) instead of the editor binaries.
  [string]$PackageDir = ''
)

<#
.SYNOPSIS
Run SpaceGod.Unreal.CSharp in game mode from the editor binaries: solo, as the co-op host, or joining a host.
Build it first with tools/build.ps1. (-Track CSharp is the only track here; the C++ port is SpaceGod.Unreal.Cpp.)
-Mode smoke runs headless (-nullrhi): the UI tour (every screen, clicking the real buttons) and the app loop with a
scripted keyboard and mouse (warp key, camera move, selection, save/load round trip), logged under LogSpaceGod, then
quits; the log is <Track>/Saved/Logs/<project>.log. Both tracks log the same lines, so the two can be compared.
-Mode views / viewshot / shots likewise run the same checks on either track; images are named <track>-*.png
(cpp-*.png for Cpp, csharp-*.png for CSharp).

.EXAMPLE
  ./tools/play.ps1 -Mode host              # listen server on port 7777
  ./tools/play.ps1 -Mode join -Address 192.168.1.20
  ./tools/play.ps1 -Track CSharp
  ./tools/play.ps1 -Track CSharp -Mode smoke
  ./tools/play.ps1 -Track CSharp -Mode shots   # windowed: the UI's screenshots into docs/images
  ./tools/play.ps1 -Track CSharp -Mode views   # headless: Sol's views (US-T27) built for a frame, counts logged
  ./tools/play.ps1 -Track CSharp -Mode demo     # windowed: the -demo scene tour, every stop captured (US-T40)
  ./tools/play.ps1 -Track CSharp -Mode viewshot # windowed: those views on screen, docs/images/csharp-views-sol.png
  ./tools/play.ps1 -Track CSharp -Mode host      # co-op (US-T39): a listen server, player 1
  ./tools/play.ps1 -Track CSharp -Mode join -Address 192.168.1.20
  ./tools/play.ps1 -Track CSharp -Mode coop-smoke # headless: a host and a client on localhost, their digests compared
  ./tools/play.ps1 -Track CSharp -Mode coop-smoke -PackageDir Builds/CSharp/Windows # the C# track's from a package (US-T67)
  ./tools/play.ps1 -Track CSharp -Mode audit     # headless: the smoke, then a probe per binding, PASS/FAIL each (US-T48)
#>

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/engine.ps1"
$repo = Split-Path -Parent $PSScriptRoot
$project = Get-ChildItem -LiteralPath (Join-Path $repo $Track) -Filter '*.uproject' | Select-Object -First 1
if (-not $project) { throw "No .uproject in $Track/." }
$engine = Find-UnrealEngine $EngineVersion
# Images are named after the track (cpp-ui-hud.png, csharp-ui-hud.png) so the two can be compared side by side.
$prefix = $Track.ToLowerInvariant()

if ($Track -eq 'CSharp') {
  if (-not (Test-Path -LiteralPath (Join-Path $project.DirectoryName 'Binaries\Managed\net10.0\UserCode.LoadOrder.json'))) {
    throw 'The C# code is not built yet: run ./tools/build.ps1 -Track CSharp first.'
  }
  # The editor binaries rebuild stale C# through UnrealSharp's UAT commands, which need a usable .NET SDK.
  . "$PSScriptRoot/dotnet-sdk.ps1"
  Use-UnrealSharpDotNet -PrivateRoot (Join-Path $project.DirectoryName 'Intermediate\DotNet')
}

if ($Mode -eq 'shots') {
  # -Mode shots: a window and the UI's screenshot tour (main menu, HUD, Help, galaxy map, market + cargo, pause menu),
  # each saved as docs/images/<track>-ui-*.png; the game quits when done.
  # US-T45: deterministic, so the two tracks' shots can be diffed (tools/imgdiff.py): both start the game at the same
  # fixed moment with a seeded UnityEngine.Random, and every frame is 1/60 s of game time (-UseFixedTimeStep -FPS=60),
  # however long it took to draw.
  $editor = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor.exe'
  $images = Join-Path $repo 'docs\images'
  $p = Start-Process -FilePath $editor -PassThru -Wait -ArgumentList @("`"$($project.FullName)`"", '/Engine/Maps/Entry', '-game',
    '-windowed', '-ResX=1600', '-ResY=900', '-nosound', '-unattended', '-log', '-UseFixedTimeStep', '-FPS=60', '-SpaceGodUiShots', "`"-capturedir=$images`"")
  Get-ChildItem -LiteralPath $images -Filter "$prefix-ui-*.png" -ErrorAction SilentlyContinue | ForEach-Object FullName
  exit $p.ExitCode
}

if ($Mode -eq 'fidelity') {
  # -Mode fidelity (CSharp, US-T40): a window and FidelityShots (asteroid belt, Kenney craft, model viewer, spawn-grid
  # thumbnails), each saved as docs/images/csharp-fidelity-*.png (or -CaptureDir), frame times logged; then quits.
  # -Track Cpp (US-T53): the model viewer's shot, docs/images/cpp-fidelity-model-viewer.png (Smoke/SpaceGodSmoke.cpp).
  $editor = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor.exe'
  if (-not $CaptureDir) { $CaptureDir = Join-Path $repo 'docs\images' }
  $p = Start-Process -FilePath $editor -PassThru -Wait -ArgumentList @("`"$($project.FullName)`"", '/Engine/Maps/Entry', '-game',
    '-windowed', '-ResX=1600', '-ResY=900', '-nosound', '-unattended', '-log', '-SpaceGodFidelityShots', "`"-capturedir=$CaptureDir`"")
  Get-ChildItem -LiteralPath $CaptureDir -Filter "$prefix-fidelity-*.png" -ErrorAction SilentlyContinue | ForEach-Object FullName
  exit $p.ExitCode
}

if ($Mode -eq 'demo') {
  # -Mode demo (either track: US-T40 C#, US-T58 C++): Unity.SpaceGod's -demo SceneTour (CaptureRunner) end to end in a
  # window, -capture saving every stop as <Track>/Saved/Screenshots/Tour/<stop>.png (or -CaptureDir); the game quits
  # when the tour ends.
  $editor = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor.exe'
  if (-not $CaptureDir) { $CaptureDir = Join-Path $project.DirectoryName 'Saved\Screenshots\Tour' }
  $t0 = Get-Date
  $p = Start-Process -FilePath $editor -PassThru -Wait -ArgumentList @("`"$($project.FullName)`"", '/Engine/Maps/Entry', '-game',
    '-windowed', '-ResX=1600', '-ResY=900', '-nosound', '-unattended', '-log', '-demo', '-capture', "`"-capturedir=$CaptureDir`"")
  $shots = @(Get-ChildItem -LiteralPath $CaptureDir -Filter '*.png' -ErrorAction SilentlyContinue | Where-Object LastWriteTime -ge $t0)
  $shots | ForEach-Object FullName
  "demo ($Track): $($shots.Count) tour stops captured"
  exit $p.ExitCode
}

if ($Mode -eq 'views' -or $Mode -eq 'viewshot') {
  # US-T27: the ported views without the app (ASpaceGameMode.RunViews / ViewsSmoke): Sol's bodies, sky, starfield,
  # interstellar stars, every ship's model, an orbit line; one frame headless (-Mode views) or a window that saves a
  # screenshot (-Mode viewshot).
  if ($Mode -eq 'views') {
    $cmd = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
    & $cmd $project.FullName '/Engine/Maps/Entry' -game -nullrhi -nosound -unattended -nosplash -stdout -FullStdOutLogOutput -SpaceGodViews |
      Select-String -Pattern 'LogSpaceGod|Error:|Fatal' | ForEach-Object Line
    exit $LASTEXITCODE
  }
  $editor = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor.exe'
  $shot = Join-Path $repo "docs\images\$prefix-views-sol.png"
  $p = Start-Process -FilePath $editor -PassThru -Wait -ArgumentList @("`"$($project.FullName)`"", '/Engine/Maps/Entry', '-game',
    '-windowed', '-ResX=1600', '-ResY=900', '-nosound', '-unattended', '-log', "`"-SpaceGodShot=$shot`"")
  if (Test-Path -LiteralPath $shot) { $shot }
  exit $p.ExitCode
}

# US-T39 co-op: the host's save goes to each client that joins (a few MB) over reliable RPCs; at Unreal's default rates
# (about 100 KB/s a client) that takes a minute, so the host and its clients raise them (no Config/*.ini change).
$coopNet = '-ini:Engine:[/Script/Engine.Player]:ConfiguredInternetSpeed=100000000,[/Script/Engine.Player]:ConfiguredLanSpeed=100000000,' +
  '[/Script/OnlineSubsystemUtils.IpNetDriver]:MaxClientRate=100000000,[/Script/OnlineSubsystemUtils.IpNetDriver]:MaxInternetClientRate=100000000'

if ($Mode -eq 'coop-smoke') {
  # -Mode coop-smoke (either track: US-T39 C#, US-T44 C++): two headless instances on localhost, a listen server (-SpaceGodCoopSmoke=host) and a
  # client (-SpaceGodCoopSmoke=client). The client joins and loads the host's save, presses '.' (warp) and '3' (select),
  # takes a station's seat through the picker, keeps it through a deliberate resync, flies a freighter's helm, trades at
  # Earth, takes a fighter's cockpit in a fight and uses its keys, takes the party to another star and has the host load
  # its quicksave (US-T43 on the C# track, US-T44 on the C++ track's), and says bye; both log a digest of the shared state every
  # 60 frames and this script compares them. Logs: <Track>/Saved/Logs/CoopSmokeHost-<run>.log and CoopSmokeClient-<run>.log.
  # US-T47: each run has its own log names and a free UDP port of its own, and every instance it started is stopped on any
  # way out (try/finally), so a host left over from an earlier run can neither be joined nor hold the logs.
  # US-T64: -PackageDir runs a package's own game exe (not its launcher, which returns at once) with no project argument.
  # A Shipping build logs nothing, so each instance also writes its co-op lines to a file of their own
  # (-SpaceGodCoopLog=CoopSmoke<Host|Client>-<run>.coop.log; the C# track's since US-T67) and the checks read those. A Shipping
  # build also ignores -ini: overrides; both tracks' net rates are in <Track>/Config/DefaultEngine.ini.
  $cmd = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
  $projectArg = @("`"$($project.FullName)`"")
  $logs = Join-Path $project.DirectoryName 'Saved\Logs'
  if ($PackageDir) {
    $package = (Resolve-Path -LiteralPath $PackageDir).Path
    $cmd = Get-ChildItem -LiteralPath (Join-Path $package "$($project.BaseName)\Binaries\Win64") -Filter "$($project.BaseName)*.exe" -ErrorAction SilentlyContinue |
      Where-Object Name -match "^$([regex]::Escape($project.BaseName))(-Win64-(Shipping|Test|DebugGame))?\.exe$" | Select-Object -First 1 -ExpandProperty FullName
    if (-not $cmd) { throw "No $($project.BaseName) game exe under $package." }
    $projectArg = @()
    $logs = Join-Path $package "$($project.BaseName)\Saved\Logs"
    "co-op smoke: package $cmd"
  }
  New-Item -ItemType Directory -Force -Path $logs | Out-Null
  $run = (Get-Date).ToString('yyyyMMdd-HHmmss') + '-' + $PID
  $hostLog = Join-Path $logs "CoopSmokeHost-$run.log"
  $clientLog = Join-Path $logs "CoopSmokeClient-$run.log"
  $hostCoopLog = Join-Path $logs "CoopSmokeHost-$run.coop.log"
  $clientCoopLog = Join-Path $logs "CoopSmokeClient-$run.coop.log"
  # A free UDP port (Unreal's IpNetDriver): one no socket holds now, from a range away from the default 7777.
  $port = $null
  foreach ($candidate in (Get-Random -InputObject (17800..18799) -Count 50)) {
    try { $u = New-Object System.Net.Sockets.UdpClient($candidate); $u.Close(); $port = $candidate; break } catch { }
  }
  if (-not $port) { throw 'co-op smoke: no free UDP port in 17800..18799' }
  "co-op smoke: run $run on port $port; logs $hostLog, $clientLog"
  $hostP = $null; $clientP = $null
  $result = 1
  try {
  $common = @('-game', '-nullrhi', '-nosound', '-unattended', '-nosplash', $coopNet)
  # US-T46: SPACEGOD_RACE_DIR set, each instance's frames and net totals go to <dir>/<track>-coop-<host|client>-<run>.json
  # (the shared sampler's -SpaceGodRaceAuto; docs/RACE.md).
  $race = @{ host = @(); client = @() }
  if ($env:SPACEGOD_RACE_DIR) {
    foreach ($side in 'host', 'client') { $race[$side] = @("`"-SpaceGodRaceAuto=$(Join-Path $env:SPACEGOD_RACE_DIR "$prefix-coop-$side-$run.json")`"") }
  }
  # US-T64: a package is told to host and join with -CoopHost / -CoopJoin=<address> (a Shipping game ignores the map URL).
  $hostUrl = if ($PackageDir) { @('-CoopHost') } else { @('/Engine/Maps/Entry?listen') }
  $clientUrl = if ($PackageDir) { @("-CoopJoin=127.0.0.1:$port") } else { @("127.0.0.1:$port") }
  $hostArgs = $projectArg + $hostUrl + $common + $race.host + @("-port=$port", '-SpaceGodCoopSmoke=host', "`"-abslog=$hostLog`"",
    "`"-SpaceGodCoopLog=$hostCoopLog`"")
  # Not -WindowStyle (ShellExecute): CreateProcess, so the instances inherit the PATH with the private .NET SDK on it.
  $hostP = Start-Process -FilePath $cmd -PassThru -NoNewWindow -ArgumentList $hostArgs -RedirectStandardOutput "$hostLog.out"
  # The client starts once the host's app is up (two UnrealSharp startups at once contend for the managed build).
  $deadline = (Get-Date).AddMinutes(4)
  function HostUp { foreach ($l in @($hostLog, $hostCoopLog)) { if ((Test-Path -LiteralPath $l) -and (Select-String -LiteralPath $l -Pattern 'UnitySpaceApp up in|host app up' -Quiet)) { return $true } }; $false }
  while (-not (HostUp)) {
    if ($hostP.HasExited -or (Get-Date) -gt $deadline) {
      throw "The co-op host did not come up (see $hostLog)."
    }
    Start-Sleep -Seconds 2
  }
  $clientArgs = $projectArg + $clientUrl + $common + $race.client + @('-SpaceGodCoopSmoke=client', "`"-abslog=$clientLog`"",
    "`"-SpaceGodCoopLog=$clientCoopLog`"")
  $clientP = Start-Process -FilePath $cmd -PassThru -NoNewWindow -ArgumentList $clientArgs -RedirectStandardOutput "$clientLog.out"
  $deadline = (Get-Date).AddMinutes(11)
  while (-not ($hostP.HasExited -and $clientP.HasExited)) {
    if ((Get-Date) -gt $deadline) {
      foreach ($p in @($hostP, $clientP)) { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
      Write-Warning 'co-op smoke: timed out; both instances stopped'
      break
    }
    Start-Sleep -Seconds 2
  }
  # A package's lines are read from its co-op logs (a Shipping build writes no other; either track's, US-T67 for C#).
  if ($PackageDir) {
    if (Test-Path -LiteralPath $hostCoopLog) { $hostLog = $hostCoopLog }
    if (Test-Path -LiteralPath $clientCoopLog) { $clientLog = $clientCoopLog }
  }
  $digests = @{ host = @{}; client = @{} }
  foreach ($side in @(@{ Name = 'host'; Log = $hostLog }, @{ Name = 'client'; Log = $clientLog })) {
    if (-not (Test-Path -LiteralPath $side.Log)) { Write-Warning "no $($side.Name) log"; continue }
    foreach ($line in (Get-Content -LiteralPath $side.Log | Where-Object { $_ -match 'SpaceGodCoop:|Error:|Fatal' })) {
      "[$($side.Name)] " + ($line -replace '^\[[^\]]*\]\[\s*\d+\]', '')
      if ($line -match 'digest frame (\d+) = ([0-9A-F]{16}) \(') { $digests[$side.Name][[long]$Matches[1]] = $Matches[2] }
    }
  }
  $frames = @($digests.client.Keys | Where-Object { $digests.host.ContainsKey($_) } | Sort-Object)
  $same = @($frames | Where-Object { $digests.host[$_] -eq $digests.client[$_] })
  $range = if ($frames.Count -gt 0) { " (frames $($frames[0])..$($frames[-1]))" } else { '' }
  "co-op smoke: $($frames.Count) frames with both digests, $($same.Count) identical$range"
  $differ = @($frames | Where-Object { $digests.host[$_] -ne $digests.client[$_] })
  # US-T43 (both tracks; the C++ track's since US-T44): every digest must match (nothing writes the Core outside the frames any more), and the only
  # syncs are the deliberate ones: the join, the client's asked-for resync (its seat kept through it on both peers) and
  # the host's load.
  $hostText = if (Test-Path -LiteralPath $hostLog) { Get-Content -LiteralPath $hostLog -Raw } else { '' }
  $clientText = if (Test-Path -LiteralPath $clientLog) { Get-Content -LiteralPath $clientLog -Raw } else { '' }
  $syncs = @([regex]::Matches($hostText, 'sync #(\d+) at frame (\d+) \(([^)]*)\)') | ForEach-Object { "#$($_.Groups[1].Value) at $($_.Groups[2].Value): $($_.Groups[3].Value)" })
  function Both([string]$pattern) { ($hostText -match $pattern) -and ($clientText -match $pattern) }
  $checks = [ordered]@{
    'every digest identical'                    = $frames.Count -ge 10 -and $differ.Count -eq 0
    'no DESYNC'                                 = -not ($hostText -cmatch 'DESYNC at')
    'syncs only the join, the asked-for one and the load' = $syncs.Count -eq 3 -and $syncs[0] -match 'joined' -and $syncs[1] -match 'deliberate resync' -and $syncs[2] -match 'loaded a save'
    'seat kept through the resync (both peers)' = Both 'keeps seat \d+ \(.*\) through the sync'
    'helm taken in the frames (both peers)'     = Both 'takes the helm of .*Sandbox Freighter'
    'helm flown on the client''s input'         = ($clientText -match '(\d+) helm inputs sent') -and [int]([regex]::Match($clientText, '(\d+) helm inputs sent').Groups[1].Value) -gt 100
    'trade carried out (both peers)'            = Both "action 'buy "
    'cockpit keys carried out (both peers)'     = Both "cockpit key 'n 7'"
    'party trip (both peers), arrival'          = (Both "action 'travel ") -and ($clientText -match 'arrived in .* with the host''s frame')
    'client load asked of the host'             = $hostText -match "asked: load quick"
    # US-T47: the galaxy map's screens and the hail, from the client, carried out on both peers.
    'speaker picked in the frames (both peers)' = Both "action 'speak "
    'strategy orders (both peers)'              = (Both "action 'production ") -and (Both "action 'objective ")
    'hail (both peers)'                         = (Both "action 'hail ") -and (Both 'hail: The ')
    'diplomacy action (both peers)'             = Both "action 'gift "
    'statecraft carried out on the client'      = $clientText -match 'smoke: statecraft .*carried out: True'
  }
  "co-op smoke: syncs: $($syncs -join '; ')"
  foreach ($k in $checks.Keys) { "co-op smoke: $($k): $($checks[$k])" }
  $result = if (@($checks.Values | Where-Object { -not $_ }).Count -gt 0) { 1 } else { 0 }
  }
  finally {
    foreach ($p in @($hostP, $clientP)) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
  }
  exit $result
}

if ($Mode -eq 'audit') {
  # -Mode audit (either track, US-T48): the smoke, then FeatureAudit's probes (Runtime/Audit/FeatureAudit.cs, C++
  # Smoke/FeatureAudit.cpp): each presses one of the original's bindings on the scripted device and checks the state the
  # original's code changes, logging 'AUDIT <id> PASS|FAIL: ...'. Same ids on both tracks; docs/FAITHFULNESS.md records them.
  # No -nosound (US-T50): the audio probes read the master volume back from Unreal's main audio device (the game plays
  # no clips, so nothing is heard).
  $cmd = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
  $lines = & $cmd $project.FullName '/Engine/Maps/Entry' -game -nullrhi -unattended -nosplash -stdout -FullStdOutLogOutput -SpaceGodSmoke -SpaceGodAudit |
    Select-String -Pattern 'AUDIT |Error:|Fatal' | ForEach-Object Line
  $lines
  $pass = @($lines | Where-Object { $_ -match 'AUDIT \S+ PASS' }).Count
  $fail = @($lines | Where-Object { $_ -match 'AUDIT \S+ FAIL' }).Count
  "audit ($Track): $pass PASS, $fail FAIL"
  if ($fail -gt 0 -or $pass -eq 0) { exit 1 }
  exit 0
}

if ($Mode -eq 'smoke') {
  $cmd = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
  & $cmd $project.FullName '/Engine/Maps/Entry' -game -nullrhi -nosound -unattended -nosplash -stdout -FullStdOutLogOutput -SpaceGodSmoke |
    Select-String -Pattern 'LogSpaceGod|Error:|Fatal' | ForEach-Object Line
  exit $LASTEXITCODE
}

$editor = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor.exe'
$map = switch ($Mode) {
  'solo' { '/Engine/Maps/Entry' }
  'host' { '/Engine/Maps/Entry?listen' }
  'join' { $Address }
}
if ($Mode -eq 'solo') { & $editor $project.FullName $map -game -windowed -ResX=1600 -ResY=900 -log }
else { & $editor $project.FullName $map -game -windowed -ResX=1600 -ResY=900 -log $coopNet } # US-T39: host / join
