using System.Text.Json;
using CodexMascot.Core;

namespace CodexMascot.App;

// Validation is read-only. The dashboard's undoable registration copies files
// into the managed library using a fresh identity, never overwriting a package.
internal static class MascotFolderImport
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, MaxDepth = 32 };
    internal static LibraryMascot Read(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Directory.Exists(full) ? full : Path.GetDirectoryName(full)!;
        if (!Directory.Exists(full) && !Path.GetFileName(full).Equals("mascot.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Loc.T("마스코트 폴더 또는 mascot.json을 선택하세요."));
        var manifest = Path.Combine(root, "mascot.json");
        if (!File.Exists(manifest)) throw new FileNotFoundException(Loc.T("선택한 폴더에 mascot.json이 없습니다."));
        RejectLink(root); RejectLink(manifest);
        if (new FileInfo(manifest).Length > 1024 * 1024) throw new InvalidDataException(Loc.T("mascot.json은 1MB 이하만 지원합니다."));
        LibraryMascot mascot;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest), new JsonDocumentOptions { MaxDepth = 32 });
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != "codex-mascot" ||
                !json.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
                !json.TryGetProperty("mascot", out var model) || model.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(Loc.T("지원하는 마스코트 라이브러리 형식이 아닙니다. (codex-mascot, 버전 1)"));
            mascot = model.Deserialize<LibraryMascot>(Options) ?? throw new InvalidDataException(Loc.T("마스코트 정보가 없습니다."));
        }
        catch (JsonException e) { throw new InvalidDataException(Loc.T("mascot.json을 읽을 수 없습니다. 파일 형식을 확인하세요."), e); }
        if (string.IsNullOrWhiteSpace(mascot.Name) || mascot.Name.Length > 200) throw new InvalidDataException(Loc.T("마스코트 이름은 1~200자로 입력해야 합니다."));
        mascot.Name = mascot.Name.Trim(); mascot.Id = Guid.NewGuid().ToString("N");
        mascot.SourceId = null; mascot.Media = null; mascot.InstalledAt = DateTimeOffset.UtcNow;
        mascot.CoverImage = string.IsNullOrWhiteSpace(mascot.CoverImage) ? null : ResolveMedia(root, mascot.CoverImage, Loc.T("대표 이미지"), true);
        var keys = CustomizationManager.States.Select(MascotConfiguration.StateKey).ToHashSet(StringComparer.Ordinal);
        mascot.States = (mascot.States ?? new()).Where(p => keys.Contains(p.Key) && p.Value is not null)
            .ToDictionary(p => p.Key, p => p.Value);
        foreach (var key in keys) mascot.States.TryAdd(key, new LibraryEventMedia());
        mascot.Events = (mascot.Events ?? new()).Where(keys.Contains).Distinct().ToList();
        mascot.Volume = Clamp(mascot.Volume, 0, 2, 1); mascot.Speed = Clamp(mascot.Speed, .25, 3, 1);
        mascot.Scale = mascot.Scale is { } scale ? Clamp(scale, .4, 3, 1) : null;
        mascot.CustomLeft = Coordinate(mascot.CustomLeft); mascot.CustomTop = Coordinate(mascot.CustomTop);
        foreach (var state in CustomizationManager.States)
        {
            var media = mascot.For(state);
            media.Image = string.IsNullOrWhiteSpace(media.Image) ? null : ResolveMedia(root, media.Image, MainWindow.StateName(state), false);
            if (!string.IsNullOrWhiteSpace(media.Sound)) media.Sound = ResolveMedia(root, media.Sound, MainWindow.StateName(state) + Loc.T(" 소리"), false, true);
            else media.Sound = null;
            media.Sounds = (media.Sounds ?? new()).Select(s => ResolveMedia(root, s, MainWindow.StateName(state) + Loc.T(" 소리"), false, true)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            media.SpriteColumns = Math.Clamp(media.SpriteColumns, 1, 32); media.SpriteRows = Math.Clamp(media.SpriteRows, 1, 32);
            media.FrameDurationMs = Math.Clamp(media.FrameDurationMs, 20, 5000);
            var settings = mascot.Settings(state);
            settings.Volume = media.SoundEnabled ? Clamp(settings.Volume, 0, 2, 1) : 0; media.SoundEnabled = true;
            settings.Speed = Clamp(settings.Speed, .25, 3, 1);
            settings.Scale = settings.Scale is { } stateScale ? Clamp(stateScale, .4, 3, 1) : null;
            if (settings.ImageDurationMs is { } duration) settings.ImageDurationMs = Math.Clamp(duration, 0, 600000);
            settings.CustomLeft = Coordinate(settings.CustomLeft); settings.CustomTop = Coordinate(settings.CustomTop);
        }
        return mascot;
    }
    private static double Clamp(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private static double? Coordinate(double? value) => value is { } n && double.IsFinite(n) ? Math.Clamp(n, int.MinValue, int.MaxValue) : null;
    private static string ResolveMedia(string root, string? relative, string label, bool cover, bool sound = false)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException(label + Loc.T(": 폴더 안의 상대 경로가 필요합니다."));
        var normalized = relative.Replace('\\', '/');
        if (normalized.Split('/').Any(part => part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException(label + Loc.T(": 허용되지 않는 파일 경로입니다."));
        var path = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(label + Loc.T(": 마스코트 폴더 밖의 파일은 불러올 수 없습니다."));
        if (!File.Exists(path)) throw new FileNotFoundException(label + Loc.T(": 파일을 찾을 수 없습니다. (") + relative + ")");
        var current = root;
        foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)) { current = Path.Combine(current, part); RejectLink(current); }
        if (sound) { MascotPackageEditor.ValidateSound(path); return path; }
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".gif" or ".webp") && (cover || !MascotMedia.IsVideo(path)))
            throw new InvalidDataException(label + Loc.T(": 지원하지 않는 이미지·영상 형식입니다."));
        MascotPackageEditor.ValidateMedia(path, cover);
        return path;
    }
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(Loc.T("바로가기·심볼릭 링크 대신 실제 마스코트 폴더와 파일을 선택하세요."));
    }
}
