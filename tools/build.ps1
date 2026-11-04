[CmdletBinding()]
param(
  # This repo is the C# track alone (US-T69); -Track stays so older command lines keep working.
  [ValidateSet('CSharp')][string]$Track = 'CSharp',
  [ValidateSet('Development', 'DebugGame', 'Shipping')][string]$Configuration = 'Development',
  [int]$MaxParallelActions = 4,
  [string]$EngineVersion = '5.8'
)

<#
.SYNOPSIS
Build a track's editor target with UnrealBuildTool (no editor needed). Close any editor holding the project
first: UBT refuses while Live Coding is active. MaxParallelActions is kept low so the build leaves memory free;
-WaitMutex queues this build behind any other UE build running on the machine.

This repo is the C# track alone. The build also: checks the vendored UnrealSharp plugin is there, works around UnrealSharp's net8.0/net10.0
detection on installed (launcher) engines, and after the C++ build compiles the managed side (UnrealSharp's
own bindings, then Script/ + Unity.SpaceGod's Core) with UnrealSharp's UAT command, as the editor would.
#>

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/engine.ps1"
$repo = Split-Path -Parent $PSScriptRoot
$project = Get-ChildItem -LiteralPath (Join-Path $repo $Track) -Filter '*.uproject' | Select-Object -First 1
if (-not $project) { throw "No .uproject in $Track/." }
$engine = Find-UnrealEngine $EngineVersion
$build = Join-Path $engine 'Engine\Build\BatchFiles\Build.bat'

if ($Track -eq 'CSharp') {
  $plugin = Join-Path $project.DirectoryName 'Plugins\UnrealSharp'
  # The plugin is vendored as plain files (Vendor/README.md), no submodule to fetch.
  if (-not (Test-Path -LiteralPath (Join-Path $plugin 'UnrealSharp.uplugin'))) {
    throw "The vendored UnrealSharp plugin is missing ($plugin); restore it with git checkout -- CSharp/Plugins/UnrealSharp."
  }
  # UnrealSharp.Automation.props picks net10.0 (what UE 5.8's UBT/UAT need) only when it finds
  # UE_5_8_OR_LATER in Engine/Intermediate/Build/BuildRulesProjects/UE5Rules/UE5Rules.csproj, which only a
  # source-built engine has; on an installed engine it stays net8.0 and UBT skips the glue plugin
  # ("Skipping net8.0 project", upstream PR #717, closed unmerged). Both projects import an optional,
  # gitignored "<project>.csproj.props" first, so define the symbol there.
  $hook = @'
<Project>
  <!-- Written by tools/build.ps1 (SpaceGod.Unreal.CSharp): this project targets UE 5.8, see UnrealSharp PR #717. -->
  <PropertyGroup>
    <UE5Rules_DefineConstants>UE_5_8_OR_LATER</UE5Rules_DefineConstants>
  </PropertyGroup>
</Project>
'@
  foreach ($csproj in @('Source\UnrealSharpManagedGlue\UnrealSharpManagedGlue.ubtplugin.csproj',
                        'Build\Scripts\UnrealSharp.Automation.csproj')) {
    Set-Content -LiteralPath (Join-Path $plugin "$csproj.props") -Value $hook -Encoding UTF8
  }
  . "$PSScriptRoot/dotnet-sdk.ps1"
  Use-UnrealSharpDotNet -PrivateRoot (Join-Path $project.DirectoryName 'Intermediate\DotNet')
}

$target = "$($project.BaseName)Editor"
# US-T49: UBT calls an object up to date when it is newer than every file it includes. A source or header edited while
# UBT compiles (several agents share this checkout) can be in objects compiled from its *old* text that finish after the
# edit and so look newer than it: they stay in the build, mixing two versions of the header (an ODR violation; it crashed
# the C++ track in cs::Dictionary::BucketOf). So any file under the module sources written during the build gets the
# time now and the build runs again (at most twice more), which recompiles everything that includes it.
$sourceRoots = @((Join-Path $project.DirectoryName 'Source'), (Join-Path $repo 'Shared\Plugins')) | Where-Object { Test-Path -LiteralPath $_ }
for ($pass = 1; ; $pass++) {
  $started = [DateTime]::UtcNow
  & $build $target Win64 $Configuration "-Project=$($project.FullName)" -WaitMutex "-MaxParallelActions=$MaxParallelActions"
  if ($LASTEXITCODE) { exit $LASTEXITCODE }
  $edited = @(Get-ChildItem -LiteralPath $sourceRoots -Recurse -File -Include '*.h', '*.hpp', '*.inl', '*.cpp' |
    Where-Object LastWriteTimeUtc -gt $started)
  if ($edited.Count -eq 0) { break }
  $now = [DateTime]::UtcNow
  foreach ($f in $edited) { $f.LastWriteTimeUtc = $now; Write-Warning "edited during the build: $($f.FullName)" }
  if ($pass -ge 3) { Write-Warning 'sources kept changing during the build; run tools/build.ps1 again once they settle.'; break }
  Write-Host "Building again so every object sees the edited file(s) (pass $($pass + 1))."
}

if ($Track -eq 'CSharp') {
  # The managed user code (Script/*.csproj and the Core projects they reference) -> Binaries/Managed, with the
  # load-order manifest the plugin reads at startup. The editor does this itself on launch; doing it here makes
  # a fresh checkout playable headless.
  & (Join-Path $PSScriptRoot 'build-managed.ps1') -Configuration $Configuration -EngineVersion $EngineVersion
  exit $LASTEXITCODE
}
