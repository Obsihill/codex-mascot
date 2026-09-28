using System.Text.Json;

namespace CodexMascot.Core;

public static class ChatWatchPolicy
{
    public static string Key(AgentKind agent, string id) => agent + ":" + id;
    public static bool Observe(MonitorConfiguration config, AgentKind agent, string? id, string? path)
    {
        if (string.IsNullOrWhiteSpace(id) || ProjectWatchPolicy.Normalize(path) is not { } normalized) return false;
        var known = config.Chats.FirstOrDefault(c => c.Agent == agent && c.Id == id);
        if (known is not null)
        {
            if (known.ProjectPath == normalized) return false;
            known.ProjectPath = normalized; return true;
        }
        config.Chats.Add(new() { Agent = agent, Id = id, ProjectPath = normalized, Enabled = config.AutoIncludeNewChats });
        return true;
    }
    public static bool Allows(MonitorConfiguration config, AgentKind agent, string? id, string? path)
    {
        if (string.IsNullOrWhiteSpace(id) || !ProjectWatchPolicy.Allows(config, path)) return false;
        return config.Chats.FirstOrDefault(c => c.Agent == agent && c.Id == id)?.Enabled ?? config.AutoIncludeNewChats;
    }
    public static IReadOnlyList<ChatWatchEntry> Discover(string home, IAgentAdapter adapter, int limit)
    {
        var result = new Dictionary<string, ChatWatchEntry>(StringComparer.Ordinal);
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        // Codex's title index is optional. Never modify agent-owned data.
        try
        {
            var index = Path.Combine(home, "session_index.jsonl");
            if (File.Exists(index))
            {
                using var stream = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                {
                    try { using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
                        if (Text(r, "id") is { } id && Text(r, "thread_name") is { } title) titles[id] = title;
                    } catch (JsonException) { }
                }
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var root = adapter.SessionsRoot(home);
        if (!Directory.Exists(root)) return Array.Empty<ChatWatchEntry>();
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*.jsonl", new EnumerationOptions
                 { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                 .OrderByDescending(f => f.LastWriteTimeUtc).Take(Math.Clamp(limit, 1, 1000)))
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var lines = new List<string>(); var size = 0;
                for (var i = 0; i < 200 && size < 262144 && reader.ReadLine() is { } line; i++) { lines.Add(line); size += line.Length; }
                var identity = adapter.ReadIdentity(lines);
                if (identity is null || ProjectWatchPolicy.Normalize(identity.Cwd) is not { } path) continue;
                var title = titles.GetValueOrDefault(identity.Id) ?? ReadTitle(lines);
                result.TryAdd(identity.Id, new() { Agent = adapter.Kind, Id = identity.Id, ProjectPath = path, Title = title ?? "" });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result.Values.ToArray();
    }
    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static string? ReadTitle(IEnumerable<string> lines)
    {
        string? prompt = null, title = null;
        foreach (var line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
                title = Text(r, "customTitle") ?? Text(r, "summary") ?? title;
                if (Text(r, "type") == "event_msg" && r.TryGetProperty("payload", out var p) && Text(p, "type") == "user_message") prompt ??= Text(p, "message");
                if (Text(r, "type") == "user" && r.TryGetProperty("message", out var m))
                {
                    prompt ??= Text(m, "content");
                    if (prompt is null && m.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                        prompt = content.EnumerateArray().Select(c => Text(c, "text")).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                }
            } catch (JsonException) { }
        }
        var value = title ?? prompt;
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length > 120 ? value[..120] + "…" : value;
    }
}
