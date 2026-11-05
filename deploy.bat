@echo off
rem Desktop shortcut target ("SpaceGod - Unreal C#"). Asks first: 1) Deploy & Run pulls latest, packages Shipping
rem even when nothing changed (-Force), publishes to C:\Apps\SpaceGod.Unreal.CSharp, refreshes the shortcut and
rem launches it (run.ps1 does the work); 2) Run starts the published copy without deploying (deploying first when
rem nothing is published yet). "deploy.bat --deploy [flags]" skips the menu and deploys without -Force; any other
rem arguments pass through to run.ps1 when deploying.
setlocal
set "APP=C:\Apps\SpaceGod.Unreal.CSharp"
set "EXE=%APP%\Unreal.SpaceGod.CSharp.exe"
set "FORCE=-Force"
set "ARGS=%*"
if /i not "%~1"=="--deploy" goto menu
set "FORCE="
set "ARGS="
shift
:collect
if "%~1"=="" goto deploy
set "ARGS=%ARGS% %1"
shift
goto collect

:menu
title SpaceGod - Unreal C#
echo.
echo   SpaceGod - Unreal C#   (SpaceGod.Unreal.CSharp, UnrealSharp)
echo     1^) Deploy ^& Run
echo     2^) Run
echo.
choice /c 12 /n /m "  Choose 1 or 2: "
if errorlevel 2 goto run

:deploy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" -Launch %FORCE% %ARGS%
if errorlevel 1 (
    echo.
    echo   Deploy failed. See the output above.
    pause
    exit /b 1
)
timeout /t 5
exit /b 0

:run
if not exist "%EXE%" (
    echo.
    echo   SpaceGod - Unreal C# is not deployed yet ^(%EXE% is missing^) - deploying first.
    goto deploy
)
start "" /d "%APP%" "%EXE%" -capture video hq
exit /b 0
