@echo off
rem Desktop shortcut target ("SpaceGod - Unreal C#"). No menu: deploys, then runs. run.ps1 pulls latest, rebuilds and
rem republishes to C:\Apps\SpaceGod.Unreal.CSharp only when something the game is built from changed (pass -Force to rebuild
rem anyway), refreshes the Desktop shortcut and starts the game (-Launch). deploy.bat keeps the
rem 1) Deploy ^& Run / 2) Run menu. Extra arguments pass through to run.ps1 (US-T71).
setlocal
title SpaceGod - Unreal C#
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" -Launch %*
if errorlevel 1 (
    echo.
    echo   Deploy failed. See the output above.
    pause
    exit /b 1
)
timeout /t 5
exit /b 0
