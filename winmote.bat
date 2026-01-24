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
