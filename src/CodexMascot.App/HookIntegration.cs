using System.Text.Json;
using System.Text.Json.Nodes;
using CodexMascot.Core;

namespace CodexMascot.App;

public static class HookIntegration
{
    // Each agent relays into its own folder so the monitor knows which mapping applies.
    public static string EventDirectory => EventDirectoryFor(AgentKind.Codex);
    public static string EventDirectoryFor(AgentKind kind) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMascot",
        kind == AgentKind.Claude ? "events-claude" : "events");
    public static string DefaultCodexHome => AgentAdapters.Codex.DefaultHome;
    public static string DefaultHomeFor(AgentKind kind) => AgentAdapters.For(kind).DefaultHome;
    private const string Marker = "-MascotBridge";

    public static bool IsInstalled(string home, AgentKind kind)
    {
        var path = Path.Combine(home, AgentAdapters.For(kind).HookConfigFileName);
        if (!File.Exists(path)) return false;
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (root?["hooks"] is not JsonObject hooks) return false;
        return EventNames(kind).All(name => hooks[name] is JsonArray groups &&
            groups.OfType<JsonObject>().Any(group => group["hooks"] is JsonArray handlers &&
                handlers.OfType<JsonObject>().Any(h => h["command"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true)));
    }

    // Codex and Claude Code use the same hook file shape, so only the file name,
    // the event names and the relay target directory differ.
    private static string[] EventNames(AgentKind kind) => kind == AgentKind.Claude
        ? new[] { "UserPromptSubmit", "Notification", "PreToolUse", "PostToolUse", "Stop", "SessionEnd" }
        : new[] { "UserPromptSubmit", "PermissionRequest", "PreToolUse", "PostToolUse", "Stop", "Interrupt", "SessionEnd" };

    private static string? Matcher(AgentKind kind, string eventName) => eventName != "PreToolUse" ? null
        : kind == AgentKind.Claude ? "AskUserQuestion|ExitPlanMode" : ".*request_user_input.*";

    public static string Install(string home) => Install(home, AgentKind.Codex);
    public static string Install(string home, AgentKind kind)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("integration 폴더가 없습니다. ZIP 전체를 풀어 주세요.", script);
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, AgentAdapters.For(kind).HookConfigFileName);
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
        if (root is null) throw new InvalidDataException("기존 설정 파일을 읽을 수 없습니다. 원본은 변경하지 않았습니다.");
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        if (root["hooks"] is not null && root["hooks"] is not JsonObject) throw new InvalidDataException("hooks 형식이 올바르지 않습니다.");
        if (root["hooks"] is null) root["hooks"] = hooks;
        RemoveOwned(hooks);
        var events = EventDirectoryFor(kind);
        foreach (var name in EventNames(kind))
        {
            var groups = hooks[name] as JsonArray;
            if (hooks[name] is not null && groups is null) throw new InvalidDataException(name + " 배열 형식을 확인하세요.");
            if (groups is null) hooks[name] = groups = new JsonArray();
            var command = "powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -File \"" + script + "\" " + Marker +
                " -EventDirectory \"" + events + "\"";
            var group = new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = 3 }) };
            if (Matcher(kind, name) is { } matcher) group["matcher"] = matcher;
            groups.Add(group);
        }
        Save(path, root);
        return path;
    }
    public static void Uninstall(string home) => Uninstall(home, AgentKind.Codex);
    public static void Uninstall(string home, AgentKind kind)
    {
        var path = Path.Combine(home, AgentAdapters.For(kind).HookConfigFileName);
        if (!File.Exists(path)) return;
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("설정 파일 형식 오류");
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
}
