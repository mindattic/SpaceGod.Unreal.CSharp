<#
.SYNOPSIS
  Builds and runs the Bench: every scenario of Bench/scenarios.json (or the ones named), one process each, writing
  Bench/results/csharp-<scenario>.json and .digest.txt, then checks each digest against Bench/expected-digests.json
  (the digests every SpaceGod track produces: the Unity original, the Unreal C++ port and this C# track). See
  Bench/README.md.

.EXAMPLE
  tools/bench.ps1                          # build Bench.CSharp (Release), run every scenario, check the digests
  tools/bench.ps1 -Scenario sol-idle,battle
  tools/bench.ps1 -NoBuild -Ticks 100      # quick smoke run with a tick cap (digests are then not checked)
  tools/bench.ps1 -Compare                 # only check the results already in Bench/results
#>
param(
    [string[]] $Scenario,
    # This repo is the C# track alone (US-T69); -Track stays so older command lines keep working.
    [ValidateSet('CSharp')] [string] $Track = 'CSharp',
    # Bench.CSharp's runtime: net10.0 is what the C# game track runs (UnrealSharp, UE 5.8); net8.0 for comparison.
    [ValidateSet('net10.0', 'net8.0')] [string] $DotNet = 'net10.0',
    [int] $Ticks = 0,
    [switch] $NoBuild,
    # Only check the results already on disk, running nothing.
    [switch] $Compare
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bench = Join-Path $root 'Bench'
$scenarios = Join-Path $bench 'scenarios.json'
$results = Join-Path $bench 'results'
New-Item -ItemType Directory -Force $results | Out-Null

$names = if ($Scenario) { $Scenario | ForEach-Object { $_ -split ',' } | Where-Object { $_ } }
         else { (Get-Content $scenarios -Raw | ConvertFrom-Json).scenarios | ForEach-Object { $_.name } }

if (-not $Compare) {
    $proj = Join-Path $bench 'CSharp\Bench.CSharp.csproj'
    if (-not $NoBuild) {
        & dotnet build $proj -c Release -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "Bench.CSharp build failed" }
    }
    $exe = Join-Path $bench "CSharp\bin\Release\$DotNet\Bench.CSharp.exe"
    foreach ($n in $names) {
        $runArgs = @('--scenario', $n, '--scenarios', $scenarios, '--out', $results)
        if ($Ticks -gt 0) { $runArgs += @('--ticks', "$Ticks") }
        & $exe @runArgs
        if ($LASTEXITCODE -ne 0) { throw "C# scenario '$n' failed (exit $LASTEXITCODE)" }
    }
}
if ($Ticks -gt 0) { Write-Host 'Tick cap set: digests not checked.'; exit 0 }

# ---------------------------------------------------------------- the recorded digests
$expected = (Get-Content (Join-Path $bench 'expected-digests.json') -Raw | ConvertFrom-Json).digests
$same = 0
foreach ($n in $names) {
    $file = Join-Path $results "csharp-$n.json"
    if (-not (Test-Path $file)) { Write-Host ("{0,-14} (no result)" -f $n) -ForegroundColor Red; continue }
    $j = Get-Content $file -Raw | ConvertFrom-Json
    $want = $expected.$n
    $t = $j.timings
    if ($want -and $j.digestHash -eq $want) {
        $same++
        Write-Host ("{0,-14} MATCH   digest {1}  tick p50 {2:0.000} ms  total {3:0.0} ms" -f $n, $j.digestHash, $t.tickP50Ms, $t.totalMs) -ForegroundColor Green
    } else {
        Write-Host ("{0,-14} DIFFERS digest {1}, expected {2}" -f $n, $j.digestHash, $(if ($want) { $want } else { '(none recorded)' })) -ForegroundColor Red
    }
}
Write-Host "`n$same of $(@($names).Count) scenario(s) match the recorded digests"
if ($same -ne @($names).Count) { exit 1 }
