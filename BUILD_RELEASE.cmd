@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice"
if "%~1"=="" goto GAME_DIR_READY
set "GAME_DIR=%~1"

:GAME_DIR_READY
set "PROJECT=%~dp0Euclid.csproj"
set "CHECK_SCRIPT=%~dp0scripts\check_project.ps1"

echo [Euclid] Release build
echo Game: "%GAME_DIR%"
echo.

if exist "%GAME_DIR%\A Dance of Fire and Ice_Data\Managed\Assembly-CSharp.dll" goto GAME_OK
echo ERROR: Assembly-CSharp.dll was not found under:
echo "%GAME_DIR%"
pause
exit /b 1

:GAME_OK
where dotnet >nul 2>nul
if not errorlevel 1 goto DOTNET_OK
echo ERROR: dotnet SDK was not found.
pause
exit /b 1

:DOTNET_OK
if not exist "%CHECK_SCRIPT%" goto BUILD
powershell -NoProfile -ExecutionPolicy Bypass -File "%CHECK_SCRIPT%"
if not errorlevel 1 goto BUILD
echo.
echo PROJECT CHECK FAILED
pause
exit /b 1

:BUILD
dotnet build "%PROJECT%" -c Release "-p:GameDir=%GAME_DIR%"
if not errorlevel 1 goto BUILD_OK
echo.
echo ============================
echo BUILD FAILED
echo ============================
pause
exit /b 1

:BUILD_OK
echo.
echo ============================
echo RELEASE BUILD COMPLETE
echo ============================
echo.
echo UMM package is under:
echo "%~dp0dist"
echo.
pause
exit /b 0
