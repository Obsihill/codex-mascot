using System.Text.Json;
using CodexMascot.Core;

namespace CodexMascot.App;

public sealed class LibraryStore
{
    internal const string BaseMascotName = "Base mascot";
    private readonly string _path;
    private readonly MascotFolderLibrary _folders;
    public string LibraryDirectory => _folders.Root;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public MascotLibrary Library { get; private set; }
    public string? Warning { get; }
    internal string UsagePath => Path.ChangeExtension(_path, ".usage.json");
    public LibraryStore(string? path = null, GlobalConfiguration? global = null, MascotConfiguration? configuration = null)
        : this(path, global, configuration, null) { }
    internal LibraryStore(string? path, GlobalConfiguration? global, MascotConfiguration? configuration, MascotLibrary? initialLibrary)
    {
        _path = path ?? Path.Combine(AppPaths.ConfigDirectory, "library.json");
        _folders = new MascotFolderLibrary(path is null ? AppPaths.LibraryDirectory : Path.ChangeExtension(Path.GetFullPath(path), ".packages"));
        try
        {
            if (File.Exists(_path)) Library = JsonSerializer.Deserialize<MascotLibrary>(File.ReadAllText(_path), Options)!;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Warning = Loc.T("라이브러리를 읽지 못해 기본 목록을 불러왔습니다. ") + e.Message;
            if (File.Exists(_path)) File.Copy(_path, _path + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
        }
        Library ??= initialLibrary ?? CreateDefault(global ?? new());
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
            if ((m.SourceId ?? m.Id) == "original" && m.Name is ("기존 마스코트" or "Original mascot"))
                m.Name = BaseMascotName;
            m.Events ??= new();
            m.States ??= new();
            // Expand old single-file entries without losing their selection or options.
            // The built-in samples and the original mascot have separate assets per state.
            var legacy = configuration ?? new MascotConfiguration();
            var builtin = m.Media?.StartsWith("builtin:", StringComparison.Ordinal) == true ? m.Media : null;
            if (Library.FolderLayoutVersion < 1)
                m.CoverImage ??= builtin is not null ? builtin + "/cover" :
                    m.Media is not null && !MascotMedia.IsVideo(m.Media) ? m.Media : legacy.For(MascotState.Idle).Image;
            foreach (var state in CustomizationManager.States)
            {
                var key = MascotConfiguration.StateKey(state);
                if (m.States.TryGetValue(key, out var entry) && entry is not null &&
                    (entry.Image is not null || Library.FolderLayoutVersion >= 1)) continue;
                var original = legacy.For(state);
                m.States[key] = new LibraryEventMedia
                {
                    Image = builtin is not null ? builtin + "/" + key : m.Media ?? original.Image,
                    Sound = entry?.Sound,
                    Sounds = entry?.Sounds ?? new(),
                    SoundEnabled = entry?.SoundEnabled ?? true,
                    SpriteColumns = m.Media is null ? original.SpriteColumns : 1,
                    SpriteRows = m.Media is null ? original.SpriteRows : 1,
                    FrameDurationMs = original.FrameDurationMs
                };
            }
            m.Media = null;
            m.Volume = double.IsFinite(m.Volume) ? Math.Clamp(m.Volume, 0, 2) : 1;
            m.Speed = double.IsFinite(m.Speed) ? Math.Clamp(m.Speed, .25, 3) : 1;
            m.Scale = m.Scale is { } size && double.IsFinite(size) ? Math.Clamp(size, .4, 3) : null;
            foreach (var state in CustomizationManager.States)
            {
                var settings = m.Settings(state);
                settings.HoldUntilClick ??= (m.SourceId ?? m.Id) is "original" or "mascat"
                    && state is MascotState.Completed or MascotState.NeedsAttention or MascotState.Failed;
                if (Library.AudioLayoutVersion < 1)
                {
                    var legacyAudio = configuration ?? new MascotConfiguration();
                    if (m.For(state).SoundCandidates().Count == 0 && !MascotMedia.IsVideo(m.For(state).Image))
                        m.For(state).Sound = legacyAudio.For(state).Sound;
                    settings.Volume *= legacyAudio.Global.SoundEnabled ? legacyAudio.Global.MasterVolume : 0;
                }
                settings.Volume = double.IsFinite(settings.Volume) ? Math.Clamp(settings.Volume, 0, 2) : 1;
                settings.Speed = double.IsFinite(settings.Speed) ? Math.Clamp(settings.Speed, .25, 3) : 1;
                settings.Scale = settings.Scale is { } scale && double.IsFinite(scale) ? Math.Clamp(scale, .4, 3) : null;
                if (settings.ImageDurationMs is { } duration) settings.ImageDurationMs = Math.Clamp(duration, 0, 600000);
                // The sound toggle was removed. Preserve old mutes as editable 0% volume.
                if (!m.For(state).SoundEnabled) { settings.Volume = 0; m.For(state).SoundEnabled = true; }
            }
        }
        if (Library.FolderLayoutVersion < 1 || Library.AudioLayoutVersion < 1)
        {
            try
            {
                if (File.Exists(_path)) File.Copy(_path, _path + ".before-folders-" + Guid.NewGuid().ToString("N"), false);
                Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { Warning = (Warning is null ? "" : Warning + "\n") + Loc.T("폴더 라이브러리 전환을 완료하지 못했습니다. 기존 목록을 사용합니다. ") + e.Message; }
        }
    }
    public void Save()
    {
        // Work on a copy so failed media imports cannot rewrite the live model or index.
        var prepared = JsonSerializer.Deserialize<MascotLibrary>(Snapshot(), Options)!;
        _folders.Save(prepared); prepared.FolderLayoutVersion = 1; prepared.AudioLayoutVersion = 1;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(prepared, Options));
        File.Move(temp, _path, true);
        Library.FolderLayoutVersion = 1;
        Library.AudioLayoutVersion = 1;
        foreach (var (entry, saved) in Library.Installed.Concat(Library.Selected).Zip(prepared.Installed.Concat(prepared.Selected)))
        {
            entry.CoverImage = saved.CoverImage;
            foreach (var (state, media) in entry.States)
            { media.Image = saved.States[state].Image; media.Sound = saved.States[state].Sound; media.Sounds = saved.States[state].Sounds; }
        }
    }
    internal string Snapshot() => JsonSerializer.Serialize(Library, Options);
    internal void Restore(string snapshot) => Library = JsonSerializer.Deserialize<MascotLibrary>(snapshot, Options)!;
    private static MascotLibrary CreateDefault(GlobalConfiguration g)
    {
        var library = new MascotLibrary { AudioLayoutVersion = 1 };
        var installedAt = DateTimeOffset.UtcNow;
        library.Installed.Add(new() { Id = "original", Name = BaseMascotName, InstalledAt = installedAt, Position = g.Position, MonitorDevice = g.MonitorDevice,
            CustomLeft = g.CustomLeft, CustomTop = g.CustomTop, CoverImage = "assets/images/main.png", States = ImageStates("assets/images"),
            Events = new() { "idle", "running", "needsAttention", "completed", "failed", "interrupted" } });
        library.Installed.Add(new() { Id = "mascat", Name = "MasCat", InstalledAt = installedAt,
            CoverImage = "assets/MasCat/main.png", States = ImageStates("assets/MasCat") });
        var mascat = library.Installed.Single(m => m.Id == "mascat");
        foreach (var mascot in library.Installed)
            foreach (var state in CustomizationManager.States)
                mascot.Settings(state).HoldUntilClick = state is MascotState.Completed or MascotState.NeedsAttention or MascotState.Failed;
        foreach (var state in CustomizationManager.States) mascat.Settings(state).ImageDurationMs = 2000;
        foreach (var state in new[] { MascotState.Completed, MascotState.Failed, MascotState.NeedsAttention })
            mascat.For(state).Sounds = Enumerable.Range(1, 7).Select(i => $"assets/MasCat/audio/meow_{i:00}.wav").ToList();
        library.Select("mascat");
        return library;
    }
    private static Dictionary<string, LibraryEventMedia> ImageStates(string folder) => CustomizationManager.States.ToDictionary(
        MascotConfiguration.StateKey, state => new LibraryEventMedia
        { Image = folder + "/" + (state == MascotState.NeedsAttention ? "attention" : MascotConfiguration.StateKey(state)) + ".png" });
    public string? MediaPath(LibraryMascot m, CustomizationManager manager, MascotState state)
        => Resolve(m.For(state).Image, manager);
    public string? CoverPath(LibraryMascot m, CustomizationManager manager) => Resolve(m.CoverImage, manager);
    private static string? Resolve(string? path, CustomizationManager manager)
        => path?.StartsWith("builtin:", StringComparison.Ordinal) == true ? path : manager.ResolveAsset(path);
    public static GlobalConfiguration Placement(LibraryMascot mascot, GlobalConfiguration global, MascotState? state = null)
    {
        var placement = state is { } s ? mascot.Settings(s) : new MascotPlaybackSettings { Scale = mascot.Scale, Position = mascot.Position, MonitorDevice = mascot.MonitorDevice, CustomLeft = mascot.CustomLeft, CustomTop = mascot.CustomTop };
        return new()
    {
        Scale = placement.Scale ?? global.Scale, Position = placement.Position, MonitorDevice = placement.MonitorDevice,
        CustomLeft = placement.CustomLeft, CustomTop = placement.CustomTop, AlwaysOnTop = global.AlwaysOnTop,
        HideBehindTaskbar = placement.HideBehindTaskbar,
        ClickThrough = false, KeepCompletedVisibleUntilClick = global.KeepCompletedVisibleUntilClick,
        BringCodexToFrontOnClick = global.BringCodexToFrontOnClick, SoundEnabled = true,
        MasterVolume = 1, ShowIdle = global.ShowIdle
    };
    }
    public static StateConfiguration Playback(LibraryMascot mascot, MascotState state, StateConfiguration original) => new()
    {
        HoldUntilClick = mascot.Settings(state).HoldUntilClick ?? false,
        Image = mascot.For(state).Image, Sound = mascot.For(state).SoundEnabled ? mascot.For(state).Sound : null,
        ShowDurationMs = !MascotMedia.IsVideo(mascot.For(state).Image) && mascot.Settings(state).ImageDurationMs is { } duration
            ? Math.Clamp(duration, 0, 600000) : original.ShowDurationMs,
        Volume = mascot.For(state).SoundEnabled ? mascot.Settings(state).Volume : 0, PlaybackSpeed = mascot.Settings(state).Speed, Loop = mascot.Settings(state).Loop,
        SpriteColumns = mascot.For(state).SpriteColumns,
        SpriteRows = mascot.For(state).SpriteRows, FrameDurationMs = mascot.For(state).FrameDurationMs
    };
    internal static string? ChooseSound(LibraryMascot mascot, MascotState state, Random? random = null)
    {
        var media = mascot.For(state);
        if (!media.SoundEnabled) return null;
        var candidates = media.SoundCandidates();
        return candidates.Count == 0 ? null : candidates[(random ?? Random.Shared).Next(candidates.Count)];
    }
}
