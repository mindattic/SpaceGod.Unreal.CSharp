@echo off
rem Desktop shortcut target ("SpaceGod - Unreal C#"). Asks first: 1) Deploy pulls latest, rebuilds and republishes to
rem C:\Apps\SpaceGod.Unreal.CSharp only when something the game is built from changed, and refreshes the shortcut;
rem 2) Deploy ^& Run does the same, then starts the game recording through MindAttic.Capture (-capture video hq,
rem US-T74). run.ps1 does the work. Arguments skip the menu and pass through to run.ps1 -Launch (e.g. run.bat -Force).
rem deploy.bat keeps the 1) Deploy ^& Run (-Force) / 2) Run menu (US-T73).
setlocal
title SpaceGod - Unreal C#
set "LAUNCH=-Launch"
if not "%~1"=="" goto deploy
echo.
echo   SpaceGod - Unreal C#
echo     1^) Deploy
echo     2^) Deploy ^& Run
echo.
choice /c 12 /n /m "  Choose 1 or 2: "
if errorlevel 2 goto deploy
set "LAUNCH="

:deploy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %LAUNCH% %*
if errorlevel 1 (
    echo.
    echo   Deploy failed. See the output above.
    pause
    exit /b 1
)
timeout /t 5
exit /b 0
