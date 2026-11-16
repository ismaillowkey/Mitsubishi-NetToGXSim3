@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

:MENU
cls
echo =======================================================
echo   NetToGXSim3 Launcher and Installer Builder
echo   Author: Ismail Lowkey
echo =======================================================
echo.
echo   [1] Run NetToGXSim3 (WPF x86)
echo   [2] Create Windows Installer (NSIS)
echo   [0] Exit
echo.
set "CHOICE="
set /p "CHOICE=Select option [1/2/0]: "

if "%CHOICE%"=="1" goto :RUN_APP
if "%CHOICE%"=="2" goto :BUILD_INSTALLER
if "%CHOICE%"=="0" goto :EXIT_CLEAN

echo.
echo [ERROR] Invalid selection "%CHOICE%". Please enter 1, 2, or 0.
echo.
pause
goto :MENU

:RUN_APP
echo.
echo =======================================================
echo   Running NetToGXSim3 (WPF x86)...
echo =======================================================
echo.
dotnet run --project "src\NetToGXSim3.Wpf\NetToGXSim3.Wpf.csproj"
if errorlevel 1 goto :RUN_FAILED
echo.
echo [INFO] Application exited cleanly.
goto :END_OK

:BUILD_INSTALLER
echo.
echo =======================================================
echo   NetToGXSim3 - Automated Build ^& Create Installer
echo   Author: Ismail Lowkey
echo =======================================================
echo.

set "STAGING_DIR=src\NetToGXSim3.Wpf\obj\publish_staging"

:: 1. Publish WPF Application in Release Mode to temporary staging directory
echo [1/3] Publishing WPF application in Release mode...
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"
dotnet publish "src\NetToGXSim3.Wpf\NetToGXSim3.Wpf.csproj" -c Release -o "%STAGING_DIR%"
if errorlevel 1 goto :PUBLISH_FAILED
echo [OK] Publish completed successfully in staging directory.
echo.

:: Extract Product Version dynamically from the published binary
for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "(Get-Item '%STAGING_DIR%\NetToGXSim3.Wpf.exe').VersionInfo.ProductVersion.Split('+')[0].Trim()"`) do set "APP_VERSION=%%v"
if not defined APP_VERSION (
    for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "$v = (Get-Item '%STAGING_DIR%\NetToGXSim3.Wpf.exe').VersionInfo.FileVersion; if ($v) { $p = $v.Split('.'); $p[0]+'.'+$p[1]+'.'+$p[2] }"`) do set "APP_VERSION=%%v"
)
echo [VERSION] Detected Application Version: %APP_VERSION%
echo.

:: 2. Locate NSIS Compiler
echo [2/3] Locating NSIS Compiler makensis.exe...
set "NSIS_PATH="

if exist "C:\Program Files (x86)\NSIS\makensis.exe" set "NSIS_PATH=C:\Program Files (x86)\NSIS\makensis.exe"
if not defined NSIS_PATH if exist "C:\Program Files\NSIS\makensis.exe" set "NSIS_PATH=C:\Program Files\NSIS\makensis.exe"

if not defined NSIS_PATH (
    where makensis >nul 2>nul
    if not errorlevel 1 set "NSIS_PATH=makensis"
)

if not defined NSIS_PATH goto :MISSING_NSIS

echo [OK] Found NSIS compiler at: "%NSIS_PATH%"
echo.

:: Ensure output directory publish exists
if not exist "publish" mkdir "publish"

:: 3. Compile installer with NSIS passing dynamic version
echo [3/3] Compiling installer.nsi for v%APP_VERSION%...
"%NSIS_PATH%" /V2 /DPRODUCT_VERSION="%APP_VERSION%" "installer.nsi"
if errorlevel 1 goto :NSIS_FAILED

:: Clean up staging directory after successful NSIS compile
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"

echo.
echo =======================================================
echo  [SUCCESS] Installer package generated successfully!
for %%F in ("publish\Setup_NetToGXSim3_v%APP_VERSION%.exe") do (
    echo  File: %%~nxF
    echo  Size: %%~zF bytes
    echo  Path: %%~dpnxF
)
echo =======================================================
goto :END_OK

:RUN_FAILED
echo.
echo =======================================================
echo  [ERROR] Failed to run NetToGXSim3!
echo =======================================================
goto :END_ERR

:PUBLISH_FAILED
echo.
echo =======================================================
echo  [ERROR] Failed to publish WPF project!
echo =======================================================
goto :END_ERR

:MISSING_NSIS
echo.
echo =======================================================
echo  [ERROR] NSIS compiler makensis.exe was not found!
echo  Please install NSIS from https://nsis.sourceforge.io/
echo  or ensure makensis.exe is in your PATH.
echo =======================================================
goto :END_ERR

:NSIS_FAILED
echo.
echo =======================================================
echo  [ERROR] Failed to build installer with NSIS!
echo =======================================================
goto :END_ERR

:END_OK
echo.
pause
exit /b 0

:END_ERR
echo.
pause
exit /b 1

:EXIT_CLEAN
exit /b 0
