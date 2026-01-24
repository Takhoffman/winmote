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

## Notes

- Output is key=value lines (no JSON I/O).
- Most input defaults to human-like motion unless `--human false` is provided.
- Screenshots return scale + display mapping fields for precise coordinate conversion.
