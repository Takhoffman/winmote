using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.Management.Deployment;
using Windows.ApplicationModel;
using System.Windows.Forms;
using System.Windows.Automation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Winmote;

public static class Program
{
    private const string AppName = "winmote";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static HumanConfig _humanConfig = HumanConfig.CreateDefault();
    private static string _outputFormat = "human";
    private static readonly Dictionary<string, string> CommandToSubcommand = new(StringComparer.OrdinalIgnoreCase)
    {
        ["get_displays"] = "display geometry",
        ["active_window"] = "window active",
        ["list_windows"] = "window list",
        ["screenshot"] = "screen capture",
        ["screen_hash"] = "screen hash",
        ["screen_diff"] = "screen diff",
        ["wait_for"] = "wait for",
        ["mouse_move"] = "mouse move",
        ["mouse_click"] = "mouse click",
        ["mouse_down"] = "mouse down",
        ["mouse_up"] = "mouse up",
        ["mouse_drag"] = "mouse drag",
        ["mouse_wheel"] = "mouse wheel",
        ["mouse_pos"] = "mouse pos",
        ["overlay_show"] = "overlay show",
        ["overlay_update"] = "overlay update",
        ["overlay_list"] = "overlay list",
        ["overlay_clear"] = "overlay clear",
        ["overlay_render"] = "overlay render",
        ["key_tap"] = "key tap",
        ["key_down"] = "key down",
        ["key_up"] = "key up",
        ["text_type"] = "keyboard type",
        ["focus_window"] = "window focus",
        ["window_move"] = "window move",
        ["window_resize"] = "window resize",
        ["window_minimize"] = "window minimize",
        ["window_maximize"] = "window maximize",
        ["window_restore"] = "window restore",
        ["window_close"] = "window close",
        ["clipboard_get"] = "clipboard get",
        ["clipboard_set"] = "clipboard set",
        ["clipboard_clear"] = "clipboard clear",
        ["clipboard_formats"] = "clipboard list",
        ["process_list"] = "process list",
        ["process_kill"] = "process kill",
        ["app_list"] = "app list",
        ["launch"] = "app launch",
        ["uia_dump"] = "uia dump",
        ["uia_find"] = "uia find",
        ["uia_click"] = "uia click",
        ["uia_set_value"] = "uia set",
        ["active_control"] = "uia active",
        ["caret_position"] = "uia caret",
        ["ocr"] = "ocr run",
        ["open_with_default"] = "file open",
        ["settings_open"] = "settings open",
        ["system_info"] = "system info",
        ["lock"] = "power lock",
        ["power_sleep"] = "power sleep",
        ["power_shutdown"] = "power shutdown",
        ["power_restart"] = "power restart",
        ["wake_display"] = "power wake",
        ["desktop_list"] = "desktop list",
        ["desktop_switch"] = "desktop switch",
        ["desktop_move_window"] = "desktop move-window",
        ["taskbar_click_app"] = "taskbar click",
        ["start_menu_search"] = "start-menu search",
        ["notifications_list"] = "notifications list",
        ["notification_clear"] = "notification clear",
        ["notification_click"] = "notification click",
        ["click_text"] = "text click",
        ["click_icon"] = "icon click",
        ["display_list"] = "display list",
        ["display_enable"] = "display enable",
        ["display_disable"] = "display disable",
        ["display_set_primary"] = "display primary",
        ["display_orientation"] = "display orientation",
        ["taskbar_pin"] = "taskbar pin",
        ["taskbar_unpin"] = "taskbar unpin",
        ["uwp_list"] = "uwp list",
        ["uwp_launch"] = "uwp launch",
        ["dpi_status"] = "dpi status",
        ["dpi_test_capture"] = "dpi test",
        ["task_list"] = "task list",
        ["task_create"] = "task create",
        ["task_run"] = "task run",
        ["task_delete"] = "task delete",
        ["sleep"] = "time sleep",
        ["human_config_set"] = "profile set",
        ["human_config_get"] = "profile get",
        ["human_profiles_list"] = "profile list",
        ["coordinator_status"] = "coordinator status",
        ["session_create"] = "session create",
        ["session_list"] = "session list",
        ["lease_acquire"] = "lease acquire",
        ["lease_list"] = "lease list",
        ["history_list"] = "history list",
        ["action_submit"] = "action submit"
    };

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Native.EnablePerMonitorDpiAwareness();
        }
        catch
        {
        }

        if (args.Length > 0 && args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase))
        {
            RunStdioLoop();
            return 0;
        }

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 2;
        }
        return RunSimpleCli(args);
    }

    private static int RunSimpleCli(string[] args)
    {
        if (args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length == 1)
            {
                PrintUsage();
                return 0;
            }
            if (args.Length >= 3 && TryMapSubcommand(args[1], args[2], out var mapped))
            {
                PrintCommandHelp(mapped);
                return 0;
            }
            PrintFriendlyError("help", $"Help requires <noun> <verb>. Example: {AppName} help screen shot");
            return 2;
        }

        if (args.Length < 2)
        {
            PrintFriendlyError("help", $"Missing command. Use '{AppName} help' to see commands.");
            return 2;
        }

        if (!TryMapSubcommand(args[0], args[1], out var cmd))
        {
            PrintFriendlyError("help", $"Unknown command '{args[0]} {args[1]}'. Use '{AppName} help' to see commands.");
            return 2;
        }

        var flags = ParseFlags(args.Skip(2).ToArray());

        if (cmd.Equals("help", StringComparison.OrdinalIgnoreCase) || flags.ContainsKey("help"))
        {
            PrintCommandHelp(cmd);
            return 0;
        }

        if (!ValidateFlagsForCommand(cmd, flags, out var error))
        {
            PrintFriendlyError(cmd, error);
            return 2;
        }

        _outputFormat = GetOutputFormat(flags);
        var argObj = BuildArgsForCommand(cmd, flags);
        JsonElement argsElement = default;
        if (argObj.Count > 0)
        {
            argsElement = JsonDocument.Parse(JsonSerializer.Serialize(argObj, JsonOptions)).RootElement;
        }

        var req = new Request
        {
            Id = "cli",
            Cmd = cmd,
            Args = argsElement
        };

        var resp = Dispatch(req);
        PrintResponse(resp, cmd);
        return resp.Ok ? 0 : 1;
    }

    private static void PrintResponse(Response resp, string cmd)
    {
        if (_outputFormat.Equals("kv", StringComparison.OrdinalIgnoreCase) || _outputFormat.Equals("machine", StringComparison.OrdinalIgnoreCase))
        {
            PrintPlainResponse(resp, cmd);
            return;
        }
        PrintHumanResponse(resp, cmd);
    }

    private static void PrintPlainResponse(Response resp, string cmd)
    {
        Console.WriteLine($"ok={resp.Ok.ToString().ToLowerInvariant()}");
        Console.WriteLine($"cmd={cmd}");
        Console.WriteLine($"ts={resp.Ts}");
        Console.WriteLine($"timing_ms={resp.TimingMs}");
        AppendCursorAndActiveWindow();
        AppendPlainEnglishNote(resp);
        if (resp.Ok)
        {
            foreach (var kv in FlattenObject(resp.Result, "result"))
            {
                Console.WriteLine($"{kv.Key}={kv.Value}");
            }
        }
        else if (resp.Error != null)
        {
            foreach (var kv in FlattenObject(resp.Error, "error"))
            {
                Console.WriteLine($"{kv.Key}={kv.Value}");
            }
        }
    }

    private static void PrintHumanResponse(Response resp, string cmd)
    {
        if (!resp.Ok)
        {
            var msg = resp.Error?.Message ?? "Unknown error";
            Console.WriteLine($"ERROR: {cmd} - {msg}");
            if (resp.Error?.Code != null)
            {
                Console.WriteLine($"- Code: {resp.Error.Code}");
            }
            return;
        }

        Console.WriteLine($"OK: {cmd}");
        var cursor = Native.GetCursorPosition();
        Console.WriteLine($"- Cursor: ({cursor.X},{cursor.Y})");
        var hwnd = Native.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            var info = WindowInfo.FromHwnd(hwnd);
            if (!string.IsNullOrWhiteSpace(info.Title))
            {
                Console.WriteLine($"- Active window: {info.Title}");
            }
            if (!string.IsNullOrWhiteSpace(info.Exe))
            {
                Console.WriteLine($"- Active app: {info.Exe}");
            }
        }

        switch (cmd.ToLowerInvariant())
        {
            case "mouse_move":
            case "mouse_click":
            case "mouse_drag":
            case "mouse_wheel":
            case "mouse_pos":
            {
                var wrote = false;
                var x = GetResultInt(resp, "x");
                var y = GetResultInt(resp, "y");
                var duration = GetResultInt(resp, "duration_ms");
                if (x.HasValue && y.HasValue)
                {
                    Console.WriteLine($"- Cursor: ({x},{y})");
                    wrote = true;
                }
                if (duration.HasValue)
                {
                    Console.WriteLine($"- Duration: {duration} ms");
                    wrote = true;
                }
                if (!wrote)
                {
                    PrintHumanDetails(resp);
                }
                break;
            }
            case "screenshot":
            {
                var wrote = false;
                var path = GetResultString(resp, "path");
                var w = GetResultInt(resp, "w");
                var h = GetResultInt(resp, "h");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    Console.WriteLine($"- Saved: {path}");
                    wrote = true;
                }
                if (w.HasValue && h.HasValue)
                {
                    Console.WriteLine($"- Image size: {w}x{h}");
                    wrote = true;
                }
                var warning = GetResultString(resp, "warning");
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    Console.WriteLine($"- Note: {warning}");
                    wrote = true;
                }
                if (!wrote)
                {
                    PrintHumanDetails(resp);
                }
                break;
            }
            case "active_window":
            case "focus_window":
            {
                var wrote = false;
                var title = GetResultString(resp, "window.title") ?? GetResultString(resp, "title");
                var exe = GetResultString(resp, "window.exe") ?? GetResultString(resp, "exe");
                if (!string.IsNullOrWhiteSpace(title))
                {
                    Console.WriteLine($"- Window: {title}");
                    wrote = true;
                }
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    Console.WriteLine($"- App: {exe}");
                    wrote = true;
                }
                if (!wrote)
                {
                    PrintHumanDetails(resp);
                }
                break;
            }
            case "list_windows":
            {
                var count = GetResultArrayCount(resp, "windows");
                if (count.HasValue)
                {
                    Console.WriteLine($"- Windows: {count}");
                }
                else
                {
                    PrintHumanDetails(resp);
                }
                break;
            }
            case "display_list":
            case "get_displays":
            {
                var count = GetResultArrayCount(resp, "displays");
                if (count.HasValue)
                {
                    Console.WriteLine($"- Displays: {count}");
                }
                else
                {
                    PrintHumanDetails(resp);
                }
                break;
            }
            case "clipboard_get":
            {
                var wrote = false;
                var format = GetResultString(resp, "format");
                var text = GetResultString(resp, "text");
                if (!string.IsNullOrWhiteSpace(format))
                {
                    Console.WriteLine($"- Format: {format}");
                    wrote = true;
                }
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var trimmed = text.Length > 120 ? text.Substring(0, 120) + "..." : text;
                    Console.WriteLine($"- Text: {trimmed}");
                    wrote = true;
                }
                if (!wrote)
                {
                    PrintHumanDetails(resp);
                }
                break;
            }
            default:
                PrintHumanDetails(resp);
                break;
        }
    }

    private static void PrintHumanDetails(Response resp)
    {
        var details = FlattenObject(resp.Result, "result")
            .Select(kv => $"{kv.Key.Replace("result.", "", StringComparison.OrdinalIgnoreCase)}: {kv.Value}")
            .ToList();
        if (details.Count == 0) return;
        Console.WriteLine("- Details:");
        foreach (var d in details.Take(12))
        {
            Console.WriteLine($"  - {d}");
        }
        if (details.Count > 12)
        {
            Console.WriteLine($"  - ... ({details.Count - 12} more)");
        }
    }

    private static int? GetResultInt(Response resp, string key)
    {
        if (TryGetResultObject(resp, key, out var value) && value != null)
        {
            if (value is int i) return i;
            if (value is long l && l >= int.MinValue && l <= int.MaxValue) return (int)l;
            if (value is double d && d >= int.MinValue && d <= int.MaxValue) return (int)d;
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string? GetResultString(Response resp, string key)
    {
        if (TryGetResultObject(resp, key, out var value) && value != null)
        {
            return value.ToString();
        }
        return null;
    }

    private static int? GetResultArrayCount(Response resp, string key)
    {
        if (TryGetResultObject(resp, key, out var value) && value is List<object?> list)
        {
            return list.Count;
        }
        return null;
    }

    private static bool TryGetResultObject(Response resp, string key, out object? value)
    {
        value = null;
        if (resp.Result is not Dictionary<string, object?> dict)
        {
            return false;
        }
        if (!key.Contains('.'))
        {
            return dict.TryGetValue(key, out value);
        }
        var parts = key.Split('.');
        object? cur = dict;
        foreach (var part in parts)
        {
            if (cur is Dictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out cur))
                {
                    return false;
                }
                continue;
            }
            return false;
        }
        value = cur;
        return true;
    }

    private static Dictionary<string, List<string>> ParseFlags(string[] args)
    {
        var flags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-h" || arg == "--help")
            {
                AddFlagValue(flags, "help", "true");
                continue;
            }
            if (!arg.StartsWith("-"))
            {
                continue;
            }
            var name = arg.TrimStart('-');
            name = NormalizeShortFlag(name);
            string value = "true";
            if (i + 1 < args.Length && (!args[i + 1].StartsWith("-") || IsNegativeNumber(args[i + 1]) || IsCsvNumbers(args[i + 1])))
            {
                value = args[++i];
            }
            AddFlagValue(flags, name, value);
        }
        return flags;
    }

    private static string GetOutputFormat(Dictionary<string, List<string>> flags)
    {
        if (TryGetString(flags, out var format, "format"))
        {
            return format;
        }
        return "human";
    }

    private static void AddFlagValue(Dictionary<string, List<string>> flags, string name, string value)
    {
        if (!flags.TryGetValue(name, out var list))
        {
            list = new List<string>();
            flags[name] = list;
        }
        list.Add(value);
    }

    private static bool ValidateFlagsForCommand(string cmd, Dictionary<string, List<string>> flags, out string error)
    {
        error = "";
        var allowed = GetAllowedFlags(cmd);
        if (allowed == null)
        {
            var suggestion = SuggestCommand(cmd);
            error = suggestion == null
                ? $"Unknown command '{cmd}'. Use '{AppName} help' to see available commands."
                : $"Unknown command '{cmd}'. Did you mean '{suggestion}'?";
            return false;
        }

        foreach (var key in flags.Keys)
        {
            if (!allowed.Contains(key))
            {
                var suggestion = SuggestFlag(key, allowed);
                error = suggestion == null
                    ? $"Unknown option '--{key}'."
                    : $"Unknown option '--{key}'. Did you mean '--{suggestion}'?";
                return false;
            }
        }

        var missing = GetMissingRequiredFlags(cmd, flags);
        if (missing.Count > 0)
        {
            error = $"Missing required option(s): {string.Join(", ", missing.Select(m => $"--{m}"))}.";
            return false;
        }

        if (!ValidateFlagValues(cmd, flags, out error))
        {
            return false;
        }

        return true;
    }

    private static HashSet<string>? GetAllowedFlags(string cmd)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "help", "human", "format" };
        switch (cmd.ToLowerInvariant())
        {
            case "get_displays":
            case "active_window":
            case "mouse_pos":
            case "human_config_get":
            case "human_profiles_list":
            case "clipboard_clear":
            case "clipboard_formats":
            case "process_list":
            case "app_list":
            case "system_info":
            case "active_control":
            case "caret_position":
            case "desktop_list":
            case "notifications_list":
            case "lock":
            case "power_sleep":
            case "power_shutdown":
            case "power_restart":
            case "wake_display":
            case "display_list":
            case "uwp_list":
            case "dpi_status":
                return set;
            case "list_windows":
                set.UnionWith(new[] { "title-contains", "title_contains", "exe-contains", "exe_contains", "visible-only", "visible_only" });
                return set;
            case "screenshot":
                set.UnionWith(new[] { "display", "rect", "hwnd", "format", "return", "max-w", "max_w", "max-h", "max_h", "quality", "include-cursor", "include_cursor", "grid", "grid-step", "grid_step", "grid-abs", "grid_abs" });
                return set;
            case "screen_hash":
                set.UnionWith(new[] { "display", "rect", "hwnd", "algo", "max-w", "max_w", "max-h", "max_h" });
                return set;
            case "screen_diff":
                set.UnionWith(new[] { "a", "b", "a-hash", "a_hash", "b-hash", "b_hash", "algo", "threshold" });
                return set;
            case "wait_for":
                set.UnionWith(new[] { "type", "timeout-ms", "timeout_ms", "poll-ms", "poll_ms", "title-contains", "title_contains", "hwnd", "pattern", "rect", "display", "min-change", "min_change", "name", "automation-id", "automation_id", "control-type", "control_type", "class-name", "class_name", "nth", "language" });
                return set;
            case "mouse_move":
                set.UnionWith(new[] { "x", "y", "mode", "duration-ms", "duration_ms", "input-mode", "input_mode", "overlay-ms", "overlay_ms", "agent", "label" });
                return set;
            case "mouse_click":
                set.UnionWith(new[] { "x", "y", "button", "clicks", "input-mode", "input_mode", "overlay-ms", "overlay_ms", "agent", "label" });
                return set;
            case "mouse_down":
            case "mouse_up":
                set.UnionWith(new[] { "x", "y", "button" });
                return set;
            case "mouse_drag":
                set.UnionWith(new[] { "from", "to", "button", "duration-ms", "duration_ms" });
                return set;
            case "mouse_wheel":
                set.UnionWith(new[] { "x", "y", "delta" });
                return set;
            case "overlay_show":
                set.UnionWith(new[] { "x", "y", "duration-ms", "duration_ms", "pulse", "agent", "label" });
                return set;
            case "overlay_update":
                set.UnionWith(new[] { "agent", "label", "x", "y", "display", "app", "window", "hwnd", "pulse", "ttl-ms", "ttl_ms" });
                return set;
            case "overlay_clear":
                set.UnionWith(new[] { "agent" });
                return set;
            case "overlay_list":
                set.UnionWith(new[] { "agent" });
                return set;
            case "overlay_render":
                set.UnionWith(new[] { "duration-ms", "duration_ms" });
                return set;
            case "key_tap":
            case "key_down":
            case "key_up":
                set.UnionWith(new[] { "keys", "key" });
                return set;
            case "text_type":
                set.UnionWith(new[] { "text", "method", "enter" });
                return set;
            case "clipboard_get":
                set.UnionWith(new[] { "format" });
                return set;
            case "clipboard_set":
                set.UnionWith(new[] { "text", "format" });
                return set;
            case "focus_window":
                set.UnionWith(new[] { "hwnd", "title-contains", "title_contains", "exe-contains", "exe_contains", "nth" });
                return set;
            case "window_move":
            case "window_resize":
                set.UnionWith(new[] { "hwnd", "title-contains", "title_contains", "exe-contains", "exe_contains", "nth", "x", "y", "w", "h", "rect" });
                return set;
            case "window_minimize":
            case "window_maximize":
            case "window_restore":
            case "window_close":
                set.UnionWith(new[] { "hwnd", "title-contains", "title_contains", "exe-contains", "exe_contains", "nth" });
                return set;
            case "process_kill":
                set.UnionWith(new[] { "pid", "name-contains", "name_contains", "force" });
                return set;
            case "uia_dump":
                set.UnionWith(new[] { "hwnd", "max-depth", "max_depth", "max-nodes", "max_nodes", "include-rects", "include_rects" });
                return set;
            case "uia_find":
                set.UnionWith(new[] { "hwnd", "name", "automation-id", "automation_id", "control-type", "control_type", "class-name", "class_name", "nth" });
                return set;
            case "uia_click":
                set.UnionWith(new[] { "hwnd", "name", "automation-id", "automation_id", "control-type", "control_type", "class-name", "class_name", "nth", "button" });
                return set;
            case "uia_set_value":
                set.UnionWith(new[] { "hwnd", "name", "automation-id", "automation_id", "control-type", "control_type", "class-name", "class_name", "nth", "value" });
                return set;
            case "ocr":
                set.UnionWith(new[] { "rect", "display", "hwnd", "language" });
                return set;
            case "open_with_default":
                set.UnionWith(new[] { "path" });
                return set;
            case "settings_open":
                set.UnionWith(new[] { "page", "uri" });
                return set;
            case "desktop_switch":
                set.UnionWith(new[] { "index", "id" });
                return set;
            case "taskbar_click_app":
                set.UnionWith(new[] { "name" });
                return set;
            case "desktop_move_window":
                set.UnionWith(new[] { "hwnd", "title-contains", "title_contains", "exe-contains", "exe_contains", "nth", "index", "id" });
                return set;
            case "start_menu_search":
                set.UnionWith(new[] { "text", "enter", "open" });
                return set;
            case "notification_clear":
                set.UnionWith(new[] { "tag", "group", "app" });
                return set;
            case "notification_click":
                set.UnionWith(new[] { "text", "pattern", "rect", "display", "language", "button" });
                return set;
            case "display_enable":
            case "display_disable":
                set.UnionWith(new[] { "display", "name", "width", "height" });
                return set;
            case "display_set_primary":
                set.UnionWith(new[] { "display", "name" });
                return set;
            case "display_orientation":
                set.UnionWith(new[] { "display", "name", "orientation" });
                return set;
            case "taskbar_pin":
            case "taskbar_unpin":
                set.UnionWith(new[] { "path" });
                return set;
            case "task_create":
                set.UnionWith(new[] { "name", "cmd", "args", "schedule", "time", "date", "interval", "user", "password" });
                return set;
            case "task_run":
            case "task_delete":
                set.UnionWith(new[] { "name" });
                return set;
            case "task_list":
                set.UnionWith(new[] { "name" });
                return set;
            case "uwp_launch":
                set.UnionWith(new[] { "aumid", "args" });
                return set;
            case "dpi_test_capture":
                set.UnionWith(new[] { "display", "rect", "hwnd", "size", "grid" });
                return set;
            case "click_text":
                set.UnionWith(new[] { "text", "pattern", "rect", "display", "hwnd", "language", "button" });
                return set;
            case "click_icon":
                set.UnionWith(new[] { "icon", "rect", "display", "hwnd", "threshold", "button" });
                return set;
            case "launch":
                set.UnionWith(new[] { "path", "args", "cwd" });
                return set;
            case "sleep":
                set.UnionWith(new[] { "ms" });
                return set;
            case "human_config_set":
                set.UnionWith(new[] { "profile", "seed" });
                return set;
            case "coordinator_status":
            case "session_list":
                return set;
            case "session_create":
                set.UnionWith(new[] { "agent", "display", "name" });
                return set;
            case "lease_acquire":
                set.UnionWith(new[] { "agent", "resource", "mode", "ttl-ms", "ttl_ms" });
                return set;
            case "lease_list":
                set.UnionWith(new[] { "agent", "resource" });
                return set;
            case "history_list":
                set.UnionWith(new[] { "limit", "agent" });
                return set;
            case "action_submit":
                set.UnionWith(new[] { "agent", "type", "x", "y", "display", "hwnd", "input-mode", "input_mode", "dry-run", "dry_run" });
                return set;
            default:
                return null;
        }
    }

    private static List<string> GetMissingRequiredFlags(string cmd, Dictionary<string, List<string>> flags)
    {
        var missing = new List<string>();
        switch (cmd.ToLowerInvariant())
        {
            case "screenshot":
                // default to display 0 if nothing specified
                break;
            case "screen_hash":
                // default to display 0 if nothing specified
                break;
            case "ocr":
                // default to display 0 if nothing specified
                break;
            case "mouse_move":
            case "mouse_click":
            case "mouse_wheel":
            case "overlay_show":
                if (!flags.ContainsKey("x")) missing.Add("x");
                if (!flags.ContainsKey("y")) missing.Add("y");
                if (cmd.Equals("mouse_wheel", StringComparison.OrdinalIgnoreCase) && !flags.ContainsKey("delta")) missing.Add("delta");
                break;
            case "overlay_update":
                if (!flags.ContainsKey("agent") && !flags.ContainsKey("label")) missing.Add("agent");
                if (!flags.ContainsKey("x")) missing.Add("x");
                if (!flags.ContainsKey("y")) missing.Add("y");
                break;
            case "mouse_drag":
                if (!flags.ContainsKey("from")) missing.Add("from");
                if (!flags.ContainsKey("to")) missing.Add("to");
                break;
            case "key_tap":
                if (!flags.ContainsKey("keys") && !flags.ContainsKey("key")) missing.Add("key(s)");
                break;
            case "key_down":
            case "key_up":
                if (!flags.ContainsKey("keys") && !flags.ContainsKey("key")) missing.Add("key(s)");
                break;
            case "text_type":
                if (!flags.ContainsKey("text")) missing.Add("text");
                break;
            case "focus_window":
            case "window_move":
            case "window_resize":
            case "window_minimize":
            case "window_maximize":
            case "window_restore":
            case "window_close":
                if (!flags.ContainsKey("hwnd") && !flags.ContainsKey("title-contains") && !flags.ContainsKey("title_contains")
                    && !flags.ContainsKey("exe-contains") && !flags.ContainsKey("exe_contains"))
                {
                    missing.Add("hwnd|title-contains|exe-contains");
                }
                break;
            case "launch":
                if (!flags.ContainsKey("path")) missing.Add("path");
                break;
            case "open_with_default":
                if (!flags.ContainsKey("path")) missing.Add("path");
                break;
            case "settings_open":
                if (!flags.ContainsKey("page") && !flags.ContainsKey("uri")) missing.Add("page|uri");
                break;
            case "clipboard_set":
                if (!flags.ContainsKey("text")) missing.Add("text");
                break;
            case "clipboard_get":
                if (!flags.ContainsKey("format") && flags.ContainsKey("text"))
                {
                    // allow previous behavior; no-op
                }
                break;
            case "process_kill":
                if (!flags.ContainsKey("pid") && !flags.ContainsKey("name-contains") && !flags.ContainsKey("name_contains"))
                {
                    missing.Add("pid|name-contains");
                }
                break;
            case "screen_diff":
                if (!flags.ContainsKey("a") && !flags.ContainsKey("a-hash") && !flags.ContainsKey("a_hash"))
                {
                    missing.Add("a|a-hash");
                }
                if (!flags.ContainsKey("b") && !flags.ContainsKey("b-hash") && !flags.ContainsKey("b_hash"))
                {
                    missing.Add("b|b-hash");
                }
                break;
            case "wait_for":
                if (!flags.ContainsKey("type")) missing.Add("type");
                break;
            case "desktop_switch":
                if (!flags.ContainsKey("index") && !flags.ContainsKey("id")) missing.Add("index|id");
                break;
            case "desktop_move_window":
                if (!flags.ContainsKey("index") && !flags.ContainsKey("id")) missing.Add("index|id");
                if (!flags.ContainsKey("hwnd") && !flags.ContainsKey("title-contains") && !flags.ContainsKey("title_contains")
                    && !flags.ContainsKey("exe-contains") && !flags.ContainsKey("exe_contains"))
                {
                    missing.Add("hwnd|title-contains|exe-contains");
                }
                break;
            case "taskbar_click_app":
                if (!flags.ContainsKey("name")) missing.Add("name");
                break;
            case "start_menu_search":
                if (!flags.ContainsKey("text")) missing.Add("text");
                break;
            case "display_enable":
            case "display_disable":
            case "display_set_primary":
            case "display_orientation":
                if (!flags.ContainsKey("display") && !flags.ContainsKey("name")) missing.Add("display|name");
                if (cmd.Equals("display_orientation", StringComparison.OrdinalIgnoreCase) && !flags.ContainsKey("orientation"))
                {
                    missing.Add("orientation");
                }
                break;
            case "taskbar_pin":
            case "taskbar_unpin":
                if (!flags.ContainsKey("path")) missing.Add("path");
                break;
            case "task_create":
                if (!flags.ContainsKey("name")) missing.Add("name");
                if (!flags.ContainsKey("cmd")) missing.Add("cmd");
                break;
            case "task_run":
            case "task_delete":
                if (!flags.ContainsKey("name")) missing.Add("name");
                break;
            case "uwp_launch":
                if (!flags.ContainsKey("aumid")) missing.Add("aumid");
                break;
            case "sleep":
                if (!flags.ContainsKey("ms")) missing.Add("ms");
                break;
            case "session_create":
                if (!flags.ContainsKey("agent")) missing.Add("agent");
                break;
            case "lease_acquire":
                if (!flags.ContainsKey("agent")) missing.Add("agent");
                if (!flags.ContainsKey("resource")) missing.Add("resource");
                break;
            case "action_submit":
                if (!flags.ContainsKey("agent")) missing.Add("agent");
                if (!flags.ContainsKey("type")) missing.Add("type");
                break;
        }
        return missing;
    }

    private static void PrintFriendlyError(string cmd, string message)
    {
        Console.Error.WriteLine("error: " + message);
        var usage = GetCommandUsage(cmd);
        if (!string.IsNullOrWhiteSpace(usage))
        {
            Console.Error.WriteLine($"usage: {FormatCmdLine(usage)}");
        }
        var examples = GetCommandExamples(cmd);
        if (examples.Length > 0)
        {
            Console.Error.WriteLine("example:");
            foreach (var ex in examples)
            {
                Console.Error.WriteLine("  " + FormatCmdLine(ex));
            }
        }
        Console.Error.WriteLine($"hint: run '{AppName} help' for examples.");
    }

    private static string WithAppName(string text)
    {
        return text.Replace("deskctl", AppName, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteHelpLine(string text)
    {
        Console.Error.WriteLine(FormatCmdLine(text));
    }

    private static string FormatCmdLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return line;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("deskctl", StringComparison.OrdinalIgnoreCase))
        {
            return WithAppName(line);
        }

        var parts = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return WithAppName(line);
        }

        var cmd = parts[1].ToLowerInvariant();
        if (CommandToSubcommand.TryGetValue(cmd, out var sub))
        {
            var tail = parts.Length > 2 ? " " + parts[2] : "";
            return $"{AppName} {sub}{tail}";
        }

        return WithAppName(line);
    }

    private static Dictionary<string, object?> BuildArgsForCommand(string cmd, Dictionary<string, List<string>> flags)
    {
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var humanEnabled = TryGetBool(flags, "human") ?? true;
        args["human"] = new { enabled = humanEnabled };

        switch (cmd.ToLowerInvariant())
        {
            case "get_displays":
            case "active_window":
            case "human_config_get":
            case "human_profiles_list":
            case "clipboard_clear":
            case "process_list":
            case "app_list":
            case "system_info":
            case "active_control":
            case "caret_position":
            case "lock":
            case "power_sleep":
            case "power_shutdown":
            case "power_restart":
            case "wake_display":
                return args;
            case "clipboard_get":
                if (TryGetString(flags, out var cfmt, "format")) args["format"] = cfmt;
                return args;
            case "list_windows":
                if (TryGetString(flags, out var title, "title-contains", "title_contains")) args["title_contains"] = title;
                if (TryGetString(flags, out var exe, "exe-contains", "exe_contains")) args["exe_contains"] = exe;
                if (TryGetBool(flags, "visible-only", "visible_only") is bool vis) args["visible_only"] = vis;
                return args;
            case "screenshot":
                if (TryGetInt(flags, out var display, "display")) args["display"] = display;
                if (TryGetString(flags, out var hwnd, "hwnd")) args["hwnd"] = hwnd;
                if (TryGetString(flags, out var rect, "rect") && TryParseRectCsv(rect, out var r))
                    args["rect"] = new { x = r.X, y = r.Y, w = r.W, h = r.H };
                if (TryGetString(flags, out var format, "format")) args["format"] = format;
                if (TryGetString(flags, out var ret, "return")) args["return"] = ret;
                if (TryGetInt(flags, out var maxW, "max-w", "max_w")) args["max_w"] = maxW;
                if (TryGetInt(flags, out var maxH, "max-h", "max_h")) args["max_h"] = maxH;
                if (TryGetInt(flags, out var quality, "quality")) args["quality"] = quality;
                if (TryGetBool(flags, "include-cursor", "include_cursor") is bool ic) args["include_cursor"] = ic;
                if (TryGetBool(flags, "grid") is bool grid) args["grid"] = grid;
                if (TryGetInt(flags, out var gridStep, "grid-step", "grid_step")) args["grid_step"] = gridStep;
                if (TryGetBool(flags, "grid-abs", "grid_abs") is bool gridAbs) args["grid_abs"] = gridAbs;
                if (!args.ContainsKey("display") && !args.ContainsKey("rect") && !args.ContainsKey("hwnd"))
                {
                    args["display"] = 0;
                }
                if (!args.ContainsKey("return"))
                {
                    args["return"] = "path";
                }
                if (!args.ContainsKey("format"))
                {
                    args["format"] = "png";
                }
                if (!args.ContainsKey("include_cursor"))
                {
                    args["include_cursor"] = true;
                }
                if (!args.ContainsKey("max_w") && !args.ContainsKey("max_h"))
                {
                    args["max_w"] = 1600;
                }
                return args;
            case "screen_hash":
                if (TryGetInt(flags, out var hdisplay, "display")) args["display"] = hdisplay;
                if (TryGetString(flags, out var hhwnd, "hwnd")) args["hwnd"] = hhwnd;
                if (TryGetString(flags, out var hrect, "rect") && TryParseRectCsv(hrect, out var hr))
                    args["rect"] = new { x = hr.X, y = hr.Y, w = hr.W, h = hr.H };
                if (TryGetString(flags, out var algo, "algo")) args["algo"] = algo;
                if (TryGetInt(flags, out var hmaxW, "max-w", "max_w")) args["max_w"] = hmaxW;
                if (TryGetInt(flags, out var hmaxH, "max-h", "max_h")) args["max_h"] = hmaxH;
                if (!args.ContainsKey("display") && !args.ContainsKey("rect") && !args.ContainsKey("hwnd"))
                {
                    args["display"] = 0;
                }
                return args;
            case "screen_diff":
                if (TryGetString(flags, out var a, "a")) args["a"] = a;
                if (TryGetString(flags, out var b, "b")) args["b"] = b;
                if (TryGetString(flags, out var ah, "a-hash", "a_hash")) args["a_hash"] = ah;
                if (TryGetString(flags, out var bh, "b-hash", "b_hash")) args["b_hash"] = bh;
                if (TryGetString(flags, out var dalgo, "algo")) args["algo"] = dalgo;
                if (TryGetInt(flags, out var thr, "threshold")) args["threshold"] = thr;
                return args;
            case "wait_for":
                if (TryGetString(flags, out var wtype, "type")) args["type"] = wtype;
                if (TryGetInt(flags, out var timeoutMs, "timeout-ms", "timeout_ms")) args["timeout_ms"] = timeoutMs;
                if (TryGetInt(flags, out var pollMs, "poll-ms", "poll_ms")) args["poll_ms"] = pollMs;
                if (TryGetString(flags, out var wtitle, "title-contains", "title_contains")) args["title_contains"] = wtitle;
                if (TryGetString(flags, out var whwnd, "hwnd")) args["hwnd"] = whwnd;
                if (TryGetString(flags, out var wpattern, "pattern")) args["pattern"] = wpattern;
                if (TryGetString(flags, out var wrect, "rect") && TryParseRectCsv(wrect, out var wr))
                    args["rect"] = new { x = wr.X, y = wr.Y, w = wr.W, h = wr.H };
                if (TryGetInt(flags, out var wdisplay, "display")) args["display"] = wdisplay;
                if (TryGetInt(flags, out var minChange, "min-change", "min_change")) args["min_change"] = minChange;
                if (TryGetString(flags, out var wname, "name")) args["name"] = wname;
                if (TryGetString(flags, out var waid, "automation-id", "automation_id")) args["automation_id"] = waid;
                if (TryGetString(flags, out var wct, "control-type", "control_type")) args["control_type"] = wct;
                if (TryGetString(flags, out var wcn, "class-name", "class_name")) args["class_name"] = wcn;
                if (TryGetInt(flags, out var wnth, "nth")) args["nth"] = wnth;
                if (TryGetString(flags, out var wlang, "language")) args["language"] = wlang;
                return args;
            case "mouse_move":
                if (TryGetInt(flags, out var mx, "x")) args["x"] = mx;
                if (TryGetInt(flags, out var my, "y")) args["y"] = my;
                if (TryGetString(flags, out var mode, "mode")) args["mode"] = mode;
                if (TryGetInt(flags, out var dur, "duration-ms", "duration_ms")) args["duration_ms"] = dur;
                if (TryGetString(flags, out var moveInputMode, "input-mode", "input_mode")) args["input_mode"] = moveInputMode;
                if (TryGetInt(flags, out var moveOverlayMs, "overlay-ms", "overlay_ms")) args["overlay_ms"] = moveOverlayMs;
                if (TryGetString(flags, out var moveLabel, "agent", "label")) args["label"] = moveLabel;
                return args;
            case "mouse_click":
                if (TryGetInt(flags, out var cx, "x")) args["x"] = cx;
                if (TryGetInt(flags, out var cy, "y")) args["y"] = cy;
                if (TryGetString(flags, out var button, "button")) args["button"] = button;
                if (TryGetInt(flags, out var clicks, "clicks")) args["clicks"] = clicks;
                if (TryGetString(flags, out var clickInputMode, "input-mode", "input_mode")) args["input_mode"] = clickInputMode;
                if (TryGetInt(flags, out var clickOverlayMs, "overlay-ms", "overlay_ms")) args["overlay_ms"] = clickOverlayMs;
                if (TryGetString(flags, out var clickLabel, "agent", "label")) args["label"] = clickLabel;
                return args;
            case "overlay_show":
                if (TryGetInt(flags, out var ox, "x")) args["x"] = ox;
                if (TryGetInt(flags, out var oy, "y")) args["y"] = oy;
                if (TryGetInt(flags, out var overlayDuration, "duration-ms", "duration_ms")) args["duration_ms"] = overlayDuration;
                if (TryGetBool(flags, "pulse") is bool pulse) args["pulse"] = pulse;
                if (TryGetString(flags, out var overlayLabel, "agent", "label")) args["label"] = overlayLabel;
                return args;
            case "overlay_update":
                if (TryGetString(flags, out var updateAgent, "agent", "label")) args["agent"] = updateAgent;
                if (TryGetInt(flags, out var updateX, "x")) args["x"] = updateX;
                if (TryGetInt(flags, out var updateY, "y")) args["y"] = updateY;
                if (TryGetInt(flags, out var updateDisplay, "display")) args["display"] = updateDisplay;
                if (TryGetString(flags, out var updateApp, "app")) args["app"] = updateApp;
                if (TryGetString(flags, out var updateWindow, "window")) args["window"] = updateWindow;
                if (TryGetString(flags, out var updateHwnd, "hwnd")) args["hwnd"] = updateHwnd;
                if (TryGetBool(flags, "pulse") is bool updatePulse) args["pulse"] = updatePulse;
                if (TryGetInt(flags, out var updateTtl, "ttl-ms", "ttl_ms")) args["ttl_ms"] = updateTtl;
                return args;
            case "overlay_clear":
                if (TryGetString(flags, out var clearAgent, "agent")) args["agent"] = clearAgent;
                return args;
            case "overlay_list":
                if (TryGetString(flags, out var listAgent, "agent")) args["agent"] = listAgent;
                return args;
            case "overlay_render":
                if (TryGetInt(flags, out var renderDuration, "duration-ms", "duration_ms")) args["duration_ms"] = renderDuration;
                return args;
            case "mouse_down":
            case "mouse_up":
                if (TryGetInt(flags, out var dx, "x")) args["x"] = dx;
                if (TryGetInt(flags, out var dy, "y")) args["y"] = dy;
                if (TryGetString(flags, out var dbutton, "button")) args["button"] = dbutton;
                return args;
            case "mouse_drag":
                if (TryGetString(flags, out var from, "from") && TryParsePointCsv(from, out var fp))
                    args["from"] = new { x = fp.X, y = fp.Y };
                if (TryGetString(flags, out var to, "to") && TryParsePointCsv(to, out var tp))
                    args["to"] = new { x = tp.X, y = tp.Y };
                if (TryGetString(flags, out var dragButton, "button")) args["button"] = dragButton;
                if (TryGetInt(flags, out var dragDur, "duration-ms", "duration_ms")) args["duration_ms"] = dragDur;
                return args;
            case "mouse_wheel":
                if (TryGetInt(flags, out var wx, "x")) args["x"] = wx;
                if (TryGetInt(flags, out var wy, "y")) args["y"] = wy;
                if (TryGetInt(flags, out var delta, "delta")) args["delta"] = delta;
                return args;
            case "key_tap":
            case "key_down":
            case "key_up":
                var keyList = new List<string>();
                if (TryGetString(flags, out var keys, "keys"))
                {
                    keyList.AddRange(keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
                if (flags.TryGetValue("key", out var singleKeys))
                {
                    keyList.AddRange(singleKeys);
                }
                if (keyList.Count > 0)
                {
                    args["keys"] = keyList.ToArray();
                }
                return args;
            case "text_type":
                if (TryGetString(flags, out var text, "text")) args["text"] = text;
                if (TryGetString(flags, out var method, "method")) args["method"] = method;
                if (TryGetBool(flags, "enter") is bool enter) args["enter"] = enter;
                return args;
            case "focus_window":
                if (TryGetString(flags, out var fhwnd, "hwnd")) args["hwnd"] = fhwnd;
                if (TryGetString(flags, out var ft, "title-contains", "title_contains")) args["title_contains"] = ft;
                if (TryGetString(flags, out var fe, "exe-contains", "exe_contains")) args["exe_contains"] = fe;
                if (TryGetInt(flags, out var nth, "nth")) args["nth"] = nth;
                return args;
            case "window_move":
            case "window_resize":
            {
                if (TryGetString(flags, out var winHwnd, "hwnd")) args["hwnd"] = winHwnd;
                if (TryGetString(flags, out var winTitle, "title-contains", "title_contains")) args["title_contains"] = winTitle;
                if (TryGetString(flags, out var winExe, "exe-contains", "exe_contains")) args["exe_contains"] = winExe;
                if (TryGetInt(flags, out var winNth, "nth")) args["nth"] = winNth;
                if (TryGetInt(flags, out var winX, "x")) args["x"] = winX;
                if (TryGetInt(flags, out var winY, "y")) args["y"] = winY;
                if (TryGetInt(flags, out var winW, "w")) args["w"] = winW;
                if (TryGetInt(flags, out var winH, "h")) args["h"] = winH;
                if (TryGetString(flags, out var winRectCsv, "rect") && TryParseRectCsv(winRectCsv, out var wr2))
                    args["rect"] = new { x = wr2.X, y = wr2.Y, w = wr2.W, h = wr2.H };
                return args;
            }
            case "window_minimize":
            case "window_maximize":
            case "window_restore":
            case "window_close":
            {
                if (TryGetString(flags, out var mhwnd, "hwnd")) args["hwnd"] = mhwnd;
                if (TryGetString(flags, out var mtitle, "title-contains", "title_contains")) args["title_contains"] = mtitle;
                if (TryGetString(flags, out var mexe, "exe-contains", "exe_contains")) args["exe_contains"] = mexe;
                if (TryGetInt(flags, out var mnth, "nth")) args["nth"] = mnth;
                return args;
            }
            case "clipboard_set":
            {
                if (TryGetString(flags, out var clipText, "text")) args["text"] = clipText;
                if (TryGetString(flags, out var sformat, "format")) args["format"] = sformat;
                return args;
            }
            case "process_kill":
                if (TryGetInt(flags, out var pid, "pid")) args["pid"] = pid;
                if (TryGetString(flags, out var pname, "name-contains", "name_contains")) args["name_contains"] = pname;
                if (TryGetBool(flags, "force") is bool force) args["force"] = force;
                return args;
            case "uia_dump":
                if (TryGetString(flags, out var uhwnd, "hwnd")) args["hwnd"] = uhwnd;
                if (TryGetInt(flags, out var maxDepth, "max-depth", "max_depth")) args["max_depth"] = maxDepth;
                if (TryGetInt(flags, out var maxNodes, "max-nodes", "max_nodes")) args["max_nodes"] = maxNodes;
                if (TryGetBool(flags, "include-rects", "include_rects") is bool ir) args["include_rects"] = ir;
                return args;
            case "uia_find":
            case "uia_click":
            case "uia_set_value":
                if (TryGetString(flags, out var uihwnd, "hwnd")) args["hwnd"] = uihwnd;
                if (TryGetString(flags, out var uiname, "name")) args["name"] = uiname;
                if (TryGetString(flags, out var uiaid, "automation-id", "automation_id")) args["automation_id"] = uiaid;
                if (TryGetString(flags, out var uict, "control-type", "control_type")) args["control_type"] = uict;
                if (TryGetString(flags, out var uicn, "class-name", "class_name")) args["class_name"] = uicn;
                if (TryGetInt(flags, out var uinth, "nth")) args["nth"] = uinth;
                if (TryGetString(flags, out var uival, "value")) args["value"] = uival;
                if (TryGetString(flags, out var uibutton, "button")) args["button"] = uibutton;
                return args;
            case "ocr":
                if (TryGetString(flags, out var ocrRect, "rect") && TryParseRectCsv(ocrRect, out var or))
                    args["rect"] = new { x = or.X, y = or.Y, w = or.W, h = or.H };
                if (TryGetInt(flags, out var ocrDisplay, "display")) args["display"] = ocrDisplay;
                if (TryGetString(flags, out var ocrHwnd, "hwnd")) args["hwnd"] = ocrHwnd;
                if (TryGetString(flags, out var ocrLang, "language")) args["language"] = ocrLang;
                if (!args.ContainsKey("display") && !args.ContainsKey("rect") && !args.ContainsKey("hwnd"))
                {
                    args["display"] = 0;
                }
                return args;
            case "open_with_default":
                if (TryGetString(flags, out var owd, "path")) args["path"] = owd;
                return args;
            case "settings_open":
                if (TryGetString(flags, out var page, "page")) args["page"] = page;
                if (TryGetString(flags, out var uri, "uri")) args["uri"] = uri;
                return args;
            case "desktop_list":
                return args;
            case "desktop_switch":
                if (TryGetInt(flags, out var dindex, "index")) args["index"] = dindex;
                if (TryGetString(flags, out var did, "id")) args["id"] = did;
                return args;
            case "desktop_move_window":
                if (TryGetInt(flags, out var dwinIndex, "index")) args["index"] = dwinIndex;
                if (TryGetString(flags, out var dwinId, "id")) args["id"] = dwinId;
                if (TryGetString(flags, out var dwhwnd, "hwnd")) args["hwnd"] = dwhwnd;
                if (TryGetString(flags, out var dwtitle, "title-contains", "title_contains")) args["title_contains"] = dwtitle;
                if (TryGetString(flags, out var dwexe, "exe-contains", "exe_contains")) args["exe_contains"] = dwexe;
                if (TryGetInt(flags, out var dwnth, "nth")) args["nth"] = dwnth;
                return args;
            case "taskbar_click_app":
                if (TryGetString(flags, out var tbname, "name")) args["name"] = tbname;
                return args;
            case "start_menu_search":
                if (TryGetString(flags, out var smtext, "text")) args["text"] = smtext;
                if (TryGetBool(flags, "enter") is bool smenter) args["enter"] = smenter;
                if (TryGetBool(flags, "open") is bool smopen) args["open"] = smopen;
                return args;
            case "audio_devices_list":
                return args;
            case "audio_default_set":
                if (TryGetString(flags, out var audId, "id")) args["id"] = audId;
                if (TryGetString(flags, out var audName, "name")) args["name"] = audName;
                if (TryGetString(flags, out var audFlow, "flow")) args["flow"] = audFlow;
                return args;
            case "mic_mute":
            case "mic_unmute":
                if (TryGetString(flags, out var micId, "id")) args["id"] = micId;
                if (TryGetString(flags, out var micName, "name")) args["name"] = micName;
                return args;
            case "notifications_list":
                return args;
            case "notification_clear":
                if (TryGetString(flags, out var ntag, "tag")) args["tag"] = ntag;
                if (TryGetString(flags, out var ngroup, "group")) args["group"] = ngroup;
                if (TryGetString(flags, out var napp, "app")) args["app"] = napp;
                return args;
            case "notification_click":
                if (TryGetString(flags, out var ncText, "text")) args["text"] = ncText;
                if (TryGetString(flags, out var ncPattern, "pattern")) args["pattern"] = ncPattern;
                if (TryGetString(flags, out var ncRect, "rect") && TryParseRectCsv(ncRect, out var ncr))
                    args["rect"] = new { x = ncr.X, y = ncr.Y, w = ncr.W, h = ncr.H };
                if (TryGetInt(flags, out var ncDisplay, "display")) args["display"] = ncDisplay;
                if (TryGetString(flags, out var ncLang, "language")) args["language"] = ncLang;
                if (TryGetString(flags, out var ncButton, "button")) args["button"] = ncButton;
                return args;
            case "display_list":
                return args;
            case "display_enable":
            case "display_disable":
                if (TryGetInt(flags, out var deDisplay, "display")) args["display"] = deDisplay;
                if (TryGetString(flags, out var deName, "name")) args["name"] = deName;
                if (TryGetInt(flags, out var deW, "width")) args["width"] = deW;
                if (TryGetInt(flags, out var deH, "height")) args["height"] = deH;
                return args;
            case "display_set_primary":
                if (TryGetInt(flags, out var dpDisplay, "display")) args["display"] = dpDisplay;
                if (TryGetString(flags, out var dpName, "name")) args["name"] = dpName;
                return args;
            case "display_orientation":
                if (TryGetInt(flags, out var dorDisplay, "display")) args["display"] = dorDisplay;
                if (TryGetString(flags, out var dorName, "name")) args["name"] = dorName;
                if (TryGetInt(flags, out var orient, "orientation")) args["orientation"] = orient;
                return args;
            case "taskbar_pin":
            case "taskbar_unpin":
                if (TryGetString(flags, out var tbPath, "path")) args["path"] = tbPath;
                return args;
            case "task_list":
                if (TryGetString(flags, out var tlistName, "name")) args["name"] = tlistName;
                return args;
            case "task_create":
                if (TryGetString(flags, out var tname, "name")) args["name"] = tname;
                if (TryGetString(flags, out var tcmd, "cmd")) args["cmd"] = tcmd;
                if (TryGetString(flags, out var targs, "args")) args["args"] = targs;
                if (TryGetString(flags, out var tsched, "schedule")) args["schedule"] = tsched;
                if (TryGetString(flags, out var ttime, "time")) args["time"] = ttime;
                if (TryGetString(flags, out var tdate, "date")) args["date"] = tdate;
                if (TryGetInt(flags, out var tinterval, "interval")) args["interval"] = tinterval;
                if (TryGetString(flags, out var tuser, "user")) args["user"] = tuser;
                if (TryGetString(flags, out var tpass, "password")) args["password"] = tpass;
                return args;
            case "task_run":
            case "task_delete":
                if (TryGetString(flags, out var trname, "name")) args["name"] = trname;
                return args;
            case "uwp_list":
                return args;
            case "uwp_launch":
                if (TryGetString(flags, out var aumid, "aumid")) args["aumid"] = aumid;
                if (TryGetString(flags, out var uargs, "args")) args["args"] = uargs;
                return args;
            case "dpi_status":
                return args;
            case "dpi_test_capture":
                if (TryGetInt(flags, out var dtDisplay, "display")) args["display"] = dtDisplay;
                if (TryGetString(flags, out var dtRect, "rect") && TryParseRectCsv(dtRect, out var dtr))
                    args["rect"] = new { x = dtr.X, y = dtr.Y, w = dtr.W, h = dtr.H };
                if (TryGetString(flags, out var dtHwnd, "hwnd")) args["hwnd"] = dtHwnd;
                if (TryGetInt(flags, out var dtSize, "size")) args["size"] = dtSize;
                if (TryGetBool(flags, "grid") is bool dtGrid) args["grid"] = dtGrid;
                return args;
            case "click_text":
                if (TryGetString(flags, out var ctText, "text")) args["text"] = ctText;
                if (TryGetString(flags, out var ctPattern, "pattern")) args["pattern"] = ctPattern;
                if (TryGetString(flags, out var ctRect, "rect") && TryParseRectCsv(ctRect, out var ctr))
                    args["rect"] = new { x = ctr.X, y = ctr.Y, w = ctr.W, h = ctr.H };
                if (TryGetInt(flags, out var ctDisplay, "display")) args["display"] = ctDisplay;
                if (TryGetString(flags, out var ctHwnd, "hwnd")) args["hwnd"] = ctHwnd;
                if (TryGetString(flags, out var ctLang, "language")) args["language"] = ctLang;
                if (TryGetString(flags, out var ctButton, "button")) args["button"] = ctButton;
                return args;
            case "click_icon":
                if (TryGetString(flags, out var ciIcon, "icon")) args["icon"] = ciIcon;
                if (TryGetString(flags, out var ciRect, "rect") && TryParseRectCsv(ciRect, out var cir))
                    args["rect"] = new { x = cir.X, y = cir.Y, w = cir.W, h = cir.H };
                if (TryGetInt(flags, out var ciDisplay, "display")) args["display"] = ciDisplay;
                if (TryGetString(flags, out var ciHwnd, "hwnd")) args["hwnd"] = ciHwnd;
                if (TryGetInt(flags, out var ciThresh, "threshold")) args["threshold"] = ciThresh;
                if (TryGetString(flags, out var ciButton, "button")) args["button"] = ciButton;
                return args;
            case "launch":
                if (TryGetString(flags, out var path, "path")) args["path"] = path;
                if (TryGetString(flags, out var largs, "args")) args["args"] = largs;
                if (TryGetString(flags, out var cwd, "cwd")) args["cwd"] = cwd;
                return args;
            case "sleep":
                if (TryGetInt(flags, out var ms, "ms")) args["ms"] = ms;
                return args;
            case "human_config_set":
                if (TryGetString(flags, out var profile, "profile")) args["profile"] = profile;
                if (TryGetInt(flags, out var seed, "seed")) args["seed"] = seed;
                return args;
            case "mouse_pos":
                return args;
            case "coordinator_status":
            case "session_list":
                return args;
            case "session_create":
                if (TryGetString(flags, out var sessionAgent, "agent")) args["agent"] = sessionAgent;
                if (TryGetString(flags, out var sessionName, "name")) args["name"] = sessionName;
                if (TryGetInt(flags, out var sessionDisplay, "display")) args["display"] = sessionDisplay;
                return args;
            case "lease_acquire":
                if (TryGetString(flags, out var leaseAgent, "agent")) args["agent"] = leaseAgent;
                if (TryGetString(flags, out var leaseResource, "resource")) args["resource"] = leaseResource;
                if (TryGetString(flags, out var leaseMode, "mode")) args["mode"] = leaseMode;
                if (TryGetInt(flags, out var leaseTtl, "ttl-ms", "ttl_ms")) args["ttl_ms"] = leaseTtl;
                return args;
            case "lease_list":
                if (TryGetString(flags, out var leaseFilterAgent, "agent")) args["agent"] = leaseFilterAgent;
                if (TryGetString(flags, out var leaseFilterResource, "resource")) args["resource"] = leaseFilterResource;
                return args;
            case "history_list":
                if (TryGetInt(flags, out var historyLimit, "limit")) args["limit"] = historyLimit;
                if (TryGetString(flags, out var historyAgent, "agent")) args["agent"] = historyAgent;
                return args;
            case "action_submit":
                if (TryGetString(flags, out var actionAgent, "agent")) args["agent"] = actionAgent;
                if (TryGetString(flags, out var actionType, "type")) args["type"] = actionType;
                if (TryGetInt(flags, out var actionX, "x")) args["x"] = actionX;
                if (TryGetInt(flags, out var actionY, "y")) args["y"] = actionY;
                if (TryGetInt(flags, out var actionDisplay, "display")) args["display"] = actionDisplay;
                if (TryGetString(flags, out var actionHwnd, "hwnd")) args["hwnd"] = actionHwnd;
                if (TryGetString(flags, out var actionInputMode, "input-mode", "input_mode")) args["input_mode"] = actionInputMode;
                if (TryGetBool(flags, "dry-run", "dry_run") is bool dryRun) args["dry_run"] = dryRun;
                return args;
            default:
                return args;
        }
    }

    private static bool TryGetString(Dictionary<string, List<string>> flags, out string value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (flags.TryGetValue(key, out var list) && list.Count > 0)
            {
                value = list[0];
                return true;
            }
        }
        value = "";
        return false;
    }

    private static bool TryGetInt(Dictionary<string, List<string>> flags, out int value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (flags.TryGetValue(key, out var list) && list.Count > 0 && int.TryParse(list[0], out value))
            {
                return true;
            }
        }
        value = 0;
        return false;
    }

    private static bool? TryGetBool(Dictionary<string, List<string>> flags, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (flags.TryGetValue(key, out var list) && list.Count > 0)
            {
                var v = list[0];
                if (bool.TryParse(v, out var b))
                {
                    return b;
                }
                if (v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
                if (v == "0" || v.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }
        }
        return null;
    }

    private static bool TryParseRectCsv(string csv, out Rect rect)
    {
        rect = new Rect();
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], out var x)) return false;
        if (!int.TryParse(parts[1], out var y)) return false;
        if (!int.TryParse(parts[2], out var w)) return false;
        if (!int.TryParse(parts[3], out var h)) return false;
        rect = new Rect { X = x, Y = y, W = w, H = h };
        return true;
    }

    private static bool IsCsvNumbers(string value)
    {
        if (!value.Contains(',')) return false;
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryParsePointCsv(string csv, out Point point)
    {
        point = new Point();
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var x)) return false;
        if (!int.TryParse(parts[1], out var y)) return false;
        point = new Point(x, y);
        return true;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine($"{AppName} CLI");
        Console.Error.WriteLine($"  {AppName} <noun> <verb> [options]");
        Console.Error.WriteLine($"  {AppName} help <noun> <verb>");
        Console.Error.WriteLine("");
        Console.Error.WriteLine("Examples:");
        Console.Error.WriteLine($"  {AppName} screen capture");
        Console.Error.WriteLine($"  {AppName} mouse move --x 100 --y 200 --human");
        Console.Error.WriteLine($"  {AppName} key tap --key CTRL --key L");
        Console.Error.WriteLine($"  {AppName} keyboard type --text \"hello\" --enter --human");
        Console.Error.WriteLine($"  {AppName} uia find --name \"Search\" --control-type Edit");
        Console.Error.WriteLine($"  {AppName} window move --title-contains Chrome --x 100 --y 100");
        Console.Error.WriteLine("");
        Console.Error.WriteLine("Notes:");
        Console.Error.WriteLine("  - Use --key multiple times (recommended) or --keys CTRL,L.");
        Console.Error.WriteLine("  - For rectangles: --rect x,y,w,h");
        Console.Error.WriteLine("  - Short flags: -x -y -d -r -f -o -k -t -m -c -s");
        Console.Error.WriteLine("  - Output format: --format human (default) or --format kv");
        Console.Error.WriteLine($"  - Use '{AppName} help <noun> <verb>' for command details.");
        Console.Error.WriteLine("");
        Console.Error.WriteLine("Commands:");
        foreach (var entry in GetAllSubcommands())
        {
            Console.Error.WriteLine($"  {AppName} {entry}");
        }
    }

    private static IEnumerable<string> GetAllSubcommands()
    {
        return CommandToSubcommand.Values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
    }

    private static void PrintCommandHelp(string cmd)
    {
        switch (cmd.ToLowerInvariant())
        {
            case "get_displays":
                WriteHelpLine("deskctl get_displays");
                break;
            case "active_window":
                WriteHelpLine("deskctl active_window");
                break;
            case "list_windows":
                WriteHelpLine("deskctl list_windows [--title-contains <text>] [--exe-contains <text>] [--visible-only true|false]");
                break;
            case "screenshot":
                WriteHelpLine("deskctl screenshot [--display <n> | --rect x,y,w,h | --hwnd <handle>]");
                Console.Error.WriteLine("                  [--format png|jpg] [--return path|b64] [--include-cursor true|false]");
                Console.Error.WriteLine("                  [--max-w <n>] [--max-h <n>] [--grid true|false] [--grid-step <n>] [--grid-abs true|false]");
                Console.Error.WriteLine("defaults: display=0, format=png, return=path, include-cursor=true, max-w=1600");
                Console.Error.WriteLine("grid: overlays a coordinate grid; grid-abs labels absolute screen coords");
                Console.Error.WriteLine("note: use --max-w 0 to disable downscaling");
                break;
            case "mouse_move":
                WriteHelpLine("deskctl mouse_move --x <n> --y <n> [--mode abs|rel] [--duration-ms <n>] [--input-mode physical|ghost] [--agent <name>] [--human true|false]");
                break;
            case "mouse_click":
                WriteHelpLine("deskctl mouse_click --x <n> --y <n> [--button left|right|middle] [--clicks <n>] [--input-mode physical|message|auto|ghost] [--agent <name>] [--human true|false]");
                break;
            case "overlay_show":
                WriteHelpLine("deskctl overlay_show --x <n> --y <n> [--duration-ms <n>] [--pulse true|false] [--agent <name>]");
                break;
            case "overlay_update":
                WriteHelpLine("deskctl overlay update --agent <name> --x <n> --y <n> [--display <n>] [--app <name>] [--window <title>] [--pulse true|false]");
                break;
            case "overlay_list":
                WriteHelpLine("deskctl overlay list [--agent <name>]");
                break;
            case "overlay_clear":
                WriteHelpLine("deskctl overlay clear [--agent <name>]");
                break;
            case "overlay_render":
                WriteHelpLine("deskctl overlay render [--duration-ms <n>]");
                break;
            case "mouse_down":
                WriteHelpLine("deskctl mouse_down --x <n> --y <n> [--button left|right|middle]");
                break;
            case "mouse_up":
                WriteHelpLine("deskctl mouse_up --x <n> --y <n> [--button left|right|middle]");
                break;
            case "mouse_drag":
                WriteHelpLine("deskctl mouse_drag --from x,y --to x,y [--button left|right|middle] [--duration-ms <n>] [--human true|false]");
                break;
            case "mouse_wheel":
                WriteHelpLine("deskctl mouse_wheel --x <n> --y <n> --delta <n> [--human true|false]");
                break;
            case "mouse_pos":
                WriteHelpLine("deskctl mouse_pos");
                break;
            case "key_tap":
                WriteHelpLine("deskctl key_tap --key CTRL --key L");
                WriteHelpLine("deskctl key_tap --keys CTRL,L");
                Console.Error.WriteLine("[--human true|false]");
                break;
            case "key_down":
                WriteHelpLine("deskctl key_down --key CTRL --key L");
                break;
            case "key_up":
                WriteHelpLine("deskctl key_up --key CTRL --key L");
                break;
            case "text_type":
                WriteHelpLine("deskctl text_type --text <text> [--method sendinput|paste] [--enter true|false] [--human true|false]");
                break;
            case "focus_window":
                WriteHelpLine("deskctl focus_window --hwnd <handle> | --title-contains <text> | --exe-contains <text>");
                break;
            case "window_move":
                WriteHelpLine("deskctl window_move --hwnd <handle> --x <n> --y <n>");
                WriteHelpLine("deskctl window_move --title-contains <text> --rect x,y,w,h");
                break;
            case "window_resize":
                WriteHelpLine("deskctl window_resize --hwnd <handle> --w <n> --h <n>");
                WriteHelpLine("deskctl window_resize --title-contains <text> --rect x,y,w,h");
                break;
            case "window_minimize":
                WriteHelpLine("deskctl window_minimize --hwnd <handle> | --title-contains <text>");
                break;
            case "window_maximize":
                WriteHelpLine("deskctl window_maximize --hwnd <handle> | --title-contains <text>");
                break;
            case "window_restore":
                WriteHelpLine("deskctl window_restore --hwnd <handle> | --title-contains <text>");
                break;
            case "window_close":
                WriteHelpLine("deskctl window_close --hwnd <handle> | --title-contains <text>");
                break;
            case "clipboard_get":
                WriteHelpLine("deskctl clipboard_get");
                break;
            case "clipboard_set":
                WriteHelpLine("deskctl clipboard_set --text <text> [--format text|html|rtf]");
                break;
            case "clipboard_clear":
                WriteHelpLine("deskctl clipboard_clear");
                break;
            case "clipboard_formats":
                WriteHelpLine("deskctl clipboard_formats");
                break;
            case "process_list":
                WriteHelpLine("deskctl process_list");
                break;
            case "process_kill":
                WriteHelpLine("deskctl process_kill --pid <n> | --name-contains <text>");
                break;
            case "app_list":
                WriteHelpLine("deskctl app_list");
                break;
            case "uia_dump":
                WriteHelpLine("deskctl uia_dump [--hwnd <handle>] [--max-depth <n>] [--max-nodes <n>] [--include-rects true|false]");
                break;
            case "uia_find":
                WriteHelpLine("deskctl uia_find [--hwnd <handle>] [--name <text>] [--automation-id <id>] [--control-type <type>] [--class-name <name>] [--nth <n>]");
                break;
            case "uia_click":
                WriteHelpLine("deskctl uia_click [--hwnd <handle>] [--name <text>] [--control-type <type>] [--nth <n>] [--button left|right]");
                break;
            case "uia_set_value":
                WriteHelpLine("deskctl uia_set_value [--hwnd <handle>] [--name <text>] [--control-type <type>] --value <text>");
                break;
            case "active_control":
                WriteHelpLine("deskctl active_control");
                break;
            case "caret_position":
                WriteHelpLine("deskctl caret_position");
                break;
            case "ocr":
                WriteHelpLine("deskctl ocr [--display <n> | --rect x,y,w,h | --hwnd <handle>] [--language en-US]");
                break;
            case "screen_hash":
                WriteHelpLine("deskctl screen_hash [--display <n> | --rect x,y,w,h | --hwnd <handle>] [--algo ahash|sha256]");
                break;
            case "screen_diff":
                WriteHelpLine("deskctl screen_diff --a <path> --b <path> [--algo ahash|sha256]");
                WriteHelpLine("deskctl screen_diff --a-hash <hash> --b-hash <hash> [--algo ahash]");
                break;
            case "wait_for":
                WriteHelpLine("deskctl wait_for --type <window_title_contains|uia_exists|ocr_regex|screen_change> [options]");
                break;
            case "open_with_default":
                WriteHelpLine("deskctl open_with_default --path <file|url>");
                break;
            case "settings_open":
                WriteHelpLine("deskctl settings_open --page <uri-fragment> | --uri <ms-settings:...>");
                break;
            case "system_info":
                WriteHelpLine("deskctl system_info");
                break;
            case "lock":
                WriteHelpLine("deskctl lock");
                break;
            case "power_sleep":
                WriteHelpLine("deskctl power_sleep");
                break;
            case "power_shutdown":
                WriteHelpLine("deskctl power_shutdown");
                break;
            case "power_restart":
                WriteHelpLine("deskctl power_restart");
                break;
            case "wake_display":
                WriteHelpLine("deskctl wake_display");
                break;
            case "desktop_list":
                WriteHelpLine("deskctl desktop_list");
                break;
            case "desktop_switch":
                WriteHelpLine("deskctl desktop_switch --index <n> | --id <guid>");
                break;
            case "desktop_move_window":
                WriteHelpLine("deskctl desktop_move_window --hwnd <handle> --index <n>");
                WriteHelpLine("deskctl desktop_move_window --title-contains <text> --id <guid>");
                break;
            case "taskbar_click_app":
                WriteHelpLine("deskctl taskbar_click_app --name <text>");
                break;
            case "start_menu_search":
                WriteHelpLine("deskctl start_menu_search --text <query> [--enter true|false]");
                break;
            case "notifications_list":
                WriteHelpLine("deskctl notifications_list");
                break;
            case "notification_clear":
                WriteHelpLine("deskctl notification_clear [--tag <tag>] [--group <group>] [--app <app>]");
                break;
            case "notification_click":
                WriteHelpLine("deskctl notification_click --text <text> [--pattern <regex>]");
                Console.Error.WriteLine("  [--rect x,y,w,h | --display <n>] [--language en-US] [--button left|right]");
                Console.Error.WriteLine("note: opens Action Center (Win+A) then clicks matched text");
                break;
            case "display_list":
                WriteHelpLine("deskctl display_list");
                break;
            case "display_enable":
                WriteHelpLine("deskctl display_enable --display <n> | --name <\\\\.\\DISPLAY1> [--width <n> --height <n>]");
                break;
            case "display_disable":
                WriteHelpLine("deskctl display_disable --display <n> | --name <\\\\.\\DISPLAY1>");
                break;
            case "display_set_primary":
                WriteHelpLine("deskctl display_set_primary --display <n> | --name <\\\\.\\DISPLAY1>");
                break;
            case "display_orientation":
                WriteHelpLine("deskctl display_orientation --display <n> | --name <\\\\.\\DISPLAY1> --orientation 0|90|180|270");
                break;
            case "taskbar_pin":
                WriteHelpLine("deskctl taskbar_pin --path <exe|appname>");
                break;
            case "taskbar_unpin":
                WriteHelpLine("deskctl taskbar_unpin --path <exe|appname>");
                break;
            case "uwp_list":
                WriteHelpLine("deskctl uwp_list");
                break;
            case "uwp_launch":
                WriteHelpLine("deskctl uwp_launch --aumid <appUserModelId> [--args <args>]");
                break;
            case "dpi_status":
                WriteHelpLine("deskctl dpi_status");
                break;
            case "dpi_test_capture":
                WriteHelpLine("deskctl dpi_test_capture [--display <n> | --rect x,y,w,h | --hwnd <handle>]");
                Console.Error.WriteLine("  [--size <n>] [--grid true|false]");
                break;
            case "task_list":
                WriteHelpLine("deskctl task_list [--name <task>]");
                break;
            case "task_create":
                WriteHelpLine("deskctl task_create --name <task> --cmd <path> [--args <args>]");
                Console.Error.WriteLine("  [--schedule once|daily|onlogon|onstartup|minute] [--time HH:MM] [--date YYYY-MM-DD]");
                Console.Error.WriteLine("  [--interval <n>] [--user <user>] [--password <pwd>]");
                break;
            case "task_run":
                WriteHelpLine("deskctl task_run --name <task>");
                break;
            case "task_delete":
                WriteHelpLine("deskctl task_delete --name <task>");
                break;
            case "click_text":
                WriteHelpLine("deskctl click_text --text <text> [--rect x,y,w,h | --display <n> | --hwnd <handle>]");
                Console.Error.WriteLine("  [--language en-US] [--button left|right]");
                break;
            case "click_icon":
                WriteHelpLine("deskctl click_icon --icon <path> [--rect x,y,w,h | --display <n> | --hwnd <handle>]");
                Console.Error.WriteLine("  [--threshold <0-100>] [--button left|right]");
                break;
            case "launch":
                WriteHelpLine("deskctl launch --path <exe|appname> [--args <args>] [--cwd <path>]");
                Console.Error.WriteLine("examples:");
                WriteHelpLine("deskctl launch --path chrome --args \"https://www.reuters.com\"");
                break;
            case "sleep":
                WriteHelpLine("deskctl sleep --ms <n>");
                break;
            case "human_config_set":
                WriteHelpLine("deskctl profile set --profile <name> [--seed <n>]");
                break;
            case "human_config_get":
                WriteHelpLine("deskctl profile get");
                break;
            case "human_profiles_list":
                WriteHelpLine("deskctl profile list");
                break;
            case "coordinator_status":
                WriteHelpLine("deskctl coordinator status");
                break;
            case "session_create":
                WriteHelpLine("deskctl session create --agent <name> [--display <n>] [--name <label>]");
                break;
            case "session_list":
                WriteHelpLine("deskctl session list");
                break;
            case "lease_acquire":
                WriteHelpLine("deskctl lease acquire --agent <name> --resource <display:n|window:hwnd|physical_cursor|keyboard_focus|clipboard> [--mode observe|semantic|message|physical] [--ttl-ms <n>]");
                break;
            case "lease_list":
                WriteHelpLine("deskctl lease list [--agent <name>] [--resource <resource>]");
                break;
            case "history_list":
                WriteHelpLine("deskctl history list [--limit <n>] [--agent <name>]");
                break;
            case "action_submit":
                WriteHelpLine("deskctl action submit --agent <name> --type click --x <n> --y <n> [--input-mode ghost|message|auto|physical] [--dry-run true|false]");
                break;
            default:
                PrintUsage();
                break;
        }
    }

    private static string GetCommandUsage(string cmd)
    {
        return cmd.ToLowerInvariant() switch
        {
            "help" => "deskctl help <noun> <verb>",
            "screenshot" => "deskctl screenshot [--display <n> | --rect x,y,w,h | --hwnd <handle>]",
            "mouse_move" => "deskctl mouse_move --x <n> --y <n> [--mode abs|rel]",
            "mouse_click" => "deskctl mouse_click --x <n> --y <n> [--button left|right|middle] [--clicks <n>] [--input-mode physical|message|auto|ghost] [--agent <name>]",
            "overlay_show" => "deskctl overlay_show --x <n> --y <n> [--duration-ms <n>] [--agent <name>]",
            "overlay_update" => "deskctl overlay update --agent <name> --x <n> --y <n>",
            "overlay_clear" => "deskctl overlay clear [--agent <name>]",
            "overlay_list" => "deskctl overlay list [--agent <name>]",
            "overlay_render" => "deskctl overlay render [--duration-ms <n>]",
            "mouse_drag" => "deskctl mouse_drag --from x,y --to x,y [--button left|right|middle]",
            "mouse_wheel" => "deskctl mouse_wheel --x <n> --y <n> --delta <n>",
            "key_tap" => "deskctl key_tap --key CTRL --key L",
            "text_type" => "deskctl text_type --text <text> [--enter true|false]",
            "focus_window" => "deskctl focus_window --hwnd <handle> | --title-contains <text> | --exe-contains <text>",
            "launch" => "deskctl launch --path <exe|appname> [--args <args>]",
            "task_create" => "deskctl task_create --name <task> --cmd <path> [--schedule once|daily|onlogon|onstartup|minute]",
            "task_run" => "deskctl task_run --name <task>",
            "task_delete" => "deskctl task_delete --name <task>",
            "task_list" => "deskctl task_list [--name <task>]",
            "coordinator_status" => "deskctl coordinator status",
            "session_create" => "deskctl session create --agent <name> [--display <n>]",
            "lease_acquire" => "deskctl lease acquire --agent <name> --resource <resource> [--mode observe|semantic|message|physical]",
            "history_list" => "deskctl history list [--limit <n>]",
            "action_submit" => "deskctl action submit --agent <name> --type click --x <n> --y <n>",
            _ => ""
        };
    }

    private static string[] GetCommandExamples(string cmd)
    {
        return cmd.ToLowerInvariant() switch
        {
            "help" => new[] { "deskctl help", "deskctl help screen capture", "deskctl help mouse move" },
            "get_displays" => new[] { "deskctl get_displays" },
            "active_window" => new[] { "deskctl active_window" },
            "list_windows" => new[] { "deskctl list_windows --title-contains Chrome", "deskctl list_windows --exe-contains chrome.exe" },
            "screenshot" => new[] { "deskctl screenshot --display 0", "deskctl screenshot --rect 100,200,800,600" },
            "screen_hash" => new[] { "deskctl screen_hash --display 0", "deskctl screen_hash --rect 0,0,400,300 --algo sha256" },
            "screen_diff" => new[] { "deskctl screen_diff --a-hash <hash> --b-hash <hash>", "deskctl screen_diff --a C:\\a.png --b C:\\b.png" },
            "wait_for" => new[] { "deskctl wait_for --type window_title_contains --title-contains \"Sign in\"", "deskctl wait_for --type screen_change --rect 0,0,300,200 --min-change 10" },
            "mouse_move" => new[] { "deskctl mouse_move --x 300 --y 400" },
            "mouse_click" => new[] { "deskctl mouse_click --x 300 --y 400" },
            "overlay_show" => new[] { "deskctl overlay_show --x 300 --y 400 --duration-ms 600 --agent Codex" },
            "overlay_update" => new[] { "deskctl overlay update --agent Codex --x 300 --y 400 --pulse true" },
            "overlay_clear" => new[] { "deskctl overlay clear --agent Codex" },
            "overlay_list" => new[] { "deskctl overlay list" },
            "overlay_render" => new[] { "deskctl overlay render --duration-ms 1000" },
            "mouse_down" => new[] { "deskctl mouse_down --x 300 --y 400" },
            "mouse_up" => new[] { "deskctl mouse_up --x 300 --y 400" },
            "mouse_drag" => new[] { "deskctl mouse_drag --from 100,100 --to 400,400" },
            "mouse_wheel" => new[] { "deskctl mouse_wheel --x 500 --y 500 --delta -240" },
            "mouse_pos" => new[] { "deskctl mouse_pos" },
            "key_tap" => new[] { "deskctl key_tap --key CTRL --key L" },
            "key_down" => new[] { "deskctl key_down --key SHIFT" },
            "key_up" => new[] { "deskctl key_up --key SHIFT" },
            "text_type" => new[] { "deskctl text_type --text \"hello\" --enter true" },
            "focus_window" => new[] { "deskctl focus_window --title-contains Chrome" },
            "window_move" => new[] { "deskctl window_move --title-contains Chrome --x 100 --y 100" },
            "window_resize" => new[] { "deskctl window_resize --title-contains Chrome --w 1200 --h 800" },
            "window_minimize" => new[] { "deskctl window_minimize --title-contains Chrome" },
            "window_maximize" => new[] { "deskctl window_maximize --title-contains Chrome" },
            "window_restore" => new[] { "deskctl window_restore --title-contains Chrome" },
            "window_close" => new[] { "deskctl window_close --title-contains Chrome" },
            "clipboard_get" => new[] { "deskctl clipboard_get", "deskctl clipboard_get --format html" },
            "clipboard_set" => new[] { "deskctl clipboard_set --text \"hello\"", "deskctl clipboard_set --text \"<b>hi</b>\" --format html" },
            "clipboard_clear" => new[] { "deskctl clipboard_clear" },
            "clipboard_formats" => new[] { "deskctl clipboard_formats" },
            "process_list" => new[] { "deskctl process_list" },
            "process_kill" => new[] { "deskctl process_kill --pid 1234", "deskctl process_kill --name-contains chrome" },
            "app_list" => new[] { "deskctl app_list" },
            "uia_dump" => new[] { "deskctl uia_dump --max-depth 4" },
            "uia_find" => new[] { "deskctl uia_find --name \"Search\" --control-type Edit" },
            "uia_click" => new[] { "deskctl uia_click --name \"OK\" --control-type Button" },
            "uia_set_value" => new[] { "deskctl uia_set_value --name \"Email\" --control-type Edit --value \"test@example.com\"" },
            "active_control" => new[] { "deskctl active_control" },
            "caret_position" => new[] { "deskctl caret_position" },
            "ocr" => new[] { "deskctl ocr --rect 0,0,300,200" },
            "open_with_default" => new[] { "deskctl open_with_default --path \"C:\\Temp\\file.txt\"" },
            "settings_open" => new[] { "deskctl settings_open --page windowsupdate", "deskctl settings_open --uri ms-settings:privacy-camera" },
            "system_info" => new[] { "deskctl system_info" },
            "lock" => new[] { "deskctl lock" },
            "power_sleep" => new[] { "deskctl power_sleep" },
            "power_shutdown" => new[] { "deskctl power_shutdown" },
            "power_restart" => new[] { "deskctl power_restart" },
            "wake_display" => new[] { "deskctl wake_display" },
            "desktop_list" => new[] { "deskctl desktop_list" },
            "desktop_switch" => new[] { "deskctl desktop_switch --index 1" },
            "desktop_move_window" => new[] { "deskctl desktop_move_window --title-contains Chrome --index 1" },
            "taskbar_click_app" => new[] { "deskctl taskbar_click_app --name Chrome" },
            "start_menu_search" => new[] { "deskctl start_menu_search --text notepad" },
            "notifications_list" => new[] { "deskctl notifications_list" },
            "notification_clear" => new[] { "deskctl notification_clear", "deskctl notification_clear --app \"Microsoft.Windows.Explorer\"" },
            "notification_click" => new[] { "deskctl notification_click --text \"Update available\"" },
            "click_text" => new[] { "deskctl click_text --text OK --rect 0,0,500,400" },
            "click_icon" => new[] { "deskctl click_icon --icon C:\\Temp\\icon.png --display 0" },
            "display_list" => new[] { "deskctl display_list" },
            "display_enable" => new[] { "deskctl display_enable --display 1 --width 1920 --height 1080" },
            "display_disable" => new[] { "deskctl display_disable --display 1" },
            "display_set_primary" => new[] { "deskctl display_set_primary --display 1" },
            "display_orientation" => new[] { "deskctl display_orientation --display 0 --orientation 90" },
            "taskbar_pin" => new[] { "deskctl taskbar_pin --path chrome" },
            "taskbar_unpin" => new[] { "deskctl taskbar_unpin --path chrome" },
            "uwp_list" => new[] { "deskctl uwp_list" },
            "uwp_launch" => new[] { "deskctl uwp_launch --aumid \"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App\"" },
            "dpi_status" => new[] { "deskctl dpi_status" },
            "dpi_test_capture" => new[] { "deskctl dpi_test_capture --display 0 --size 256 --grid true" },
            "task_list" => new[] { "deskctl task_list", "deskctl task_list --name \"Test\"" },
            "task_create" => new[] { "deskctl task_create --name \"Test\" --cmd \"C:\\Windows\\System32\\notepad.exe\" --schedule once --time 14:30 --date 2026-01-23" },
            "task_run" => new[] { "deskctl task_run --name \"Test\"" },
            "task_delete" => new[] { "deskctl task_delete --name \"Test\"" },
            "sleep" => new[] { "deskctl sleep --ms 500" },
            "human_config_set" => new[] { "deskctl profile set --profile human_fast" },
            "human_config_get" => new[] { "deskctl profile get" },
            "human_profiles_list" => new[] { "deskctl profile list" },
            "coordinator_status" => new[] { "deskctl coordinator status" },
            "session_create" => new[] { "deskctl session create --agent Codex --display 0" },
            "session_list" => new[] { "deskctl session list" },
            "lease_acquire" => new[] { "deskctl lease acquire --agent Codex --resource display:0 --mode message" },
            "lease_list" => new[] { "deskctl lease list --agent Codex" },
            "history_list" => new[] { "deskctl history list --limit 20" },
            "action_submit" => new[] { "deskctl action submit --agent Codex --type click --x 300 --y 400 --input-mode ghost" },
            _ => Array.Empty<string>()
        };
    }

    private static bool ValidateFlagValues(string cmd, Dictionary<string, List<string>> flags, out string error)
    {
        error = "";

        if (TryGetString(flags, out var rect, "rect") && !TryParseRectCsv(rect, out _))
        {
            error = "Invalid --rect. Expected: x,y,w,h (e.g., 100,200,800,600).";
            return false;
        }
        if (TryGetString(flags, out var from, "from") && !TryParsePointCsv(from, out _))
        {
            error = "Invalid --from. Expected: x,y (e.g., 100,200).";
            return false;
        }
        if (TryGetString(flags, out var to, "to") && !TryParsePointCsv(to, out _))
        {
            error = "Invalid --to. Expected: x,y (e.g., 300,400).";
            return false;
        }

        foreach (var key in new[] { "x", "y", "delta", "display", "max-w", "max_w", "max-h", "max_h", "quality", "grid-step", "grid_step", "duration-ms", "duration_ms", "overlay-ms", "overlay_ms", "clicks", "ms", "seed", "nth", "interval", "orientation", "size", "threshold", "width", "height", "timeout-ms", "timeout_ms", "poll-ms", "poll_ms", "min-change", "min_change", "ttl-ms", "ttl_ms", "limit" })
        {
            if (flags.TryGetValue(key, out var list))
            {
                foreach (var v in list)
                {
                    if (!int.TryParse(v, out _))
                    {
                        error = $"Invalid --{key} value '{v}'. Expected an integer.";
                        return false;
                    }
                }
            }
        }

        foreach (var key in new[] { "human", "include-cursor", "include_cursor", "grid", "grid-abs", "grid_abs", "visible-only", "visible_only", "enter", "open", "pulse", "dry-run", "dry_run" })
        {
            if (flags.TryGetValue(key, out var list))
            {
                foreach (var v in list)
                {
                    if (!bool.TryParse(v, out _) && v != "1" && v != "0" && !v.Equals("yes", StringComparison.OrdinalIgnoreCase) && !v.Equals("no", StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"Invalid --{key} value '{v}'. Expected true/false.";
                        return false;
                    }
                }
            }
        }

        if (cmd.Equals("display_orientation", StringComparison.OrdinalIgnoreCase))
        {
            if (flags.TryGetValue("orientation", out var vals))
            {
                var v = vals.FirstOrDefault();
                if (v != null && v is not ("0" or "90" or "180" or "270"))
                {
                    error = "Invalid --orientation. Expected 0, 90, 180, or 270.";
                    return false;
                }
            }
        }

        return true;
    }

    private static string? SuggestFlag(string unknown, HashSet<string> allowed)
    {
        string? best = null;
        var bestScore = int.MaxValue;
        foreach (var a in allowed)
        {
            var score = Levenshtein(unknown.ToLowerInvariant(), a.ToLowerInvariant());
            if (score < bestScore)
            {
                bestScore = score;
                best = a;
            }
        }
        return bestScore <= 3 ? best : null;
    }

    private static string? SuggestCommand(string unknown)
    {
        var commands = GetAllCommands();
        string? best = null;
        var bestScore = int.MaxValue;
        foreach (var c in commands)
        {
            var score = Levenshtein(unknown.ToLowerInvariant(), c.ToLowerInvariant());
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        return bestScore <= 3 ? best : null;
    }

    private static string[] GetAllCommands()
    {
        return new[]
        {
            "help",
            "get_displays",
            "active_window",
            "list_windows",
            "screenshot",
            "screen_hash",
            "screen_diff",
            "wait_for",
            "mouse_move",
            "mouse_click",
            "mouse_down",
            "mouse_up",
            "mouse_drag",
            "mouse_wheel",
            "mouse_pos",
            "overlay_show",
            "overlay_update",
            "overlay_list",
            "overlay_clear",
            "overlay_render",
            "key_tap",
            "key_down",
            "key_up",
            "text_type",
            "focus_window",
            "window_move",
            "window_resize",
            "window_minimize",
            "window_maximize",
            "window_restore",
            "window_close",
            "clipboard_get",
            "clipboard_set",
            "clipboard_clear",
            "process_list",
            "process_kill",
            "app_list",
            "uia_dump",
            "uia_find",
            "uia_click",
            "uia_set_value",
            "active_control",
            "caret_position",
            "ocr",
            "open_with_default",
            "settings_open",
            "system_info",
            "lock",
            "power_sleep",
            "power_shutdown",
            "power_restart",
            "wake_display",
            "desktop_list",
            "desktop_switch",
            "desktop_move_window",
            "taskbar_click_app",
            "start_menu_search",
            "notifications_list",
            "notification_clear",
            "notification_click",
            "click_text",
            "click_icon",
            "display_list",
            "display_enable",
            "display_disable",
            "display_set_primary",
            "display_orientation",
            "taskbar_pin",
            "taskbar_unpin",
            "uwp_list",
            "uwp_launch",
            "dpi_status",
            "dpi_test_capture",
            "task_list",
            "task_create",
            "task_run",
            "task_delete",
            "launch",
            "sleep",
            "human_config_set",
            "human_config_get",
            "human_profiles_list",
            "coordinator_status",
            "session_create",
            "session_list",
            "lease_acquire",
            "lease_list",
            "history_list",
            "action_submit"
        };
    }

    private static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }
        return dp[a.Length, b.Length];
    }

    private static string ResolveLaunchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (Path.IsPathRooted(path) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var name = path.Trim().Trim('"');
        if (name.Equals("chrome", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        if (name.Equals("edge", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return "msedge.exe";
        }

        if (name.Equals("firefox", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return "firefox.exe";
        }

        if (name.Equals("notepad", StringComparison.OrdinalIgnoreCase))
        {
            return "notepad.exe";
        }

        if (name.Equals("calc", StringComparison.OrdinalIgnoreCase) || name.Equals("calculator", StringComparison.OrdinalIgnoreCase))
        {
            return "calc.exe";
        }

        if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
        {
            return "explorer.exe";
        }

        if (name.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            return "cmd.exe";
        }

        if (name.Equals("powershell", StringComparison.OrdinalIgnoreCase))
        {
            return "powershell.exe";
        }

        if (name.Equals("wt", StringComparison.OrdinalIgnoreCase) || name.Equals("terminal", StringComparison.OrdinalIgnoreCase))
        {
            return "wt.exe";
        }

        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return name + ".exe";
        }

        return name;
    }

    private static IntPtr ResolveWindowHandle(JsonElement args)
    {
        if (args.TryGetProperty("hwnd", out var h))
        {
            var hwnd = Native.ParseHwnd(h.GetString());
            if (hwnd == IntPtr.Zero) throw new DeskCtlException("INVALID_ARGS", "invalid hwnd");
            return hwnd;
        }

        string? titleContains = args.TryGetProperty("title_contains", out var tc) ? tc.GetString() : null;
        string? exeContains = args.TryGetProperty("exe_contains", out var ec) ? ec.GetString() : null;
        int nth = args.TryGetProperty("nth", out var n) ? n.GetInt32() : 0;
        var found = WindowHelper.FindWindow(titleContains, exeContains, nth);
        if (found == IntPtr.Zero)
        {
            throw new DeskCtlException("NOT_FOUND", "window not found");
        }
        return found;
    }

    private static Rect ResolveCaptureRect(JsonElement args, bool allowDefaultDisplay)
    {
        if (args.TryGetProperty("display", out var disp))
        {
            return DisplayHelper.GetDisplayRect(disp.GetInt32());
        }
        if (args.TryGetProperty("rect", out var r))
        {
            return Rect.FromJson(r);
        }
        if (args.TryGetProperty("hwnd", out var h))
        {
            return WindowInfo.FromHwnd(Native.ParseHwnd(h.GetString())).Rect;
        }
        if (allowDefaultDisplay)
        {
            return DisplayHelper.GetDisplayRect(0);
        }
        throw new DeskCtlException("INVALID_ARGS", "rect requires display, rect, or hwnd");
    }

    private static string ResolveDisplayName(JsonElement args)
    {
        if (args.TryGetProperty("name", out var nameEl))
        {
            var name = nameEl.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        if (args.TryGetProperty("display", out var disp))
        {
            int index = disp.GetInt32();
            var screens = Screen.AllScreens;
            if (index >= 0 && index < screens.Length)
            {
                return screens[index].DeviceName;
            }
        }
        throw new DeskCtlException("INVALID_ARGS", "display requires --display or --name");
    }

    public static T RunSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured != null) throw captured;
        return result;
    }

    private static string? SafeGetExe(Process proc)
    {
        try
        {
            return proc.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeGetTitle(Process proc)
    {
        try
        {
            return proc.MainWindowTitle;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindOnPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(part.Trim(), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }
        return null;
    }

    private static string[] ParseKeyList(JsonElement args)
    {
        var list = new List<string>();
        if (args.TryGetProperty("keys", out var keysEl) && keysEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var k in keysEl.EnumerateArray())
            {
                if (k.ValueKind == JsonValueKind.String)
                {
                    list.Add(k.GetString() ?? "");
                }
            }
        }
        else if (args.TryGetProperty("keys", out var keysStr) && keysStr.ValueKind == JsonValueKind.String)
        {
            list.AddRange((keysStr.GetString() ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        if (args.TryGetProperty("key", out var keyEl))
        {
            if (keyEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var k in keyEl.EnumerateArray())
                {
                    if (k.ValueKind == JsonValueKind.String) list.Add(k.GetString() ?? "");
                }
            }
            else if (keyEl.ValueKind == JsonValueKind.String)
            {
                list.Add(keyEl.GetString() ?? "");
            }
        }
        return list.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();
    }

    private static OcrResult RunOcr(Rect rect, string? language)
    {
        using var bmp = CaptureScreen(rect, false);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        var stream = ms.AsRandomAccessStream();
        var decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
        var software = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();
        OcrEngine engine;
        if (!string.IsNullOrWhiteSpace(language))
        {
            engine = OcrEngine.TryCreateFromLanguage(new Language(language)) ?? OcrEngine.TryCreateFromUserProfileLanguages();
        }
        else
        {
            engine = OcrEngine.TryCreateFromUserProfileLanguages();
        }
        if (engine == null)
        {
            throw new DeskCtlException("INTERNAL_ERROR", "OCR engine unavailable");
        }
        var result = engine.RecognizeAsync(software).AsTask().GetAwaiter().GetResult();
        var words = result.Lines.SelectMany(line => line.Words.Select(word => new OcrWord
        {
            Text = word.Text,
            Rect = new Rect
            {
                X = rect.X + (int)word.BoundingRect.X,
                Y = rect.Y + (int)word.BoundingRect.Y,
                W = (int)word.BoundingRect.Width,
                H = (int)word.BoundingRect.Height
            }
        })).ToArray();
        return new OcrResult { Text = result.Text, Words = words };
    }

    private static object ComputeHashResult(Rect rect, Bitmap bmp, string? algo)
    {
        var normalized = (algo ?? "ahash").ToLowerInvariant();
        string hash = normalized switch
        {
            "sha256" => ComputeSha256(bmp),
            _ => ComputeAHash(bmp)
        };
        if (normalized != "sha256" && normalized != "ahash")
        {
            normalized = "ahash";
        }
        return new { hash, algo = normalized, rect, w = bmp.Width, h = bmp.Height };
    }

    private static string ComputeAHash(Bitmap bmp)
    {
        using var small = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
        using (var gfx = Graphics.FromImage(small))
        {
            gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            gfx.DrawImage(bmp, new Rectangle(0, 0, 8, 8));
        }
        int total = 0;
        var gray = new int[64];
        int idx = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                var c = small.GetPixel(x, y);
                var g = (c.R + c.G + c.B) / 3;
                gray[idx++] = g;
                total += g;
            }
        }
        int avg = total / 64;
        ulong bits = 0;
        for (int i = 0; i < 64; i++)
        {
            if (gray[i] >= avg)
            {
                bits |= 1UL << (63 - i);
            }
        }
        return bits.ToString("X16");
    }

    private static string ComputeSha256(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(ms);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static double HashDiffScore(string a, string b, string algo)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 1.0;
        if (algo.Equals("sha256", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? 0.0 : 1.0;
        }
        try
        {
            ulong va = Convert.ToUInt64(a, 16);
            ulong vb = Convert.ToUInt64(b, 16);
            ulong xor = va ^ vb;
            int bits = 0;
            while (xor != 0)
            {
                bits += (int)(xor & 1);
                xor >>= 1;
            }
            return bits / 64.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private static Bitmap LoadBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DeskCtlException("INVALID_ARGS", "path required");
        }
        if (!File.Exists(path))
        {
            throw new DeskCtlException("NOT_FOUND", $"file not found: {path}");
        }
        using var temp = (Bitmap)Image.FromFile(path);
        return new Bitmap(temp);
    }

    private static double ImageDiffScore(Bitmap a, Bitmap b)
    {
        int w = Math.Min(a.Width, b.Width);
        int h = Math.Min(a.Height, b.Height);
        if (w <= 0 || h <= 0) return 1.0;
        double diff = 0;
        int count = w * h;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var ca = a.GetPixel(x, y);
                var cb = b.GetPixel(x, y);
                diff += Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
            }
        }
        return diff / (count * 255.0 * 3.0);
    }

    private static UiaNode BuildUiaTree(AutomationElement element, bool includeRects, int maxDepth, int maxNodes, ref int count)
    {
        if (element == null || count >= maxNodes) return new UiaNode();
        count++;
        var node = UiaNode.CreateFromElement(element, includeRects);
        if (maxDepth <= 0 || count >= maxNodes)
        {
            return node;
        }

        var children = new List<UiaNode>();
        var walker = TreeWalker.ControlViewWalker;
        var child = walker.GetFirstChild(element);
        while (child != null && count < maxNodes)
        {
            var childNode = BuildUiaTree(child, includeRects, maxDepth - 1, maxNodes, ref count);
            children.Add(childNode);
            child = walker.GetNextSibling(child);
        }
        node.Children = children;
        return node;
    }

    private static AutomationElement? FindUiaElement(IntPtr hwnd, UiaQuery query, out UiaElementInfo info)
    {
        info = new UiaElementInfo();
        var root = AutomationElement.FromHandle(hwnd);
        var walker = TreeWalker.ControlViewWalker;
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        int index = 0;
        int maxNodes = 5000;

        while (queue.Count > 0 && maxNodes-- > 0)
        {
            var el = queue.Dequeue();
            if (MatchesUia(el, query))
            {
                if (index == query.Nth)
                {
                    info = UiaElementInfo.FromElement(el, true);
                    return el;
                }
                index++;
            }

            var child = walker.GetFirstChild(el);
            while (child != null)
            {
                queue.Enqueue(child);
                child = walker.GetNextSibling(child);
            }
        }
        return null;
    }

    private static bool MatchesUia(AutomationElement element, UiaQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.NameContains))
        {
            var name = element.Current.Name ?? "";
            if (!name.Contains(query.NameContains, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrWhiteSpace(query.AutomationId))
        {
            var id = element.Current.AutomationId ?? "";
            if (!id.Equals(query.AutomationId, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrWhiteSpace(query.ClassName))
        {
            var cn = element.Current.ClassName ?? "";
            if (!cn.Equals(query.ClassName, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrWhiteSpace(query.ControlType))
        {
            var ct = element.Current.ControlType;
            var name = ct.ProgrammaticName?.Replace("ControlType.", "");
            if (!string.Equals(name, query.ControlType, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static IEnumerable<KeyValuePair<string, string>> FlattenObject(object? obj, string prefix)
    {
        if (obj == null)
        {
            yield return new KeyValuePair<string, string>(prefix, "null");
            yield break;
        }

        if (obj is string s)
        {
            yield return new KeyValuePair<string, string>(prefix, s);
            yield break;
        }

        if (obj is bool b)
        {
            yield return new KeyValuePair<string, string>(prefix, b.ToString().ToLowerInvariant());
            yield break;
        }

        if (obj is int or long or short or byte or uint or ulong or ushort or sbyte or double or float or decimal)
        {
            yield return new KeyValuePair<string, string>(prefix, Convert.ToString(obj) ?? "");
            yield break;
        }

        if (obj is DateTime or DateTimeOffset or TimeSpan or Guid or Enum)
        {
            yield return new KeyValuePair<string, string>(prefix, obj.ToString() ?? "");
            yield break;
        }

        if (obj is IEnumerable<object> list)
        {
            int idx = 0;
            foreach (var item in list)
            {
                foreach (var kv in FlattenObject(item, $"{prefix}[{idx}]"))
                {
                    yield return kv;
                }
                idx++;
            }
            if (idx == 0)
            {
                yield return new KeyValuePair<string, string>(prefix, "[]");
            }
            yield break;
        }

        if (obj is System.Collections.IEnumerable nonGeneric && obj is not string)
        {
            int idx = 0;
            foreach (var item in nonGeneric)
            {
                foreach (var kv in FlattenObject(item, $"{prefix}[{idx}]"))
                {
                    yield return kv;
                }
                idx++;
            }
            if (idx == 0)
            {
                yield return new KeyValuePair<string, string>(prefix, "[]");
            }
            yield break;
        }

        var type = obj.GetType();
        var props = type.GetProperties();
        if (props.Length == 0)
        {
            yield return new KeyValuePair<string, string>(prefix, obj.ToString() ?? "");
            yield break;
        }

        foreach (var prop in props)
        {
            object? value;
            try
            {
                value = prop.GetValue(obj);
            }
            catch
            {
                continue;
            }
            foreach (var kv in FlattenObject(value, $"{prefix}.{ToSnake(prop.Name)}"))
            {
                yield return kv;
            }
        }
    }

    private static object? JsonElementToObject(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in el.EnumerateObject())
                {
                    dict[prop.Name] = JsonElementToObject(prop.Value);
                }
                return dict;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in el.EnumerateArray())
                {
                    list.Add(JsonElementToObject(item));
                }
                return list;
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                if (el.TryGetInt64(out var l)) return l;
                if (el.TryGetDouble(out var d)) return d;
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcessCapture(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        if (proc == null)
        {
            return (-1, "", "failed to start process");
        }
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout.Trim(), stderr.Trim());
    }

    private static string ParseDate(string input)
    {
        if (DateTime.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt.ToString("MM/dd/yyyy");
        }
        if (DateTime.TryParse(input, out var any))
        {
            return any.ToString("MM/dd/yyyy");
        }
        throw new DeskCtlException("INVALID_ARGS", "date must be YYYY-MM-DD");
    }

    private static string ToSnake(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static bool TryMapSubcommand(string nounRaw, string verbRaw, out string mapped)
    {
        mapped = "";
        var noun = nounRaw.Replace("-", "_").ToLowerInvariant();
        var verb = verbRaw.Replace("-", "_").ToLowerInvariant();

        string? cmd = noun switch
        {
            "mouse" => verb switch
            {
                "move" => "mouse_move",
                "click" => "mouse_click",
                "down" => "mouse_down",
                "up" => "mouse_up",
                "drag" => "mouse_drag",
                "wheel" => "mouse_wheel",
                "pos" => "mouse_pos",
                "position" => "mouse_pos",
                _ => null
            },
            "overlay" => verb switch
            {
                "show" => "overlay_show",
                "update" => "overlay_update",
                "list" => "overlay_list",
                "clear" => "overlay_clear",
                "render" => "overlay_render",
                _ => null
            },
            "key" => verb switch
            {
                "tap" => "key_tap",
                "down" => "key_down",
                "up" => "key_up",
                _ => null
            },
            "keyboard" => verb switch
            {
                "type" => "text_type",
                _ => null
            },
            "window" => verb switch
            {
                "focus" => "focus_window",
                "move" => "window_move",
                "resize" => "window_resize",
                "minimize" => "window_minimize",
                "maximize" => "window_maximize",
                "restore" => "window_restore",
                "close" => "window_close",
                "list" => "list_windows",
                "active" => "active_window",
                _ => null
            },
            "display" => verb switch
            {
                "list" => "display_list",
                "enable" => "display_enable",
                "disable" => "display_disable",
                "primary" => "display_set_primary",
                "orientation" => "display_orientation",
                "geometry" => "get_displays",
                "info" => "get_displays",
                _ => null
            },
            "screen" => verb switch
            {
                "capture" => "screenshot",
                "hash" => "screen_hash",
                "diff" => "screen_diff",
                _ => null
            },
            "clipboard" => verb switch
            {
                "get" => "clipboard_get",
                "set" => "clipboard_set",
                "clear" => "clipboard_clear",
                "list" => "clipboard_formats",
                _ => null
            },
            "process" => verb switch
            {
                "list" => "process_list",
                "kill" => "process_kill",
                _ => null
            },
            "app" => verb switch
            {
                "list" => "app_list",
                "launch" => "launch",
                _ => null
            },
            "uia" => verb switch
            {
                "dump" => "uia_dump",
                "find" => "uia_find",
                "click" => "uia_click",
                "set" => "uia_set_value",
                _ => null
            },
            "ocr" => verb switch
            {
                "run" => "ocr",
                _ => null
            },
            "wait" => verb switch
            {
                "for" => "wait_for",
                _ => null
            },
            "file" => verb switch
            {
                "open" => "open_with_default",
                _ => null
            },
            "settings" => verb switch
            {
                "open" => "settings_open",
                _ => null
            },
            "system" => verb switch
            {
                "info" => "system_info",
                _ => null
            },
            "power" => verb switch
            {
                "sleep" => "power_sleep",
                "shutdown" => "power_shutdown",
                "restart" => "power_restart",
                "lock" => "lock",
                "wake" => "wake_display",
                _ => null
            },
            "desktop" => verb switch
            {
                "list" => "desktop_list",
                "switch" => "desktop_switch",
                "move_window" => "desktop_move_window",
                _ => null
            },
            "taskbar" => verb switch
            {
                "click" => "taskbar_click_app",
                "pin" => "taskbar_pin",
                "unpin" => "taskbar_unpin",
                _ => null
            },
            "start" => verb switch
            {
                "search" => "start_menu_search",
                _ => null
            },
            "start_menu" => verb switch
            {
                "search" => "start_menu_search",
                _ => null
            },
            "notifications" => verb switch
            {
                "list" => "notifications_list",
                _ => null
            },
            "notification" => verb switch
            {
                "clear" => "notification_clear",
                "click" => "notification_click",
                _ => null
            },
            "text" => verb switch
            {
                "click" => "click_text",
                _ => null
            },
            "icon" => verb switch
            {
                "click" => "click_icon",
                _ => null
            },
            "click" => verb switch
            {
                "text" => "click_text",
                "icon" => "click_icon",
                _ => null
            },
            "uwp" => verb switch
            {
                "list" => "uwp_list",
                "launch" => "uwp_launch",
                _ => null
            },
            "dpi" => verb switch
            {
                "status" => "dpi_status",
                "test" => "dpi_test_capture",
                _ => null
            },
            "task" => verb switch
            {
                "list" => "task_list",
                "create" => "task_create",
                "run" => "task_run",
                "delete" => "task_delete",
                _ => null
            },
            "time" => verb switch
            {
                "sleep" => "sleep",
                _ => null
            },
            "profile" => verb switch
            {
                "set" => "human_config_set",
                "get" => "human_config_get",
                "list" => "human_profiles_list",
                _ => null
            },
            "coordinator" => verb switch
            {
                "status" => "coordinator_status",
                _ => null
            },
            "session" => verb switch
            {
                "create" => "session_create",
                "list" => "session_list",
                _ => null
            },
            "lease" => verb switch
            {
                "acquire" => "lease_acquire",
                "list" => "lease_list",
                _ => null
            },
            "history" => verb switch
            {
                "list" => "history_list",
                _ => null
            },
            "action" => verb switch
            {
                "submit" => "action_submit",
                _ => null
            },
            _ => null
        };

        if (cmd == null)
        {
            return false;
        }

        mapped = cmd;
        return true;
    }

    private static string NormalizeShortFlag(string name)
    {
        return name switch
        {
            "d" => "display",
            "x" => "x",
            "y" => "y",
            "r" => "rect",
            "f" => "format",
            "o" => "return",
            "c" => "include-cursor",
            "g" => "grid",
            "k" => "key",
            "t" => "text",
            "m" => "mode",
            "s" => "ms",
            "w" => "w",
            "h" => "h",
            "p" => "path",
            "i" => "index",
            "n" => "name",
            _ => name
        };
    }

    private static void AppendCursorAndActiveWindow()
    {
        var pt = Native.GetCursorPosition();
        Console.WriteLine($"cursor_x={pt.X}");
        Console.WriteLine($"cursor_y={pt.Y}");
        if (DisplayHelper.TryGetDisplayForPoint(pt, out var display))
        {
            Console.WriteLine($"cursor_display_id={display.Id}");
            Console.WriteLine($"cursor_display_w={display.Rect.W}");
            Console.WriteLine($"cursor_display_h={display.Rect.H}");
        }
        var hwnd = Native.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            var info = WindowInfo.FromHwnd(hwnd);
            Console.WriteLine($"active_hwnd={info.Hwnd}");
            Console.WriteLine($"active_title={info.Title}");
            if (!string.IsNullOrWhiteSpace(info.Exe))
            {
                Console.WriteLine($"active_exe={info.Exe}");
            }
            Console.WriteLine($"active_pid={info.Pid}");
            Console.WriteLine($"active_visible={info.Visible.ToString().ToLowerInvariant()}");
            Console.WriteLine($"active_minimized={info.Minimized.ToString().ToLowerInvariant()}");
            Console.WriteLine($"active_rect_x={info.Rect.X}");
            Console.WriteLine($"active_rect_y={info.Rect.Y}");
            Console.WriteLine($"active_rect_w={info.Rect.W}");
            Console.WriteLine($"active_rect_h={info.Rect.H}");
        }
    }

    private static void AppendPlainEnglishNote(Response resp)
    {
        if (!resp.Ok || resp.Result == null) return;
        try
        {
            var json = JsonSerializer.Serialize(resp.Result, JsonOptions);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("imageRect", out _) && root.TryGetProperty("scaleX", out var sx) && root.TryGetProperty("scaleY", out var sy))
            {
                bool hasWarning = root.TryGetProperty("warning", out var warnEl) && warnEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(warnEl.GetString());
                if (root.TryGetProperty("gridApplied", out var gridEl) && gridEl.ValueKind == JsonValueKind.True)
                {
                    var scaleX = sx.GetDouble();
                    var scaleY = sy.GetDouble();
                    Console.WriteLine("note=Grid lines are drawn on the screenshot. Labels show pixel coordinates.");
                    Console.WriteLine($"note=To map image coords to screen coords: screen_x = source_rect.x + (image_x / {scaleX}); screen_y = source_rect.y + (image_y / {scaleY}).");
                    if (hasWarning)
                    {
                        Console.WriteLine("note=Grid overlay had a fallback; check result.warning for details.");
                    }
                }
            }
        }
        catch
        {
            // ignore note failures
        }
    }

    private static bool IsNegativeNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("-")) return false;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static void RunStdioLoop()
    {
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            line = line.TrimStart('\uFEFF');

            Response resp;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("jsonrpc", out _) || doc.RootElement.TryGetProperty("method", out _))
                {
                    var mcpResponse = HandleMcpMessage(doc.RootElement);
                    if (mcpResponse != null)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(mcpResponse, JsonOptions));
                    }
                    continue;
                }

                var req = JsonSerializer.Deserialize<Request>(line, JsonOptions);
                if (req == null || string.IsNullOrWhiteSpace(req.Cmd))
                {
                    resp = Response.ErrorResponse("INVALID_ARGS", "invalid request");
                }
                else
                {
                    resp = Dispatch(req);
                }
            }
            catch (Exception ex)
            {
                resp = Response.ErrorResponse("INVALID_ARGS", ex.Message);
            }

            Console.WriteLine(JsonSerializer.Serialize(resp, JsonOptions));
        }
    }

    private static object? HandleMcpMessage(JsonElement root)
    {
        var id = root.TryGetProperty("id", out var idEl) ? JsonElementToObject(idEl) : null;
        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
        if (string.IsNullOrWhiteSpace(method))
        {
            return McpError(id, -32600, "missing method");
        }

        if (id == null && method.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return method switch
        {
            "initialize" => new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "winmote", version = "0.1.0" }
                }
            },
            "tools/list" => new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    tools = GetAllCommands().Where(c => c != "help").Select(c => new
                    {
                        name = c,
                        description = $"Run winmote {c}.",
                        inputSchema = new
                        {
                            type = "object",
                            additionalProperties = true
                        }
                    }).ToArray()
                }
            },
            "tools/call" => HandleMcpToolCall(id, root),
            _ => McpError(id, -32601, $"unknown method '{method}'")
        };
    }

    private static object HandleMcpToolCall(object? id, JsonElement root)
    {
        if (!root.TryGetProperty("params", out var p) || !p.TryGetProperty("name", out var nameEl))
        {
            return McpError(id, -32602, "tools/call requires params.name");
        }

        var name = nameEl.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return McpError(id, -32602, "tool name required");
        }

        JsonElement args = default;
        if (p.TryGetProperty("arguments", out var a))
        {
            args = a;
        }
        args = ApplyMcpDefaults(name, args);

        var resp = Dispatch(new Request { Id = id?.ToString(), Cmd = name, Args = args });
        return new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(resp, JsonOptions)
                    }
                },
                isError = !resp.Ok
            }
        };
    }

    private static JsonElement ApplyMcpDefaults(string command, JsonElement args)
    {
        if (!CoordinatorCommandNeedsAgent(command))
        {
            return args;
        }

        var dict = args.ValueKind == JsonValueKind.Object
            ? JsonElementToObject(args) as Dictionary<string, object?>
            : new Dictionary<string, object?>();
        dict ??= new Dictionary<string, object?>();

        if (!dict.ContainsKey("agent") || dict["agent"] == null || string.IsNullOrWhiteSpace(dict["agent"]?.ToString()))
        {
            dict["agent"] = GetDefaultMcpAgentName();
        }

        if (command == "session_create" && !dict.ContainsKey("display"))
        {
            dict["display"] = 0;
        }

        return JsonSerializer.SerializeToElement(dict, JsonOptions);
    }

    private static bool CoordinatorCommandNeedsAgent(string command)
    {
        return command is "session_create" or "lease_acquire" or "lease_list" or "history_list" or "action_submit" or "overlay_update" or "overlay_clear" or "overlay_list";
    }

    private static string GetDefaultMcpAgentName()
    {
        var env = Environment.GetEnvironmentVariable("WINMOTE_AGENT")
            ?? Environment.GetEnvironmentVariable("CODEX_AGENT_NAME")
            ?? Environment.GetEnvironmentVariable("CODEX_SESSION_ID")
            ?? "Codex";
        env = Regex.Replace(env.Trim(), @"\s+", "-");
        return env.Length <= 32 ? env : env[..32];
    }

    private static object McpError(object? id, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        };
    }

    private static Response Dispatch(Request req)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = req.Cmd switch
            {
                "get_displays" => CmdGetDisplays(),
                "active_window" => CmdActiveWindow(),
                "list_windows" => CmdListWindows(req.Args),
                "screenshot" => CmdScreenshot(req.Args),
                "screen_hash" => CmdScreenHash(req.Args),
                "screen_diff" => CmdScreenDiff(req.Args),
                "wait_for" => CmdWaitFor(req.Args),
                "mouse_move" => CmdMouseMove(req.Args),
                "mouse_click" => CmdMouseClick(req.Args),
                "mouse_drag" => CmdMouseDrag(req.Args),
                "mouse_wheel" => CmdMouseWheel(req.Args),
                "mouse_down" => CmdMouseDown(req.Args),
                "mouse_up" => CmdMouseUp(req.Args),
                "overlay_show" => CmdOverlayShow(req.Args),
                "overlay_update" => CmdOverlayUpdate(req.Args),
                "overlay_list" => CmdOverlayList(req.Args),
                "overlay_clear" => CmdOverlayClear(req.Args),
                "overlay_render" => CmdOverlayRender(req.Args),
                "key_tap" => CmdKeyTap(req.Args),
                "key_down" => CmdKeyDown(req.Args),
                "key_up" => CmdKeyUp(req.Args),
                "text_type" => CmdTextType(req.Args),
                "focus_window" => CmdFocusWindow(req.Args),
                "window_move" => CmdWindowMove(req.Args),
                "window_resize" => CmdWindowResize(req.Args),
                "window_minimize" => CmdWindowMinimize(req.Args),
                "window_maximize" => CmdWindowMaximize(req.Args),
                "window_restore" => CmdWindowRestore(req.Args),
                "window_close" => CmdWindowClose(req.Args),
                "clipboard_get" => CmdClipboardGet(req.Args),
                "clipboard_set" => CmdClipboardSet(req.Args),
                "clipboard_clear" => CmdClipboardClear(),
                "process_list" => CmdProcessList(),
                "process_kill" => CmdProcessKill(req.Args),
                "app_list" => CmdAppList(),
                "uia_dump" => CmdUiaDump(req.Args),
                "uia_find" => CmdUiaFind(req.Args),
                "uia_click" => CmdUiaClick(req.Args),
                "uia_set_value" => CmdUiaSetValue(req.Args),
                "active_control" => CmdActiveControl(),
                "caret_position" => CmdCaretPosition(),
                "ocr" => CmdOcr(req.Args),
                "open_with_default" => CmdOpenWithDefault(req.Args),
                "settings_open" => CmdSettingsOpen(req.Args),
                "system_info" => CmdSystemInfo(),
                "desktop_list" => CmdDesktopList(),
                "desktop_switch" => CmdDesktopSwitch(req.Args),
                "desktop_move_window" => CmdDesktopMoveWindow(req.Args),
                "taskbar_click_app" => CmdTaskbarClickApp(req.Args),
                "start_menu_search" => CmdStartMenuSearch(req.Args),
                "notifications_list" => CmdNotificationsList(),
                "notification_clear" => CmdNotificationClear(req.Args),
                "notification_click" => CmdNotificationClick(req.Args),
                "display_list" => CmdDisplayList(),
                "display_enable" => CmdDisplayEnableDisable(req.Args, true),
                "display_disable" => CmdDisplayEnableDisable(req.Args, false),
                "display_set_primary" => CmdDisplaySetPrimary(req.Args),
                "display_orientation" => CmdDisplayOrientation(req.Args),
                "taskbar_pin" => CmdTaskbarPin(req.Args, true),
                "taskbar_unpin" => CmdTaskbarPin(req.Args, false),
                "uwp_list" => CmdUwpList(),
                "uwp_launch" => CmdUwpLaunch(req.Args),
                "dpi_status" => CmdDpiStatus(),
                "dpi_test_capture" => CmdDpiTestCapture(req.Args),
                "task_list" => CmdTaskList(req.Args),
                "task_create" => CmdTaskCreate(req.Args),
                "task_run" => CmdTaskRun(req.Args),
                "task_delete" => CmdTaskDelete(req.Args),
                "click_text" => CmdClickText(req.Args),
                "click_icon" => CmdClickIcon(req.Args),
                "clipboard_formats" => CmdClipboardFormats(),
                "lock" => CmdLock(),
                "power_sleep" => CmdPowerSleep(),
                "power_shutdown" => CmdPowerShutdown(),
                "power_restart" => CmdPowerRestart(),
                "wake_display" => CmdWakeDisplay(),
                "launch" => CmdLaunch(req.Args),
                "sleep" => CmdSleep(req.Args),
                "mouse_pos" => CmdMousePos(),
                "human_config_set" => CmdHumanConfigSet(req.Args),
                "human_config_get" => CmdHumanConfigGet(),
                "human_profiles_list" => CmdHumanProfilesList(),
                "coordinator_status" => CmdCoordinatorStatus(),
                "session_create" => CmdSessionCreate(req.Args),
                "session_list" => CmdSessionList(),
                "lease_acquire" => CmdLeaseAcquire(req.Args),
                "lease_list" => CmdLeaseList(req.Args),
                "history_list" => CmdHistoryList(req.Args),
                "action_submit" => CmdActionSubmit(req.Args),
                _ => throw new DeskCtlException("INVALID_CMD", $"unknown cmd '{req.Cmd}'")
            };

            sw.Stop();
            return Response.OkResponse(req.Id, result, sw.ElapsedMilliseconds);
        }
        catch (DeskCtlException ex)
        {
            sw.Stop();
            return Response.ErrorResponse(req.Id, ex.Code, ex.Message, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Response.ErrorResponse(req.Id, "INTERNAL_ERROR", ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static object CmdGetDisplays()
    {
        var displays = new List<DisplayInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Native.RECT lprc, IntPtr lparam) =>
        {
            var info = new Native.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<Native.MONITORINFOEX>();
            if (Native.GetMonitorInfo(hMon, ref info))
            {
                uint dpiX = 96;
                uint dpiY = 96;
                Native.GetDpiForMonitorSafe(hMon, ref dpiX, ref dpiY);

                displays.Add(new DisplayInfo
                {
                    Id = displays.Count,
                    Name = info.szDevice,
                    Rect = Rect.FromNative(info.rcMonitor),
                    Dpi = (int)dpiX,
                    Scale = Math.Round(dpiX / 96.0, 2),
                    Primary = (info.dwFlags & 1) == 1
                });
            }
            return true;
        }, IntPtr.Zero);

        return new { displays };
    }

    private static object CmdActiveWindow()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            throw new DeskCtlException("NOT_FOUND", "no active window");
        }

        return new { window = WindowInfo.FromHwnd(hwnd) };
    }

    private static object CmdListWindows(JsonElement args)
    {
        string? titleContains = args.TryGetProperty("title_contains", out var tc) ? tc.GetString() : null;
        string? exeContains = args.TryGetProperty("exe_contains", out var ec) ? ec.GetString() : null;
        bool visibleOnly = !args.TryGetProperty("visible_only", out var vo) || vo.GetBoolean();

        var windows = new List<WindowInfo>();
        Native.EnumWindows((hwnd, lparam) =>
        {
            if (visibleOnly && !Native.IsWindowVisible(hwnd))
            {
                return true;
            }

            var info = WindowInfo.FromHwnd(hwnd);
            if (!string.IsNullOrEmpty(titleContains) && !info.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(exeContains) && (info.Exe == null || !info.Exe.Contains(exeContains, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            windows.Add(info);
            return true;
        }, IntPtr.Zero);

        return new { windows };
    }
    private static object CmdScreenshot(JsonElement args)
    {
        var format = args.TryGetProperty("format", out var fmt) ? fmt.GetString() : "png";
        var returnMode = args.TryGetProperty("return", out var ret) ? ret.GetString() : "b64";
        var includeCursor = args.TryGetProperty("include_cursor", out var ic) && ic.GetBoolean();
        var grid = args.TryGetProperty("grid", out var g) && g.GetBoolean();
        var gridStep = args.TryGetProperty("grid_step", out var gs) ? gs.GetInt32() : 100;
        var gridAbs = args.TryGetProperty("grid_abs", out var ga) && ga.GetBoolean();

        Rect rect;
        if (args.TryGetProperty("display", out var disp))
        {
            rect = DisplayHelper.GetDisplayRect(disp.GetInt32());
        }
        else if (args.TryGetProperty("rect", out var r))
        {
            rect = Rect.FromJson(r);
        }
        else if (args.TryGetProperty("hwnd", out var h))
        {
            rect = WindowInfo.FromHwnd(Native.ParseHwnd(h.GetString())).Rect;
        }
        else
        {
            throw new DeskCtlException("INVALID_ARGS", "screenshot requires display, rect, or hwnd");
        }

        if (rect.W <= 0 || rect.H <= 0)
        {
            throw new DeskCtlException("INVALID_ARGS", "rect must be non-empty");
        }

        int maxW = args.TryGetProperty("max_w", out var mw) ? mw.GetInt32() : 0;
        int maxH = args.TryGetProperty("max_h", out var mh) ? mh.GetInt32() : 0;

        string? warning = null;
        Bitmap bmp;
        try
        {
            bmp = CaptureScreen(rect, includeCursor);
        }
        catch
        {
            // Retry with safest settings.
            warning = "capture_failed_retry_no_cursor_no_scale";
            includeCursor = false;
            maxW = 0;
            maxH = 0;
            bmp = CaptureScreen(rect, includeCursor);
        }

        Bitmap outputBmp = bmp;
        if (maxW > 0 || maxH > 0)
        {
            try
            {
                outputBmp = ImageHelper.Downscale(bmp, maxW, maxH);
            }
            catch
            {
                outputBmp = bmp;
                warning = "downscale_failed_fallback_to_original";
            }
        }

        var imageId = $"img-{Guid.NewGuid():N}";
        var scaleX = Math.Round((double)outputBmp.Width / rect.W, 5);
        var scaleY = Math.Round((double)outputBmp.Height / rect.H, 5);
        var imageRect = new Rect { X = 0, Y = 0, W = outputBmp.Width, H = outputBmp.Height };
        Rect? displayRect = null;
        if (args.TryGetProperty("display", out var dispForRect))
        {
            displayRect = DisplayHelper.GetDisplayRect(dispForRect.GetInt32());
        }
        bool gridApplied = false;
        if (grid && gridStep > 0)
        {
            try
            {
                ImageHelper.DrawGrid(outputBmp, rect, scaleX, scaleY, gridStep, gridAbs);
                gridApplied = true;
            }
            catch
            {
                warning = warning ?? "grid_failed_skipped";
            }
        }
        if (returnMode == "path")
        {
            var tempDir = Path.Combine(Path.GetTempPath(), AppName);
            Directory.CreateDirectory(tempDir);
            var ext = format == "jpg" ? "jpg" : "png";
            var path = Path.Combine(tempDir, $"{imageId}.{ext}");
            try
            {
                ImageHelper.SaveImage(outputBmp, path, format);
            }
            catch
            {
                if (!ReferenceEquals(outputBmp, bmp))
                {
                    outputBmp.Dispose();
                    outputBmp = bmp;
                }
                warning = "save_failed_fallback_to_original";
                ImageHelper.SaveImage(outputBmp, path, format);
            }

            var outW = outputBmp.Width;
            var outH = outputBmp.Height;
            if (!ReferenceEquals(outputBmp, bmp))
            {
                outputBmp.Dispose();
            }

            return new
            {
                path,
                w = outW,
                h = outH,
                rect,
                imageRect,
                sourceRect = rect,
                displayRect,
                scaleX,
                scaleY,
                warning,
                gridApplied,
                imageId
            };
        }

        using var ms = new MemoryStream();
        try
        {
            ImageHelper.SaveImage(outputBmp, ms, format);
        }
        catch
        {
            if (!ReferenceEquals(outputBmp, bmp))
            {
                outputBmp.Dispose();
                outputBmp = bmp;
            }
            warning = "save_failed_fallback_to_original";
            ImageHelper.SaveImage(outputBmp, ms, format);
        }
        var b64 = Convert.ToBase64String(ms.ToArray());

        var outWb64 = outputBmp.Width;
        var outHb64 = outputBmp.Height;
        if (!ReferenceEquals(outputBmp, bmp))
        {
            outputBmp.Dispose();
        }

        return new
        {
            imageB64 = b64,
            w = outWb64,
            h = outHb64,
            rect,
            imageRect,
            sourceRect = rect,
            displayRect,
            scaleX,
            scaleY,
            warning,
            gridApplied,
            imageId
        };
    }

    private static Bitmap CaptureScreen(Rect rect, bool includeCursor)
    {
        if (rect.W <= 0 || rect.H <= 0)
        {
            throw new DeskCtlException("INVALID_ARGS", "rect must be non-empty");
        }

        var bmp = new Bitmap(rect.W, rect.H, PixelFormat.Format32bppArgb);
        using (var gfx = Graphics.FromImage(bmp))
        {
            gfx.CopyFromScreen(rect.X, rect.Y, 0, 0, new Size(rect.W, rect.H), CopyPixelOperation.SourceCopy);
        }

        if (includeCursor)
        {
            CursorHelper.DrawCursor(bmp, rect);
        }

        return bmp;
    }

    private static object CmdMouseMove(JsonElement args)
    {
        var mode = args.TryGetProperty("mode", out var m) ? m.GetString() : "abs";
        var x = args.GetProperty("x").GetInt32();
        var y = args.GetProperty("y").GetInt32();
        var inputMode = GetInputMode(args, "physical");
        var overlayMs = args.TryGetProperty("overlay_ms", out var om) ? om.GetInt32() : 350;
        var label = GetOverlayLabel(args);

        var human = Humanization.ParseHuman(args);
        var start = Native.GetCursorPosition();
        var target = mode == "rel" ? new Point(start.X + x, start.Y + y) : new Point(x, y);

        if (inputMode == "ghost")
        {
            GhostCursorOverlay.ShowAt(target, overlayMs, false, label);
            return new { x = target.X, y = target.Y, durationMs = overlayMs, samples = 1, overshot = false, inputMode = "ghost", label };
        }

        if (!human.Enabled)
        {
            Native.SetCursorPos(target.X, target.Y);
            return new { x = target.X, y = target.Y, durationMs = 0, samples = 1, overshot = false, inputMode = "physical" };
        }

        var moveResult = Humanization.HumanMouseMove(start, target, human, _humanConfig);
        return new
        {
            x = moveResult.End.X,
            y = moveResult.End.Y,
            durationMs = moveResult.DurationMs,
            samples = moveResult.Samples,
            overshot = moveResult.Overshot,
            inputMode = "physical"
        };
    }

    private static object CmdMouseClick(JsonElement args)
    {
        var x = args.GetProperty("x").GetInt32();
        var y = args.GetProperty("y").GetInt32();
        var button = args.TryGetProperty("button", out var b) ? b.GetString() : "left";
        var clicks = args.TryGetProperty("clicks", out var c) ? c.GetInt32() : 1;
        var inputMode = GetInputMode(args, "physical");
        var overlayMs = args.TryGetProperty("overlay_ms", out var om) ? om.GetInt32() : 220;
        var label = GetOverlayLabel(args);

        if (inputMode is "ghost" or "message" or "auto")
        {
            GhostCursorOverlay.ShowAt(new Point(x, y), overlayMs, true, label);
        }

        if (inputMode is "ghost")
        {
            return new { clicked = false, x, y, button, inputMode = "ghost", note = "ghost mode only previews the target; no input was sent" };
        }

        if (inputMode is "message" or "auto")
        {
            if (TryWindowMessageClick(x, y, button, clicks, out var hwnd, out var error))
            {
                return new { clicked = true, x, y, button, clicks, inputMode = "message", hwnd = $"0x{hwnd.ToInt64():X}" };
            }

            if (inputMode == "message")
            {
                throw new DeskCtlException("INPUT_BACKEND_FAILED", error ?? "window message click failed");
            }
        }

        var human = Humanization.ParseHuman(args);
        Native.SetCursorPos(x, y);

        int totalMs = 0;
        int downMs = 0;

        for (int i = 0; i < clicks; i++)
        {
            if (human.Enabled)
            {
                var pre = Humanization.RandomRangeMs(_humanConfig.Mouse.PreMs, human.PreMs);
                Thread.Sleep(pre);
                totalMs += pre;
                downMs = Humanization.RandomRangeMs(_humanConfig.Mouse.DownMs, human.DownMs);
            }

            var down = button == "right" ? Native.MOUSEEVENTF.RIGHTDOWN : Native.MOUSEEVENTF.LEFTDOWN;
            var up = button == "right" ? Native.MOUSEEVENTF.RIGHTUP : Native.MOUSEEVENTF.LEFTUP;

            Native.SendMouse(down);
            if (downMs > 0)
            {
                Thread.Sleep(downMs);
                totalMs += downMs;
            }
            Native.SendMouse(up);

            if (i < clicks - 1)
            {
                int inter = human.Enabled ? Humanization.RandomRangeMs(_humanConfig.Mouse.InterClickMs, human.InterClickMs) : 0;
                if (inter > 0)
                {
                    Thread.Sleep(inter);
                    totalMs += inter;
                }
            }
        }

        return new { clicked = true, x, y, durationMs = totalMs, downMs, inputMode = "physical" };
    }

    private static object CmdOverlayShow(JsonElement args)
    {
        var x = args.GetProperty("x").GetInt32();
        var y = args.GetProperty("y").GetInt32();
        var durationMs = args.TryGetProperty("duration_ms", out var d) ? d.GetInt32() : 700;
        var pulse = args.TryGetProperty("pulse", out var p) && p.GetBoolean();
        var label = GetOverlayLabel(args);
        GhostCursorOverlay.ShowAt(new Point(x, y), durationMs, pulse, label);
        return new { shown = true, x, y, durationMs, pulse, label };
    }

    private static object CmdOverlayUpdate(JsonElement args)
    {
        var agent = args.TryGetProperty("agent", out var a) ? a.GetString() : GetOverlayLabel(args);
        if (string.IsNullOrWhiteSpace(agent))
        {
            throw new DeskCtlException("INVALID_ARGS", "agent required");
        }
        var x = args.GetProperty("x").GetInt32();
        var y = args.GetProperty("y").GetInt32();
        var display = args.TryGetProperty("display", out var d) ? d.GetInt32() : 0;
        var app = args.TryGetProperty("app", out var appEl) ? appEl.GetString() : null;
        var window = args.TryGetProperty("window", out var winEl) ? winEl.GetString() : null;
        var hwnd = args.TryGetProperty("hwnd", out var hwndEl) ? hwndEl.GetString() : null;
        var pulse = args.TryGetProperty("pulse", out var p) && p.GetBoolean();
        var ttlMs = args.TryGetProperty("ttl_ms", out var ttlEl) ? ttlEl.GetInt32() : 30000;
        return CoordinatorStore.UpdateOverlay(agent, x, y, display, app, window, hwnd, pulse, ttlMs);
    }

    private static object CmdOverlayList(JsonElement args)
    {
        var agent = args.TryGetProperty("agent", out var a) ? a.GetString() : null;
        return CoordinatorStore.ListOverlay(agent);
    }

    private static object CmdOverlayClear(JsonElement args)
    {
        var agent = args.TryGetProperty("agent", out var a) ? a.GetString() : null;
        return CoordinatorStore.ClearOverlay(agent);
    }

    private static object CmdOverlayRender(JsonElement args)
    {
        var durationMs = args.TryGetProperty("duration_ms", out var d) ? d.GetInt32() : 1200;
        var overlay = CoordinatorStore.GetActiveOverlayCursors();
        GhostCursorOverlay.ShowMany(overlay, durationMs);
        return new { shown = overlay.Count, durationMs };
    }

    private static string? GetOverlayLabel(JsonElement args)
    {
        if (!args.TryGetProperty("label", out var labelEl))
        {
            return null;
        }

        var label = labelEl.GetString();
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        label = label.Trim();
        return label.Length <= 32 ? label : label[..32];
    }

    private static string GetInputMode(JsonElement args, string fallback)
    {
        var mode = args.TryGetProperty("input_mode", out var im) ? im.GetString() : fallback;
        mode = string.IsNullOrWhiteSpace(mode) ? fallback : mode.Trim().ToLowerInvariant();
        return mode is "physical" or "message" or "auto" or "ghost"
            ? mode
            : throw new DeskCtlException("INVALID_ARGS", "input_mode must be physical, message, auto, or ghost");
    }

    private static bool TryWindowMessageClick(int x, int y, string? button, int clicks, out IntPtr hwnd, out string? error)
    {
        error = null;
        hwnd = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });
        if (hwnd == IntPtr.Zero)
        {
            error = "no window at target point";
            return false;
        }

        var clientPoint = new Native.POINT { X = x, Y = y };
        if (!Native.ScreenToClient(hwnd, ref clientPoint))
        {
            error = "failed to convert screen point to client coordinates";
            return false;
        }

        var normalizedButton = string.IsNullOrWhiteSpace(button) ? "left" : button.Trim().ToLowerInvariant();
        var downMsg = normalizedButton == "right" ? Native.WM_RBUTTONDOWN : Native.WM_LBUTTONDOWN;
        var upMsg = normalizedButton == "right" ? Native.WM_RBUTTONUP : Native.WM_LBUTTONUP;
        var wParam = normalizedButton == "right" ? new IntPtr(Native.MK_RBUTTON) : new IntPtr(Native.MK_LBUTTON);
        var lParam = Native.MakeLParam(clientPoint.X, clientPoint.Y);

        for (var i = 0; i < Math.Max(1, clicks); i++)
        {
            if (!Native.PostMessage(hwnd, downMsg, wParam, lParam) || !Native.PostMessage(hwnd, upMsg, IntPtr.Zero, lParam))
            {
                error = "PostMessage returned false";
                return false;
            }
        }

        return true;
    }

    private static object CmdMouseDrag(JsonElement args)
    {
        var from = Rect.FromJson(args.GetProperty("from"));
        var to = Rect.FromJson(args.GetProperty("to"));
        var button = args.TryGetProperty("button", out var b) ? b.GetString() : "left";
        var durationMs = args.TryGetProperty("duration_ms", out var d) ? d.GetInt32() : 0;

        Native.SetCursorPos(from.X, from.Y);
        var down = button == "right" ? Native.MOUSEEVENTF.RIGHTDOWN : Native.MOUSEEVENTF.LEFTDOWN;
        var up = button == "right" ? Native.MOUSEEVENTF.RIGHTUP : Native.MOUSEEVENTF.LEFTUP;
        Native.SendMouse(down);

        if (durationMs <= 0)
        {
            Native.SetCursorPos(to.X, to.Y);
        }
        else
        {
            Humanization.LinearMove(new Point(from.X, from.Y), new Point(to.X, to.Y), durationMs);
        }

        Native.SendMouse(up);
        return new { dragged = true };
    }

    private static object CmdMouseWheel(JsonElement args)
    {
        var delta = args.GetProperty("delta").GetInt32();
        var x = args.TryGetProperty("x", out var xEl) ? xEl.GetInt32() : Native.GetCursorPosition().X;
        var y = args.TryGetProperty("y", out var yEl) ? yEl.GetInt32() : Native.GetCursorPosition().Y;
        Native.SetCursorPos(x, y);
        Native.SendMouseWheel(delta);
        return new { scrolled = true };
    }

    private static object CmdMouseDown(JsonElement args)
    {
        var x = args.TryGetProperty("x", out var xEl) ? xEl.GetInt32() : Native.GetCursorPosition().X;
        var y = args.TryGetProperty("y", out var yEl) ? yEl.GetInt32() : Native.GetCursorPosition().Y;
        var button = args.TryGetProperty("button", out var b) ? b.GetString() : "left";
        Native.SetCursorPos(x, y);
        var down = button == "right" ? Native.MOUSEEVENTF.RIGHTDOWN : Native.MOUSEEVENTF.LEFTDOWN;
        Native.SendMouse(down);
        return new { down = true, x, y, button };
    }

    private static object CmdMouseUp(JsonElement args)
    {
        var x = args.TryGetProperty("x", out var xEl) ? xEl.GetInt32() : Native.GetCursorPosition().X;
        var y = args.TryGetProperty("y", out var yEl) ? yEl.GetInt32() : Native.GetCursorPosition().Y;
        var button = args.TryGetProperty("button", out var b) ? b.GetString() : "left";
        Native.SetCursorPos(x, y);
        var up = button == "right" ? Native.MOUSEEVENTF.RIGHTUP : Native.MOUSEEVENTF.LEFTUP;
        Native.SendMouse(up);
        return new { up = true, x, y, button };
    }

    private static object CmdKeyTap(JsonElement args)
    {
        if (!args.TryGetProperty("keys", out var keysEl) || keysEl.ValueKind != JsonValueKind.Array)
        {
            throw new DeskCtlException("INVALID_ARGS", "keys[] required");
        }

        var keys = keysEl.EnumerateArray().Select(k => k.GetString() ?? string.Empty).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        if (keys.Count == 0)
        {
            throw new DeskCtlException("INVALID_ARGS", "keys[] required");
        }

        var human = Humanization.ParseHuman(args);
        var mods = keys.Take(keys.Count - 1).ToList();
        var main = keys[^1];

        foreach (var mod in mods)
        {
            Native.SendKeyDown(KeyMap.ToVirtualKey(mod));
        }

        if (human.Enabled)
        {
            Thread.Sleep(Humanization.RandomRangeMs(_humanConfig.Keyboard.ChordRollMs, human.ChordRollMs));
        }

        Native.SendKeyDown(KeyMap.ToVirtualKey(main));
        int holdMs = human.Enabled ? Humanization.RandomRangeMs(_humanConfig.Keyboard.HoldMs, human.HoldMs) : 0;
        if (holdMs > 0)
        {
            Thread.Sleep(holdMs);
        }
        Native.SendKeyUp(KeyMap.ToVirtualKey(main));

        foreach (var mod in mods.AsEnumerable().Reverse())
        {
            Native.SendKeyUp(KeyMap.ToVirtualKey(mod));
        }

        return new { sent = true, keys };
    }

    private static object CmdTextType(JsonElement args)
    {
        var text = args.GetProperty("text").GetString() ?? string.Empty;
        var enter = args.TryGetProperty("enter", out var en) && en.GetBoolean();
        var method = args.TryGetProperty("method", out var m) ? m.GetString() : "sendinput";
        var human = Humanization.ParseHuman(args);

        if (method != "sendinput")
        {
            throw new DeskCtlException("INVALID_ARGS", "only method=sendinput supported in v0");
        }

        int durationMs = 0;
        int mistakes = 0;
        int corrected = 0;

        foreach (var ch in text)
        {
            if (human.Enabled && Humanization.ShouldMistype(_humanConfig.Keyboard, human))
            {
                var wrong = Humanization.MistypeChar(ch);
                if (wrong != '\0')
                {
                    Native.SendUnicodeChar(wrong);
                    mistakes++;
                    Thread.Sleep(Humanization.RandomRangeMs(_humanConfig.Keyboard.InterKeyMs, human.InterKeyMs));
                    Native.SendVirtualKey(Native.VK_BACK);
                    corrected++;
                }
            }

            Native.SendUnicodeChar(ch);
            if (human.Enabled)
            {
                int keyDown = Humanization.RandomRangeMs(_humanConfig.Keyboard.KeyDownMs, human.KeyDownMs);
                int inter = Humanization.RandomRangeMs(_humanConfig.Keyboard.InterKeyMs, human.InterKeyMs);
                Thread.Sleep(keyDown + inter);
                durationMs += keyDown + inter;

                if (Humanization.ShouldPauseAfterChar(ch, _humanConfig.Keyboard, human))
                {
                    int pauseMs = Humanization.RandomPauseAfterChar(ch, _humanConfig.Keyboard, human);
                    Thread.Sleep(pauseMs);
                    durationMs += pauseMs;
                }
            }
        }

        if (enter)
        {
            Native.SendVirtualKey(Native.VK_RETURN);
        }

        return new { typed = true, len = text.Length, durationMs, mistakes, corrected, enter };
    }

    private static object CmdFocusWindow(JsonElement args)
    {
        IntPtr hwnd;
        if (args.TryGetProperty("hwnd", out var h))
        {
            hwnd = Native.ParseHwnd(h.GetString());
        }
        else
        {
            string? titleContains = args.TryGetProperty("title_contains", out var tc) ? tc.GetString() : null;
            string? exeContains = args.TryGetProperty("exe_contains", out var ec) ? ec.GetString() : null;
            int nth = args.TryGetProperty("nth", out var n) ? n.GetInt32() : 0;
            hwnd = WindowHelper.FindWindow(titleContains, exeContains, nth);
        }

        if (hwnd == IntPtr.Zero)
        {
            throw new DeskCtlException("NOT_FOUND", "window not found");
        }

        Native.SetForegroundWindow(hwnd);
        return new { focused = true, hwnd = $"0x{hwnd.ToInt64():X}" };
    }

    private static object CmdWindowMove(JsonElement args)
    {
        var hwnd = ResolveWindowHandle(args);
        var rect = WindowInfo.FromHwnd(hwnd).Rect;
        if (args.TryGetProperty("rect", out var r))
        {
            rect = Rect.FromJson(r);
        }
        if (args.TryGetProperty("x", out var x)) rect.X = x.GetInt32();
        if (args.TryGetProperty("y", out var y)) rect.Y = y.GetInt32();
        Native.MoveWindow(hwnd, rect.X, rect.Y, rect.W, rect.H, true);
        return new { moved = true, hwnd = $"0x{hwnd.ToInt64():X}", rect };
    }

    private static object CmdWindowResize(JsonElement args)
    {
        var hwnd = ResolveWindowHandle(args);
        var rect = WindowInfo.FromHwnd(hwnd).Rect;
        if (args.TryGetProperty("rect", out var r))
        {
            rect = Rect.FromJson(r);
        }
        if (args.TryGetProperty("w", out var w)) rect.W = w.GetInt32();
        if (args.TryGetProperty("h", out var h)) rect.H = h.GetInt32();
        if (args.TryGetProperty("x", out var x)) rect.X = x.GetInt32();
        if (args.TryGetProperty("y", out var y)) rect.Y = y.GetInt32();
        Native.MoveWindow(hwnd, rect.X, rect.Y, rect.W, rect.H, true);
        return new { resized = true, hwnd = $"0x{hwnd.ToInt64():X}", rect };
    }

    private static object CmdWindowMinimize(JsonElement args)
    {
        var hwnd = ResolveWindowHandle(args);
        Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
        return new { minimized = true, hwnd = $"0x{hwnd.ToInt64():X}" };
    }

    private static object CmdWindowMaximize(JsonElement args)
    {
        var hwnd = ResolveWindowHandle(args);
        Native.ShowWindow(hwnd, Native.SW_MAXIMIZE);
        return new { maximized = true, hwnd = $"0x{hwnd.ToInt64():X}" };
    }

    private static object CmdWindowRestore(JsonElement args)
    {
        var hwnd = ResolveWindowHandle(args);
        Native.ShowWindow(hwnd, Native.SW_RESTORE);
        return new { restored = true, hwnd = $"0x{hwnd.ToInt64():X}" };
    }

    private static object CmdWindowClose(JsonElement args)
    {
        var hwnd = ResolveWindowHandle(args);
        Native.PostMessage(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        return new { closed = true, hwnd = $"0x{hwnd.ToInt64():X}" };
    }

    private static object CmdClipboardGet(JsonElement args)
    {
        var format = args.TryGetProperty("format", out var f) ? (f.GetString() ?? "text") : "text";
        return RunSta(() => ClipboardGetImpl(format));
    }

    private static object CmdClipboardSet(JsonElement args)
    {
        var text = args.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var format = args.TryGetProperty("format", out var f) ? (f.GetString() ?? "text") : "text";
        RunSta(() =>
        {
            if (format.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                Clipboard.SetText(text);
            }
            else if (format.Equals("html", StringComparison.OrdinalIgnoreCase))
            {
                Clipboard.SetText(text, TextDataFormat.Html);
            }
            else if (format.Equals("rtf", StringComparison.OrdinalIgnoreCase))
            {
                Clipboard.SetText(text, TextDataFormat.Rtf);
            }
            else
            {
                throw new DeskCtlException("INVALID_ARGS", "clipboard_set supports format: text|html|rtf");
            }
            return true;
        });
        return new { set = true, len = text.Length, format };
    }

    private static object CmdClipboardClear()
    {
        RunSta(() => { Clipboard.Clear(); return true; });
        return new { cleared = true };
    }

    private static object CmdClipboardFormats()
    {
        return new { formats = new[] { "text", "html", "rtf", "image", "files" } };
    }

    private static object ClipboardGetImpl(string format)
    {
        if (format.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            return new { text, format = "text" };
        }
        if (format.Equals("html", StringComparison.OrdinalIgnoreCase))
        {
            var text = Clipboard.ContainsText(TextDataFormat.Html) ? Clipboard.GetText(TextDataFormat.Html) : "";
            return new { text, format = "html" };
        }
        if (format.Equals("rtf", StringComparison.OrdinalIgnoreCase))
        {
            var text = Clipboard.ContainsText(TextDataFormat.Rtf) ? Clipboard.GetText(TextDataFormat.Rtf) : "";
            return new { text, format = "rtf" };
        }
        if (format.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            if (!Clipboard.ContainsImage())
            {
                return new { text = "", format = "image", path = "", w = 0, h = 0 };
            }
            using var img = Clipboard.GetImage();
            if (img == null) return new { text = "", format = "image", path = "", w = 0, h = 0 };
            using var bmp = new Bitmap(img);
            var imageId = $"clip-{Guid.NewGuid():N}";
            var tempDir = Path.Combine(Path.GetTempPath(), AppName);
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, $"{imageId}.png");
            ImageHelper.SaveImage(bmp, path, "png");
            return new { format = "image", path, w = bmp.Width, h = bmp.Height };
        }
        if (format.Equals("files", StringComparison.OrdinalIgnoreCase))
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return new { format = "files", files = Array.Empty<string>() };
            }
            var list = Clipboard.GetFileDropList();
            var files = list.Cast<string>().ToArray();
            return new { format = "files", files };
        }

        throw new DeskCtlException("INVALID_ARGS", "clipboard_get supports format: text|html|rtf|image|files");
    }

    private static object CmdProcessList()
    {
        var list = Process.GetProcesses()
            .OrderBy(p => p.ProcessName)
            .Select(p => new
            {
                pid = p.Id,
                name = p.ProcessName,
                exe = SafeGetExe(p),
                title = SafeGetTitle(p)
            })
            .ToArray();
        return new { processes = list };
    }

    private static object CmdProcessKill(JsonElement args)
    {
        var killed = new List<int>();
        if (args.TryGetProperty("pid", out var pidEl))
        {
            var pid = pidEl.GetInt32();
            var proc = Process.GetProcessById(pid);
            proc.Kill(true);
            killed.Add(pid);
        }
        else
        {
            var nameContains = args.TryGetProperty("name_contains", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(nameContains))
            {
                throw new DeskCtlException("INVALID_ARGS", "process_kill requires pid or name-contains");
            }
            foreach (var proc in Process.GetProcesses())
            {
                if (proc.ProcessName.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        proc.Kill(true);
                        killed.Add(proc.Id);
                    }
                    catch
                    {
                    }
                }
            }
        }
        return new { killed = killed.ToArray() };
    }

    private static object CmdAppList()
    {
        var names = new[] { "chrome", "edge", "firefox", "notepad", "calc", "explorer", "cmd", "powershell", "wt" };
        var apps = names.Select(n =>
        {
            var resolved = ResolveLaunchPath(n);
            return new
            {
                name = n,
                resolvedPath = resolved,
                exists = File.Exists(resolved) || FindOnPath(resolved) != null
            };
        }).ToArray();
        return new { apps };
    }

    private static object CmdUiaDump(JsonElement args)
    {
        var hwnd = args.TryGetProperty("hwnd", out var h) ? Native.ParseHwnd(h.GetString()) : Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            throw new DeskCtlException("NOT_FOUND", "no active window");
        }
        var maxDepth = args.TryGetProperty("max_depth", out var md) ? md.GetInt32() : 6;
        var maxNodes = args.TryGetProperty("max_nodes", out var mn) ? mn.GetInt32() : 2000;
        var includeRects = !args.TryGetProperty("include_rects", out var ir) || ir.GetBoolean();
        var root = AutomationElement.FromHandle(hwnd);
        int count = 0;
        var node = BuildUiaTree(root, includeRects, maxDepth, maxNodes, ref count);
        return new { root = node, nodes = count };
    }

    private static object CmdUiaFind(JsonElement args)
    {
        var hwnd = args.TryGetProperty("hwnd", out var h) ? Native.ParseHwnd(h.GetString()) : Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            throw new DeskCtlException("NOT_FOUND", "no active window");
        }
        var query = UiaQuery.FromArgs(args);
        var element = FindUiaElement(hwnd, query, out var info);
        if (element == null)
        {
            throw new DeskCtlException("NOT_FOUND", "ui element not found");
        }
        return new { element = info };
    }

    private static object CmdUiaClick(JsonElement args)
    {
        var hwnd = args.TryGetProperty("hwnd", out var h) ? Native.ParseHwnd(h.GetString()) : Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            throw new DeskCtlException("NOT_FOUND", "no active window");
        }
        var query = UiaQuery.FromArgs(args);
        var element = FindUiaElement(hwnd, query, out var info);
        if (element == null || info.Rect == null)
        {
            throw new DeskCtlException("NOT_FOUND", "ui element not found");
        }
        var button = args.TryGetProperty("button", out var b) ? b.GetString() : "left";
        var centerX = info.Rect.X + info.Rect.W / 2;
        var centerY = info.Rect.Y + info.Rect.H / 2;
        var clickArgs = JsonSerializer.SerializeToElement(new { x = centerX, y = centerY, button, human = new { enabled = true } });
        var result = CmdMouseClick(clickArgs);
        return new { clicked = true, element = info, mouse = result };
    }

    private static object CmdUiaSetValue(JsonElement args)
    {
        var hwnd = args.TryGetProperty("hwnd", out var h) ? Native.ParseHwnd(h.GetString()) : Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            throw new DeskCtlException("NOT_FOUND", "no active window");
        }
        var query = UiaQuery.FromArgs(args);
        if (!args.TryGetProperty("value", out var v))
        {
            throw new DeskCtlException("INVALID_ARGS", "value required");
        }
        var value = v.GetString() ?? string.Empty;
        var element = FindUiaElement(hwnd, query, out var info);
        if (element == null)
        {
            throw new DeskCtlException("NOT_FOUND", "ui element not found");
        }
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj) && vpObj is ValuePattern vp)
        {
            vp.SetValue(value);
            return new { set = true, method = "value_pattern", element = info };
        }

        element.SetFocus();
        foreach (var ch in value)
        {
            Native.SendUnicodeChar(ch);
        }
        return new { set = true, method = "sendinput", element = info };
    }

    private static object CmdActiveControl()
    {
        var element = AutomationElement.FocusedElement;
        if (element == null)
        {
            throw new DeskCtlException("NOT_FOUND", "no focused control");
        }
        return new { element = UiaElementInfo.FromElement(element, true) };
    }

    private static object CmdCaretPosition()
    {
        var element = AutomationElement.FocusedElement;
        if (element == null)
        {
            throw new DeskCtlException("NOT_FOUND", "no focused control");
        }
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var tpObj) && tpObj is TextPattern tp)
        {
            var ranges = tp.GetSelection();
            if (ranges.Length > 0)
            {
                var rects = ranges[0].GetBoundingRectangles();
                if (rects.Length > 0)
                {
                    var r = rects[0];
                    var rect = new Rect { X = (int)r.X, Y = (int)r.Y, W = (int)r.Width, H = (int)r.Height };
                    return new { active = true, rect };
                }
            }
        }
        throw new DeskCtlException("NOT_FOUND", "caret position not available");
    }

    private static object CmdOcr(JsonElement args)
    {
        var rect = ResolveCaptureRect(args, true);
        var language = args.TryGetProperty("language", out var l) ? l.GetString() : null;
        var result = RunOcr(rect, language);
        return new { text = result.Text, words = result.Words };
    }

    private static object CmdScreenHash(JsonElement args)
    {
        var rect = ResolveCaptureRect(args, true);
        using var bmp = CaptureScreen(rect, false);
        var algo = args.TryGetProperty("algo", out var a) ? a.GetString() : "ahash";
        var hasMaxW = args.TryGetProperty("max_w", out var mw);
        var hasMaxH = args.TryGetProperty("max_h", out var mh);
        if (hasMaxW || hasMaxH)
        {
            var maxW = hasMaxW && mw.ValueKind == JsonValueKind.Number ? mw.GetInt32() : 0;
            var maxH = hasMaxH && mh.ValueKind == JsonValueKind.Number ? mh.GetInt32() : 0;
            using var scaled = ImageHelper.Downscale(bmp, maxW, maxH);
            return ComputeHashResult(rect, scaled, algo);
        }
        return ComputeHashResult(rect, bmp, algo);
    }

    private static object CmdScreenDiff(JsonElement args)
    {
        var algo = args.TryGetProperty("algo", out var a) ? a.GetString() : "ahash";
        if (args.TryGetProperty("a_hash", out var ah) && args.TryGetProperty("b_hash", out var bh))
        {
            var aHash = ah.GetString() ?? "";
            var bHash = bh.GetString() ?? "";
            var hashScore = HashDiffScore(aHash, bHash, algo ?? "ahash");
            return new { changeScore = hashScore, algo = algo ?? "ahash" };
        }

        if (!args.TryGetProperty("a", out var ap) || !args.TryGetProperty("b", out var bp))
        {
            throw new DeskCtlException("INVALID_ARGS", "screen_diff requires --a/--b or --a-hash/--b-hash");
        }

        using var aBmp = LoadBitmap(ap.GetString());
        using var bBmp = LoadBitmap(bp.GetString());
        var diffScore = ImageDiffScore(aBmp, bBmp);
        return new { changeScore = diffScore, algo = "pixel" };
    }

    private static object CmdWaitFor(JsonElement args)
    {
        var type = args.TryGetProperty("type", out var t) ? t.GetString() : "";
        var timeoutMs = args.TryGetProperty("timeout_ms", out var tm) ? tm.GetInt32() : 10000;
        var pollMs = args.TryGetProperty("poll_ms", out var pm) ? pm.GetInt32() : 250;
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "window_title_contains":
                {
                    var hwnd = args.TryGetProperty("hwnd", out var h) ? Native.ParseHwnd(h.GetString()) : Native.GetForegroundWindow();
                    var title = hwnd == IntPtr.Zero ? "" : Native.GetWindowTitle(hwnd);
                    var expected = args.TryGetProperty("title_contains", out var tc) ? tc.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(expected) && title.Contains(expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return new { met = true, elapsedMs = (int)sw.ElapsedMilliseconds, detail = new { title } };
                    }
                    break;
                }
                case "uia_exists":
                {
                    var hwnd = args.TryGetProperty("hwnd", out var h) ? Native.ParseHwnd(h.GetString()) : Native.GetForegroundWindow();
                    if (hwnd != IntPtr.Zero)
                    {
                        var query = UiaQuery.FromArgs(args);
                        var element = FindUiaElement(hwnd, query, out var info);
                        if (element != null)
                        {
                            return new { met = true, elapsedMs = (int)sw.ElapsedMilliseconds, detail = info };
                        }
                    }
                    break;
                }
                case "ocr_regex":
                {
                    var pattern = args.TryGetProperty("pattern", out var p) ? p.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(pattern))
                    {
                        var rect = ResolveCaptureRect(args, true);
                        var language = args.TryGetProperty("language", out var l) ? l.GetString() : null;
                        var ocr = RunOcr(rect, language);
                        var text = ocr.Text ?? "";
                        if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                        {
                            return new { met = true, elapsedMs = (int)sw.ElapsedMilliseconds, detail = new { text } };
                        }
                    }
                    break;
                }
                case "screen_change":
                {
                    var rect = ResolveCaptureRect(args, true);
                    var minChange = args.TryGetProperty("min_change", out var mc) ? mc.GetInt32() : 8;
                    string firstHash;
                    using (var bmp = CaptureScreen(rect, false))
                    {
                        firstHash = ComputeAHash(bmp);
                    }
                    string nextHash;
                    using (var bmp = CaptureScreen(rect, false))
                    {
                        nextHash = ComputeAHash(bmp);
                    }
                    var diff = HashDiffScore(firstHash, nextHash, "ahash");
                    if (diff * 100 >= minChange)
                    {
                        return new { met = true, elapsedMs = (int)sw.ElapsedMilliseconds, detail = new { changeScore = diff } };
                    }
                    break;
                }
                default:
                    throw new DeskCtlException("INVALID_ARGS", $"unknown wait_for type '{type}'");
            }
            Thread.Sleep(pollMs);
        }

        return new { met = false, elapsedMs = (int)sw.ElapsedMilliseconds };
    }

    private static object CmdKeyDown(JsonElement args)
    {
        var keys = ParseKeyList(args);
        foreach (var key in keys)
        {
            var vk = KeyMap.ToVirtualKey(key);
            Native.SendKeyDown(vk);
        }
        return new { down = true, keys };
    }

    private static object CmdKeyUp(JsonElement args)
    {
        var keys = ParseKeyList(args);
        foreach (var key in keys)
        {
            var vk = KeyMap.ToVirtualKey(key);
            Native.SendKeyUp(vk);
        }
        return new { up = true, keys };
    }

    private static object CmdOpenWithDefault(JsonElement args)
    {
        var path = args.TryGetProperty("path", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DeskCtlException("INVALID_ARGS", "path required");
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return new { opened = true, path };
    }

    private static object CmdSettingsOpen(JsonElement args)
    {
        var uri = args.TryGetProperty("uri", out var u) ? u.GetString() : null;
        var page = args.TryGetProperty("page", out var p) ? p.GetString() : null;
        var target = uri;
        if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(page))
        {
            target = page.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ? page : $"ms-settings:{page}";
        }
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new DeskCtlException("INVALID_ARGS", "page or uri required");
        }
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return new { opened = true, uri = target };
    }

    private static object CmdSystemInfo()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return new
        {
            osVersion = Environment.OSVersion.VersionString,
            osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            machine = Environment.MachineName,
            user = Environment.UserName,
            isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator),
            is64Bit = Environment.Is64BitProcess,
            dotnet = Environment.Version.ToString()
        };
    }

    private static object CmdLock()
    {
        var ok = Native.LockWorkStation();
        return new { locked = ok };
    }

    private static object CmdPowerSleep()
    {
        var ok = Native.SetSuspendState(false, true, true);
        return new { sleep = ok };
    }

    private static object CmdPowerShutdown()
    {
        var ok = Native.ExitWindowsEx(Native.EWX_SHUTDOWN | Native.EWX_FORCEIFHUNG, 0);
        return new { shutdown = ok };
    }

    private static object CmdPowerRestart()
    {
        var ok = Native.ExitWindowsEx(Native.EWX_REBOOT | Native.EWX_FORCEIFHUNG, 0);
        return new { restart = ok };
    }

    private static object CmdWakeDisplay()
    {
        Native.SendMessage(Native.HWND_BROADCAST, Native.WM_SYSCOMMAND, new IntPtr(Native.SC_MONITORPOWER), new IntPtr(-1));
        return new { wake = true };
    }

    private static object CmdDesktopList()
    {
        var manager = VirtualDesktopHelper.TryCreateInternalManager();
        if (manager == null)
        {
            throw new DeskCtlException("INTERNAL_ERROR", "virtual desktop manager unavailable");
        }
        var desktops = VirtualDesktopHelper.GetDesktops(manager);
        var current = manager.GetCurrentDesktop();
        var currentId = current.GetId();
        var list = desktops.Select((d, i) => new
        {
            index = i,
            id = d.GetId().ToString(),
            current = d.GetId() == currentId
        }).ToArray();
        return new { desktops = list, currentId = currentId.ToString(), count = list.Length };
    }

    private static object CmdDesktopSwitch(JsonElement args)
    {
        var manager = VirtualDesktopHelper.TryCreateInternalManager();
        if (manager == null)
        {
            throw new DeskCtlException("INTERNAL_ERROR", "virtual desktop manager unavailable");
        }

        var target = VirtualDesktopHelper.ResolveDesktop(manager, args);
        manager.SwitchDesktop(target);
        return new { switched = true, id = target.GetId().ToString() };
    }

    private static object CmdDesktopMoveWindow(JsonElement args)
    {
        var manager = VirtualDesktopHelper.TryCreateInternalManager();
        if (manager == null)
        {
            throw new DeskCtlException("INTERNAL_ERROR", "virtual desktop manager unavailable");
        }

        var hwnd = ResolveWindowHandle(args);
        var target = VirtualDesktopHelper.ResolveDesktop(manager, args);
        manager.MoveWindowToDesktop(hwnd, target);
        return new { moved = true, hwnd = $"0x{hwnd.ToInt64():X}", id = target.GetId().ToString() };
    }

    private static object CmdTaskbarClickApp(JsonElement args)
    {
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DeskCtlException("INVALID_ARGS", "name required");
        }

        var taskbarHwnd = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd == IntPtr.Zero)
        {
            throw new DeskCtlException("NOT_FOUND", "taskbar not found");
        }

        var query = new UiaQuery { NameContains = name, ControlType = "Button", Nth = 0 };
        var element = FindUiaElement(taskbarHwnd, query, out var info);
        if (element == null || info.Rect == null)
        {
            throw new DeskCtlException("NOT_FOUND", "taskbar button not found");
        }

        var centerX = info.Rect.X + info.Rect.W / 2;
        var centerY = info.Rect.Y + info.Rect.H / 2;
        var clickArgs = JsonSerializer.SerializeToElement(new { x = centerX, y = centerY, button = "left", human = new { enabled = true } });
        var result = CmdMouseClick(clickArgs);
        return new { clicked = true, element = info, mouse = result };
    }

    private static object CmdStartMenuSearch(JsonElement args)
    {
        var text = args.TryGetProperty("text", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DeskCtlException("INVALID_ARGS", "text required");
        }
        var enter = !args.TryGetProperty("enter", out var e) || e.GetBoolean();

        Native.SendKeyDown(Native.VK_LWIN);
        Native.SendKeyUp(Native.VK_LWIN);
        Thread.Sleep(150);

        foreach (var ch in text)
        {
            Native.SendUnicodeChar(ch);
        }

        if (enter)
        {
            Native.SendVirtualKey(Native.VK_RETURN);
        }

        return new { searched = true, text, enter };
    }

    private static object CmdAudioDevicesList()
    {
        try
        {
            var devices = AudioHelper.ListDevices();
            var currentRender = AudioHelper.GetDefaultDeviceId(EDataFlow.eRender, ERole.eConsole);
            var currentCapture = AudioHelper.GetDefaultDeviceId(EDataFlow.eCapture, ERole.eConsole);

            var list = devices.Select(d => new
            {
                id = d.Id,
                name = d.Name,
                flow = d.Flow == EDataFlow.eRender ? "render" : "capture",
                state = d.State.ToString(),
                isDefaultConsole = d.Id == (d.Flow == EDataFlow.eRender ? currentRender : currentCapture)
            }).ToArray();
            return new { devices = list };
        }
        catch (Exception ex)
        {
            throw new DeskCtlException("INTERNAL_ERROR", $"audio list failed: {ex.Message}");
        }
    }

    private static object CmdAudioDefaultSet(JsonElement args)
    {
        var flow = args.TryGetProperty("flow", out var f) ? f.GetString() : "render";
        var dataFlow = flow != null && flow.Equals("capture", StringComparison.OrdinalIgnoreCase) ? EDataFlow.eCapture : EDataFlow.eRender;

        string? id = args.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DeskCtlException("INVALID_ARGS", "audio_default_set requires id or name");
            }
            var match = AudioHelper.ListDevices()
                .Where(d => d.Flow == dataFlow && d.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (match == null)
            {
                throw new DeskCtlException("NOT_FOUND", "audio device not found");
            }
            id = match.Id;
        }

        AudioHelper.SetDefaultDevice(id, ERole.eConsole);
        AudioHelper.SetDefaultDevice(id, ERole.eMultimedia);
        AudioHelper.SetDefaultDevice(id, ERole.eCommunications);
        return new { set = true, id, flow = dataFlow == EDataFlow.eRender ? "render" : "capture" };
    }

    private static object CmdMicMute(JsonElement args, bool mute)
    {
        var id = args.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                var match = AudioHelper.ListDevices()
                    .Where(d => d.Flow == EDataFlow.eCapture && d.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                if (match == null)
                {
                    throw new DeskCtlException("NOT_FOUND", "microphone not found");
                }
                id = match.Id;
            }
            else
            {
                id = AudioHelper.GetDefaultDeviceId(EDataFlow.eCapture, ERole.eConsole);
            }
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new DeskCtlException("NOT_FOUND", "microphone not found");
        }

        AudioHelper.SetMute(id, mute);
        return new { muted = mute, id };
    }

    private static object CmdNotificationsList()
    {
        try
        {
            var history = ToastNotificationManager.History;
            var toasts = history.GetHistory();
            var list = new List<object>();
            foreach (var toast in toasts)
            {
                string? appId = null;
                try
                {
                    appId = toast.RemoteId;
                }
                catch
                {
                }
                var xml = toast.Content;
                var text = ExtractToastText(xml);
                list.Add(new
                {
                    tag = toast.Tag,
                    group = toast.Group,
                    app = appId,
                    text
                });
            }
            return new { notifications = list.ToArray(), count = list.Count };
        }
        catch (Exception ex)
        {
            return new { notifications = Array.Empty<object>(), count = 0, warning = $"notifications list failed: {ex.Message}" };
        }
    }

    private static object CmdNotificationClear(JsonElement args)
    {
        var history = ToastNotificationManager.History;
        var tag = args.TryGetProperty("tag", out var t) ? t.GetString() : null;
        var group = args.TryGetProperty("group", out var g) ? g.GetString() : null;
        var app = args.TryGetProperty("app", out var a) ? a.GetString() : null;

        if (string.IsNullOrWhiteSpace(tag) && string.IsNullOrWhiteSpace(group) && string.IsNullOrWhiteSpace(app))
        {
            history.Clear();
            return new { cleared = true, scope = "all" };
        }

        if (!string.IsNullOrWhiteSpace(tag) && !string.IsNullOrWhiteSpace(group))
        {
            history.Remove(tag, group);
            return new { cleared = true, tag, group };
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            history.Remove(tag);
            return new { cleared = true, tag };
        }

        if (!string.IsNullOrWhiteSpace(app))
        {
            history.Clear(app);
            return new { cleared = true, app };
        }

        return new { cleared = false };
    }

    private static object CmdNotificationClick(JsonElement args)
    {
        var text = args.TryGetProperty("text", out var t) ? t.GetString() : null;
        var pattern = args.TryGetProperty("pattern", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(pattern))
        {
            throw new DeskCtlException("INVALID_ARGS", "notification_click requires text or pattern");
        }

        // Open Action Center (Win+A)
        Native.SendKeyDown(Native.VK_LWIN);
        Native.SendVirtualKey((ushort)'A');
        Native.SendKeyUp(Native.VK_LWIN);
        Thread.Sleep(400);

        var clickObj = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(text)) clickObj["text"] = text;
        if (!string.IsNullOrWhiteSpace(pattern)) clickObj["pattern"] = pattern;
        if (args.TryGetProperty("rect", out var r)) clickObj["rect"] = JsonElementToObject(r);
        if (args.TryGetProperty("display", out var d)) clickObj["display"] = d.GetInt32();
        if (args.TryGetProperty("language", out var l)) clickObj["language"] = l.GetString();
        if (args.TryGetProperty("button", out var b)) clickObj["button"] = b.GetString();
        var clickArgs = JsonSerializer.SerializeToElement(clickObj);

        var result = CmdClickText(clickArgs);
        return new { clicked = true, via = "action_center", result };
    }

    private static object CmdDisplayList()
    {
        var list = new List<object>();
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            uint? state = null;
            if (DisplayHelper.TryGetDisplayDevice(screens[i].DeviceName, out var dd))
            {
                state = dd.StateFlags;
            }
            list.Add(new
            {
                index = i,
                name = screens[i].DeviceName,
                primary = screens[i].Primary,
                rect = new Rect { X = screens[i].Bounds.X, Y = screens[i].Bounds.Y, W = screens[i].Bounds.Width, H = screens[i].Bounds.Height },
                state = state?.ToString() ?? ""
            });
        }
        return new { displays = list };
    }

    private static object CmdDisplayEnableDisable(JsonElement args, bool enable)
    {
        var name = ResolveDisplayName(args);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DeskCtlException("INVALID_ARGS", "display name not found");
        }

        if (!Native.TryGetCurrentDisplaySettings(name, out var mode))
        {
            throw new DeskCtlException("OS_ERROR", "failed to get display settings");
        }

        if (enable)
        {
            int width = args.TryGetProperty("width", out var w) ? w.GetInt32() : (int)mode.dmPelsWidth;
            int height = args.TryGetProperty("height", out var h) ? h.GetInt32() : (int)mode.dmPelsHeight;
            if (width <= 0 || height <= 0)
            {
                throw new DeskCtlException("INVALID_ARGS", "width/height required to enable display");
            }
            mode.dmPelsWidth = (uint)width;
            mode.dmPelsHeight = (uint)height;
            mode.dmFields |= Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT;
        }
        else
        {
            mode.dmPelsWidth = 0;
            mode.dmPelsHeight = 0;
            mode.dmFields |= Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT;
        }

        Native.ChangeDisplaySettingsEx(name, ref mode, IntPtr.Zero, Native.CDS_UPDATEREGISTRY | Native.CDS_NORESET, IntPtr.Zero);
        var res = Native.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (res != Native.DISP_CHANGE_SUCCESSFUL)
        {
            throw new DeskCtlException("OS_ERROR", $"ChangeDisplaySettings failed ({res})");
        }
        return new { enabled = enable, name };
    }

    private static object CmdDisplaySetPrimary(JsonElement args)
    {
        var name = ResolveDisplayName(args);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DeskCtlException("INVALID_ARGS", "display name not found");
        }
        if (!Native.TryGetCurrentDisplaySettings(name, out var mode))
        {
            throw new DeskCtlException("OS_ERROR", "failed to get display settings");
        }
        mode.dmPositionX = 0;
        mode.dmPositionY = 0;
        mode.dmFields |= Native.DM_POSITION;
        Native.ChangeDisplaySettingsEx(name, ref mode, IntPtr.Zero, Native.CDS_SET_PRIMARY | Native.CDS_UPDATEREGISTRY | Native.CDS_NORESET, IntPtr.Zero);
        var res = Native.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (res != Native.DISP_CHANGE_SUCCESSFUL)
        {
            throw new DeskCtlException("OS_ERROR", $"ChangeDisplaySettings failed ({res})");
        }
        return new { primary = true, name };
    }

    private static object CmdDisplayOrientation(JsonElement args)
    {
        var name = ResolveDisplayName(args);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DeskCtlException("INVALID_ARGS", "display name not found");
        }
        if (!args.TryGetProperty("orientation", out var o))
        {
            throw new DeskCtlException("INVALID_ARGS", "orientation required");
        }
        var deg = o.GetInt32();
        int orientation = deg switch
        {
            0 => Native.DMDO_DEFAULT,
            90 => Native.DMDO_90,
            180 => Native.DMDO_180,
            270 => Native.DMDO_270,
            _ => throw new DeskCtlException("INVALID_ARGS", "orientation must be 0|90|180|270")
        };

        if (!Native.TryGetCurrentDisplaySettings(name, out var mode))
        {
            throw new DeskCtlException("OS_ERROR", "failed to get display settings");
        }

        bool swap = (mode.dmDisplayOrientation == Native.DMDO_90 || mode.dmDisplayOrientation == Native.DMDO_270) ^
                    (orientation == Native.DMDO_90 || orientation == Native.DMDO_270);
        if (swap)
        {
            uint tmp = mode.dmPelsWidth;
            mode.dmPelsWidth = mode.dmPelsHeight;
            mode.dmPelsHeight = tmp;
        }
        mode.dmDisplayOrientation = (uint)orientation;
        mode.dmFields |= Native.DM_DISPLAYORIENTATION | Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT;

        var res = Native.ChangeDisplaySettingsEx(name, ref mode, IntPtr.Zero, Native.CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (res != Native.DISP_CHANGE_SUCCESSFUL)
        {
            throw new DeskCtlException("OS_ERROR", $"ChangeDisplaySettings failed ({res})");
        }
        return new { orientation = deg, name };
    }

    private static object CmdTaskbarPin(JsonElement args, bool pin)
    {
        var path = args.TryGetProperty("path", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DeskCtlException("INVALID_ARGS", "path required");
        }
        var resolved = ResolveLaunchPath(path);
        if (!File.Exists(resolved))
        {
            var fromPath = FindOnPath(resolved);
            if (fromPath != null)
            {
                resolved = fromPath;
            }
        }
        if (!File.Exists(resolved))
        {
            throw new DeskCtlException("NOT_FOUND", $"file not found: {resolved}");
        }

        var verb = pin ? "pin to taskbar" : "unpin from taskbar";
        if (!TryInvokeShellVerb(resolved, verb))
        {
            throw new DeskCtlException("OS_ERROR", $"verb not found: {verb}");
        }
        return new { pinned = pin, path = resolved };
    }

    private static object CmdUwpList()
    {
        var list = new List<object>();
        var pm = new PackageManager();
        foreach (var package in pm.FindPackagesForUser(string.Empty))
        {
            try
            {
                var entries = package.GetAppListEntriesAsync().AsTask().GetAwaiter().GetResult();
                foreach (var entry in entries)
                {
                    var name = entry.DisplayInfo?.DisplayName ?? "";
                    list.Add(new
                    {
                        aumid = entry.AppUserModelId,
                        name,
                        package = package.Id.Name,
                        package_family = package.Id.FamilyName,
                        publisher = package.PublisherDisplayName
                    });
                }
            }
            catch
            {
            }
        }
        return new { apps = list.ToArray(), count = list.Count };
    }

    private static object CmdUwpLaunch(JsonElement args)
    {
        var aumid = args.TryGetProperty("aumid", out var a) ? a.GetString() : null;
        if (string.IsNullOrWhiteSpace(aumid))
        {
            throw new DeskCtlException("INVALID_ARGS", "aumid required");
        }
        var arguments = args.TryGetProperty("args", out var ar) ? ar.GetString() ?? "" : "";
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        var hr = manager.ActivateApplication(aumid, arguments, ActivateOptions.None, out var pid);
        if (hr != 0)
        {
            throw new DeskCtlException("OS_ERROR", $"activation failed (hr=0x{hr:X8})");
        }
        return new { launched = true, aumid, pid };
    }

    private static object CmdDpiStatus()
    {
        var list = new List<object>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Native.RECT lprc, IntPtr lparam) =>
        {
            var info = new Native.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<Native.MONITORINFOEX>();
            if (Native.GetMonitorInfo(hMon, ref info))
            {
                uint dpiX = 96;
                uint dpiY = 96;
                Native.GetDpiForMonitorSafe(hMon, ref dpiX, ref dpiY);
                var scale = Math.Round(dpiX / 96.0, 2);
                list.Add(new
                {
                    name = info.szDevice,
                    rect = Rect.FromNative(info.rcMonitor),
                    dpiX = (int)dpiX,
                    dpiY = (int)dpiY,
                    scale
                });
            }
            return true;
        }, IntPtr.Zero);

        return new { monitors = list.ToArray() };
    }

    private static object CmdDpiTestCapture(JsonElement args)
    {
        var rect = ResolveCaptureRect(args, true);
        var size = args.TryGetProperty("size", out var s) ? s.GetInt32() : 256;
        if (size < 64) size = 64;
        if (size > 2048) size = 2048;
        var grid = !args.TryGetProperty("grid", out var g) || g.GetBoolean();

        var srcW = rect.W;
        var srcH = rect.H;
        var x = rect.X + Math.Max(0, (srcW - size) / 2);
        var y = rect.Y + Math.Max(0, (srcH - size) / 2);
        var crop = new Rect { X = x, Y = y, W = Math.Min(size, srcW), H = Math.Min(size, srcH) };

        using var bmp = CaptureScreen(crop, false);
        if (grid)
        {
            ImageHelper.DrawGrid(bmp, crop, 1.0, 1.0, 16, true);
        }
        var imageId = $"img-{Guid.NewGuid():N}";
        var tempDir = Path.Combine(Path.GetTempPath(), AppName);
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, $"{imageId}.png");
        ImageHelper.SaveImage(bmp, path, "png");

        return new
        {
            path,
            w = bmp.Width,
            h = bmp.Height,
            rect = crop,
            sourceRect = rect,
            imageId
        };
    }

    private static object CmdTaskList(JsonElement args)
    {
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
        var cmd = "schtasks.exe";
        var cmdArgs = name != null && name.Length > 0
            ? $"/Query /FO LIST /TN \"{name}\""
            : "/Query /FO LIST";
        var res = RunProcessCapture(cmd, cmdArgs);
        if (res.ExitCode != 0)
        {
            throw new DeskCtlException("OS_ERROR", string.IsNullOrWhiteSpace(res.StdErr) ? res.StdOut : res.StdErr);
        }
        return new { output = res.StdOut };
    }

    private static object CmdTaskRun(JsonElement args)
    {
        var name = args.GetProperty("name").GetString() ?? "";
        var res = RunProcessCapture("schtasks.exe", $"/Run /TN \"{name}\"");
        if (res.ExitCode != 0)
        {
            throw new DeskCtlException("OS_ERROR", string.IsNullOrWhiteSpace(res.StdErr) ? res.StdOut : res.StdErr);
        }
        return new { ran = true, name };
    }

    private static object CmdTaskDelete(JsonElement args)
    {
        var name = args.GetProperty("name").GetString() ?? "";
        var res = RunProcessCapture("schtasks.exe", $"/Delete /TN \"{name}\" /F");
        if (res.ExitCode != 0)
        {
            throw new DeskCtlException("OS_ERROR", string.IsNullOrWhiteSpace(res.StdErr) ? res.StdOut : res.StdErr);
        }
        return new { deleted = true, name };
    }

    private static object CmdTaskCreate(JsonElement args)
    {
        var name = args.GetProperty("name").GetString() ?? "";
        var cmd = args.GetProperty("cmd").GetString() ?? "";
        var argStr = args.TryGetProperty("args", out var a) ? a.GetString() : null;
        var schedule = args.TryGetProperty("schedule", out var s) ? s.GetString() : "once";
        var time = args.TryGetProperty("time", out var t) ? t.GetString() : null;
        var date = args.TryGetProperty("date", out var d) ? d.GetString() : null;
        var interval = args.TryGetProperty("interval", out var i) ? i.GetInt32() : 1;
        var user = args.TryGetProperty("user", out var u) ? u.GetString() : null;
        var password = args.TryGetProperty("password", out var p) ? p.GetString() : null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cmd))
        {
            throw new DeskCtlException("INVALID_ARGS", "task_create requires name and cmd");
        }

        var sch = (schedule ?? "once").ToLowerInvariant();
        var sc = sch switch
        {
            "once" => "ONCE",
            "daily" => "DAILY",
            "onlogon" => "ONLOGON",
            "onstartup" => "ONSTART",
            "minute" => "MINUTE",
            _ => throw new DeskCtlException("INVALID_ARGS", "schedule must be once|daily|onlogon|onstartup|minute")
        };

        var tr = string.IsNullOrWhiteSpace(argStr) ? cmd : $"{cmd} {argStr}";
        var sb = new StringBuilder();
        sb.Append("/Create /F ");
        sb.Append($"/TN \"{name}\" ");
        sb.Append($"/TR \"{tr}\" ");
        sb.Append($"/SC {sc} ");

        if (sc == "ONCE" || sc == "DAILY")
        {
            if (string.IsNullOrWhiteSpace(time))
            {
                throw new DeskCtlException("INVALID_ARGS", "time required for once/daily schedule (HH:MM)");
            }
            sb.Append($"/ST {time} ");
            var useDate = string.IsNullOrWhiteSpace(date) ? DateTime.Today.ToString("MM/dd/yyyy") : ParseDate(date);
            sb.Append($"/SD {useDate} ");
            if (sc == "DAILY" && interval > 1)
            {
                sb.Append($"/MO {interval} ");
            }
        }
        else if (sc == "MINUTE")
        {
            if (interval < 1) interval = 1;
            sb.Append($"/MO {interval} ");
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            sb.Append($"/RU \"{user}\" ");
            sb.Append($"/RP \"{password ?? ""}\" ");
        }

        var res = RunProcessCapture("schtasks.exe", sb.ToString().Trim());
        if (res.ExitCode != 0)
        {
            throw new DeskCtlException("OS_ERROR", string.IsNullOrWhiteSpace(res.StdErr) ? res.StdOut : res.StdErr);
        }
        return new { created = true, name, schedule = sc };
    }

    private static bool TryInvokeShellVerb(string path, string verb)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return false;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var folderPath = Path.GetDirectoryName(path);
            var fileName = Path.GetFileName(path);
            dynamic folder = shell.NameSpace(folderPath);
            if (folder == null) return false;
            dynamic item = folder.ParseName(fileName);
            if (item == null) return false;
            dynamic verbs = item.Verbs();
            int count = verbs.Count();
            for (int i = 0; i < count; i++)
            {
                dynamic v = verbs.Item(i);
                string name = v.Name as string ?? "";
                var normalized = NormalizeVerb(name);
                if (normalized.Contains(verb, StringComparison.OrdinalIgnoreCase))
                {
                    v.DoIt();
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static string NormalizeVerb(string verb)
    {
        var v = verb.Replace("&", "").Replace("...", "").Trim().ToLowerInvariant();
        return v;
    }

    private static object CmdRegionSelect()
    {
        var rect = RegionSelector.SelectRegion();
        if (rect == null)
        {
            throw new DeskCtlException("NOT_FOUND", "region not selected");
        }
        return new { rect };
    }

    private static object CmdRegionFromWindow(JsonElement args)
    {
        var hwnd = ResolveWindowHandle(args);
        var mode = args.TryGetProperty("mode", out var m) ? m.GetString() : "full";
        var padding = args.TryGetProperty("padding", out var p) ? p.GetInt32() : 0;
        Rect rect;
        if (mode != null && mode.Equals("client", StringComparison.OrdinalIgnoreCase))
        {
            rect = WindowInfo.FromHwnd(hwnd).Rect;
            var client = Native.GetClientRectScreen(hwnd);
            rect = client;
        }
        else
        {
            rect = WindowInfo.FromHwnd(hwnd).Rect;
        }

        if (padding != 0)
        {
            rect = new Rect
            {
                X = rect.X - padding,
                Y = rect.Y - padding,
                W = rect.W + padding * 2,
                H = rect.H + padding * 2
            };
        }

        return new { rect, hwnd = $"0x{hwnd.ToInt64():X}" };
    }

    private static object CmdClickText(JsonElement args)
    {
        var text = args.TryGetProperty("text", out var t) ? t.GetString() : null;
        var pattern = args.TryGetProperty("pattern", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(pattern))
        {
            throw new DeskCtlException("INVALID_ARGS", "click_text requires text or pattern");
        }

        var rect = ResolveCaptureRect(args, true);
        var language = args.TryGetProperty("language", out var l) ? l.GetString() : null;
        var ocr = RunOcr(rect, language);

        OcrWord? match = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            match = ocr.Words.FirstOrDefault(w => w.Text.Equals(text, StringComparison.OrdinalIgnoreCase));
        }
        if (match == null && !string.IsNullOrWhiteSpace(pattern))
        {
            var rx = new Regex(pattern, RegexOptions.IgnoreCase);
            match = ocr.Words.FirstOrDefault(w => rx.IsMatch(w.Text));
        }

        if (match == null)
        {
            throw new DeskCtlException("NOT_FOUND", "text not found");
        }

        var button = args.TryGetProperty("button", out var b) ? b.GetString() : "left";
        var centerX = match.Rect.X + match.Rect.W / 2;
        var centerY = match.Rect.Y + match.Rect.H / 2;
        var clickArgs = JsonSerializer.SerializeToElement(new { x = centerX, y = centerY, button, human = new { enabled = true } });
        var result = CmdMouseClick(clickArgs);
        return new { clicked = true, word = match, mouse = result };
    }

    private static object CmdClickIcon(JsonElement args)
    {
        var icon = args.TryGetProperty("icon", out var i) ? i.GetString() : null;
        if (string.IsNullOrWhiteSpace(icon))
        {
            throw new DeskCtlException("INVALID_ARGS", "click_icon requires icon path");
        }
        if (!File.Exists(icon))
        {
            throw new DeskCtlException("NOT_FOUND", $"icon not found: {icon}");
        }

        var rect = ResolveCaptureRect(args, true);
        var threshold = args.TryGetProperty("threshold", out var th) ? th.GetInt32() : 90;
        if (threshold < 0) threshold = 0;
        if (threshold > 100) threshold = 100;

        using var screenBmp = CaptureScreen(rect, false);
        using var iconBmp = LoadBitmap(icon);
        var match = ImageHelper.FindTemplate(screenBmp, iconBmp, threshold / 100.0);
        if (match == null)
        {
            throw new DeskCtlException("NOT_FOUND", "icon not found");
        }

        var button = args.TryGetProperty("button", out var b) ? b.GetString() : "left";
        var centerX = rect.X + match.X + match.W / 2;
        var centerY = rect.Y + match.Y + match.H / 2;
        var clickArgs = JsonSerializer.SerializeToElement(new { x = centerX, y = centerY, button, human = new { enabled = true } });
        var result = CmdMouseClick(clickArgs);
        return new { clicked = true, match = match, mouse = result };
    }

    private static string ExtractToastText(XmlDocument xml)
    {
        try
        {
            var texts = xml.GetElementsByTagName("text");
            var sb = new StringBuilder();
            foreach (var node in texts)
            {
                if (node is XmlElement el)
                {
                    var value = el.InnerText?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        if (sb.Length > 0) sb.Append(" | ");
                        sb.Append(value);
                    }
                }
            }
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static object CmdLaunch(JsonElement args)
    {
        var path = args.GetProperty("path").GetString();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DeskCtlException("INVALID_ARGS", "path required");
        }
        var argStr = args.TryGetProperty("args", out var a) ? a.GetString() : null;
        var cwd = args.TryGetProperty("cwd", out var c) ? c.GetString() : null;

        var resolvedPath = ResolveLaunchPath(path);
        var psi = new ProcessStartInfo(resolvedPath)
        {
            Arguments = argStr ?? string.Empty,
            WorkingDirectory = cwd ?? string.Empty,
            UseShellExecute = true
        };

        var proc = Process.Start(psi);
        return new { launched = true, pid = proc?.Id ?? 0, resolvedPath };
    }

    private static object CmdSleep(JsonElement args)
    {
        var ms = args.GetProperty("ms").GetInt32();
        Thread.Sleep(ms);
        return new { sleptMs = ms };
    }

    private static object CmdMousePos()
    {
        var pt = Native.GetCursorPosition();
        return new { x = pt.X, y = pt.Y };
    }

    private static object CmdCoordinatorStatus()
    {
        return CoordinatorStore.Status();
    }

    private static object CmdSessionCreate(JsonElement args)
    {
        var agent = args.GetProperty("agent").GetString();
        if (string.IsNullOrWhiteSpace(agent))
        {
            throw new DeskCtlException("INVALID_ARGS", "agent required");
        }
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
        var display = args.TryGetProperty("display", out var d) ? d.GetInt32() : 0;
        return CoordinatorStore.CreateSession(agent, name, display);
    }

    private static object CmdSessionList()
    {
        return CoordinatorStore.ListSessions();
    }

    private static object CmdLeaseAcquire(JsonElement args)
    {
        var agent = args.GetProperty("agent").GetString();
        var resource = args.GetProperty("resource").GetString();
        if (string.IsNullOrWhiteSpace(agent) || string.IsNullOrWhiteSpace(resource))
        {
            throw new DeskCtlException("INVALID_ARGS", "agent and resource required");
        }
        var mode = args.TryGetProperty("mode", out var m) ? m.GetString() : "message";
        var ttlMs = args.TryGetProperty("ttl_ms", out var t) ? t.GetInt32() : 30000;
        return CoordinatorStore.AcquireLease(agent, resource, mode, ttlMs);
    }

    private static object CmdLeaseList(JsonElement args)
    {
        var agent = args.TryGetProperty("agent", out var a) ? a.GetString() : null;
        var resource = args.TryGetProperty("resource", out var r) ? r.GetString() : null;
        return CoordinatorStore.ListLeases(agent, resource);
    }

    private static object CmdHistoryList(JsonElement args)
    {
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        var agent = args.TryGetProperty("agent", out var a) ? a.GetString() : null;
        return CoordinatorStore.ListHistory(limit, agent);
    }

    private static object CmdActionSubmit(JsonElement args)
    {
        var agent = args.GetProperty("agent").GetString();
        var type = args.GetProperty("type").GetString();
        if (string.IsNullOrWhiteSpace(agent) || string.IsNullOrWhiteSpace(type))
        {
            throw new DeskCtlException("INVALID_ARGS", "agent and type required");
        }

        var dryRun = !args.TryGetProperty("dry_run", out var dr) || dr.GetBoolean();
        var inputMode = args.TryGetProperty("input_mode", out var im) ? im.GetString() : "ghost";
        object? execution = null;

        if (type.Equals("click", StringComparison.OrdinalIgnoreCase))
        {
            if (!args.TryGetProperty("x", out var xEl) || !args.TryGetProperty("y", out var yEl))
            {
                throw new DeskCtlException("INVALID_ARGS", "click action requires x and y");
            }

            var x = xEl.GetInt32();
            var y = yEl.GetInt32();
            CoordinatorStore.UpdateOverlay(agent, x, y, args.TryGetProperty("display", out var displayEl) ? displayEl.GetInt32() : 0, null, null, args.TryGetProperty("hwnd", out var hwndEl) ? hwndEl.GetString() : null, true, 30000);
            if (!dryRun)
            {
                var clickArgs = JsonSerializer.SerializeToElement(new
                {
                    x,
                    y,
                    input_mode = inputMode,
                    label = agent,
                    human = new { enabled = true }
                }, JsonOptions);
                execution = CmdMouseClick(clickArgs);
            }
            else
            {
                execution = new { dryRun = true, inputMode, x, y };
            }
        }
        else
        {
            throw new DeskCtlException("INVALID_ARGS", "only click action is implemented in the coordinator spine");
        }

        return CoordinatorStore.RecordAction(agent, type, inputMode, dryRun, execution);
    }

    private static object CmdHumanConfigSet(JsonElement args)
    {
        _humanConfig = HumanConfig.FromJson(args, _humanConfig);
        return new { applied = true, profile = _humanConfig.Profile };
    }

    private static object CmdHumanConfigGet()
    {
        return _humanConfig;
    }

    private static object CmdHumanProfilesList()
    {
        return new
        {
            profiles = new[]
            {
                new { name = "robot", description = "No humanization; fastest/most precise" },
                new { name = "human_slow", description = "Careful movement, slower typing" },
                new { name = "human", description = "Natural default" },
                new { name = "human_fast", description = "Snappier but still natural" }
            }
        };
    }
}
public sealed class Request
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("cmd")] public string? Cmd { get; set; }
    [JsonPropertyName("args")] public JsonElement Args { get; set; }
}

public static class CoordinatorStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string DataDir => ResolveDataDir();
    private static string StatePath => Path.Combine(DataDir, "coordinator-state.json");
    private static string HistoryPath => Path.Combine(DataDir, "history.jsonl");

    public static object Status()
    {
        lock (Gate)
        {
            var state = LoadState();
            PruneExpiredLeases(state);
            SaveState(state);
            return new
            {
                controlPlane = "winmote",
                coordinator = "local",
                statePath = StatePath,
                historyPath = HistoryPath,
                sessions = state.Sessions.Count,
                leases = state.Leases.Count,
                activeLeases = state.Leases.Count(l => l.ExpiresAt > DateTimeOffset.Now),
                overlayCursors = state.Overlay.Count,
                updatedAt = state.UpdatedAt
            };
        }
    }

    public static object CreateSession(string agent, string? name, int display)
    {
        lock (Gate)
        {
            var state = LoadState();
            var now = DateTimeOffset.Now;
            var existing = state.Sessions.FirstOrDefault(s => s.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new CoordinatorSession
                {
                    SessionId = $"session_{Guid.NewGuid():N}",
                    Agent = agent,
                    Name = string.IsNullOrWhiteSpace(name) ? agent : name.Trim(),
                    Display = display,
                    CreatedAt = now
                };
                state.Sessions.Add(existing);
            }
            existing.Name = string.IsNullOrWhiteSpace(name) ? existing.Name : name.Trim();
            existing.Display = display;
            existing.LastSeenAt = now;
            TouchAndSave(state);
            AppendHistory(new CoordinatorHistoryRecord
            {
                Agent = agent,
                Action = "session.create",
                Result = "ok",
                Details = new Dictionary<string, object?> { ["session_id"] = existing.SessionId, ["display"] = display }
            });
            return new { session = existing };
        }
    }

    public static object ListSessions()
    {
        lock (Gate)
        {
            var state = LoadState();
            return new { sessions = state.Sessions.OrderBy(s => s.Agent, StringComparer.OrdinalIgnoreCase).ToList() };
        }
    }

    public static object AcquireLease(string agent, string resource, string? mode, int ttlMs)
    {
        lock (Gate)
        {
            var state = LoadState();
            PruneExpiredLeases(state);
            mode = NormalizeLeaseMode(mode);
            ttlMs = Math.Clamp(ttlMs, 1000, 300000);
            var now = DateTimeOffset.Now;
            var conflict = state.Leases.FirstOrDefault(l =>
                l.ExpiresAt > now &&
                l.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase) &&
                !l.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase));
            if (conflict != null)
            {
                throw new DeskCtlException("LEASE_CONFLICT", $"resource '{resource}' is leased by '{conflict.Agent}' until {conflict.ExpiresAt:o}");
            }

            var lease = state.Leases.FirstOrDefault(l =>
                l.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase) &&
                l.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase));
            if (lease == null)
            {
                lease = new CoordinatorLease
                {
                    LeaseId = $"lease_{Guid.NewGuid():N}",
                    Agent = agent,
                    Resource = resource,
                    CreatedAt = now
                };
                state.Leases.Add(lease);
            }

            lease.Mode = mode;
            lease.ExpiresAt = now.AddMilliseconds(ttlMs);
            TouchAndSave(state);
            AppendHistory(new CoordinatorHistoryRecord
            {
                Agent = agent,
                Action = "lease.acquire",
                Resource = resource,
                Mode = mode,
                Result = "ok",
                Details = new Dictionary<string, object?> { ["lease_id"] = lease.LeaseId, ["ttl_ms"] = ttlMs }
            });
            return new { lease };
        }
    }

    public static object ListLeases(string? agent, string? resource)
    {
        lock (Gate)
        {
            var state = LoadState();
            PruneExpiredLeases(state);
            SaveState(state);
            var leases = state.Leases.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(agent))
            {
                leases = leases.Where(l => l.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(resource))
            {
                leases = leases.Where(l => l.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase));
            }
            return new { leases = leases.OrderBy(l => l.Resource, StringComparer.OrdinalIgnoreCase).ToList() };
        }
    }

    public static object RecordAction(string agent, string type, string? inputMode, bool dryRun, object? execution)
    {
        var record = new CoordinatorHistoryRecord
        {
            Agent = agent,
            Action = $"action.{type}",
            Mode = inputMode,
            Result = dryRun ? "dry_run" : "ok",
            Details = execution
        };
        AppendHistory(record);
        return new { accepted = true, dryRun, action = type, inputMode, execution };
    }

    public static object UpdateOverlay(string agent, int x, int y, int display, string? app, string? window, string? hwnd, bool pulse, int ttlMs)
    {
        lock (Gate)
        {
            var state = LoadState();
            PruneExpiredOverlay(state);
            ttlMs = Math.Clamp(ttlMs, 1000, 300000);
            var cursor = state.Overlay.FirstOrDefault(o => o.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase));
            if (cursor == null)
            {
                cursor = new CoordinatorOverlayCursor { Agent = agent };
                state.Overlay.Add(cursor);
            }

            cursor.Label = agent;
            cursor.X = x;
            cursor.Y = y;
            cursor.Display = display;
            cursor.App = app;
            cursor.Window = window;
            cursor.Hwnd = hwnd;
            cursor.Pulse = pulse;
            cursor.UpdatedAt = DateTimeOffset.Now;
            cursor.ExpiresAt = cursor.UpdatedAt.AddMilliseconds(ttlMs);
            TouchAndSave(state);
            return new { cursor };
        }
    }

    public static object ListOverlay(string? agent)
    {
        lock (Gate)
        {
            var state = LoadState();
            PruneExpiredOverlay(state);
            SaveState(state);
            var cursors = state.Overlay.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(agent))
            {
                cursors = cursors.Where(c => c.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase));
            }
            return new { cursors = cursors.OrderBy(c => c.Agent, StringComparer.OrdinalIgnoreCase).ToList() };
        }
    }

    public static object ClearOverlay(string? agent)
    {
        lock (Gate)
        {
            var state = LoadState();
            var removed = string.IsNullOrWhiteSpace(agent)
                ? state.Overlay.Count
                : state.Overlay.RemoveAll(c => c.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(agent))
            {
                state.Overlay.Clear();
            }
            TouchAndSave(state);
            return new { cleared = removed, agent };
        }
    }

    public static List<CoordinatorOverlayCursor> GetActiveOverlayCursors()
    {
        lock (Gate)
        {
            var state = LoadState();
            PruneExpiredOverlay(state);
            SaveState(state);
            return state.Overlay.OrderBy(c => c.Agent, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public static object ListHistory(int limit, string? agent)
    {
        limit = Math.Clamp(limit, 1, 200);
        Directory.CreateDirectory(DataDir);
        if (!File.Exists(HistoryPath))
        {
            return new { records = Array.Empty<CoordinatorHistoryRecord>() };
        }

        var lines = File.ReadLines(HistoryPath).Reverse();
        var records = new List<CoordinatorHistoryRecord>();
        foreach (var line in lines)
        {
            if (records.Count >= limit) break;
            try
            {
                var record = JsonSerializer.Deserialize<CoordinatorHistoryRecord>(line, Options);
                if (record == null) continue;
                if (!string.IsNullOrWhiteSpace(agent) && !record.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase)) continue;
                records.Add(record);
            }
            catch
            {
            }
        }
        return new { records };
    }

    private static CoordinatorState LoadState()
    {
        Directory.CreateDirectory(DataDir);
        if (!File.Exists(StatePath))
        {
            return new CoordinatorState();
        }
        try
        {
            return JsonSerializer.Deserialize<CoordinatorState>(File.ReadAllText(StatePath), Options) ?? new CoordinatorState();
        }
        catch
        {
            return new CoordinatorState();
        }
    }

    private static void TouchAndSave(CoordinatorState state)
    {
        state.UpdatedAt = DateTimeOffset.Now;
        SaveState(state);
    }

    private static void SaveState(CoordinatorState state)
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, Options));
    }

    private static void AppendHistory(CoordinatorHistoryRecord record)
    {
        Directory.CreateDirectory(DataDir);
        record.Ts = DateTimeOffset.Now;
        File.AppendAllText(HistoryPath, JsonSerializer.Serialize(record, Options).Replace(Environment.NewLine, "") + Environment.NewLine);
    }

    private static string ResolveDataDir()
    {
        var configured = Environment.GetEnvironmentVariable("WINMOTE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var preferred = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winmote");
        try
        {
            Directory.CreateDirectory(preferred);
            var probe = Path.Combine(preferred, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return preferred;
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "Winmote");
        }
    }

    private static void PruneExpiredLeases(CoordinatorState state)
    {
        var now = DateTimeOffset.Now;
        state.Leases.RemoveAll(l => l.ExpiresAt <= now);
        PruneExpiredOverlay(state);
    }

    private static void PruneExpiredOverlay(CoordinatorState state)
    {
        var now = DateTimeOffset.Now;
        state.Overlay.RemoveAll(o => o.ExpiresAt <= now);
    }

    private static string NormalizeLeaseMode(string? mode)
    {
        mode = string.IsNullOrWhiteSpace(mode) ? "message" : mode.Trim().ToLowerInvariant();
        return mode is "observe" or "semantic" or "message" or "physical"
            ? mode
            : throw new DeskCtlException("INVALID_ARGS", "mode must be observe, semantic, message, or physical");
    }
}

public sealed class CoordinatorState
{
    [JsonPropertyName("sessions")] public List<CoordinatorSession> Sessions { get; set; } = new();
    [JsonPropertyName("leases")] public List<CoordinatorLease> Leases { get; set; } = new();
    [JsonPropertyName("overlay")] public List<CoordinatorOverlayCursor> Overlay { get; set; } = new();
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class CoordinatorSession
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("agent")] public string Agent { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("display")] public int Display { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("last_seen_at")] public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class CoordinatorLease
{
    [JsonPropertyName("lease_id")] public string LeaseId { get; set; } = "";
    [JsonPropertyName("agent")] public string Agent { get; set; } = "";
    [JsonPropertyName("resource")] public string Resource { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "message";
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class CoordinatorOverlayCursor
{
    [JsonPropertyName("agent")] public string Agent { get; set; } = "";
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("display")] public int Display { get; set; }
    [JsonPropertyName("app")] public string? App { get; set; }
    [JsonPropertyName("window")] public string? Window { get; set; }
    [JsonPropertyName("hwnd")] public string? Hwnd { get; set; }
    [JsonPropertyName("pulse")] public bool Pulse { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class CoordinatorHistoryRecord
{
    [JsonPropertyName("ts")] public DateTimeOffset Ts { get; set; } = DateTimeOffset.Now;
    [JsonPropertyName("agent")] public string Agent { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("resource")] public string? Resource { get; set; }
    [JsonPropertyName("mode")] public string? Mode { get; set; }
    [JsonPropertyName("result")] public string Result { get; set; } = "";
    [JsonPropertyName("details")] public object? Details { get; set; }
}

public sealed class Response
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("result")] public object? Result { get; set; }
    [JsonPropertyName("error")] public ErrorObj? Error { get; set; }
    [JsonPropertyName("timing_ms")] public long TimingMs { get; set; }
    [JsonPropertyName("ts")] public string Ts { get; set; } = DateTimeOffset.Now.ToString("o");

    public static Response OkResponse(string? id, object result, long timingMs) =>
        new() { Id = id, Ok = true, Result = result, Error = null, TimingMs = timingMs };

    public static Response ErrorResponse(string code, string message) =>
        new() { Id = null, Ok = false, Result = null, Error = new ErrorObj { Code = code, Message = message }, TimingMs = 0 };

    public static Response ErrorResponse(string? id, string code, string message, long timingMs) =>
        new() { Id = id, Ok = false, Result = null, Error = new ErrorObj { Code = code, Message = message }, TimingMs = timingMs };
}

public sealed class ErrorObj
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("details")] public object? Details { get; set; }
}

public sealed class DeskCtlException : Exception
{
    public string Code { get; }
    public DeskCtlException(string code, string message) : base(message) => Code = code;
}

public sealed class Rect
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("w")] public int W { get; set; }
    [JsonPropertyName("h")] public int H { get; set; }

    public static Rect FromJson(JsonElement el) =>
        new() { X = el.GetProperty("x").GetInt32(), Y = el.GetProperty("y").GetInt32(), W = el.GetProperty("w").GetInt32(), H = el.GetProperty("h").GetInt32() };

    public static Rect FromNative(Native.RECT r) =>
        new() { X = r.Left, Y = r.Top, W = r.Right - r.Left, H = r.Bottom - r.Top };
}

public sealed class DisplayInfo
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("rect")] public Rect Rect { get; set; } = new();
    [JsonPropertyName("dpi")] public int Dpi { get; set; }
    [JsonPropertyName("scale")] public double Scale { get; set; }
    [JsonPropertyName("primary")] public bool Primary { get; set; }
}

public sealed class WindowInfo
{
    [JsonPropertyName("hwnd")] public string? Hwnd { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("exe")] public string? Exe { get; set; }
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("rect")] public Rect Rect { get; set; } = new();
    [JsonPropertyName("visible")] public bool Visible { get; set; }
    [JsonPropertyName("minimized")] public bool Minimized { get; set; }

    public static WindowInfo FromHwnd(IntPtr hwnd)
    {
        var info = new WindowInfo();
        info.Hwnd = $"0x{hwnd.ToInt64():X}";
        info.Title = Native.GetWindowTitle(hwnd);
        info.Pid = Native.GetWindowProcessId(hwnd);
        info.Exe = WindowHelper.TryGetProcessName(info.Pid);
        info.Visible = Native.IsWindowVisible(hwnd);
        info.Minimized = Native.IsIconic(hwnd);
        if (Native.GetWindowRect(hwnd, out var r))
        {
            info.Rect = Rect.FromNative(r);
        }
        return info;
    }
}

public class UiaElementInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("automation_id")] public string? AutomationId { get; set; }
    [JsonPropertyName("control_type")] public string? ControlType { get; set; }
    [JsonPropertyName("class_name")] public string? ClassName { get; set; }
    [JsonPropertyName("rect")] public Rect? Rect { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("focused")] public bool Focused { get; set; }
    [JsonPropertyName("offscreen")] public bool Offscreen { get; set; }
    [JsonPropertyName("runtime_id")] public string? RuntimeId { get; set; }

    public static UiaElementInfo FromElement(AutomationElement element, bool includeRect)
    {
        var rect = element.Current.BoundingRectangle;
        return new UiaElementInfo
        {
            Name = element.Current.Name,
            AutomationId = element.Current.AutomationId,
            ControlType = element.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", ""),
            ClassName = element.Current.ClassName,
            Rect = includeRect && rect != System.Windows.Rect.Empty
                ? new Rect { X = (int)rect.X, Y = (int)rect.Y, W = (int)rect.Width, H = (int)rect.Height }
                : null,
            Enabled = element.Current.IsEnabled,
            Focused = element.Current.HasKeyboardFocus,
            Offscreen = element.Current.IsOffscreen,
            RuntimeId = string.Join(",", element.GetRuntimeId())
        };
    }
}

public sealed class UiaNode : UiaElementInfo
{
    [JsonPropertyName("children")] public List<UiaNode>? Children { get; set; }

    public static UiaNode CreateFromElement(AutomationElement element, bool includeRect)
    {
        var info = UiaElementInfo.FromElement(element, includeRect);
        return new UiaNode
        {
            Name = info.Name,
            AutomationId = info.AutomationId,
            ControlType = info.ControlType,
            ClassName = info.ClassName,
            Rect = info.Rect,
            Enabled = info.Enabled,
            Focused = info.Focused,
            Offscreen = info.Offscreen,
            RuntimeId = info.RuntimeId
        };
    }
}

public sealed class UiaQuery
{
    public string? NameContains { get; set; }
    public string? AutomationId { get; set; }
    public string? ControlType { get; set; }
    public string? ClassName { get; set; }
    public int Nth { get; set; }

    public static UiaQuery FromArgs(JsonElement args)
    {
        return new UiaQuery
        {
            NameContains = args.TryGetProperty("name", out var n) ? n.GetString() : null,
            AutomationId = args.TryGetProperty("automation_id", out var a) ? a.GetString() : null,
            ControlType = args.TryGetProperty("control_type", out var ct) ? ct.GetString() : null,
            ClassName = args.TryGetProperty("class_name", out var cn) ? cn.GetString() : null,
            Nth = args.TryGetProperty("nth", out var nth) ? nth.GetInt32() : 0
        };
    }
}

public sealed class OcrWord
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("rect")] public Rect Rect { get; set; } = new();
    [JsonPropertyName("conf")] public double? Conf { get; set; }
}

public sealed class OcrResult
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("words")] public OcrWord[] Words { get; set; } = Array.Empty<OcrWord>();
}

public static class VirtualDesktopHelper
{
    private static readonly Guid CLSID_ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid IID_IServiceProvider = new("6d5140c1-7436-11ce-8034-00aa006009fa");
    private static readonly Guid CLSID_VirtualDesktopManagerInternal = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid IID_IVirtualDesktopManagerInternal = new("F31574D6-B682-4CDC-BD56-1827860ABEC6");

    public static IVirtualDesktopManagerInternal? TryCreateInternalManager()
    {
        try
        {
            var shellType = Type.GetTypeFromCLSID(CLSID_ImmersiveShell);
            if (shellType == null) return null;
            var shell = Activator.CreateInstance(shellType);
            var sp = (IServiceProvider)shell!;
            sp.QueryService(CLSID_VirtualDesktopManagerInternal, IID_IVirtualDesktopManagerInternal, out var ppv);
            return (IVirtualDesktopManagerInternal)ppv!;
        }
        catch
        {
            return null;
        }
    }

    public static List<IVirtualDesktop> GetDesktops(IVirtualDesktopManagerInternal manager)
    {
        var result = new List<IVirtualDesktop>();
        var array = manager.GetDesktops();
        int count = array.GetCount();
        for (int i = 0; i < count; i++)
        {
            var id = typeof(IVirtualDesktop).GUID;
            array.GetAt(i, ref id, out var obj);
            if (obj is IVirtualDesktop vd)
            {
                result.Add(vd);
            }
        }
        return result;
    }

    public static IVirtualDesktop ResolveDesktop(IVirtualDesktopManagerInternal manager, JsonElement args)
    {
        if (args.TryGetProperty("id", out var idEl))
        {
            var id = idEl.GetString();
            if (Guid.TryParse(id, out var guid))
            {
                return manager.FindDesktop(guid);
            }
            throw new DeskCtlException("INVALID_ARGS", "invalid desktop id");
        }
        if (args.TryGetProperty("index", out var indexEl))
        {
            var index = indexEl.GetInt32();
            var list = GetDesktops(manager);
            if (index < 0 || index >= list.Count)
            {
                throw new DeskCtlException("INVALID_ARGS", "desktop index out of range");
            }
            return list[index];
        }
        throw new DeskCtlException("INVALID_ARGS", "desktop_switch requires index or id");
    }
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
public interface IServiceProvider
{
    void QueryService([In] Guid guidService, [In] Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppvObject);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9")]
public interface IObjectArray
{
    int GetCount();
    void GetAt(int i, ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object obj);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("FF72FFDD-BE7E-43FC-9C03-AD81681E88E4")]
public interface IVirtualDesktop
{
    Guid GetId();
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6")]
public interface IVirtualDesktopManagerInternal
{
    int GetCount();
    void MoveViewToDesktop(object pView, IVirtualDesktop desktop);
    bool CanViewMoveDesktops(object pView);
    IVirtualDesktop GetCurrentDesktop();
    IObjectArray GetDesktops();
    IVirtualDesktop GetAdjacentDesktop(IVirtualDesktop referenceDesktop, int direction);
    void SwitchDesktop(IVirtualDesktop desktop);
    IVirtualDesktop CreateDesktop();
    void RemoveDesktop(IVirtualDesktop desktop, IVirtualDesktop fallbackDesktop);
    IVirtualDesktop FindDesktop(Guid desktopId);
    void MoveWindowToDesktop(IntPtr hwnd, IVirtualDesktop desktop);
}

public static class AudioHelper
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid CLSID_PolicyConfigClient = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    private static readonly Guid IID_IPolicyConfig = new("F8679F50-850A-41CF-9C72-430F290290C8");

    public static List<AudioDevice> ListDevices()
    {
        var list = new List<AudioDevice>();
        var enumerator = CreateEnumerator();
        foreach (EDataFlow flow in new[] { EDataFlow.eRender, EDataFlow.eCapture })
        {
            enumerator.EnumAudioEndpoints(flow, DeviceState.ACTIVE, out var collection);
            int count = collection.GetCount();
            for (int i = 0; i < count; i++)
            {
                var device = collection.Item(i);
                list.Add(DescribeDevice(device, flow));
            }
        }
        return list;
    }

    public static string? GetDefaultDeviceId(EDataFlow flow, ERole role)
    {
        var enumerator = CreateEnumerator();
        enumerator.GetDefaultAudioEndpoint(flow, role, out var device);
        return device.GetId();
    }

    public static void SetDefaultDevice(string id, ERole role)
    {
        var policy = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_PolicyConfigClient)!)!;
        var hr = policy.SetDefaultEndpoint(id, role);
        if (hr != 0)
        {
            throw new DeskCtlException("OS_ERROR", $"failed to set default audio device (hr=0x{hr:X8})");
        }
    }

    public static void SetMute(string id, bool mute)
    {
        var enumerator = CreateEnumerator();
        enumerator.GetDevice(id, out var device);
        var iid = IID_IAudioEndpointVolume;
        var obj = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero);
        var volume = (IAudioEndpointVolume)obj;
        var hr = volume.SetMute(mute, Guid.Empty);
        if (hr != 0)
        {
            throw new DeskCtlException("OS_ERROR", $"failed to set mute (hr=0x{hr:X8})");
        }
    }

    private static IMMDeviceEnumerator CreateEnumerator()
    {
        var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
        var obj = Activator.CreateInstance(type!);
        return (IMMDeviceEnumerator)obj!;
    }

    private static AudioDevice DescribeDevice(IMMDevice device, EDataFlow flow)
    {
        device.GetState(out var state);
        var store = device.OpenPropertyStore(StorageAccessMode.Read);
        var name = GetStringProperty(store, PropertyKeys.PKEY_Device_FriendlyName);
        return new AudioDevice { Id = device.GetId(), Name = name ?? "", Flow = flow, State = state };
    }

    private static string? GetStringProperty(IPropertyStore store, PROPERTYKEY key)
    {
        store.GetValue(ref key, out var value);
        try
        {
            return value.GetString();
        }
        finally
        {
            value.Clear();
        }
    }
}

public sealed class AudioDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public EDataFlow Flow { get; set; }
    public DeviceState State { get; set; }
}

public enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2
}

public enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

[Flags]
public enum DeviceState
{
    ACTIVE = 0x00000001,
    DISABLED = 0x00000002,
    NOTPRESENT = 0x00000004,
    UNPLUGGED = 0x00000008,
    MASK_ALL = 0x0000000F
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    int RegisterEndpointNotificationCallback(IntPtr client);
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-C0DDBD6F1AC2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceCollection
{
    int GetCount();
    IMMDevice Item(int index);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice
{
    object Activate([In] ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams);
    IPropertyStore OpenPropertyStore(StorageAccessMode stgmAccess);
    string GetId();
    int GetState(out DeviceState state);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStore
{
    int GetCount();
    PROPERTYKEY GetAt(int iProp);
    int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
    int Commit();
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolume
{
    int RegisterControlChangeNotify(IntPtr pNotify);
    int UnregisterControlChangeNotify(IntPtr pNotify);
    int GetChannelCount(out int pnChannelCount);
    int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
    int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
    int GetMasterVolumeLevel(out float pfLevelDB);
    int GetMasterVolumeLevelScalar(out float pfLevel);
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
    int SetMute(bool bMute, Guid pguidEventContext);
    int GetMute(out bool pbMute);
    int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
    int VolumeStepUp(Guid pguidEventContext);
    int VolumeStepDown(Guid pguidEventContext);
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPolicyConfig
{
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr format);
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool bDefault, IntPtr format);
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr format, IntPtr mix);
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool bDefault, IntPtr min, IntPtr def);
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr min, IntPtr def);
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PROPERTYKEY key, IntPtr pv);
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PROPERTYKEY key, IntPtr pv);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ERole role);
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool visible);
}

[StructLayout(LayoutKind.Sequential)]
public struct PROPERTYKEY
{
    public Guid fmtid;
    public int pid;
}

public static class PropertyKeys
{
    public static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14
    };
}

[StructLayout(LayoutKind.Explicit)]
public struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr pwszVal;

    public string? GetString()
    {
        return vt == (ushort)VarEnum.VT_LPWSTR ? Marshal.PtrToStringUni(pwszVal) : null;
    }

    public void Clear()
    {
        PropVariantClear(ref this);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);
}

public enum StorageAccessMode
{
    Read = 0
}

[Flags]
public enum CLSCTX
{
    CLSCTX_INPROC_SERVER = 0x1,
    CLSCTX_INPROC_HANDLER = 0x2,
    CLSCTX_LOCAL_SERVER = 0x4,
    CLSCTX_REMOTE_SERVER = 0x10,
    CLSCTX_ALL = CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER
}

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
public class ApplicationActivationManager
{
}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IApplicationActivationManager
{
    int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        ActivateOptions options,
        out uint processId);

    int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        IntPtr itemArray,
        [MarshalAs(UnmanagedType.LPWStr)] string verb,
        out uint processId);

    int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        IntPtr itemArray,
        out uint processId);
}

[Flags]
public enum ActivateOptions
{
    None = 0x00000000,
    DesignMode = 0x00000001,
    NoErrorUI = 0x00000002,
    NoSplashScreen = 0x00000004,
}
public sealed class HumanConfig
{
    [JsonPropertyName("profile")] public string Profile { get; set; } = "human";
    [JsonPropertyName("seed")] public int? Seed { get; set; }
    [JsonPropertyName("mouse")] public HumanMouse Mouse { get; set; } = new();
    [JsonPropertyName("keyboard")] public HumanKeyboard Keyboard { get; set; } = new();

    public static HumanConfig CreateDefault()
    {
        return new HumanConfig
        {
            Profile = "human",
            Mouse = new HumanMouse(),
            Keyboard = new HumanKeyboard()
        };
    }

    public static HumanConfig FromJson(JsonElement el, HumanConfig current)
    {
        var cfg = new HumanConfig
        {
            Profile = current.Profile,
            Seed = current.Seed,
            Mouse = current.Mouse.Clone(),
            Keyboard = current.Keyboard.Clone()
        };

        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("profile", out var p)) cfg.Profile = p.GetString() ?? cfg.Profile;
            if (el.TryGetProperty("seed", out var s) && s.ValueKind == JsonValueKind.Number) cfg.Seed = s.GetInt32();
            if (el.TryGetProperty("mouse", out var m)) cfg.Mouse.Apply(m);
            if (el.TryGetProperty("keyboard", out var k)) cfg.Keyboard.Apply(k);
        }

        if (cfg.Profile == "robot")
        {
            cfg.Mouse.Enabled = false;
            cfg.Keyboard.Enabled = false;
        }
        else if (cfg.Profile == "human_slow")
        {
            cfg.Mouse.Speed = "slow";
            cfg.Keyboard.Cps = new Range(4.5, 6.5);
        }
        else if (cfg.Profile == "human_fast")
        {
            cfg.Mouse.Speed = "fast";
            cfg.Keyboard.Cps = new Range(7.0, 10.0);
        }

        return cfg;
    }
}

public sealed class HumanMouse
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("speed")] public string Speed { get; set; } = "normal";
    [JsonPropertyName("path")] public string Path { get; set; } = "bezier";
    [JsonPropertyName("overshoot_prob")] public double OvershootProb { get; set; } = 0.08;
    [JsonPropertyName("overshoot_px")] public Range OvershootPx { get; set; } = new(2, 18);
    [JsonPropertyName("jitter_px")] public Range JitterPx { get; set; } = new(0.2, 1.2);
    [JsonPropertyName("tremor_hz")] public Range TremorHz { get; set; } = new(8, 14);
    [JsonPropertyName("micro_pause_prob")] public double MicroPauseProb { get; set; } = 0.06;
    [JsonPropertyName("micro_pause_ms")] public Range MicroPauseMs { get; set; } = new(20, 120);
    [JsonPropertyName("step_ms")] public Range StepMs { get; set; } = new(5, 12);
    [JsonPropertyName("pre_ms")] public Range PreMs { get; set; } = new(30, 220);
    [JsonPropertyName("down_ms")] public Range DownMs { get; set; } = new(20, 90);
    [JsonPropertyName("inter_click_ms")] public Range InterClickMs { get; set; } = new(90, 260);

    public HumanMouse Clone() => (HumanMouse)MemberwiseClone();

    public void Apply(JsonElement el)
    {
        if (el.TryGetProperty("speed", out var s)) Speed = s.GetString() ?? Speed;
        if (el.TryGetProperty("path", out var p)) Path = p.GetString() ?? Path;
        if (el.TryGetProperty("overshoot_prob", out var op)) OvershootProb = op.GetDouble();
        if (el.TryGetProperty("overshoot_px", out var o)) OvershootPx = Range.FromJson(o, OvershootPx);
        if (el.TryGetProperty("jitter_px", out var j)) JitterPx = Range.FromJson(j, JitterPx);
        if (el.TryGetProperty("tremor_hz", out var t)) TremorHz = Range.FromJson(t, TremorHz);
        if (el.TryGetProperty("micro_pause_prob", out var mp)) MicroPauseProb = mp.GetDouble();
        if (el.TryGetProperty("micro_pause_ms", out var mpm)) MicroPauseMs = Range.FromJson(mpm, MicroPauseMs);
        if (el.TryGetProperty("step_ms", out var sm)) StepMs = Range.FromJson(sm, StepMs);
        if (el.TryGetProperty("pre_ms", out var pre)) PreMs = Range.FromJson(pre, PreMs);
        if (el.TryGetProperty("down_ms", out var dm)) DownMs = Range.FromJson(dm, DownMs);
        if (el.TryGetProperty("inter_click_ms", out var icm)) InterClickMs = Range.FromJson(icm, InterClickMs);
    }
}

public sealed class HumanKeyboard
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("style")] public string Style { get; set; } = "touch_typing";
    [JsonPropertyName("cps")] public Range Cps { get; set; } = new(5.5, 8.5);
    [JsonPropertyName("key_down_ms")] public Range KeyDownMs { get; set; } = new(18, 55);
    [JsonPropertyName("inter_key_ms")] public Range InterKeyMs { get; set; } = new(40, 180);
    [JsonPropertyName("word_pause_prob")] public double WordPauseProb { get; set; } = 0.25;
    [JsonPropertyName("word_pause_ms")] public Range WordPauseMs { get; set; } = new(60, 260);
    [JsonPropertyName("punct_pause_prob")] public double PunctPauseProb { get; set; } = 0.5;
    [JsonPropertyName("punct_pause_ms")] public Range PunctPauseMs { get; set; } = new(80, 380);
    [JsonPropertyName("mistake_prob")] public double MistakeProb { get; set; } = 0.02;
    [JsonPropertyName("correction_style")] public string CorrectionStyle { get; set; } = "backspace";
    [JsonPropertyName("chord_roll_ms")] public Range ChordRollMs { get; set; } = new(8, 35);
    [JsonPropertyName("hold_ms")] public Range HoldMs { get; set; } = new(35, 120);

    public HumanKeyboard Clone() => (HumanKeyboard)MemberwiseClone();

    public void Apply(JsonElement el)
    {
        if (el.TryGetProperty("style", out var s)) Style = s.GetString() ?? Style;
        if (el.TryGetProperty("cps", out var cps)) Cps = Range.FromJson(cps, Cps);
        if (el.TryGetProperty("key_down_ms", out var kd)) KeyDownMs = Range.FromJson(kd, KeyDownMs);
        if (el.TryGetProperty("inter_key_ms", out var ik)) InterKeyMs = Range.FromJson(ik, InterKeyMs);
        if (el.TryGetProperty("word_pause_prob", out var wp)) WordPauseProb = wp.GetDouble();
        if (el.TryGetProperty("word_pause_ms", out var wpm)) WordPauseMs = Range.FromJson(wpm, WordPauseMs);
        if (el.TryGetProperty("punct_pause_prob", out var pp)) PunctPauseProb = pp.GetDouble();
        if (el.TryGetProperty("punct_pause_ms", out var ppm)) PunctPauseMs = Range.FromJson(ppm, PunctPauseMs);
        if (el.TryGetProperty("mistake_prob", out var mp)) MistakeProb = mp.GetDouble();
        if (el.TryGetProperty("correction_style", out var cs)) CorrectionStyle = cs.GetString() ?? CorrectionStyle;
        if (el.TryGetProperty("chord_roll_ms", out var crm)) ChordRollMs = Range.FromJson(crm, ChordRollMs);
        if (el.TryGetProperty("hold_ms", out var hm)) HoldMs = Range.FromJson(hm, HoldMs);
    }
}

public readonly struct Range
{
    public double Min { get; }
    public double Max { get; }
    public Range(double min, double max)
    {
        Min = min;
        Max = max;
    }

    public static Range FromJson(JsonElement el, Range fallback)
    {
        if (el.ValueKind == JsonValueKind.Array && el.GetArrayLength() == 2)
        {
            return new Range(el[0].GetDouble(), el[1].GetDouble());
        }
        return fallback;
    }
}
public static class Humanization
{
    private static Random _rng = new();

    public static HumanArgs ParseHuman(JsonElement args)
    {
        if (!args.TryGetProperty("human", out var h) || h.ValueKind != JsonValueKind.Object)
        {
            return HumanArgs.Disabled();
        }

        var enabled = !h.TryGetProperty("enabled", out var e) || e.GetBoolean();
        var human = new HumanArgs { Enabled = enabled };
        if (h.TryGetProperty("pre_ms", out var pre)) human.PreMs = Range.FromJson(pre, human.PreMs);
        if (h.TryGetProperty("down_ms", out var down)) human.DownMs = Range.FromJson(down, human.DownMs);
        if (h.TryGetProperty("inter_click_ms", out var ic)) human.InterClickMs = Range.FromJson(ic, human.InterClickMs);
        if (h.TryGetProperty("chord_roll_ms", out var cr)) human.ChordRollMs = Range.FromJson(cr, human.ChordRollMs);
        if (h.TryGetProperty("hold_ms", out var hm)) human.HoldMs = Range.FromJson(hm, human.HoldMs);
        if (h.TryGetProperty("key_down_ms", out var kd)) human.KeyDownMs = Range.FromJson(kd, human.KeyDownMs);
        if (h.TryGetProperty("inter_key_ms", out var ik)) human.InterKeyMs = Range.FromJson(ik, human.InterKeyMs);
        if (h.TryGetProperty("word_pause_ms", out var wp)) human.WordPauseMs = Range.FromJson(wp, human.WordPauseMs);
        if (h.TryGetProperty("punct_pause_ms", out var pp)) human.PunctPauseMs = Range.FromJson(pp, human.PunctPauseMs);
        if (h.TryGetProperty("max_duration_ms", out var md)) human.MaxDurationMs = md.GetInt32();
        if (h.TryGetProperty("settle_ms", out var sm)) human.SettleMs = Range.FromJson(sm, human.SettleMs);
        if (h.TryGetProperty("aim_noise_px", out var an)) human.AimNoisePx = Range.FromJson(an, human.AimNoisePx);
        return human;
    }

    public static MouseMoveResult HumanMouseMove(Point start, Point target, HumanArgs args, HumanConfig config)
    {
        if (config.Seed.HasValue)
        {
            _rng = new Random(config.Seed.Value);
        }

        var distance = Distance(start, target);
        var overshoot = config.Mouse.OvershootProb > _rng.NextDouble();
        Point overshootPoint = target;
        if (overshoot && distance > 12)
        {
            var dir = Normalize(target.X - start.X, target.Y - start.Y);
            var overshootPx = RandomRange(config.Mouse.OvershootPx);
            overshootPoint = new Point(
                (int)Math.Round(target.X + dir.X * overshootPx),
                (int)Math.Round(target.Y + dir.Y * overshootPx));
        }

        int duration = EstimateMoveDuration(distance, config.Mouse.Speed);
        if (args.MaxDurationMs > 0)
        {
            duration = Math.Min(duration, args.MaxDurationMs);
        }

        var totalDuration = duration;
        int samples = 0;

        samples += MoveBezier(start, overshootPoint, duration, config, args);
        if (overshoot)
        {
            int settle = (int)Math.Round(RandomRange(args.SettleMs));
            Thread.Sleep(settle);
            totalDuration += settle;
            samples += MoveBezier(overshootPoint, target, (int)Math.Max(40, duration * 0.25), config, args);
        }

        return new MouseMoveResult
        {
            End = target,
            DurationMs = totalDuration,
            Samples = samples,
            Overshot = overshoot
        };
    }

    public static void LinearMove(Point start, Point target, int durationMs)
    {
        int steps = Math.Max(2, durationMs / 10);
        for (int i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var x = (int)Math.Round(start.X + (target.X - start.X) * t);
            var y = (int)Math.Round(start.Y + (target.Y - start.Y) * t);
            Native.SetCursorPos(x, y);
            Thread.Sleep(durationMs / steps);
        }
    }

    public static bool ShouldMistype(HumanKeyboard cfg, HumanArgs human)
    {
        if (!human.Enabled) return false;
        return _rng.NextDouble() < cfg.MistakeProb;
    }

    public static char MistypeChar(char ch)
    {
        if (!char.IsLetter(ch)) return '\0';
        var lower = char.ToLowerInvariant(ch);
        if (!KeyMap.Adjacent.TryGetValue(lower, out var list) || list.Count == 0) return '\0';
        return list[_rng.Next(list.Count)];
    }

    public static bool ShouldPauseAfterChar(char ch, HumanKeyboard cfg, HumanArgs human)
    {
        if (!human.Enabled) return false;
        if (char.IsWhiteSpace(ch))
        {
            return _rng.NextDouble() < cfg.WordPauseProb;
        }
        if (char.IsPunctuation(ch))
        {
            return _rng.NextDouble() < cfg.PunctPauseProb;
        }
        return false;
    }

    public static int RandomPauseAfterChar(char ch, HumanKeyboard cfg, HumanArgs human)
    {
        if (char.IsWhiteSpace(ch))
        {
            return RandomRangeMs(cfg.WordPauseMs, human.WordPauseMs);
        }
        return RandomRangeMs(cfg.PunctPauseMs, human.PunctPauseMs);
    }

    public static int RandomRangeMs(Range baseRange, Range overrideRange)
    {
        var range = overrideRange.Min > 0 || overrideRange.Max > 0 ? overrideRange : baseRange;
        return (int)Math.Round(RandomRange(range));
    }

    public static double RandomRange(Range range)
    {
        var min = Math.Min(range.Min, range.Max);
        var max = Math.Max(range.Min, range.Max);
        return min + (max - min) * _rng.NextDouble();
    }

    private static int MoveBezier(Point start, Point end, int durationMs, HumanConfig cfg, HumanArgs human)
    {
        var stepMs = (int)Math.Round(RandomRange(cfg.Mouse.StepMs));
        var steps = Math.Max(3, durationMs / Math.Max(1, stepMs));
        var control1 = BezierControl(start, end, 0.3);
        var control2 = BezierControl(start, end, 0.7);

        int samples = 0;
        for (int i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var eased = EaseInOut(t);
            var pt = BezierPoint(start, control1, control2, end, eased);
            var jitter = RandomRange(cfg.Mouse.JitterPx);
            var jittered = new Point(
                (int)Math.Round(pt.X + jitter * (_rng.NextDouble() - 0.5)),
                (int)Math.Round(pt.Y + jitter * (_rng.NextDouble() - 0.5)));

            Native.SetCursorPos(jittered.X, jittered.Y);
            samples++;

            if (cfg.Mouse.MicroPauseProb > _rng.NextDouble())
            {
                Thread.Sleep((int)Math.Round(RandomRange(cfg.Mouse.MicroPauseMs)));
            }

            Thread.Sleep(stepMs);
        }

        return samples;
    }

    private static Point BezierControl(Point start, Point end, double t)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var mid = new Point((int)Math.Round(start.X + dx * t), (int)Math.Round(start.Y + dy * t));
        var perpendicular = new Point(-dy, dx);
        var norm = Normalize(perpendicular.X, perpendicular.Y);
        var offset = _rng.NextDouble() * 40 - 20;
        return new Point(
            (int)Math.Round(mid.X + norm.X * offset),
            (int)Math.Round(mid.Y + norm.Y * offset));
    }

    private static Point BezierPoint(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var u = 1 - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;
        var x = uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X;
        var y = uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y;
        return new Point((int)Math.Round(x), (int)Math.Round(y));
    }

    private static double EaseInOut(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

    private static int EstimateMoveDuration(double distance, string speed)
    {
        var factor = speed switch
        {
            "fast" => 1.4,
            "slow" => 3.2,
            _ => 2.2
        };
        return (int)Math.Clamp(distance * factor + 60, 80, 1600);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static (double X, double Y) Normalize(double x, double y)
    {
        var len = Math.Sqrt(x * x + y * y);
        if (len < 0.0001) return (0, 0);
        return (x / len, y / len);
    }
}

public sealed class HumanArgs
{
    public bool Enabled { get; set; }
    public Range PreMs { get; set; }
    public Range DownMs { get; set; }
    public Range InterClickMs { get; set; }
    public Range ChordRollMs { get; set; }
    public Range HoldMs { get; set; }
    public Range KeyDownMs { get; set; } = new(0, 0);
    public Range InterKeyMs { get; set; } = new(0, 0);
    public Range WordPauseMs { get; set; } = new(0, 0);
    public Range PunctPauseMs { get; set; } = new(0, 0);
    public Range SettleMs { get; set; } = new(15, 80);
    public Range AimNoisePx { get; set; } = new(0, 1.5);
    public int MaxDurationMs { get; set; } = 1200;

    public static HumanArgs Disabled() => new() { Enabled = false };
}

public sealed class MouseMoveResult
{
    public Point End { get; set; }
    public int DurationMs { get; set; }
    public int Samples { get; set; }
    public bool Overshot { get; set; }
}
public static class DisplayHelper
{
    public static bool TryGetDisplayDevice(string deviceName, out Native.DISPLAY_DEVICE device)
    {
        device = new Native.DISPLAY_DEVICE();
        device.cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>();
        for (uint i = 0; i < 16; i++)
        {
            var dd = new Native.DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>();
            if (!Native.EnumDisplayDevices(null, i, ref dd, 0))
            {
                break;
            }
            if (dd.DeviceName != null && dd.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
            {
                device = dd;
                return true;
            }
        }
        return false;
    }
    public static Rect GetDisplayRect(int id)
    {
        if (TryGetDisplayRectFromScreen(id, out var rect))
        {
            return rect;
        }
        var displays = new List<DisplayInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Native.RECT lprc, IntPtr lparam) =>
        {
            var info = new Native.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<Native.MONITORINFOEX>();
            if (Native.GetMonitorInfo(hMon, ref info))
            {
                uint dpiX = 96;
                uint dpiY = 96;
                Native.GetDpiForMonitorSafe(hMon, ref dpiX, ref dpiY);

                displays.Add(new DisplayInfo
                {
                    Id = displays.Count,
                    Name = info.szDevice,
                    Rect = Rect.FromNative(info.rcMonitor),
                    Dpi = (int)dpiX,
                    Scale = Math.Round(dpiX / 96.0, 2),
                    Primary = (info.dwFlags & 1) == 1
                });
            }
            return true;
        }, IntPtr.Zero);

        if (id < 0 || id >= displays.Count)
        {
            throw new DeskCtlException("NOT_FOUND", "display not found");
        }

        return displays[id].Rect;
    }

    public static bool TryGetDisplayForPoint(Point pt, out DisplayInfo display)
    {
        if (TryGetDisplayForPointFromScreen(pt, out display))
        {
            return true;
        }
        var displays = new List<DisplayInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Native.RECT lprc, IntPtr lparam) =>
        {
            var info = new Native.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<Native.MONITORINFOEX>();
            if (Native.GetMonitorInfo(hMon, ref info))
            {
                uint dpiX = 96;
                uint dpiY = 96;
                Native.GetDpiForMonitorSafe(hMon, ref dpiX, ref dpiY);

                displays.Add(new DisplayInfo
                {
                    Id = displays.Count,
                    Name = info.szDevice,
                    Rect = Rect.FromNative(info.rcMonitor),
                    Dpi = (int)dpiX,
                    Scale = Math.Round(dpiX / 96.0, 2),
                    Primary = (info.dwFlags & 1) == 1
                });
            }
            return true;
        }, IntPtr.Zero);

        foreach (var d in displays)
        {
            if (pt.X >= d.Rect.X && pt.X < d.Rect.X + d.Rect.W &&
                pt.Y >= d.Rect.Y && pt.Y < d.Rect.Y + d.Rect.H)
            {
                display = d;
                return true;
            }
        }

        display = new DisplayInfo();
        return false;
    }

    private static bool TryGetDisplayRectFromScreen(int id, out Rect rect)
    {
        rect = new Rect();
        try
        {
            var screens = Screen.AllScreens;
            if (id < 0 || id >= screens.Length)
            {
                return false;
            }
            var bounds = screens[id].Bounds;
            rect = new Rect { X = bounds.X, Y = bounds.Y, W = bounds.Width, H = bounds.Height };
            return rect.W > 0 && rect.H > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDisplayForPointFromScreen(Point pt, out DisplayInfo display)
    {
        display = new DisplayInfo();
        try
        {
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var b = screens[i].Bounds;
                if (pt.X >= b.X && pt.X < b.X + b.Width && pt.Y >= b.Y && pt.Y < b.Y + b.Height)
                {
                    display = new DisplayInfo
                    {
                        Id = i,
                        Name = screens[i].DeviceName,
                        Rect = new Rect { X = b.X, Y = b.Y, W = b.Width, H = b.Height },
                        Dpi = 96,
                        Scale = 1.0,
                        Primary = screens[i].Primary
                    };
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }
}

public static class WindowHelper
{
    public static string? TryGetProcessName(int pid)
    {
        try
        {
            return Process.GetProcessById(pid).MainModule?.ModuleName;
        }
        catch
        {
            return null;
        }
    }

    public static IntPtr FindWindow(string? titleContains, string? exeContains, int nth)
    {
        int index = 0;
        IntPtr match = IntPtr.Zero;
        Native.EnumWindows((hwnd, lparam) =>
        {
            if (!Native.IsWindowVisible(hwnd))
            {
                return true;
            }

            var title = Native.GetWindowTitle(hwnd);
            if (!string.IsNullOrEmpty(titleContains) && !title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(exeContains))
            {
                var pid = Native.GetWindowProcessId(hwnd);
                var exe = TryGetProcessName(pid);
                if (exe == null || !exe.Contains(exeContains, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (index == nth)
            {
                match = hwnd;
                return false;
            }

            index++;
            return true;
        }, IntPtr.Zero);

        return match;
    }
}

public static class CursorHelper
{
    public static void DrawCursor(Bitmap bmp, Rect rect)
    {
        var cursorInfo = new Native.CURSORINFO();
        cursorInfo.cbSize = Marshal.SizeOf<Native.CURSORINFO>();
        if (!Native.GetCursorInfo(ref cursorInfo) || cursorInfo.flags != Native.CURSOR_SHOWING)
        {
            return;
        }

        var cursorPos = cursorInfo.ptScreenPos;
        var x = cursorPos.X - rect.X;
        var y = cursorPos.Y - rect.Y;

        using var gfx = Graphics.FromImage(bmp);
        Native.DrawIcon(gfx.GetHdc(), x, y, cursorInfo.hCursor);
        gfx.ReleaseHdc();
    }
}

public static class GhostCursorOverlay
{
    public static void ShowAt(Point screenPoint, int durationMs, bool pulse, string? label)
    {
        durationMs = Math.Clamp(durationMs, 60, 5000);
        Program.RunSta(() =>
        {
            using var form = new GhostCursorForm(screenPoint, durationMs, pulse, label);
            form.ShowDialog();
            return true;
        });
    }

    public static void ShowMany(IReadOnlyList<CoordinatorOverlayCursor> cursors, int durationMs)
    {
        durationMs = Math.Clamp(durationMs, 60, 10000);
        if (cursors.Count == 0)
        {
            return;
        }

        Program.RunSta(() =>
        {
            using var form = new GhostCursorForm(cursors, durationMs);
            form.ShowDialog();
            return true;
        });
    }

    private sealed class GhostCursorForm : Form
    {
        private readonly Point _screenPoint;
        private readonly bool _pulse;
        private readonly string? _label;
        private readonly List<CoordinatorOverlayCursor>? _cursors;
        private System.Windows.Forms.Timer _timer = null!;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public GhostCursorForm(Point screenPoint, int durationMs, bool pulse, string? label)
        {
            _screenPoint = screenPoint;
            _pulse = pulse;
            _label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            _cursors = null;
            ConfigureWindow(durationMs);
        }

        public GhostCursorForm(IReadOnlyList<CoordinatorOverlayCursor> cursors, int durationMs)
        {
            _screenPoint = Point.Empty;
            _pulse = false;
            _label = null;
            _cursors = cursors.Select(c => new CoordinatorOverlayCursor
            {
                Agent = c.Agent,
                Label = c.Label,
                X = c.X,
                Y = c.Y,
                Display = c.Display,
                App = c.App,
                Window = c.Window,
                Hwnd = c.Hwnd,
                Pulse = c.Pulse,
                UpdatedAt = c.UpdatedAt,
                ExpiresAt = c.ExpiresAt
            }).ToList();
            ConfigureWindow(durationMs);
        }

        private void ConfigureWindow(int durationMs)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = VirtualScreenBounds();
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;

            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += (_, _) =>
            {
                if (_sw.ElapsedMilliseconds >= durationMs)
                {
                    Close();
                    return;
                }
                Invalidate();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_TRANSPARENT = 0x00000020;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (_cursors != null)
            {
                foreach (var cursor in _cursors)
                {
                    DrawCursor(e.Graphics, cursor.X - Bounds.X, cursor.Y - Bounds.Y, cursor.Pulse, cursor.Label ?? cursor.Agent);
                }
                return;
            }

            DrawCursor(e.Graphics, _screenPoint.X - Bounds.X, _screenPoint.Y - Bounds.Y, _pulse, _label);
        }

        private void DrawCursor(Graphics graphics, float x, float y, bool pulse, string? label)
        {
            using var fill = new SolidBrush(Color.FromArgb(230, 0, 120, 215));
            using var outline = new Pen(Color.White, 2);
            var cursor = new PointF[]
            {
                new(x, y),
                new(x + 2, y + 25),
                new(x + 8, y + 18),
                new(x + 14, y + 32),
                new(x + 20, y + 29),
                new(x + 14, y + 16),
                new(x + 24, y + 16)
            };
            graphics.FillPolygon(fill, cursor);
            graphics.DrawPolygon(outline, cursor);

            if (!string.IsNullOrWhiteSpace(label))
            {
                DrawLabel(graphics, x + 26, y + 10, label);
            }

            if (pulse)
            {
                var progress = Math.Min(1f, _sw.ElapsedMilliseconds / 260f);
                var radius = 10 + progress * 24;
                var alpha = (int)(170 * (1 - progress));
                using var pulsePen = new Pen(Color.FromArgb(alpha, 0, 120, 215), 3);
                graphics.DrawEllipse(pulsePen, x - radius, y - radius, radius * 2, radius * 2);
            }
        }

        private static void DrawLabel(Graphics graphics, float x, float y, string label)
        {
            using var font = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
            var textSize = graphics.MeasureString(label, font);
            var rect = new RectangleF(x, y, textSize.Width + 14, textSize.Height + 8);
            using var bg = new SolidBrush(Color.FromArgb(235, 20, 24, 31));
            using var border = new Pen(Color.FromArgb(230, 255, 255, 255), 1);
            using var text = new SolidBrush(Color.White);
            graphics.FillRectangle(bg, rect);
            graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            graphics.DrawString(label, font, text, x + 7, y + 4);
        }

        private static Rectangle VirtualScreenBounds()
        {
            var left = Screen.AllScreens.Min(s => s.Bounds.Left);
            var top = Screen.AllScreens.Min(s => s.Bounds.Top);
            var right = Screen.AllScreens.Max(s => s.Bounds.Right);
            var bottom = Screen.AllScreens.Max(s => s.Bounds.Bottom);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }
}

public static class RegionSelector
{
    public static Rect? SelectRegion()
    {
        Rect? result = null;
        Program.RunSta(() =>
        {
            using var form = new RegionSelectForm(r => result = r);
            form.ShowDialog();
            return true;
        });
        return result;
    }

    private sealed class RegionSelectForm : Form
    {
        private readonly Action<Rect?> _onComplete;
        private Point _start;
        private Point _end;
        private bool _dragging;

        public RegionSelectForm(Action<Rect?> onComplete)
        {
            _onComplete = onComplete;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Opacity = 0.2;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    _onComplete(null);
                    Close();
                }
            };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _dragging = true;
            _start = e.Location;
            _end = e.Location;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            _end = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            _end = e.Location;
            var rect = ToRect(_start, _end);
            _onComplete(rect.W > 0 && rect.H > 0 ? rect : null);
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_dragging) return;
            var rect = ToRectangle(_start, _end);
            using var pen = new Pen(Color.Lime, 2);
            using var brush = new SolidBrush(Color.FromArgb(40, Color.Lime));
            e.Graphics.DrawRectangle(pen, rect);
            e.Graphics.FillRectangle(brush, rect);
        }

        private static Rect ToRect(Point a, Point b)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            var w = Math.Abs(a.X - b.X);
            var h = Math.Abs(a.Y - b.Y);
            return new Rect { X = x, Y = y, W = w, H = h };
        }

        private static Rectangle ToRectangle(Point a, Point b)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            var w = Math.Abs(a.X - b.X);
            var h = Math.Abs(a.Y - b.Y);
            return new Rectangle(x, y, w, h);
        }
    }
}

public static class ImageHelper
{
    public static Bitmap Downscale(Bitmap src, int maxW, int maxH)
    {
        var scale = 1.0;
        if (maxW > 0) scale = Math.Min(scale, maxW / (double)src.Width);
        if (maxH > 0) scale = Math.Min(scale, maxH / (double)src.Height);
        if (scale >= 1.0) return src;

        int w = (int)Math.Round(src.Width * scale);
        int h = (int)Math.Round(src.Height * scale);
        if (w <= 0 || h <= 0) return src;
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var gfx = Graphics.FromImage(dst);
        gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        gfx.DrawImage(src, new Rectangle(0, 0, w, h));
        return dst;
    }

    public static void SaveImage(Bitmap bmp, Stream stream, string? format)
    {
        if (format == "jpg")
        {
            var enc = ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.MimeType == "image/jpeg");
            if (enc != null)
            {
                bmp.Save(stream, enc, null);
            }
            else
            {
                bmp.Save(stream, ImageFormat.Png);
            }
        }
        else
        {
            bmp.Save(stream, ImageFormat.Png);
        }
    }

    public static void SaveImage(Bitmap bmp, string path, string? format)
    {
        using var fs = File.Create(path);
        SaveImage(bmp, fs, format);
    }

    public static void DrawGrid(Bitmap bmp, Rect sourceRect, double scaleX, double scaleY, int step, bool absolute)
    {
        if (step <= 0) return;
        using var gfx = Graphics.FromImage(bmp);
        using var font = new Font("Segoe UI", 9, FontStyle.Regular, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        using var shadowBrush = new SolidBrush(Color.Black);

        int imgW = bmp.Width;
        int imgH = bmp.Height;
        int stepX = Math.Max(1, (int)Math.Round(step * scaleX));
        int stepY = Math.Max(1, (int)Math.Round(step * scaleY));

        InvertGridLines(bmp, stepX, stepY);

        for (int x = 0; x <= imgW; x += stepX)
        {
            int label = absolute ? sourceRect.X + (int)Math.Round(x / scaleX) : (int)Math.Round(x / scaleX);
            var text = label.ToString();
            gfx.DrawString(text, font, shadowBrush, x + 3, 3);
            gfx.DrawString(text, font, textBrush, x + 2, 2);
        }

        for (int y = 0; y <= imgH; y += stepY)
        {
            int label = absolute ? sourceRect.Y + (int)Math.Round(y / scaleY) : (int)Math.Round(y / scaleY);
            var text = label.ToString();
            gfx.DrawString(text, font, shadowBrush, 3, y + 3);
            gfx.DrawString(text, font, textBrush, 2, y + 2);
        }
    }

    public static Rect? FindTemplate(Bitmap haystack, Bitmap needle, double threshold)
    {
        int w = haystack.Width;
        int h = haystack.Height;
        int nw = needle.Width;
        int nh = needle.Height;
        if (nw <= 0 || nh <= 0 || nw > w || nh > h) return null;

        var hayData = haystack.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var nedData = needle.LockBits(new Rectangle(0, 0, nw, nh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* hayBase = (byte*)hayData.Scan0;
                byte* nedBase = (byte*)nedData.Scan0;
                int hayStride = hayData.Stride;
                int nedStride = nedData.Stride;
                for (int y = 0; y <= h - nh; y++)
                {
                    for (int x = 0; x <= w - nw; x++)
                    {
                        double score = 0;
                        int count = 0;
                        for (int j = 0; j < nh; j++)
                        {
                            byte* hayRow = hayBase + (y + j) * hayStride;
                            byte* nedRow = nedBase + j * nedStride;
                            for (int i = 0; i < nw; i++)
                            {
                                byte* hp = hayRow + (x + i) * 4;
                                byte* np = nedRow + i * 4;
                                int dr = hp[2] - np[2];
                                int dg = hp[1] - np[1];
                                int db = hp[0] - np[0];
                                double diff = Math.Sqrt(dr * dr + dg * dg + db * db) / (Math.Sqrt(3) * 255.0);
                                score += 1.0 - diff;
                                count++;
                            }
                        }
                        double avg = score / Math.Max(1, count);
                        if (avg >= threshold)
                        {
                            return new Rect { X = x, Y = y, W = nw, H = nh };
                        }
                    }
                }
            }
        }
        finally
        {
            haystack.UnlockBits(hayData);
            needle.UnlockBits(nedData);
        }
        return null;
    }

    private static void InvertGridLines(Bitmap bmp, int stepX, int stepY)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* basePtr = (byte*)data.Scan0;
                int stride = data.Stride;
                for (int y = 0; y < bmp.Height; y++)
                {
                    bool onH = (y % stepY) == 0;
                    byte* row = basePtr + y * stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (onH || (x % stepX) == 0)
                        {
                            byte* px = row + x * 4;
                            px[0] = (byte)(255 - px[0]);
                            px[1] = (byte)(255 - px[1]);
                            px[2] = (byte)(255 - px[2]);
                        }
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}

public static class KeyMap
{
    public static readonly Dictionary<char, List<char>> Adjacent = new()
    {
        ['a'] = new List<char> { 's', 'q', 'w', 'z' },
        ['b'] = new List<char> { 'v', 'g', 'h', 'n' },
        ['c'] = new List<char> { 'x', 'd', 'f', 'v' },
        ['d'] = new List<char> { 's', 'e', 'r', 'f', 'c', 'x' },
        ['e'] = new List<char> { 'w', 's', 'd', 'r' },
        ['f'] = new List<char> { 'd', 'r', 't', 'g', 'v', 'c' },
        ['g'] = new List<char> { 'f', 't', 'y', 'h', 'b', 'v' },
        ['h'] = new List<char> { 'g', 'y', 'u', 'j', 'n', 'b' },
        ['i'] = new List<char> { 'u', 'j', 'k', 'o' },
        ['j'] = new List<char> { 'h', 'u', 'i', 'k', 'm', 'n' },
        ['k'] = new List<char> { 'j', 'i', 'o', 'l', 'm' },
        ['l'] = new List<char> { 'k', 'o', 'p' },
        ['m'] = new List<char> { 'n', 'j', 'k' },
        ['n'] = new List<char> { 'b', 'h', 'j', 'm' },
        ['o'] = new List<char> { 'i', 'k', 'l', 'p' },
        ['p'] = new List<char> { 'o', 'l' },
        ['q'] = new List<char> { 'w', 'a' },
        ['r'] = new List<char> { 'e', 'd', 'f', 't' },
        ['s'] = new List<char> { 'a', 'w', 'e', 'd', 'x', 'z' },
        ['t'] = new List<char> { 'r', 'f', 'g', 'y' },
        ['u'] = new List<char> { 'y', 'h', 'j', 'i' },
        ['v'] = new List<char> { 'c', 'f', 'g', 'b' },
        ['w'] = new List<char> { 'q', 'a', 's', 'e' },
        ['x'] = new List<char> { 'z', 's', 'd', 'c' },
        ['y'] = new List<char> { 't', 'g', 'h', 'u' },
        ['z'] = new List<char> { 'a', 's', 'x' }
    };

    private static readonly Dictionary<string, ushort> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CTRL"] = Native.VK_CONTROL,
        ["SHIFT"] = Native.VK_SHIFT,
        ["ALT"] = Native.VK_MENU,
        ["WIN"] = Native.VK_LWIN,
        ["ENTER"] = Native.VK_RETURN,
        ["TAB"] = Native.VK_TAB,
        ["ESC"] = Native.VK_ESCAPE,
        ["BACKSPACE"] = Native.VK_BACK,
        ["DELETE"] = Native.VK_DELETE,
        ["UP"] = Native.VK_UP,
        ["DOWN"] = Native.VK_DOWN,
        ["LEFT"] = Native.VK_LEFT,
        ["RIGHT"] = Native.VK_RIGHT,
        ["HOME"] = Native.VK_HOME,
        ["END"] = Native.VK_END,
        ["PAGEUP"] = Native.VK_PRIOR,
        ["PAGEDOWN"] = Native.VK_NEXT,
        ["SPACE"] = Native.VK_SPACE
    };

    public static ushort ToVirtualKey(string key)
    {
        var normalized = NormalizeKeyName(key);
        if (Map.TryGetValue(normalized, out var vk)) return vk;
        if (key.Length == 1)
        {
            return (ushort)char.ToUpperInvariant(key[0]);
        }
        throw new DeskCtlException("INVALID_ARGS", $"unknown key '{key}'");
    }

    private static string NormalizeKeyName(string key)
    {
        var k = key.Trim().Replace("_", "").Replace("-", "").ToUpperInvariant();
        return k switch
        {
            "PAGEDOWN" => "PAGEDOWN",
            "PGDN" => "PAGEDOWN",
            "PAGEDN" => "PAGEDOWN",
            "PAGEUP" => "PAGEUP",
            "PGUP" => "PAGEUP",
            "PAGEDOWNKEY" => "PAGEDOWN",
            "PAGEUPKEY" => "PAGEUP",
            _ => k
        };
    }
}

public static class Native
{
    public const uint CURSOR_SHOWING = 0x00000001;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_BACK = 0x08;
    public const ushort VK_DELETE = 0x2E;
    public const ushort VK_UP = 0x26;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_HOME = 0x24;
    public const ushort VK_END = 0x23;
    public const ushort VK_PRIOR = 0x21;
    public const ushort VK_NEXT = 0x22;
    public const ushort VK_SPACE = 0x20;
    public const int SW_MINIMIZE = 6;
    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint SC_MONITORPOWER = 0xF170;
    public const int MK_LBUTTON = 0x0001;
    public const int MK_RBUTTON = 0x0002;
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    public const uint EWX_SHUTDOWN = 0x00000001;
    public const uint EWX_REBOOT = 0x00000002;
    public const uint EWX_FORCEIFHUNG = 0x00000010;
    public const int DISP_CHANGE_SUCCESSFUL = 0;
    public const int CDS_UPDATEREGISTRY = 0x00000001;
    public const int CDS_NORESET = 0x10000000;
    public const int CDS_SET_PRIMARY = 0x00000010;
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int DM_PELSWIDTH = 0x00080000;
    public const int DM_PELSHEIGHT = 0x00100000;
    public const int DM_POSITION = 0x00000020;
    public const int DM_DISPLAYORIENTATION = 0x00000080;
    public const int DMDO_DEFAULT = 0;
    public const int DMDO_90 = 1;
    public const int DMDO_180 = 2;
    public const int DMDO_270 = 3;

    public static void EnablePerMonitorDpiAwareness()
    {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }

    public static IntPtr ParseHwnd(string? hwnd)
    {
        if (string.IsNullOrWhiteSpace(hwnd)) return IntPtr.Zero;
        if (hwnd.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return new IntPtr(Convert.ToInt64(hwnd, 16));
        }
        return new IntPtr(Convert.ToInt64(hwnd));
    }

    public static string GetWindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        var sb = new StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static int GetWindowProcessId(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    public static Point GetCursorPosition()
    {
        GetCursorPos(out var pt);
        return new Point(pt.X, pt.Y);
    }

    public static IntPtr MakeLParam(int low, int high)
    {
        return new IntPtr((high << 16) | (low & 0xFFFF));
    }

    public static void SendMouse(MOUSEEVENTF flags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = (uint)flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void SendMouseWheel(int delta)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = (uint)delta,
                    dwFlags = (uint)MOUSEEVENTF.WHEEL,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void SendVirtualKey(ushort vk)
    {
        SendKeyDown(vk);
        SendKeyUp(vk);
    }

    public static void SendKeyDown(ushort vk)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void SendKeyUp(ushort vk)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void SendUnicodeChar(char ch)
    {
        var inputDown = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        var inputUp = inputDown;
        inputUp.U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        SendInput(2, new[] { inputDown, inputUp }, Marshal.SizeOf<INPUT>());
    }

    public static bool GetDpiForMonitorSafe(IntPtr hMonitor, ref uint dpiX, ref uint dpiY)
    {
        try
        {
            return GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY) == 0;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    public enum MOUSEEVENTF : uint
    {
        MOVE = 0x0001,
        LEFTDOWN = 0x0002,
        LEFTUP = 0x0004,
        RIGHTDOWN = 0x0008,
        RIGHTUP = 0x0010,
        WHEEL = 0x0800
    }

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("Shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    public static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    public static Rect GetClientRectScreen(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var rect))
        {
            return new Rect();
        }
        var topLeft = new POINT { X = rect.Left, Y = rect.Top };
        var bottomRight = new POINT { X = rect.Right, Y = rect.Bottom };
        ClientToScreen(hwnd, ref topLeft);
        ClientToScreen(hwnd, ref bottomRight);
        return new Rect
        {
            X = topLeft.X,
            Y = topLeft.Y,
            W = bottomRight.X - topLeft.X,
            H = bottomRight.Y - topLeft.Y
        };
    }

    public static bool TryGetCurrentDisplaySettings(string deviceName, out DEVMODE mode)
    {
        mode = new DEVMODE();
        mode.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        return EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode);
    }

    [DllImport("user32.dll")]
    public static extern bool LockWorkStation();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
