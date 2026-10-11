using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using CodexMascot.Core;

namespace CodexMascot.App;

public static class HookIntegration
{
    // Each agent relays into its own folder so the monitor knows which mapping applies.
    public static string EventDirectory => EventDirectoryFor(AgentKind.Codex);
    public static string EventDirectoryFor(AgentKind kind) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMascot",
        kind == AgentKind.Codex ? "events" : "events-" + kind.ToString().ToLowerInvariant());
    public static string DefaultCodexHome => AgentAdapters.Codex.DefaultHome;
    public static string DefaultHomeFor(AgentKind kind) => AgentAdapters.For(kind).DefaultHome;
    private const string Marker = "-MascotBridge";

    internal static void EnsureInstalled(string home, AgentKind kind)
    {
        if (kind is AgentKind.Antigravity or AgentKind.Kimi or AgentKind.Windsurf or AgentKind.Kiro)
        {
            if (!IsInstalled(home, kind)) Install(home, kind);
            return;
        }
        if (kind is AgentKind.Cursor or AgentKind.Copilot)
        {
            var currentScript = Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1");
            var config = Path.Combine(home, AgentAdapters.For(kind).HookConfigFileName);
            if (!IsInstalled(home, kind) || !File.ReadAllText(config).Contains(currentScript, StringComparison.OrdinalIgnoreCase))
                Install(home, kind);
            return;
        }
        if (kind is AgentKind.OpenCode or AgentKind.Amp) { Install(home, kind); return; }
        // Refresh a stale installation path, but do not rewrite a healthy config
        // or generate a backup on every monitoring restart.
        var path = Path.Combine(home, AgentAdapters.For(kind).HookConfigFileName);
        var script = Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1");
        if (IsInstalled(home, kind))
        {
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            var owned = root["hooks"]!.AsObject().SelectMany(p => (p.Value as JsonArray ?? new()).OfType<JsonObject>())
                .SelectMany(g => (g["hooks"] as JsonArray ?? new()).OfType<JsonObject>())
                .Select(h => h["command"]?.GetValue<string>()).Where(c => c?.Contains(Marker, StringComparison.Ordinal) == true).ToArray();
            if (owned.All(c => c!.Contains("\"" + script + "\"", StringComparison.OrdinalIgnoreCase) &&
                               c.Contains("-ExecutionPolicy Bypass", StringComparison.OrdinalIgnoreCase))) return;
        }
        Install(home, kind);
    }

    public static bool IsInstalled(string home, AgentKind kind)
    {
        var path = Path.Combine(home, AgentAdapters.For(kind).HookConfigFileName);
        if (!File.Exists(path)) return false;
        if (kind == AgentKind.Kimi)
        {
            var content = File.ReadAllText(path);
            return content.Contains(KimiBegin, StringComparison.Ordinal) &&
                content.Contains(KimiEnd, StringComparison.Ordinal) &&
                content.Contains("event = \"TurnStarted\"", StringComparison.Ordinal) &&
                content.Contains(JsonSerializer.Serialize(RelayCommand(
                    Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1"), kind, "TurnStarted")),
                    StringComparison.Ordinal);
        }
        if (kind == AgentKind.Antigravity)
        {
            var antigravityRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return antigravityRoot?["agent-mascot"] is JsonObject owned && new[] { "PreInvocation", "Stop" }.All(name =>
                owned[name] is JsonArray handlers && handlers.OfType<JsonObject>().Any(h =>
                    h["command"]?.GetValue<string>() == AntigravityCommand(
                        Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1"), name)));
        }
        if (kind == AgentKind.Windsurf)
        {
            var windsurfRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return windsurfRoot?["hooks"] is JsonObject windsurfHooks && new[] { "pre_user_prompt", "post_cascade_response" }.All(name =>
                windsurfHooks[name] is JsonArray handlers && handlers.OfType<JsonObject>().Any(h =>
                    h["powershell"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true &&
                    h["powershell"]!.GetValue<string>().Contains(Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1"), StringComparison.OrdinalIgnoreCase)));
        }
        if (kind == AgentKind.Kiro)
        {
            var kiroRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return kiroRoot?["hooks"] is JsonArray kiroHooks && new[] { "UserPromptSubmit", "Stop", "SessionEnd" }.All(name =>
                kiroHooks.OfType<JsonObject>().Any(h => h["trigger"]?.GetValue<string>() == name &&
                    h["action"]?["command"]?.GetValue<string>() == KiroCommand(
                        Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1"), name)));
        }
        if (kind == AgentKind.OpenCode) return File.ReadAllText(path).Contains("MascotBridge", StringComparison.Ordinal);
        if (kind == AgentKind.Amp) return File.ReadAllText(path).Contains("MascotBridge", StringComparison.Ordinal);
        if (kind == AgentKind.Cursor)
        {
            var cursorRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return cursorRoot?["hooks"] is JsonObject cursorHooks && new[] { "beforeSubmitPrompt", "stop", "sessionEnd" }.All(name =>
                cursorHooks[name] is JsonArray entries && entries.OfType<JsonObject>().Any(entry =>
                    entry["command"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true));
        }
        if (kind == AgentKind.Copilot)
        {
            var copilotRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return copilotRoot?["hooks"] is JsonObject copilotHooks &&
                new[] { "userPromptSubmitted", "agentStop", "notification", "sessionEnd" }.All(name =>
                    copilotHooks[name] is JsonArray entries && entries.OfType<JsonObject>().Any(IsOwnedCopilot));
        }
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (root?["hooks"] is not JsonObject hooks) return false;
        return EventNames(kind).All(name => hooks[name] is JsonArray groups &&
            groups.OfType<JsonObject>().Any(group => group["hooks"] is JsonArray handlers &&
                handlers.OfType<JsonObject>().Any(h => h["command"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true)));
    }

    // These agents share grouped command-hook JSON, but use different events and timeouts.
    private static string[] EventNames(AgentKind kind) => kind == AgentKind.Claude
        ? new[] { "UserPromptSubmit", "Notification", "PreToolUse", "PostToolUse", "Stop", "SessionEnd" }
        : kind == AgentKind.Codex
            ? new[] { "UserPromptSubmit", "PermissionRequest", "PreToolUse", "PostToolUse", "Stop", "Interrupt", "SessionEnd" }
            : kind == AgentKind.Gemini
                ? new[] { "BeforeAgent", "AfterAgent", "Notification", "SessionEnd" }
                : kind == AgentKind.Qwen
                    ? new[] { "UserPromptSubmit", "Stop", "StopFailure", "PermissionRequest", "SessionEnd" }
                    : Array.Empty<string>();

    private static string? Matcher(AgentKind kind, string eventName) => eventName != "PreToolUse" ? null
        : kind == AgentKind.Claude ? "AskUserQuestion|ExitPlanMode" : ".*request_user_input.*";

    public static string Install(string home) => Install(home, AgentKind.Codex);
    public static string Install(string home, AgentKind kind)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException(Loc.T("integration 폴더가 없습니다. ZIP 전체를 풀어 주세요."), script);
        if (kind == AgentKind.Antigravity) return InstallAntigravity(home, script);
        if (kind == AgentKind.Kimi) return InstallKimi(home, script);
        if (kind == AgentKind.Windsurf) return InstallWindsurf(home, script);
        if (kind == AgentKind.Kiro) return InstallKiro(home, script);
        if (kind == AgentKind.Cursor) return InstallCursor(home, script);
        if (kind == AgentKind.OpenCode) return InstallOpenCode(home);
        if (kind == AgentKind.Amp) return InstallAmp(home);
        if (kind == AgentKind.Copilot) return InstallCopilot(home, script);
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, AgentAdapters.For(kind).HookConfigFileName);
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
        if (root is null) throw new InvalidDataException(Loc.T("기존 설정 파일을 읽을 수 없습니다. 원본은 변경하지 않았습니다."));
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        if (root["hooks"] is not null && root["hooks"] is not JsonObject) throw new InvalidDataException(Loc.T("hooks 형식이 올바르지 않습니다."));
        if (root["hooks"] is null) root["hooks"] = hooks;
        RemoveOwned(hooks);
        var events = EventDirectoryFor(kind);
        foreach (var name in EventNames(kind))
        {
            var groups = hooks[name] as JsonArray;
            if (hooks[name] is not null && groups is null) throw new InvalidDataException(name + Loc.T(" 배열 형식을 확인하세요."));
            if (groups is null) hooks[name] = groups = new JsonArray();
            var command = "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" " + Marker +
                " -EventDirectory \"" + events + "\"";
            var timeout = kind == AgentKind.Gemini ? 3000 : 3;
            var group = new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = timeout }) };
            if (Matcher(kind, name) is { } matcher) group["matcher"] = matcher;
            groups.Add(group);
        }
        Save(path, root);
        return path;
    }

    private static string InstallCopilot(string home, string script)
    {
        var path = Path.Combine(home, AgentAdapters.Copilot.HookConfigFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
        if (root is null) throw new InvalidDataException("Existing Copilot hook file is not a JSON object: " + path);
        if (File.Exists(path) && !root.ToJsonString().Contains(Marker, StringComparison.Ordinal))
            throw new InvalidDataException("Existing Copilot hook file is not owned by Agent Mascot: " + path);
        var events = EventDirectoryFor(AgentKind.Copilot);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        if (root["hooks"] is not null && root["hooks"] is not JsonObject)
            throw new InvalidDataException("Copilot hooks must be a JSON object");
        root["version"] ??= 1;
        root["hooks"] ??= hooks;
        RemoveOwnedCopilot(hooks);
        foreach (var name in new[] { "userPromptSubmitted", "agentStop", "notification", "sessionEnd" })
        {
            if (hooks[name] is not JsonArray entries)
            {
                if (hooks[name] is not null) throw new InvalidDataException("Copilot hook event is not an array: " + name);
                hooks[name] = entries = new JsonArray();
            }
            entries.Add(new JsonObject
            {
                ["type"] = "command", ["exec"] = "powershell.exe", ["args"] = new JsonArray(
                    "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                    Marker, "-EventDirectory", events, "-HookEventName", name), ["timeoutSec"] = 3
            });
        }
        Save(path, root);
        return path;
    }

    private static bool IsOwnedCopilot(JsonObject entry) => entry["args"] is JsonArray args &&
        args.Any(arg => arg is JsonValue value && value.TryGetValue<string>(out var text) && text == Marker);

    private static void RemoveOwnedCopilot(JsonObject hooks)
    {
        foreach (var property in hooks.ToArray())
            if (property.Value is JsonArray entries)
                foreach (var entry in entries.OfType<JsonObject>().ToArray())
                    if (IsOwnedCopilot(entry)) entries.Remove(entry);
    }

    private static string InstallOpenCode(string home)
    {
        var path = Path.Combine(home, AgentAdapters.OpenCode.HookConfigFileName);
        var template = Path.Combine(AppContext.BaseDirectory, "integration", "agent-mascot-opencode.js");
        if (!File.Exists(template)) throw new FileNotFoundException("OpenCode relay is missing", template);
        var content = File.ReadAllText(template).Replace("__AGENT_MASCOT_EVENTS__",
            JsonSerializer.Serialize(EventDirectoryFor(AgentKind.OpenCode)), StringComparison.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var previous = File.ReadAllText(path);
            if (!previous.Contains("MascotBridge", StringComparison.Ordinal))
                throw new InvalidDataException("Existing OpenCode plugin is not owned by Agent Mascot: " + path);
            if (previous == content) return path;
            File.Copy(path, path + ".mascot-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), false);
        }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, true);
        return path;
    }

    private static string InstallAmp(string home)
    {
        var path = Path.Combine(home, AgentAdapters.Amp.HookConfigFileName);
        var template = Path.Combine(AppContext.BaseDirectory, "integration", "agent-mascot-amp.js");
        if (!File.Exists(template)) throw new FileNotFoundException("Amp relay is missing", template);
        var content = File.ReadAllText(template).Replace("__AGENT_MASCOT_EVENTS__",
            JsonSerializer.Serialize(EventDirectoryFor(AgentKind.Amp)), StringComparison.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var previous = File.ReadAllText(path);
            if (!previous.Contains("MascotBridge", StringComparison.Ordinal))
                throw new InvalidDataException("Existing Amp plugin is not owned by Agent Mascot: " + path);
            if (previous == content) return path;
            File.Copy(path, path + ".mascot-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), false);
        }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, true);
        return path;
    }

    private static string InstallCursor(string home, string script)
    {
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, AgentAdapters.Cursor.HookConfigFileName);
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
        if (root is null) throw new InvalidDataException("Cursor hooks.json is not a JSON object");
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        if (root["hooks"] is not null && root["hooks"] is not JsonObject)
            throw new InvalidDataException("Cursor hooks must be a JSON object");
        root["version"] ??= 1;
        root["hooks"] ??= hooks;
        foreach (var property in hooks.ToArray())
            if (property.Value is JsonArray existing)
                foreach (var entry in existing.OfType<JsonObject>().ToArray())
                    if (entry["command"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true)
                        existing.Remove(entry);
        foreach (var name in new[] { "beforeSubmitPrompt", "stop", "sessionEnd" })
        {
            if (hooks[name] is not JsonArray entries)
            {
                if (hooks[name] is not null) throw new InvalidDataException("Cursor hook event is not an array: " + name);
                hooks[name] = entries = new JsonArray();
            }
            entries.Add(new JsonObject
            {
                ["command"] = "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" +
                    script + "\" " + Marker + " -EventDirectory \"" + EventDirectoryFor(AgentKind.Cursor) + "\" -HookEventName " + name,
                ["timeout"] = 3
            });
        }
        Save(path, root);
        return path;
    }
    private static string RelayCommand(string script, AgentKind kind, string eventName, bool antigravityStop = false) =>
        "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script +
        "\" " + Marker + " -EventDirectory \"" + EventDirectoryFor(kind) + "\" -HookEventName " + eventName +
        (antigravityStop ? " -AntigravityStop" : "");

    // Antigravity CLI passes quoted Windows -File paths through as literal quotes.
    // EncodedCommand is a single argument and also works when the app lives in Program Files.
    private static string AntigravityCommand(string script, string eventName)
    {
        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var command = "& " + Quote(script) + " -MascotBridge -EventDirectory " +
            Quote(EventDirectoryFor(AgentKind.Antigravity)) + " -HookEventName " + eventName +
            (eventName == "Stop" ? " -AntigravityStop" : "");
        return "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
    }

    private static bool IsOwnedAntigravity(JsonNode? node)
    {
        if (node?.ToJsonString().Contains(Marker, StringComparison.Ordinal) == true) return true;
        if (node is not JsonObject owned) return false;
        foreach (var eventName in new[] { "PreInvocation", "Stop" })
        {
            if (owned[eventName] is not JsonArray handlers) continue;
            foreach (var handler in handlers.OfType<JsonObject>())
            {
                var command = handler["command"]?.GetValue<string>();
                var encoded = command?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (encoded is null || !command!.Contains("-EncodedCommand ", StringComparison.Ordinal)) continue;
                try
                {
                    var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
                    if (decoded.Contains(Marker, StringComparison.Ordinal) &&
                        decoded.Contains("Send-MascotEvent.ps1", StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (FormatException) { }
            }
        }
        return false;
    }

    private static string InstallAntigravity(string home, string script)
    {
        var path = Path.Combine(home, AgentAdapters.Antigravity.HookConfigFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
        if (root is null) throw new InvalidDataException("Antigravity hooks.json must be an object");
        if (root["agent-mascot"] is { } previous && !IsOwnedAntigravity(previous))
            throw new InvalidDataException("Antigravity hook name 'agent-mascot' is owned by another tool");
        root["agent-mascot"] = new JsonObject
        {
            ["PreInvocation"] = new JsonArray(new JsonObject
            {
                ["type"] = "command", ["command"] = AntigravityCommand(script, "PreInvocation"), ["timeout"] = 3
            }),
            ["Stop"] = new JsonArray(new JsonObject
            {
                ["type"] = "command", ["command"] = AntigravityCommand(script, "Stop"), ["timeout"] = 3
            })
        };
        Save(path, root);
        return path;
    }

    private const string KimiBegin = "# AgentMascot-MascotBridge begin";
    private const string KimiEnd = "# AgentMascot-MascotBridge end";
    private static string RemoveKimiBlock(string text)
    {
        var begin = text.IndexOf(KimiBegin, StringComparison.Ordinal);
        if (begin < 0) return text;
        var end = text.IndexOf(KimiEnd, begin, StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("Kimi AgentMascot block has no end marker");
        end += KimiEnd.Length;
        if (end < text.Length && text[end] == '\r') end++;
        if (end < text.Length && text[end] == '\n') end++;
        return text.Remove(begin, end - begin);
    }

    private static string InstallKimi(string home, string script)
    {
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, AgentAdapters.Kimi.HookConfigFileName);
        var previous = File.Exists(path) ? File.ReadAllText(path) : "";
        var kept = RemoveKimiBlock(previous);
        var lines = new List<string> { KimiBegin };
        foreach (var name in new[] { "TurnStarted", "PermissionRequest", "PermissionResult", "Stop", "StopFailure", "Interrupt", "SessionEnd" })
        {
            lines.Add("[[hooks]]");
            lines.Add("event = " + JsonSerializer.Serialize(name));
            lines.Add("command = " + JsonSerializer.Serialize(RelayCommand(script, AgentKind.Kimi, name)));
            lines.Add("timeout = 3");
            lines.Add("");
        }
        lines.Add(KimiEnd);
        var content = kept.TrimEnd('\r', '\n') + (kept.Length == 0 ? "" : Environment.NewLine + Environment.NewLine) +
            string.Join(Environment.NewLine, lines) + Environment.NewLine;
        if (content == previous) return path;
        SaveText(path, content);
        return path;
    }

    private static void RemoveOwnedWindsurf(JsonObject hooks)
    {
        foreach (var property in hooks.ToArray())
            if (property.Value is JsonArray entries)
                foreach (var entry in entries.OfType<JsonObject>().ToArray())
                    if (entry["powershell"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true)
                        entries.Remove(entry);
    }

    private static string InstallWindsurf(string home, string script)
    {
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, AgentAdapters.Windsurf.HookConfigFileName);
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
        if (root is null) throw new InvalidDataException("Windsurf hooks.json must be an object");
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        if (root["hooks"] is not null && root["hooks"] is not JsonObject)
            throw new InvalidDataException("Windsurf hooks must be an object");
        root["hooks"] ??= hooks;
        RemoveOwnedWindsurf(hooks);
        foreach (var name in new[] { "pre_user_prompt", "post_cascade_response" })
        {
            if (hooks[name] is not JsonArray entries)
            {
                if (hooks[name] is not null) throw new InvalidDataException("Windsurf hook event is not an array: " + name);
                hooks[name] = entries = new JsonArray();
            }
            entries.Add(new JsonObject { ["powershell"] = RelayCommand(script, AgentKind.Windsurf, name), ["show_output"] = false });
        }
        Save(path, root);
        return path;
    }

    private static string KiroCommand(string script, string eventName) =>
        RelayCommand(script, AgentKind.Kiro, eventName) + " -QuietOutput";

    private static bool IsOwnedKiro(JsonObject hook) =>
        hook["action"]?["command"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true;

    private static string InstallKiro(string home, string script)
    {
        var path = Path.Combine(home, AgentAdapters.Kiro.HookConfigFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
        if (root is null) throw new InvalidDataException("Kiro hook file must be an object");
        if (File.Exists(path) && root["hooks"] is not JsonArray)
            throw new InvalidDataException("Existing Kiro hook file is not an array owned by Agent Mascot");
        var hooks = root["hooks"] as JsonArray ?? new JsonArray();
        if (File.Exists(path) && !hooks.OfType<JsonObject>().Any(IsOwnedKiro))
            throw new InvalidDataException("Existing Kiro hook file is not owned by Agent Mascot: " + path);
        root["version"] ??= "v1";
        root["hooks"] ??= hooks;
        foreach (var old in hooks.OfType<JsonObject>().Where(IsOwnedKiro).ToArray()) hooks.Remove(old);
        foreach (var name in new[] { "UserPromptSubmit", "Stop", "SessionEnd" })
            hooks.Add(new JsonObject
            {
                ["name"] = "Agent Mascot " + name,
                ["trigger"] = name,
                ["action"] = new JsonObject { ["type"] = "command", ["command"] = KiroCommand(script, name) },
                ["timeout"] = 5
            });
        Save(path, root);
        return path;
    }

    public static void Uninstall(string home) => Uninstall(home, AgentKind.Codex);
    public static void Uninstall(string home, AgentKind kind)
    {
        var path = Path.Combine(home, AgentAdapters.For(kind).HookConfigFileName);
        if (!File.Exists(path)) return;
        if (kind == AgentKind.Antigravity)
        {
            var antigravityRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Antigravity hook config error");
            if (IsOwnedAntigravity(antigravityRoot["agent-mascot"]))
            { antigravityRoot.Remove("agent-mascot"); Save(path, antigravityRoot); }
            return;
        }
        if (kind == AgentKind.Kimi)
        {
            var previous = File.ReadAllText(path);
            var kept = RemoveKimiBlock(previous);
            if (kept != previous) SaveText(path, kept);
            return;
        }
        if (kind == AgentKind.Windsurf)
        {
            var windsurfRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Windsurf hook config error");
            if (windsurfRoot["hooks"] is JsonObject windsurfHooks) RemoveOwnedWindsurf(windsurfHooks);
            Save(path, windsurfRoot);
            return;
        }
        if (kind == AgentKind.Kiro)
        {
            var kiroRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Kiro hook config error");
            if (kiroRoot["hooks"] is JsonArray kiroHooks)
            {
                var owned = kiroHooks.OfType<JsonObject>().Where(IsOwnedKiro).ToArray();
                if (owned.Length > 0)
                {
                    foreach (var old in owned) kiroHooks.Remove(old);
                    Save(path, kiroRoot);
                }
            }
            return;
        }
        if (kind == AgentKind.OpenCode)
        {
            if (IsInstalled(home, kind))
            {
                File.Copy(path, path + ".mascot-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), false);
                File.Delete(path);
            }
            return;
        }
        if (kind == AgentKind.Amp)
        {
            if (IsInstalled(home, kind))
            {
                File.Copy(path, path + ".mascot-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), false);
                File.Delete(path);
            }
            return;
        }
        if (kind == AgentKind.Copilot)
        {
            var copilotRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Copilot hook config error");
            if (copilotRoot["hooks"] is JsonObject copilotHooks) RemoveOwnedCopilot(copilotHooks);
            Save(path, copilotRoot);
            return;
        }
        if (kind == AgentKind.Cursor)
        {
            var cursorRoot = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Cursor hook config error");
            if (cursorRoot["hooks"] is JsonObject cursorHooks)
                foreach (var property in cursorHooks.ToArray())
                    if (property.Value is JsonArray entries)
                        foreach (var entry in entries.OfType<JsonObject>().ToArray())
                            if (entry["command"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true)
                                entries.Remove(entry);
            Save(path, cursorRoot);
            return;
        }
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException(Loc.T("설정 파일 형식 오류"));
        if (root["hooks"] is JsonObject hooks) RemoveOwned(hooks);
        Save(path, root);
    }
    private static void RemoveOwned(JsonObject hooks)
    {
        foreach (var property in hooks.ToArray())
        {
            if (property.Value is not JsonArray groups) continue;
            foreach (var group in groups.OfType<JsonObject>().ToArray())
            {
                if (group["hooks"] is not JsonArray handlers) continue;
                foreach (var h in handlers.OfType<JsonObject>().ToArray())
                    if (h["command"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true) handlers.Remove(h);
                if (handlers.Count == 0) groups.Remove(group);
            }
        }
    }
    private static void Save(string path, JsonObject root)
    {
        if (File.Exists(path)) File.Copy(path, path + ".mascot-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), false);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
    private static void SaveText(string path, string content)
    {
        if (File.Exists(path)) File.Copy(path, path + ".mascot-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), false);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, true);
    }
}
