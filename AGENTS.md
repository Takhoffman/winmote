# Winmote Agent Memory

This file is loaded into every agent working in this repository. Keep it short, durable, and optimized for development decisions.

## Project Identity

Winmote is a Windows-only desktop automation system for screenshots, UI Automation, OCR, input, windows, taskbar/start, displays, notifications, virtual desktops, clipboard, app launch, and task scheduling.

Winmote is also a Codex plugin. Treat `.codex-plugin/plugin.json`, `.mcp.json`, `skills/`, and `scripts/winmote-mcp.cmd` as part of the product surface, not packaging afterthoughts.

The product direction is the Winmote Control Plane: a coordinated multi-agent desktop-control runtime.

- Agents and MCP clients submit intents, not raw ownership of the mouse.
- A local Winmote Coordinator owns sessions, leases, policy, action history, and execution routing.
- Overlay and tray/operator UI visualize Control Plane state.
- Input routing should prefer semantic UIA, then window-message input, then physical input only behind an explicit lease/policy.
- Physical cursor, keyboard focus, foreground window, and clipboard are global Windows resources and must be coordinated.

## Repository Rules

- Follow the `plugin-creator` skill when changing plugin structure, plugin metadata, marketplace behavior, plugin cachebuster/update flow, or `.codex-plugin/plugin.json`.
- Keep `.codex-plugin/plugin.json` valid and free of placeholders. Do not add `apps` or `mcpServers` manifest fields unless the companion files actually exist.
- Keep `.mcp.json` aligned with the plugin's MCP server entry and local startup behavior.
- Keep `winmote-skill/SKILL.md` up to date whenever CLI commands, flags, or behavior change.
- Also update `skills/winmote-cli/SKILL.md` when the installed/local skill guidance changes.
- Prefer small, coherent changes that preserve the existing single-project C#/.NET structure unless a larger architecture change is explicitly part of the work.
- Do not bury new commands only in code. Update CLI help, validation, argument parsing, docs, and skill files together.
- Treat user-visible desktop automation as safety-sensitive. Avoid real clicks, keystrokes, process kills, power actions, display changes, or destructive app actions during tests unless explicitly requested.

## Plugin-Creator Workflow

Use `C:\Users\Tak-Windows10\.codex\skills\.system\plugin-creator\SKILL.md` for any work that changes the Codex plugin surface. This includes `.codex-plugin/plugin.json`, `.mcp.json`, `scripts/winmote-mcp.cmd`, plugin `skills/`, marketplace entries, cachebuster/update behavior, install/reinstall instructions, or plugin directory layout.

When the skill applies:

- Read the `plugin-creator` skill first, then inspect Winmote's plugin files before editing: `.codex-plugin/plugin.json`, `.mcp.json`, `scripts/winmote-mcp.cmd`, `skills/winmote-cli/SKILL.md`, and `winmote-skill/SKILL.md`.
- Keep the manifest schema-valid. The plugin folder name and plugin manifest `"name"` must stay aligned as `winmote`; required structure must remain present; unsupported manifest fields must not be added.
- Do not hand-edit marketplace files for local development updates. Use the skill's cachebuster/update flow, especially `scripts/update_plugin_cachebuster.py`, and consult `references/installing-and-updating.md` from the skill when reinstall behavior matters.
- Run the skill validator after plugin-surface edits:
  `python C:\Users\Tak-Windows10\.codex\skills\.system\plugin-creator\scripts\validate_plugin.py C:\github.com\takhoffman\winmote`
- If plugin behavior, CLI commands, flags, or startup behavior changed, also update both skill docs and run safe smoke tests for help/argument parsing or MCP startup paths.
- In final handoffs for plugin work, state which plugin files changed, the validator command/result, any smoke tests run, and whether a cachebuster/reinstall step is still needed.

## Current Technical Shape

- Main app: `winmote/Program.cs`
- Project: `winmote/winmote.csproj`
- CLI launcher/install helper: `winmote.bat`
- Plugin manifest: `.codex-plugin/plugin.json`
- Plugin MCP config: `.mcp.json`
- Plugin MCP launcher: `scripts/winmote-mcp.cmd`
- Public skill docs: `winmote-skill/SKILL.md`
- Local skill docs: `skills/winmote-cli/SKILL.md`

## Development Defaults

- Use `rg` / `rg --files` first for search.
- Build with `dotnet build .\winmote\winmote.csproj`.
- Validate plugin changes with the plugin-creator validator from `C:\Users\Tak-Windows10\.codex\skills\.system\plugin-creator\scripts\validate_plugin.py`.
- Before finalizing behavior changes, run command-level smoke tests for help/argument parsing and safe non-destructive paths.
- Existing builds may show a `WinRT.Runtime` assembly conflict warning. Report it, but do not treat it as failure unless it becomes an error or changes behavior.

## Token-Efficient Agent Workflow

- Start by reading this file, then only the source files needed for the current task.
- Prefer `rg -n "<symbol>"` and targeted `Get-Content ... | Select-Object -Skip ... -First ...` reads over loading all of `Program.cs`.
- For plugin work, inspect `.codex-plugin/plugin.json`, `.mcp.json`, `scripts/winmote-mcp.cmd`, and relevant skill docs before reading unrelated source.
