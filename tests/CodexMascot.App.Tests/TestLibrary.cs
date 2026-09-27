using CodexMascot.App;
using CodexMascot.Core;

// Rich fixtures remain available for regression tests, never as release defaults.
internal static class TestLibrary
{
    internal static LibraryStore Create(string path, GlobalConfiguration? global = null)
    {
        var g = global ?? new();
        var library = new MascotLibrary();
        var installedAt = DateTimeOffset.UtcNow;
        library.Installed.Add(new() { Id = "original", Name = "기존 마스코트", InstalledAt = installedAt, Position = g.Position, MonitorDevice = g.MonitorDevice,
            CustomLeft = g.CustomLeft, CustomTop = g.CustomTop, Events = new() { "idle", "running", "needsAttention", "completed", "failed", "interrupted" } });
        foreach (var (id, name) in new[] { ("bot", "민트 봇"), ("cat", "살구 고양이"), ("ghost", "라일락 유령"), ("star", "레몬 스타"), ("sprout", "새싹 친구"), ("blob", "블루 젤리") })
            library.Installed.Add(new() { Id = id, Name = name, Media = "builtin:" + id, InstalledAt = installedAt,
                States = CustomizationManager.States.ToDictionary(MascotConfiguration.StateKey, s => new LibraryEventMedia { Sound = "builtin-sound:" + id + "/" + MascotConfiguration.StateKey(s) }) });
        library.Select("original");
        return new LibraryStore(path, global, null, library);
    }
}
