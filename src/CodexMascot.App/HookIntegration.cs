using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexMascot.App;

public static class HookIntegration
{
    public static string EventDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMascot", "events");
    public static string DefaultCodexHome => Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private const string Marker = "-MascotBridge";
    public static string Install(string codexHome)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("integration 폴더가 없습니다. ZIP 전체를 풀어 주세요.", script);
        Directory.CreateDirectory(codexHome);
        var path = Path.Combine(codexHome, "hooks.json");
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
        if (root is null) throw new InvalidDataException("기존 hooks.json을 읽을 수 없습니다. 원본은 변경하지 않았습니다.");
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        if (root["hooks"] is not null && root["hooks"] is not JsonObject) throw new InvalidDataException("hooks 형식이 올바르지 않습니다.");
        if (root["hooks"] is null) root["hooks"] = hooks;
        RemoveOwned(hooks);
        foreach (var name in new[] { "UserPromptSubmit", "PermissionRequest", "PreToolUse", "PostToolUse", "Stop", "Interrupt", "SessionEnd" })
        {
            var groups = hooks[name] as JsonArray;
            if (hooks[name] is not null && groups is null) throw new InvalidDataException(name + " 배열 형식을 확인하세요.");
            if (groups is null) hooks[name] = groups = new JsonArray();
            var command = "powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -File \"" + script + "\" " + Marker;
            var group = new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = 3 }) };
            if (name == "PreToolUse") group["matcher"] = ".*request_user_input.*";
            groups.Add(group);
        }
        Save(path, root);
        return path;
    }
    public static void Uninstall(string codexHome)
    {
        var path = Path.Combine(codexHome, "hooks.json");
        if (!File.Exists(path)) return;
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("hooks.json 형식 오류");
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
