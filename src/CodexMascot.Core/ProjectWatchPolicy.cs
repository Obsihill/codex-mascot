namespace CodexMascot.Core;

public static class ProjectWatchPolicy
{
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = path.Trim();
            if (!Path.IsPathRooted(path)) return null;
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return null; }
    }
    public static bool Observe(MonitorConfiguration config, string? project)
    {
        var path = Normalize(project);
        if (path is null || config.Projects.Any(p => string.Equals(Normalize(p.Path), path, StringComparison.OrdinalIgnoreCase))) return false;
        config.Projects.Add(new() { Path = path, Enabled = config.AutoIncludeNewProjects }); return true;
    }
    public static bool Allows(MonitorConfiguration config, string? project)
    {
        var path = Normalize(project);
        if (path is null) return false;
        return config.Projects.FirstOrDefault(p => string.Equals(Normalize(p.Path), path, StringComparison.OrdinalIgnoreCase))?.Enabled
            ?? config.AutoIncludeNewProjects;
    }
    // Read only transcript headers, including while monitoring is paused.
    public static IReadOnlyList<string> Discover(string home, IAgentAdapter adapter, int limit)
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = adapter.SessionsRoot(home);
        if (!Directory.Exists(root)) return Array.Empty<string>();
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*.jsonl", new EnumerationOptions
                 { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                 .OrderByDescending(f => f.LastWriteTimeUtc).Take(Math.Clamp(limit, 1, 1000)))
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var lines = new List<string>();
                for (var i = 0; i < adapter.HeadLines && reader.ReadLine() is { } line; i++) lines.Add(line);
                if (Normalize(adapter.ReadIdentity(lines)?.Cwd) is { } path) projects.Add(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        return projects.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
