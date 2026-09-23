@echo off
setlocal enabledelayedexpansion

echo ======================================================================
echo    NetToGXSim3 - Automated Build ^& Create Installer
echo    Developed by: Ismail Lowkey
echo ======================================================================
echo.

cd /d "%~dp0"

:: 1. Publish WPF Application in Release Mode
echo [1/3] Publishing WPF application in Release mode...
dotnet publish "src\NetToGXSim3.Wpf\NetToGXSim3.Wpf.csproj" -c Release -o "publish"
if errorlevel 1 (
    echo.
    echo [ERROR] Failed to publish WPF project! Please check the build errors above.
    pause
    exit /b 1
)
echo [OK] Publish completed successfully in publish directory.
echo.

:: Extract Product Version dynamically from the published binary (Single Source of Truth)
for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "(Get-Item 'publish\NetToGXSim3.Wpf.exe').VersionInfo.ProductVersion.Split('+')[0].Trim()"`) do set "APP_VERSION=%%v"
if not defined APP_VERSION set "APP_VERSION=0.6.1"
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

if not defined NSIS_PATH goto :NsisNotFound

echo [OK] Found NSIS compiler at: "%NSIS_PATH%"
echo.

:: 3. Compile installer with NSIS passing dynamic version
echo [3/3] Compiling installer.nsi for v%APP_VERSION%...
"%NSIS_PATH%" /V2 /DPRODUCT_VERSION="%APP_VERSION%" "installer.nsi"
if errorlevel 1 (
    echo.
    echo [ERROR] Failed to build installer with NSIS!
    pause
    exit /b 1
)

echo.
echo ======================================================================
echo  [SUCCESS] Installer package generated successfully!
echo  File: Setup_NetToGXSim3_v%APP_VERSION%.exe
echo ======================================================================
echo.
pause
exit /b 0

:NsisNotFound
echo.
echo [ERROR] NSIS compiler makensis.exe was not found!
echo Please install NSIS from https://nsis.sourceforge.io/
echo or ensure makensis.exe is added to your system PATH or Program Files.
pause
exit /b 1
