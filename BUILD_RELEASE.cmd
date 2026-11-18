@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PROJECT=%~dp0Euclid.csproj"
set "CHECK_SCRIPT=%~dp0scripts\check_project.ps1"

echo [Euclid] Release build
if not "%~1"=="" goto SHOW_GAME_OVERRIDE
if defined ADOFAI_DIR goto SHOW_ADOFAI_DIR
echo Game path: Directory.Build.props or project default
goto SHOW_GAME_PATH_DONE

:SHOW_GAME_OVERRIDE
echo Game path override: "%~1"
goto SHOW_GAME_PATH_DONE

:SHOW_ADOFAI_DIR
echo Game path: ADOFAI_DIR

:SHOW_GAME_PATH_DONE
echo.

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
if "%~1"=="" goto BUILD_FROM_CONFIG
dotnet build "%PROJECT%" -c Release "-p:GameDir=%~1"
goto BUILD_RESULT

:BUILD_FROM_CONFIG
dotnet build "%PROJECT%" -c Release

:BUILD_RESULT
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
