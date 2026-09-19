using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using CodexMascot.Core;
namespace CodexMascot.App;

public sealed class CustomizationManager
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public MascotConfiguration Configuration { get; private set; } = new();
    public string? LoadWarning { get; private set; }
    public CustomizationManager() { AppPaths.EnsureFolders(); Load(); }
    public void Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
                Configuration = JsonSerializer.Deserialize<MascotConfiguration>(File.ReadAllText(AppPaths.ConfigFile), Options) ?? new();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            LoadWarning = "설정 파일을 읽지 못해 기본값을 사용합니다: " + e.Message;
            if (File.Exists(AppPaths.ConfigFile)) File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"), false);
            Configuration = new();
        }
        Validate();
        Save();
    }
    private void Validate()
    {
        Configuration.Global ??= new(); Configuration.Monitor ??= new();
        Configuration.Monitor.RecentProjects ??= new();
        Configuration.States = new Dictionary<string, StateConfiguration>(Configuration.States ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (var state in States)
        {
            var key = MascotConfiguration.StateKey(state);
            if (!Configuration.States.TryGetValue(key, out var cfg) || cfg is null)
                Configuration.States[key] = cfg = new MascotConfiguration().For(state);
            cfg.ShowDurationMs = Math.Clamp(cfg.ShowDurationMs, 0, 600000);
            cfg.Volume = Math.Clamp(cfg.Volume, 0, 1);
            cfg.SpriteColumns = Math.Clamp(cfg.SpriteColumns, 1, 32); cfg.SpriteRows = Math.Clamp(cfg.SpriteRows, 1, 32);
            cfg.FrameDurationMs = Math.Clamp(cfg.FrameDurationMs, 20, 5000);
        }
        Configuration.Global.MasterVolume = Math.Clamp(Configuration.Global.MasterVolume, 0, 1);
        Configuration.Global.Scale = Math.Clamp(Configuration.Global.Scale, .4, 3);
    }
    public static readonly MascotState[] States = { MascotState.Idle, MascotState.Running, MascotState.NeedsAttention, MascotState.Completed, MascotState.Failed, MascotState.Interrupted };
    public void Save()
    {
        Validate();
        var temp = AppPaths.ConfigFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Configuration, Options));
        File.Move(temp, AppPaths.ConfigFile, true);
    }
    public string? ResolveAsset(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var full = Path.IsPathRooted(path) ? path : Path.Combine(AppPaths.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? full : null;
    }
    public string? ResolveImage(MascotState state) => ResolveAsset(Configuration.For(state).Image)
        ?? ResolveAsset(new MascotConfiguration().For(state).Image);
    public string ImportAsset(string file, bool sound)
    {
        var name = Path.GetFileNameWithoutExtension(file) + "-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(file).ToLowerInvariant();
        var relative = "assets/" + (sound ? "sounds/" : "images/") + name;
        File.Copy(file, Path.Combine(AppPaths.BaseDirectory, relative), false);
        return relative;
    }
    public void OpenAssetsFolder() => Process.Start(new ProcessStartInfo(AppPaths.AssetsDirectory) { UseShellExecute = true });
    public void Reset()
    {
        var monitor = Configuration.Monitor;
        var startup = Configuration.Global.StartWithWindows;
        Configuration = new MascotConfiguration { Monitor = monitor };
        Configuration.Global.StartWithWindows = startup;
        Save();
    }
    public void Export(string zipPath)
    {
        var copy = JsonSerializer.Deserialize<MascotConfiguration>(JsonSerializer.Serialize(Configuration, Options), Options)!;
        copy.Monitor = new(); copy.Global.StartWithWindows = false;
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var state in States)
        {
            var cfg = copy.For(state);
            foreach (var sound in new[] { false, true })
            {
                var source = ResolveAsset(sound ? cfg.Sound : cfg.Image);
                if (source is null) continue;
                var target = "assets/" + (sound ? "sounds/" : "images/") + MascotConfiguration.StateKey(state) + Path.GetExtension(source);
                zip.CreateEntryFromFile(source, target);
                if (sound) cfg.Sound = target; else cfg.Image = target;
            }
        }
        using var writer = new StreamWriter(zip.CreateEntry("mascot.json").Open());
        writer.Write(JsonSerializer.Serialize(copy, Options));
    }
    public void ImportTheme(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var configEntry = zip.GetEntry("mascot.json") ?? throw new InvalidDataException("mascot.json이 없는 테마입니다.");
        if (configEntry.Length > 1024 * 1024) throw new InvalidDataException("테마 설정이 너무 큽니다.");
        using var reader = new StreamReader(configEntry.Open());
        var theme = JsonSerializer.Deserialize<MascotConfiguration>(reader.ReadToEnd(), Options) ?? throw new InvalidDataException("테마 형식 오류");
        var targetRoot = Path.Combine(AppPaths.AssetsDirectory, "themes", Guid.NewGuid().ToString("N"));
        var total = 0L;
        foreach (var cfg in (theme.States ?? new()).Values)
        {
            foreach (var sound in new[] { false, true })
            {
                var name = sound ? cfg.Sound : cfg.Image;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var entry = zip.GetEntry(name.Replace('\\', '/')) ?? throw new InvalidDataException("테마 에셋 누락: " + name);
                total += entry.Length;
                if (entry.Length > 50 * 1024 * 1024 || total > 200 * 1024 * 1024) throw new InvalidDataException("테마 파일이 너무 큽니다.");
                var target = Path.GetFullPath(Path.Combine(targetRoot, name));
                if (!target.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("허용되지 않는 테마 경로");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target)) entry.ExtractToFile(target);
                var relative = Path.GetRelativePath(AppPaths.BaseDirectory, target).Replace('\\', '/');
                if (sound) cfg.Sound = relative; else cfg.Image = relative;
            }
        }
        theme.Monitor = Configuration.Monitor;
        theme.Global ??= new();
        theme.Global.StartWithWindows = Configuration.Global.StartWithWindows;
        Configuration = theme; Save();
    }
}
