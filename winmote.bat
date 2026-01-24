@echo off
setlocal enabledelayedexpansion

set CMD=%1
if "%CMD%"=="" goto :usage

if /I "%CMD%"=="install" goto :install
if /I "%CMD%"=="update" goto :update
if /I "%CMD%"=="build" goto :build

:usage
echo Usage: winmote.bat install ^| update ^| build
exit /b 1

:install
call :ensure_dotnet
if errorlevel 1 exit /b 1
call :build
if errorlevel 1 exit /b 1
call :copy
if errorlevel 1 exit /b 1
call :addpath
if errorlevel 1 exit /b 1
call :verify
if errorlevel 1 exit /b 1
echo Winmote installed. Restart your terminal to use "winmote".
exit /b 0

:update
call :ensure_dotnet
if errorlevel 1 exit /b 1
call :build
if errorlevel 1 exit /b 1
call :copy
if errorlevel 1 exit /b 1
call :verify
if errorlevel 1 exit /b 1
echo Winmote updated.
exit /b 0

:build
REM Framework-dependent build (avoids publish conflicts)
if not exist ".\winmote" (
  echo ERROR: .\winmote not found. Run this from the repo root.
  exit /b 1
)
dotnet build .\winmote -c Release
exit /b %errorlevel%

:ensure_dotnet
where dotnet >nul 2>&1
if errorlevel 1 goto :install_dotnet
for /f "delims=" %%A in ('dotnet --list-sdks 2^>nul ^| findstr /R /C:"^9\.0\."') do set HAS_SDK=1
if defined HAS_SDK exit /b 0
goto :install_dotnet

:install_dotnet
echo .NET 9 SDK not found. Installing...
where winget >nul 2>&1
if not errorlevel 1 (
  winget install -e --id Microsoft.DotNet.SDK.9 --accept-package-agreements --accept-source-agreements
  if errorlevel 1 (
    echo ERROR: winget failed to install .NET 9 SDK.
    exit /b 1
  )
  exit /b 0
)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$i=$env:TEMP+'\\dotnet-install.ps1';" ^
  "Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $i;" ^
  "& $i -Channel 9.0 -Quality GA;" ^
  "Remove-Item $i -Force"
if errorlevel 1 (
  echo ERROR: dotnet-install failed. Install .NET 9 SDK manually and re-run.
  exit /b 1
)
set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
exit /b 0

:copy
set TARGET=C:\Program Files\Winmote
if not exist "%TARGET%" mkdir "%TARGET%"
set SRCDIR=.\winmote\bin\Release\net9.0-windows10.0.19041.0
if not exist "%SRCDIR%" (
  echo ERROR: Build output not found at %SRCDIR%.
  exit /b 1
)
copy /y "%SRCDIR%\winmote.exe" "%TARGET%\winmote.exe" >nul
copy /y "%SRCDIR%\winmote.dll" "%TARGET%\winmote.dll" >nul
copy /y "%SRCDIR%\winmote.deps.json" "%TARGET%\winmote.deps.json" >nul
copy /y "%SRCDIR%\winmote.runtimeconfig.json" "%TARGET%\winmote.runtimeconfig.json" >nul
if exist "%SRCDIR%\WinRT.Runtime.dll" copy /y "%SRCDIR%\WinRT.Runtime.dll" "%TARGET%\WinRT.Runtime.dll" >nul
if exist "%SRCDIR%\Microsoft.Windows.SDK.NET.dll" copy /y "%SRCDIR%\Microsoft.Windows.SDK.NET.dll" "%TARGET%\Microsoft.Windows.SDK.NET.dll" >nul
if errorlevel 1 (
  echo ERROR: Copy failed. Try running this script as Administrator.
  exit /b 1
)
exit /b 0

:addpath
set "OLDPATH="
for /f "usebackq tokens=2,*" %%A in (`reg query "HKCU\Environment" /v Path 2^>nul`) do set OLDPATH=%%B
echo %OLDPATH% | find /I "C:\Program Files\Winmote" >nul
if errorlevel 1 (
  if "%OLDPATH%"=="" (
    set NEWPATH=C:\Program Files\Winmote
  ) else (
    set NEWPATH=%OLDPATH%;C:\Program Files\Winmote
  )
  reg add "HKCU\Environment" /v Path /t REG_EXPAND_SZ /d "%NEWPATH%" /f >nul
)
exit /b 0

:verify
set TARGET=C:\Program Files\Winmote
set EXE=%TARGET%\winmote.exe
set DLL=%TARGET%\winmote.dll
if not exist "%EXE%" (
  echo ERROR: %EXE% not found.
  exit /b 1
)
if not exist "%DLL%" (
  echo ERROR: %DLL% not found.
  exit /b 1
)
REM Sanity check: run help
"%EXE%" help >nul 2>&1
if errorlevel 1 (
  echo ERROR: winmote.exe failed to run. Try restarting the terminal.
  exit /b 1
)
exit /b 0
