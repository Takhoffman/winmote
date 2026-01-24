---
name: winmote-cli
description: Windows desktop automation CLI for screenshots, UIA, OCR, input, windows, taskbar/start, displays, notifications, virtual desktops, clipboard, UWP, and task scheduling. Use when operating the winmote CLI to control the local Windows desktop.
---

# Winmote CLI Skill

## Quick use
- Use the local CLI to drive the Windows desktop.
- Output is human-readable by default. Use `--format kv` for key=value.
- Most commands default to human-like input unless `--human false` is set.
- Commands are noun-verb subcommands: `winmote <noun> <verb> [options]`.

## Command map (noun → verbs)

Display / Screen
- `display geometry` — list display geometry/DPI/scale.
- `display list` — list connected displays and states.
- `display enable --display <n>|--name <\\\\.\\DISPLAY1> [--width <n> --height <n>]` — enable a display.
- `display disable --display <n>|--name <\\\\.\\DISPLAY1>` — disable a display.
- `display primary --display <n>|--name <\\\\.\\DISPLAY1>` — set primary display.
- `display orientation --display <n>|--name <\\\\.\\DISPLAY1> --orientation 0|90|180|270` — rotate display.
- `screen capture [--display <n>|--rect x,y,w,h|--hwnd <handle>] [--format png|jpg] [--return path|b64] [--grid true|false]` — screenshot.
- `screen hash [--display <n>|--rect x,y,w,h|--hwnd <handle>] [--algo ahash|sha256]` — hash region.
- `screen diff --a <path>|--a-hash <hash> --b <path>|--b-hash <hash>` — compare images.

Windows / Desktop
- `window active` — get active window info.
- `window list [--title-contains <text>] [--exe-contains <text>] [--visible-only true|false]` — list windows.
- `window focus --hwnd <handle>|--title-contains <text>|--exe-contains <text>` — focus a window.
- `window move --hwnd <handle>|--title-contains <text> --x <n> --y <n> | --rect x,y,w,h` — move a window.
- `window resize --hwnd <handle>|--title-contains <text> --w <n> --h <n> | --rect x,y,w,h` — resize a window.
- `window minimize --hwnd <handle>|--title-contains <text>` — minimize.
- `window maximize --hwnd <handle>|--title-contains <text>` — maximize.
- `window restore --hwnd <handle>|--title-contains <text>` — restore.
- `window close --hwnd <handle>|--title-contains <text>` — close.
- `desktop list` — list virtual desktops.
- `desktop switch --index <n>|--id <guid>` — switch desktops.
- `desktop move-window --hwnd <handle>|--title-contains <text> --index <n>|--id <guid>` — move window.

Mouse / Keyboard
- `mouse move --x <n> --y <n> [--mode abs|rel] [--human true|false]` — move cursor.
- `mouse click --x <n> --y <n> [--button left|right|middle]` — click.
- `mouse down --x <n> --y <n> [--button left|right|middle]` — button down.
- `mouse up --x <n> --y <n> [--button left|right|middle]` — button up.
- `mouse drag --from x,y --to x,y [--button left|right|middle]` — drag.
- `mouse wheel --x <n> --y <n> --delta <n>` — scroll.
- `mouse pos` — get cursor position.
- `key tap --key CTRL --key L | --keys CTRL,L` — hotkey chord.
- `key down --key SHIFT` — key down.
- `key up --key SHIFT` — key up.
- `keyboard type --text <text> [--method sendinput|paste] [--enter true|false]` — type text.

UIA / OCR
- `uia dump [--hwnd <handle>] [--max-depth <n>] [--max-nodes <n>]` — UIA tree.
- `uia find [--hwnd <handle>] [--name <text>] [--control-type <type>] [--nth <n>]` — find element.
- `uia click [--hwnd <handle>] [--name <text>] [--control-type <type>]` — click element.
- `uia set [--hwnd <handle>] [--name <text>] [--control-type <type>] --value <text>` — set value.
- `uia active` — active control info.
- `uia caret` — caret position.
- `ocr run [--rect x,y,w,h|--display <n>|--hwnd <handle>] [--language en-US]` — OCR region.

Clipboard / Files / Apps
- `clipboard get [--format text|html|rtf|image|files]` — read clipboard.
- `clipboard set --text <text> [--format text|html|rtf]` — set clipboard.
- `clipboard clear` — clear clipboard.
- `clipboard list` — list supported formats.
- `file open --path <file|url>` — open with default handler.
- `app list` — list installed apps.
- `app launch --path <exe|appname> [--args <args>]` — launch app.

System / Power / Time
- `system info` — OS/elevation/bitness info.
- `power lock` — lock the workstation.
- `power sleep` — sleep the system.
- `power shutdown` — shut down the system.
- `power restart` — restart the system.
- `power wake` — wake display.
- `time sleep --ms <n>` — sleep for N ms.

Taskbar / Start / Notifications
- `taskbar click --name <text>` — click a taskbar app.
- `taskbar pin --path <exe|appname>` — pin to taskbar.
- `taskbar unpin --path <exe|appname>` — unpin from taskbar.
- `start-menu search --text <query> [--enter true|false]` — start menu search.
- `notifications list` — list notifications.
- `notification clear [--tag <tag>] [--group <group>] [--app <app>]` — clear notifications.
- `notification click --text <text> [--pattern <regex>]` — click a notification.

Smart click
- `text click --text <text> [--rect x,y,w,h|--display <n>|--hwnd <handle>]` — OCR + click.
- `icon click --icon <path> [--rect x,y,w,h|--display <n>|--hwnd <handle>]` — template match + click.

UWP / DPI / Tasks / Human
- `uwp list` — list UWP apps.
- `uwp launch --aumid <id> [--args <args>]` — launch UWP app.
- `dpi status` — DPI awareness info.
- `dpi test [--display <n>|--rect x,y,w,h|--hwnd <handle>]` — DPI test capture.
- `task list [--name <task>]` — list scheduled tasks.
- `task create --name <task> --cmd <path> [--args <args>]` — create task.
- `task run --name <task>` — run task.
- `task delete --name <task>` — delete task.
- `profile list` — list humanization profiles.
- `profile get` — show current humanization config.
- `profile set --profile <name> [--seed <n>]` — set profile.

## When adding commands
-
