@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "REPO_ROOT=%%~fI"

set "REPO_DLL=%REPO_ROOT%\winmote\bin\Release\net9.0-windows10.0.19041.0\winmote.dll"
if exist "%REPO_DLL%" (
  dotnet "%REPO_DLL%" mcp
  exit /b %errorlevel%
)

set "REPO_DLL=%REPO_ROOT%\winmote\bin\Debug\net9.0-windows10.0.19041.0\winmote.dll"
if exist "%REPO_DLL%" (
  dotnet "%REPO_DLL%" mcp
  exit /b %errorlevel%
)

set "USER_EXE=%LOCALAPPDATA%\Programs\Winmote\winmote.exe"
if exist "%USER_EXE%" (
  "%USER_EXE%" mcp
  exit /b %errorlevel%
)

winmote mcp
exit /b %errorlevel%
