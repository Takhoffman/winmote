@echo off
setlocal

set "PLUGIN_ROOT=%~dp0"
set "SCRIPT=%PLUGIN_ROOT%scripts\winmote-mcp.cmd"

if exist "%SCRIPT%" (
  call "%SCRIPT%"
  exit /b %errorlevel%
)

echo winmote plugin launcher could not find scripts\winmote-mcp.cmd under "%PLUGIN_ROOT%" 1>&2
exit /b 1
