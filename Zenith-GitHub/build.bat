@echo off
setlocal EnableExtensions
title Zenith PC Optimizer - Build
cd /d "%~dp0"

echo.
echo   ==============================================
echo     Zenith PC Optimizer  -  one-click build
echo   ==============================================
echo.

rem ---- 1. Find a .NET SDK (version 8 or newer) ----
set "DOTNET="
where dotnet >nul 2>nul && set "DOTNET=dotnet"
if not defined DOTNET if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"

if defined DOTNET (
    "%DOTNET%" --list-sdks 2>nul | findstr /r /c:"^[89]\." /c:"^[1-9][0-9]\." >nul || set "DOTNET="
)

rem ---- 2. Install the .NET 8 SDK with winget if needed ----
if not defined DOTNET (
    echo   The .NET SDK was not found. Installing it with winget ^(one time, ~250 MB^)...
    echo.
    winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
    if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
)

if not defined DOTNET (
    echo.
    echo   [!] Could not install the .NET SDK automatically.
    echo       Download ".NET 8 SDK" for Windows x64 from https://dotnet.microsoft.com/download
    echo       install it, then run build.bat again.
    echo.
    pause
    exit /b 1
)

rem ---- 3. Build a single self-contained .exe ----
echo.
echo   Building... (the first build downloads some packages and takes a minute)
echo.
"%DOTNET%" publish "ZenithOptimizer\ZenithOptimizer.csproj" -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "%~dp0dist" -nologo

if errorlevel 1 (
    echo.
    echo   [!] Build failed. Scroll up to see the error.
    echo.
    pause
    exit /b 1
)

echo.
echo   ==============================================
echo     Done!  dist\ZenithOptimizer.exe
echo   ==============================================
echo.
echo   Just double-click it - it asks for admin rights by itself.
echo   You can copy the .exe anywhere; it needs no install.
echo.
start "" explorer "%~dp0dist"
pause
