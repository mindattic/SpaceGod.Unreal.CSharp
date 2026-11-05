#Requires -Version 5.1
<#
.SYNOPSIS
    Pulls the latest SpaceGod.Unreal.CSharp, packages the game (Shipping, tools\package.ps1), publishes a standalone
    copy to C:\Apps\SpaceGod.Unreal.CSharp\ and refreshes the Desktop shortcut "SpaceGod – Unreal C#".

.DESCRIPTION
    1. Refreshes the Desktop shortcut (it opens deploy.bat, whose menu offers 1) Deploy & Run and 2) Run).
    2. git pull --rebase --autostash (a failed pull deploys the commit already checked out).
    3. Skips the rest when nothing the game is built from (CSharp, Shared, Vendor) changed since the deployed commit.
    4. Stops a running published copy, then tools\package.ps1 -Configuration Shipping -> Builds\CSharp\Windows\.
    5. Copies the package to C:\Apps\SpaceGod.Unreal.CSharp\ and records the commit in .deploy-commit.

.PARAMETER Launch
    After publishing (or when already up to date), run the published game.

.PARAMETER Force
    Rebuild and republish even when the deployed commit already matches the game's sources. deploy.bat's
    "1) Deploy & Run" passes it.

.PARAMETER LauncherOnly
    Refresh the Desktop shortcut and exit, without pulling, building or stopping anything.

.PARAMETER MaxParallelActions
    UnrealBuildTool's parallel compile limit (default 4, so the build leaves memory free).

.NOTES
    Never prompts, so agents and /deploy can run it unattended. Needs Unreal Engine 5.8 (tools\engine.ps1 finds it,
    or set UE_ROOT) and Visual Studio 2022's C++ workload; the .NET SDK UnrealSharp needs is installed privately by
    tools\dotnet-sdk.ps1 when the machine's cannot build it.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File run.ps1 -Launch
    powershell -ExecutionPolicy Bypass -File run.ps1 -Force -Launch
    powershell -ExecutionPolicy Bypass -File run.ps1 -LauncherOnly
#>
param([switch]$Launch, [switch]$Force, [switch]$LauncherOnly, [int]$MaxParallelActions = 4)

$ErrorActionPreference = 'Stop'

$repoDir   = $PSScriptRoot
$name      = 'SpaceGod.Unreal.CSharp'
$title     = 'SpaceGod – Unreal C#'
$out       = "C:\Apps\$name"
$exeName   = 'Unreal.SpaceGod.CSharp.exe'          # tools\package.ps1's launcher name
$buildDir  = Join-Path $repoDir 'Builds\CSharp\Windows'
$shortcutBat = Join-Path $repoDir 'run.bat'   # the Desktop shortcut's target: 1) Deploy or 2) Deploy & Run (US-T73)
$marker    = Join-Path $out '.deploy-commit'
# US-T74: every launch records (docs/CAPTURE.md); hq, as lossless video falls behind on this machine.
$captureArgs = @('-capture', 'video', 'hq')

# GetFolderPath, not $HOME\Desktop: OneDrive may have moved the Desktop folder.
function Set-Shortcut([string]$exe) {
    $shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) "$title.lnk"
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    $link.TargetPath       = $shortcutBat
    $link.WorkingDirectory = $repoDir
    if (Test-Path -LiteralPath $exe) { $link.IconLocation = "$exe,0" }
    $link.Description      = "$title ($name): 1) Deploy or 2) Deploy & Run"
    $link.Save()
    Write-Host "    Shortcut -> $shortcut" -ForegroundColor Gray
}

function Start-Game {
    $exe = Join-Path $out $exeName
    Write-Host "  Launching $exe" -ForegroundColor Cyan
    Start-Process -FilePath $exe -WorkingDirectory $out -ArgumentList $captureArgs
}

Set-Shortcut (Join-Path $out $exeName)
if ($LauncherOnly) { exit 0 }

# ── Pull latest ──────────────────────────────────────────────────────────────
git -C $repoDir pull --rebase --autostash -q
if ($LASTEXITCODE -ne 0) { Write-Host "  git pull failed in $repoDir - deploying the current commit." -ForegroundColor DarkYellow }
$head = (git -C $repoDir rev-parse HEAD).Trim()

# ── Up to date? ──────────────────────────────────────────────────────────────
$upToDate = $false
if (-not $Force -and (Test-Path -LiteralPath (Join-Path $out $exeName)) -and (Test-Path -LiteralPath $marker)) {
    $deployed = (Get-Content -LiteralPath $marker -Raw).Trim()
    git -C $repoDir diff --quiet $deployed $head -- CSharp Shared Vendor 2>$null
    $upToDate = $LASTEXITCODE -eq 0
    if ($upToDate -and $deployed -ne $head) { Set-Content -LiteralPath $marker -Value $head -NoNewline }
}
if ($upToDate) {
    Write-Host "  Already deployed at $head (no game source changed) - skipping the rebuild." -ForegroundColor Green
    if ($Launch) { Start-Game }
    exit 0
}

# ── Stop a running published copy ────────────────────────────────────────────
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($out, 'OrdinalIgnoreCase') }
if ($running) {
    $running | Stop-Process -Force
    Write-Host "  Stopped the running game ($(@($running).Count) process(es))." -ForegroundColor DarkYellow
    Start-Sleep -Milliseconds 800
}

# ── Package (Shipping) ───────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Packaging Shipping (tools\package.ps1)...' -ForegroundColor Cyan
& (Join-Path $repoDir 'tools\package.ps1') -Configuration Shipping -MaxParallelActions $MaxParallelActions
if (-not $?) { Write-Host '  Packaging failed.' -ForegroundColor Red; exit 1 }
$builtExe = Join-Path $buildDir $exeName
if (-not (Test-Path -LiteralPath $builtExe)) { Write-Host "  No $exeName at $builtExe." -ForegroundColor Red; exit 1 }

# ── Publish ──────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "  Publishing $buildDir -> $out" -ForegroundColor Cyan
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item -Recurse -Force (Join-Path $buildDir '*') $out
$exe = Join-Path $out $exeName
if (-not (Test-Path -LiteralPath $exe)) { Write-Host "  Publish completed but $exe is missing." -ForegroundColor Red; exit 1 }
Set-Content -LiteralPath $marker -Value $head -NoNewline
Set-Shortcut $exe

Write-Host ''
Write-Host "  Published $title at $head." -ForegroundColor Green
Write-Host "    Exe : $exe" -ForegroundColor Gray
if ($Launch) { Start-Game }
exit 0
