using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexMascot.Core;

namespace CodexMascot.App;

// Each installed package owns its media and a portable, relative-path manifest.
// The library index remains authoritative for selections and independent instance settings.
internal sealed class MascotFolderLibrary
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public string Root { get; }
    public MascotFolderLibrary(string root) => Root = Path.GetFullPath(root);
    internal string PackageDirectory(string id)
    {
        var safe = id.Length is > 0 and <= 80 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? id : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        return Path.Combine(Root, "mascot-" + safe);
    }
    internal void Save(MascotLibrary library)
    {
        var manifests = new List<(string Path, string Json)>();
        foreach (var mascot in library.Installed)
        {
            var folder = PackageDirectory(mascot.Id);
            var imported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in new[] { mascot }.Concat(library.Selected.Where(m => m.SourceId == mascot.Id)))
            {
                entry.CoverImage = CopyMedia(entry.CoverImage, "cover", folder, imported);
                foreach (var (state, media) in entry.States)
                    media.Image = CopyMedia(media.Image, SafeSlot(state), folder, imported);
            }
            var portable = JsonSerializer.Deserialize<LibraryMascot>(JsonSerializer.Serialize(mascot))!;
            portable.SourceId = null;
            portable.CoverImage = RelativeMedia(portable.CoverImage, folder);
            foreach (var media in portable.States.Values) media.Image = RelativeMedia(media.Image, folder);
            manifests.Add((Path.Combine(folder, "mascot.json"), JsonSerializer.Serialize(new { format = "codex-mascot", version = 1, mascot = portable }, Options)));
        }
        // Do not replace manifests until every referenced asset has been copied.
        foreach (var (path, json) in manifests) WriteJson(path, json);
    }
    internal LibraryMascot SavePackage(LibraryMascot mascot)
    {
        var copy = JsonSerializer.Deserialize<LibraryMascot>(JsonSerializer.Serialize(mascot))!;
        Save(new MascotLibrary { Installed = new() { copy } });
        return copy;
    }
    private static string SafeSlot(string value) => value.All(char.IsAsciiLetterOrDigit) && value.Length is > 0 and < 40 ? value : "media";
    private static string? CopyMedia(string? source, string slot, string folder, Dictionary<string, string> imported)
    {
        if (string.IsNullOrWhiteSpace(source)) return source;
        var builtin = source.StartsWith("builtin:", StringComparison.Ordinal);
        var full = builtin ? source : Resolve(source);
        if (imported.TryGetValue(full, out var cached)) return cached;
        if (!builtin && !File.Exists(full)) throw new FileNotFoundException("마스코트 파일을 찾을 수 없습니다. 기존 파일과 설정은 유지됩니다.", full);
        if (!builtin && full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return imported[full] = AssetPath(full);

        byte[]? png = builtin ? RenderBuiltin(source[8..]) : null;
        string hash;
        if (png is not null) hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        else { using var stream = File.OpenRead(full); hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
        var extension = builtin ? ".png" : Path.GetExtension(full).ToLowerInvariant();
        var mediaFolder = Path.Combine(folder, "media"); Directory.CreateDirectory(mediaFolder);
        var destination = Path.Combine(mediaFolder, slot + "-" + hash + extension);
        if (!File.Exists(destination))
        {
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (png is not null) File.WriteAllBytes(temp, png); else File.Copy(full, temp, false);
                File.Move(temp, destination, false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return imported[full] = AssetPath(destination);
    }
    private static string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppPaths.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
    private static string AssetPath(string path) => path.StartsWith(AppPaths.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        ? Path.GetRelativePath(AppPaths.BaseDirectory, path).Replace('\\', '/') : path;
    private static string? RelativeMedia(string? path, string folder) => string.IsNullOrWhiteSpace(path) ? path : Path.GetRelativePath(folder, Resolve(path)).Replace('\\', '/');
    private static byte[] RenderBuiltin(string key)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawImage(DemoMascotArtwork.Create(key), new Rect(0, 0, 400, 360));
        var bitmap = new RenderTargetBitmap(400, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
    private static void WriteJson(string path, string json)
    {
        if (File.Exists(path) && File.ReadAllText(path) == json) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, json); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
