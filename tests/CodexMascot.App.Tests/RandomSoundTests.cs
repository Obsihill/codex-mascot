using System.IO;
using System.Text.Json.Nodes;
using CodexMascot.App;
using CodexMascot.Core;

internal static class RandomSoundTests
{
    internal static void Run(Action<bool, string> check, string dir)
    {
        var file = Path.Combine(dir, "random-sounds.json");
        var store = new LibraryStore(file); var manager = new CustomizationManager();
        var mascot = store.Library.Installed.Single(m => m.Id == "mascat");
        var files = mascot.For(MascotState.Completed).SoundCandidates();
        foreach (var path in files) { MascotPackageEditor.ValidateSound(manager.ResolveAsset(path)!); check(true, "provided meow WAV decodes successfully"); }
        var before = store.Snapshot(); var rng = new Random(13);
        var drawn = Enumerable.Range(0, 700).Select(_ => LibraryStore.ChooseSound(mascot, MascotState.Completed, rng)).ToArray();
        check(drawn.All(s => files.Contains(s!)) && drawn.Distinct().Count() == 7, "random selection can choose all seven sounds and only members of the pool");
        check(store.Snapshot() == before, "random draws never overwrite stored sound choices");
        check(LibraryStore.ChooseSound(mascot, MascotState.Idle) is null && LibraryStore.ChooseSound(mascot, MascotState.Running) is null, "silent states remain silent");
        store.Library.Select("mascat"); store.Save();
        store.Library.Selected[0].For(MascotState.Completed).Sounds.Clear();
        check(store.Library.Selected[1].For(MascotState.Completed).Sounds.Count == 7 && mascot.For(MascotState.Completed).Sounds.Count == 7, "duplicate selections have independent random pools");
        var folders = new MascotFolderLibrary(store.LibraryDirectory);
        var imported = MascotFolderImport.Read(folders.PackageDirectory("mascat"));
        check(imported.For(MascotState.Completed).Sounds.Count == 7 && imported.For(MascotState.Completed).Sounds.All(File.Exists), "portable import resolves all random sound files");
        check(Directory.GetFiles(Path.Combine(folders.PackageDirectory("mascat"), "media"), "*.wav").Length == 7, "three state pools and duplicates share seven copied files, not twenty-one");
        check(new LibraryStore(file).Library.Installed.Single(m => m.Id == "mascat").For(MascotState.Failed).Sounds.Count == 7, "random pools survive restart");
        var paths = CustomizationManager.States.ToDictionary(MascotConfiguration.StateKey, s => mascot.For(s).Image);
        paths["cover"] = mascot.CoverImage;
        var choices = CustomizationManager.States.ToDictionary(s => MascotConfiguration.StateKey(s) + ".sound", s => mascot.For(s).SoundCandidates().ToArray());
        var package = MascotPackageEditor.BuildPackage("랜덤 소리", paths, new HashSet<string>(), manager, soundChoices: choices);
        check(package.For(MascotState.Completed).Sounds.Count == 7 && package.For(MascotState.Completed).Sounds.All(s => manager.ResolveAsset(s) is not null), "manual registration includes every multi-selected sound");
        mascot.For(MascotState.Completed).Sounds = new(); mascot.For(MascotState.Completed).Sound = "single.wav";
        check(LibraryStore.ChooseSound(mascot, MascotState.Completed) == "single.wav", "old single-sound packages remain compatible");
        var manifest = Path.Combine(folders.PackageDirectory("mascat"), "mascot.json");
        var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        json["mascot"]!["states"]!["completed"]!["sounds"]![0] = "../escape.wav";
        File.WriteAllText(manifest, json.ToJsonString());
        var rejected = false; try { MascotFolderImport.Read(manifest); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "every random sound path is checked against folder traversal");
    }
}
