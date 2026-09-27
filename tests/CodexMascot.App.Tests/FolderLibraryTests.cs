using System.IO;
using System.Text.Json;
using CodexMascot.App;
using CodexMascot.Core;

internal static class FolderLibraryTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public static void Run(Action<bool, string> check, string dir)
    {
        var file = Path.Combine(dir, "folder-library.json");
        var store = new LibraryStore(file);
        var folders = new MascotFolderLibrary(store.LibraryDirectory);
        var manager = new CustomizationManager();
        check(store.Library.FolderLayoutVersion == 1 && Directory.GetDirectories(store.LibraryDirectory).Length == 7, "fresh library stores every installed mascot in its own folder");
        foreach (var m in store.Library.Installed)
        {
            var folder = folders.PackageDirectory(m.Id);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "mascot.json")));
            var root = document.RootElement;
            check(root.GetProperty("format").GetString() == "codex-mascot" && root.GetProperty("version").GetInt32() == 1, "folder manifest declares its format and version");
            var saved = root.GetProperty("mascot").Deserialize<LibraryMascot>(Options)!;
            var media = new[] { saved.CoverImage }.Concat(saved.States.Values.Select(s => s.Image)).ToArray();
            check(media.Length == 7 && media.All(p => p is not null && !Path.IsPathRooted(p) && p.StartsWith("media/") && File.Exists(Path.Combine(folder, p))), "portable manifest references cover plus all six state files inside its folder");
            check(m.States.Values.All(s => !s.Image!.StartsWith("builtin:")) && manager.ResolveAsset(m.CoverImage) is not null, "built-in vector samples are materialized as real PNG assets for folder portability");
        }
        store.Library.Select("bot"); store.Library.Select("bot");
        var first = store.Library.Selected[^2]; var second = store.Library.Selected[^1];
        first.Settings(MascotState.Completed).Volume = .2; second.Settings(MascotState.Completed).Volume = 1.6;
        var folderPath = folders.PackageDirectory("bot");
        var mediaCount = Directory.GetFiles(Path.Combine(folderPath, "media")).Length;
        store.Save();
        check(Directory.GetFiles(Path.Combine(folderPath, "media")).Length == mediaCount && first.CoverImage == second.CoverImage, "duplicate selections reuse installed media without duplicating files");
        var reload = new LibraryStore(file);
        check(reload.Library.Find(first.Id)!.Settings(MascotState.Completed).Volume == .2 && reload.Library.Find(second.Id)!.Settings(MascotState.Completed).Volume == 1.6, "independent selected-instance settings remain in the library index across restart");
        var template = store.Library.Installed.Single(m => m.Id == "bot"); template.Settings(MascotState.Completed).Volume = .6; store.Save();
        using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folderPath, "mascot.json"))))
            check(document.RootElement.GetProperty("mascot").Deserialize<LibraryMascot>(Options)!.Settings(MascotState.Completed).Volume == .6, "installed defaults synchronize into package manifest independently of selected copies");
        var bytesBefore = File.ReadAllText(file);
        var oldImage = template.For(MascotState.Failed).Image;
        template.For(MascotState.Failed).Image = Path.Combine(dir, "missing-folder-media.png");
        var failedSnapshot = store.Snapshot(); var failed = false;
        try { store.Save(); } catch (FileNotFoundException) { failed = true; }
        check(failed && File.ReadAllText(file) == bytesBefore && store.Snapshot() == failedSnapshot, "missing asset cannot partially rewrite the library index or live media paths");
        template.For(MascotState.Failed).Image = oldImage;
        var history = new LibraryHistory(store);
        history.Commit("remove bot", () => { store.Library.Selected.RemoveAll(m => m.SourceId == "bot"); store.Library.Installed.Remove(template); });
        check(Directory.Exists(folderPath) && File.Exists(Path.Combine(folderPath, "mascot.json")), "removing installed mascot preserves its recoverable folder and files");
        history.Undo(); check(store.Library.Find(first.Id) is not null && manager.ResolveAsset(store.Library.Find(first.Id)!.CoverImage) is not null, "undo deletion restores selection and usable folder media");

        var originalImage = manager.ResolveImage(MascotState.Idle)!;
        var legacyFile = Path.Combine(dir, "old-folder-migration.json");
        File.WriteAllText(legacyFile, JsonSerializer.Serialize(new { installed = new[] { new { id = "legacy", name = "기존", media = originalImage, volume = .7 } }, selectedIds = new[] { "legacy", "legacy" } }));
        var beforeMigration = File.ReadAllText(legacyFile);
        var migrated = new LibraryStore(legacyFile);
        check(migrated.Library.FolderLayoutVersion == 1 && migrated.Library.Selected.Count == 2 && migrated.Library.Selected.All(m => m.Volume == .7), "legacy index migrates automatically preserving duplicates and user settings");
        var backups = Directory.GetFiles(dir, "old-folder-migration.json.before-folders-*");
        check(backups.Length == 1 && File.ReadAllText(backups[0]) == beforeMigration && File.Exists(originalImage), "migration backs up original index and leaves original source files intact");
        _ = new LibraryStore(legacyFile);
        check(Directory.GetFiles(dir, "old-folder-migration.json.before-folders-*").Length == 1, "successful migration is not repeated on each startup");
        var legacyFolder = new MascotFolderLibrary(migrated.LibraryDirectory).PackageDirectory("legacy");
        check(Directory.GetFiles(Path.Combine(legacyFolder, "media")).Length == 1, "shared cover/state source is copied only once per package");
        var moved = Path.Combine(dir, "portable-mascot"); Directory.CreateDirectory(moved);
        foreach (var path in Directory.EnumerateFiles(legacyFolder, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(moved, Path.GetRelativePath(legacyFolder, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target);
        }
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(moved, "mascot.json"))))
        {
            var package = manifest.RootElement.GetProperty("mascot").Deserialize<LibraryMascot>(Options)!;
            check(new[] { package.CoverImage }.Concat(package.States.Values.Select(s => s.Image)).All(p => File.Exists(Path.Combine(moved, p!))), "folder is portable without absolute media paths in its manifest");
        }
        check(folders.PackageDirectory("../../escape").StartsWith(store.LibraryDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "untrusted package IDs cannot escape library root");
        var video = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
        if (!string.IsNullOrWhiteSpace(video))
        {
            store.Library.Installed.Single(m => m.Id == "cat").For(MascotState.Completed).Image = video; store.Save();
            var copied = manager.ResolveAsset(store.Library.Installed.Single(m => m.Id == "cat").For(MascotState.Completed).Image)!;
            check(copied.StartsWith(folders.PackageDirectory("cat"), StringComparison.OrdinalIgnoreCase) && Path.GetExtension(copied) == ".mp4" && File.ReadAllBytes(copied).SequenceEqual(File.ReadAllBytes(video)), "MP4 package copying preserves original video and audio bytes");
        }
    }
}
