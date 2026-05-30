# Winmote

Winmote is a Windows-only CLI for desktop automation: screenshots, UIA, OCR, mouse/keyboard input, window management, taskbar/start menu, displays, audio, notifications, virtual desktops, clipboard, and task scheduling.

## Build

```powershell
dotnet build .\deskctl -c Release
```

## Run

```powershell
dotnet .\deskctl\bin\Release\net9.0-windows10.0.19041.0\winmote.dll help
```

## Install (simple)

Use the batch file from the repo root:

```powershell
.\winmote.bat install
```

This publishes a single-file `winmote.exe`, copies it to `C:\Program Files\Winmote`, and adds that folder to your user PATH. You may need to run the script as Administrator to write to Program Files.

## CLI shape

Winmote uses noun-verb subcommands:

```
winmote <noun> <verb> [options]
```

Examples:

```
winmote screen capture
winmote mouse move --x 100 --y 200
winmote keyboard type --text "hello" --enter
winmote window list --visible-only true
```

## Control Plane

Winmote is moving toward a local Control Plane architecture. The Coordinator keeps agent sessions, resource leases, and action history so multiple agents can operate without fighting over global Windows resources.

Examples:

```powershell
winmote coordinator status
winmote session create --agent Codex --display 0
winmote lease acquire --agent Codex --resource display:0 --mode message
winmote action submit --agent Codex --type click --x 300 --y 400
winmote overlay update --agent Codex --x 300 --y 400 --pulse true
winmote overlay render --duration-ms 1200
winmote history list --limit 20
```

`action submit` defaults to dry-run so intent can be audited before real input is sent.
MCP calls that require an agent can omit it; Winmote defaults to `WINMOTE_AGENT`, `CODEX_AGENT_NAME`, `CODEX_SESSION_ID`, or `Codex`.

## Notes

- Output is key=value lines (no JSON I/O).
- Most input defaults to human-like motion unless `--human false` is provided.
- Screenshots return scale + display mapping fields for precise coordinate conversion.
