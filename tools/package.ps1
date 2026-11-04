[CmdletBinding()]
param(
  # This repo is the C# track alone (US-T69); -Track stays so older command lines keep working.
  [ValidateSet('CSharp')][string]$Track = 'CSharp',
  [ValidateSet('Development', 'Shipping')][string]$Configuration = 'Development',
  [int]$MaxParallelActions = 4,
  [string]$EngineVersion = '5.8',
  # Skip the editor build that cooking needs (use when tools/build.ps1 -Track <Track> has just run).
  [switch]$SkipEditorBuild,
  # After packaging, boot the packaged exe headless (-nullrhi -unattended) and print its LogSpaceGod lines.
  [switch]$Verify,
  # Only boot-check the existing package in Builds/<Track> (implies -Verify; nothing is built).
  [switch]$VerifyOnly,
  # CSharp: extra arguments for the managed publish (UnrealSharp PackageProject -UserParams), e.g.
  # '-p:DefaultItemExcludesInProjectFolder=*/**' to compile only the top-level .cs files of the Script projects.
  [string[]]$ManagedBuildArgs = @()
)

<#
.SYNOPSIS
Package a track as a standalone Win64 game: UAT BuildCookRun (build, cook, stage, pak, archive) into
Builds/<Track>/Windows/ (gitignored), with the game exe named Unreal.SpaceGod.Cpp.exe / Unreal.SpaceGod.CSharp.exe.

.DESCRIPTION
Steps:
 1. tools/build.ps1 -Track <Track> (Development editor target): the cooker runs the project's editor modules. For
    CSharp this also sets up UnrealSharp's net10 hooks and .NET SDK and publishes the managed code for the editor.
 2. RunUAT BuildCookRun -platform=Win64 -clientconfig=<Configuration> -build -cook -stage -pak -archive.
    UBT runs with -WaitMutex (queued behind any other UE build) and -MaxParallelActions (default 4).
 3. CSharp only: UnrealSharp's PackageProject UAT command (what its editor "Package Project" button runs) publishes
    UnrealSharp's bindings, the generated glue, Script/ (the game code) and its Core references (Unity.SpaceGod's
    Core, Unity.Shared's Core) self-contained, with the .NET runtime, into
    <archive>/UnrealSpaceGodCSharp/Binaries/Managed/net10.0 -- the folder the packaged plugin loads from
    (UnrealSharp::Paths::GetUserAssemblyDirectory = ProjectDir/Binaries/Managed/net10.0).
 4. Renames the launcher exe (see below).

Exe name. UE names a game exe after its target, and a target name is a C# class name, so it cannot contain dots:
UAT writes <archive>/<Target>.exe, a small bootstrap launcher, plus the real game in
<archive>/<Target>/Binaries/Win64/<Target>[-Win64-Shipping].exe. The launcher does not derive anything from its own
file name: it carries the relative path of the real exe as an embedded resource (BootstrapPackagedGame), and the real
exe finds its content from its own folder and the compiled-in project name. So the launcher is renamed to
Unreal.SpaceGod.<Track>.exe and the inner exe keeps the target name; -Verify boots the renamed launcher to prove it.

.EXAMPLE
  ./tools/package.ps1 -Track Cpp
  ./tools/package.ps1 -Track CSharp -Verify
  ./tools/package.ps1 -Track CSharp -Configuration Shipping
#>

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/engine.ps1"
$repo = Split-Path -Parent $PSScriptRoot
$project = Get-ChildItem -LiteralPath (Join-Path $repo $Track) -Filter '*.uproject' | Select-Object -First 1
if (-not $project) { throw "No .uproject in $Track/." }
$engine = Find-UnrealEngine $EngineVersion
$uat = Join-Path $engine 'Engine\Build\BatchFiles\RunUAT.bat'
$target = $project.BaseName                       # the Game target: Source/<Target>.Target.cs
$archiveRoot = Join-Path $repo "Builds\$Track"
$archive = Join-Path $archiveRoot 'Windows'       # UAT appends the cooked platform folder
$exeName = "Unreal.SpaceGod.$Track.exe"
$renamed = Join-Path $archive $exeName
if ($VerifyOnly) {
  if (-not (Test-Path -LiteralPath $renamed)) { throw "No package at ${renamed}: run without -VerifyOnly first." }
  $Verify = $true
} else {

# 1. The editor build the cooker needs.
if (-not $SkipEditorBuild) {
  & (Join-Path $PSScriptRoot 'build.ps1') -Track $Track -Configuration Development -MaxParallelActions $MaxParallelActions -EngineVersion $EngineVersion
  if ($LASTEXITCODE) { throw "Editor build failed ($LASTEXITCODE)." }
}
if ($Track -eq 'CSharp') {
  . "$PSScriptRoot/dotnet-sdk.ps1"
  Use-UnrealSharpDotNet -PrivateRoot (Join-Path $project.DirectoryName 'Intermediate\DotNet')
}

# 2. Build, cook, stage, pak, archive. A stale archive would leave old files (and an old renamed exe) behind.
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Recurse -Force }
if ($Track -eq 'CSharp') {
  # UnrealSharp's editor module runs `RunUAT BuildUserSolution` on every startup, the cook commandlet included
  # (UnrealSharpCore.cpp StartupModule, not skipped in commandlets). That nested UAT then fails with "A conflicting
  # instance of AutomationTool is already running" because this BuildCookRun holds UAT's single-instance mutex.
  # uebp_UATMutexNoWait=1 is what UAT itself sets for the child UATs it spawns (ProcessUtils.cs); here it is set for
  # the whole run so the cooker's child inherits it. UBT's own mutex (-WaitMutex) still serialises the builds.
  $env:uebp_UATMutexNoWait = '1'
}
& $uat BuildCookRun "-project=$($project.FullName)" -noP4 -utf8output -unattended `
  -platform=Win64 "-clientconfig=$Configuration" "-target=$target" `
  -build -cook -stage -pak -archive "-archivedirectory=$archiveRoot" `
  "-ubtargs=-WaitMutex -MaxParallelActions=$MaxParallelActions"
$cookExit = $LASTEXITCODE
Remove-Item Env:uebp_UATMutexNoWait -ErrorAction SilentlyContinue
if ($cookExit) { throw "BuildCookRun failed ($cookExit)." }

$launcher = Join-Path $archive "$target.exe"
if (-not (Test-Path -LiteralPath $launcher)) { throw "BuildCookRun archived no launcher at $launcher." }

# 3. UnrealSharp's managed side, into the packaged project's Binaries/Managed/net10.0.
if ($Track -eq 'CSharp') {
  $packagedProject = Join-Path $archive $target
  & $uat PackageProject "-ScriptDir=$($project.DirectoryName)" "-ScriptsForProject=$($project.FullName)" `
    "-Project=$($project.FullName)" "-ArchiveDirectory=$packagedProject" `
    "-UEBuildConfig=$Configuration" -UETargetType=Game -TargetPlatform=Win64 `
    @($ManagedBuildArgs | ForEach-Object { "-UserParams=$_" })
  if ($LASTEXITCODE) { throw "UnrealSharp PackageProject failed ($LASTEXITCODE)." }
  $managed = Join-Path $packagedProject 'Binaries\Managed\net10.0'
  foreach ($required in 'UnrealSharp.dll', 'ManagedUnrealSpaceGodCSharp.dll', 'Unity.SpaceGod.Core.dll',
                        'Unity.Shared.Core.dll', 'UserCode.LoadOrder.json', 'hostfxr.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $managed $required))) { throw "Packaged managed code lacks $required ($managed)." }
  }
}

# 4. The exe name.
Move-Item -LiteralPath $launcher -Destination $renamed -Force
}

$size = (Get-ChildItem -LiteralPath $archive -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Packaged {0} ({1}): {2} ({3:N1} MB staged)" -f $Track, $Configuration, $renamed, ($size / 1MB))

if (-not $Verify) { exit 0 }

# Boot check: headless, unattended. The C# game mode quits itself after a few ticks under -SpaceGodSmoke; the C++
# one has no smoke mode yet, so it runs until its map has loaded and is then stopped. Shipping builds write no log.
$log = Join-Path $archive "$target\Saved\Logs\$target.log"
Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
$gameArgs = @('-nullrhi', '-nosound', '-unattended', '-nosplash', '-log')
if ($Track -eq 'CSharp') { $gameArgs += '-SpaceGodSmoke' }
$done = if ($Track -eq 'CSharp') { 'SpaceGodSmoke: done' } else { 'LogLoad: Took .* to LoadMap' }
$proc = Start-Process -FilePath $renamed -ArgumentList $gameArgs -PassThru -WindowStyle Hidden
$deadline = (Get-Date).AddMinutes(10)
$ok = $false
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 2
  if ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -Pattern $done -Quiet)) { $ok = $true; break }
  $inner = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($archive, 'OrdinalIgnoreCase') }
  if ($proc.HasExited -and -not $inner) { break }
}
if ($Track -eq 'CSharp' -and $ok) { Start-Sleep -Seconds 5 }  # let the requested quit finish
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($archive, 'OrdinalIgnoreCase') } |
  Stop-Process -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $log) {
  Select-String -LiteralPath $log -Pattern 'LogSpaceGod|Game class is|LogUnrealSharp.*(Error|Fatal)|Fatal error|LogLoad: Took' | ForEach-Object Line
}
if (-not $ok) { throw "The packaged $Track game did not reach '$done' (log: $log)." }
Write-Host "Boot check passed: $renamed"
