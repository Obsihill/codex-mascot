using System.Text.Json;
using CodexMascot.Core;

namespace CodexMascot.App;

public sealed class LibraryStore
{
    private readonly string _path;
    private readonly MascotFolderLibrary _folders;
    public string LibraryDirectory => _folders.Root;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public MascotLibrary Library { get; private set; }
    public string? Warning { get; }
    internal string UsagePath => Path.ChangeExtension(_path, ".usage.json");
    public LibraryStore(string? path = null, GlobalConfiguration? global = null, MascotConfiguration? configuration = null)
    {
        _path = path ?? Path.Combine(AppPaths.ConfigDirectory, "library.json");
        _folders = new MascotFolderLibrary(path is null ? AppPaths.LibraryDirectory : Path.ChangeExtension(Path.GetFullPath(path), ".packages"));
        try
        {
            if (File.Exists(_path)) Library = JsonSerializer.Deserialize<MascotLibrary>(File.ReadAllText(_path), Options)!;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Warning = "라이브러리를 읽지 못해 기본 목록을 불러왔습니다. " + e.Message;
            if (File.Exists(_path)) File.Copy(_path, _path + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
        }
        Library ??= CreateDefault(global ?? new());
        if (Library.InstalledSort is not ("name" or "installed" or "preference")) Library.InstalledSort = "installed";
        Library.InstalledPanelRatio = double.IsFinite(Library.InstalledPanelRatio) ? Math.Clamp(Library.InstalledPanelRatio, .2, .8) : 4.0 / 7;
        Library.Installed ??= new(); Library.Selected ??= new();
        Library.Installed = Library.Installed.Where(m => m is not null).DistinctBy(m => m.Id).ToList();
        if (Library.SelectedIds is not null)
        {
            foreach (var id in Library.SelectedIds) Library.Select(id);
            Library.SelectedIds = null;
        }
        Library.Selected = Library.Selected.Where(m => m is not null && Library.Installed.Any(p => p.Id == m.SourceId)).DistinctBy(m => m.Id).ToList();
        foreach (var m in Library.Installed.Concat(Library.Selected))
        {
            m.Events ??= new();
            m.States ??= new();
            // Expand old single-file entries without losing their selection or options.
            // The built-in samples and the original mascot have separate assets per state.
            var legacy = configuration ?? new MascotConfiguration();
            var builtin = m.Media?.StartsWith("builtin:", StringComparison.Ordinal) == true ? m.Media : null;
            m.CoverImage ??= builtin is not null ? builtin + "/cover" :
                m.Media is not null && !MascotMedia.IsVideo(m.Media) ? m.Media : legacy.For(MascotState.Idle).Image;
            foreach (var state in CustomizationManager.States)
            {
                var key = MascotConfiguration.StateKey(state);
                if (m.States.TryGetValue(key, out var entry) && entry?.Image is not null) continue;
                var original = legacy.For(state);
                m.States[key] = new LibraryEventMedia
                {
                    Image = builtin is not null ? builtin + "/" + key : m.Media ?? original.Image,
                    SoundEnabled = entry?.SoundEnabled ?? true,
                    SpriteColumns = m.Media is null ? original.SpriteColumns : 1,
                    SpriteRows = m.Media is null ? original.SpriteRows : 1,
                    FrameDurationMs = original.FrameDurationMs
                };
            }
            m.Media = null;
            m.Volume = double.IsFinite(m.Volume) ? Math.Clamp(m.Volume, 0, 2) : 1;
            m.Speed = double.IsFinite(m.Speed) ? Math.Clamp(m.Speed, .25, 3) : 1;
            foreach (var state in CustomizationManager.States)
            {
                var settings = m.Settings(state);
                settings.Volume = double.IsFinite(settings.Volume) ? Math.Clamp(settings.Volume, 0, 2) : 1;
                settings.Speed = double.IsFinite(settings.Speed) ? Math.Clamp(settings.Speed, .25, 3) : 1;
                if (settings.ImageDurationMs is { } duration) settings.ImageDurationMs = Math.Clamp(duration, 0, 600000);
                // The sound toggle was removed. Preserve old mutes as editable 0% volume.
                if (!m.For(state).SoundEnabled) { settings.Volume = 0; m.For(state).SoundEnabled = true; }
            }
        }
        if (Library.FolderLayoutVersion < 1)
        {
            try
            {
                if (File.Exists(_path)) File.Copy(_path, _path + ".before-folders-" + Guid.NewGuid().ToString("N"), false);
                Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { Warning = (Warning is null ? "" : Warning + "\n") + "폴더 라이브러리 전환을 완료하지 못했습니다. 기존 목록을 사용합니다. " + e.Message; }
        }
    }
    public void Save()
    {
        // Work on a copy so failed media imports cannot rewrite the live model or index.
        var prepared = JsonSerializer.Deserialize<MascotLibrary>(Snapshot(), Options)!;
        _folders.Save(prepared); prepared.FolderLayoutVersion = 1;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(prepared, Options));
        File.Move(temp, _path, true);
        Library.FolderLayoutVersion = 1;
        foreach (var (entry, saved) in Library.Installed.Concat(Library.Selected).Zip(prepared.Installed.Concat(prepared.Selected)))
        {
            entry.CoverImage = saved.CoverImage;
            foreach (var (state, media) in entry.States) media.Image = saved.States[state].Image;
        }
    }
    internal string Snapshot() => JsonSerializer.Serialize(Library, Options);
    internal void Restore(string snapshot) => Library = JsonSerializer.Deserialize<MascotLibrary>(snapshot, Options)!;
    private static MascotLibrary CreateDefault(GlobalConfiguration g)
    {
        var library = new MascotLibrary();
        var installedAt = DateTimeOffset.UtcNow;
        library.Installed.Add(new() { Id = "original", Name = "기존 마스코트", InstalledAt = installedAt, Position = g.Position, MonitorDevice = g.MonitorDevice,
            CustomLeft = g.CustomLeft, CustomTop = g.CustomTop, Events = new() { "idle", "running", "needsAttention", "completed", "failed", "interrupted" } });
        foreach (var (id, name) in new[] { ("bot", "민트 봇"), ("cat", "살구 고양이"), ("ghost", "라일락 유령"), ("star", "레몬 스타"), ("sprout", "새싹 친구"), ("blob", "블루 젤리") })
            library.Installed.Add(new() { Id = id, Name = name, Media = "builtin:" + id, InstalledAt = installedAt });
        library.Select("original");
        return library;
    }
    public string? MediaPath(LibraryMascot m, CustomizationManager manager, MascotState state)
        => Resolve(m.For(state).Image, manager);
    public string? CoverPath(LibraryMascot m, CustomizationManager manager) => Resolve(m.CoverImage, manager);
    private static string? Resolve(string? path, CustomizationManager manager)
        => path?.StartsWith("builtin:", StringComparison.Ordinal) == true ? path : manager.ResolveAsset(path);
    public static GlobalConfiguration Placement(LibraryMascot mascot, GlobalConfiguration global, MascotState? state = null)
    {
        var placement = state is { } s ? mascot.Settings(s) : new MascotPlaybackSettings { Position = mascot.Position, MonitorDevice = mascot.MonitorDevice, CustomLeft = mascot.CustomLeft, CustomTop = mascot.CustomTop };
        return new()
    {
        Scale = global.Scale, Position = placement.Position, MonitorDevice = placement.MonitorDevice,
        CustomLeft = placement.CustomLeft, CustomTop = placement.CustomTop, AlwaysOnTop = global.AlwaysOnTop,
        ClickThrough = global.ClickThrough, KeepCompletedVisibleUntilClick = global.KeepCompletedVisibleUntilClick,
        BringCodexToFrontOnClick = global.BringCodexToFrontOnClick, SoundEnabled = global.SoundEnabled,
        MasterVolume = global.MasterVolume, ShowIdle = global.ShowIdle
    };
    }
    public static StateConfiguration Playback(LibraryMascot mascot, MascotState state, StateConfiguration original) => new()
    {
        Image = mascot.For(state).Image, Sound = mascot.For(state).SoundEnabled ? original.Sound : null,
        ShowDurationMs = !MascotMedia.IsVideo(mascot.For(state).Image) && mascot.Settings(state).ImageDurationMs is { } duration
            ? Math.Clamp(duration, 0, 600000) : original.ShowDurationMs,
        Volume = mascot.For(state).SoundEnabled ? mascot.Settings(state).Volume : 0, PlaybackSpeed = mascot.Settings(state).Speed, Loop = mascot.Settings(state).Loop,
        SpriteColumns = mascot.For(state).SpriteColumns,
        SpriteRows = mascot.For(state).SpriteRows, FrameDurationMs = mascot.For(state).FrameDurationMs
    };
}
