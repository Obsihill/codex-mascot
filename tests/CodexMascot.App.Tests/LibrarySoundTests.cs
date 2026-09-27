using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodexMascot.App;
using CodexMascot.Core;

internal static class LibrarySoundTests
{
    internal static void Run(Action<bool, string> check, string dir)
    {
        var context = SynchronizationContext.Current;
        try { RunCore(check, dir); }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
    private static void RunCore(Action<bool, string> check, string dir)
    {
        var manager = new CustomizationManager();
        var file = Path.Combine(dir, "audio-library.json");
        var store = TestLibrary.Create(file);
        var folders = new MascotFolderLibrary(store.LibraryDirectory);
        var hashes = new HashSet<string>();
        foreach (var mascot in store.Library.Installed.Where(m => m.Id != "original"))
        {
            foreach (var state in CustomizationManager.States)
            {
                var sound = manager.ResolveAsset(mascot.For(state).Sound)!;
                MascotPackageEditor.ValidateSound(sound);
                using var reader = new NAudio.Wave.AudioFileReader(sound);
                var data = new float[44100]; var count = reader.Read(data, 0, data.Length);
                check(count > 0 && data.Take(count).Any(v => Math.Abs(v) > .01) && reader.TotalTime.TotalSeconds < 1, "sample state sound decodes to short, non-silent PCM");
                hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sound))));
            }
            var imported = MascotFolderImport.Read(folders.PackageDirectory(mascot.Id));
            check(CustomizationManager.States.All(s => File.Exists(imported.For(s).Sound)), "all six sounds resolve inside imported portable package");
        }
        check(hashes.Count >= 30, "example mascots have distinct state chimes");
        var bot = store.Library.Installed.Single(m => m.Id == "bot");
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            using var player = new SoundPlayerService();
            string? feedback = null; player.Feedback += (_, text) => feedback = text;
            player.Play(manager.ResolveAsset(bot.For(MascotState.Completed).Sound), 0);
            check(player.IsPrepared && feedback?.Contains("재생 시작") == true, "generated sample opens and starts through actual audio output at silent test volume");
            player.Stop(); check(!player.IsPrepared, "stopping sample releases its audio output");
        }
        var before = bot.For(MascotState.Completed).Sound;
        store.Library.Select("bot"); store.Library.Select("bot"); store.Save();
        var first = store.Library.Selected[^2]; var second = store.Library.Selected[^1];
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, manager);
        var host = new Window { Content = dashboard, Width = 1100, Height = 800, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            host.Show(); host.UpdateLayout(); ((ListBox)dashboard.FindName("SelectedList")).SelectedIndex = store.Library.Selected.Count - 2;
            ((Button)dashboard.FindName("StateSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var alternate = manager.ResolveAsset(bot.For(MascotState.Failed).Sound)!;
            check(dashboard.FindName("StateMediaOptions") is null && dashboard.FindName("StateSoundName") is null, "state inspector omits media editing and sound file labels");
            var history = new LibraryHistory(store);
            check(history.Commit("sound fixture", () => first.For(MascotState.Completed).Sound = alternate), "library persists independent sound data");
            check(first.For(MascotState.Completed).Sound != before && second.For(MascotState.Completed).Sound == before && bot.For(MascotState.Completed).Sound == before, "sound edit does not change sibling selection or installed template");
            check(history.Commit("remove sound fixture", () => first.For(MascotState.Completed).Sound = null) && LibraryStore.Playback(first, MascotState.Completed, new StateConfiguration { Sound = "legacy.wav" }).Sound is null, "removing sound never falls back to global audio");
            history.Undo();
            check(store.Library.Find(first.Id)!.For(MascotState.Completed).Sound is not null, "sound removal supports undo");
            check(new LibraryStore(file).Library.Find(first.Id)!.For(MascotState.Completed).Sound == store.Library.Find(first.Id)!.For(MascotState.Completed).Sound, "sound edits persist across reload");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(dashboard, Path.ChangeExtension(screenshot, ".sounds.png"));
        }
        finally { dashboard.Shutdown(); host.Close(); }

        var paths = CustomizationManager.States.ToDictionary(MascotConfiguration.StateKey, s => bot.For(s).Image);
        paths["cover"] = bot.CoverImage;
        foreach (var state in CustomizationManager.States) paths[MascotConfiguration.StateKey(state) + ".sound"] = bot.For(state).Sound;
        var built = MascotPackageEditor.BuildPackage("소리 테스트", paths, new HashSet<string>(), manager);
        check(built.States.Values.All(s => s.SoundCandidates().Count == 1 && manager.ResolveAsset(s.SoundCandidates()[0]) is not null), "direct registration includes all selected sounds in owned package");
        var editor = new MascotPackageEditor(manager, null) { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            var tabs = ((StackPanel)((ScrollViewer)editor.Content).Content).Children.OfType<TabControl>().Single();
            tabs.SelectedIndex = 1; editor.Show(); editor.UpdateLayout();
            var panel = (StackPanel)((TabItem)tabs.Items[1]).Content;
            check(panel.Children.OfType<DockPanel>().SelectMany(p => p.Children.OfType<Button>()).Count(b => b.Name.EndsWith("_soundPick")) == 6, "direct registration offers one sound picker per state");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(editor, Path.ChangeExtension(screenshot, ".registration-sounds.png"));
        }
        finally { editor.Close(); }
        var video = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
        if (!string.IsNullOrWhiteSpace(video))
        {
            store.Library.Selected.Clear(); store.Library.Select("bot");
            var selected = store.Library.Selected.Single(); selected.For(MascotState.Completed).Image = video;
            selected.Settings(MascotState.Completed).Volume = 0;
            using var group = new MascotPresentationGroup();
            group.Show(store, manager, MascotState.Completed, true);
            check(((MediaElement)group.Windows.Single().FindName("MascotVideo")).IsMuted, "assigned state sound suppresses embedded video audio");
            selected.For(MascotState.Completed).Sound = null;
            group.Show(store, manager, MascotState.Completed, true);
            check(!((MediaElement)group.Windows.Single().FindName("MascotVideo")).IsMuted, "removing separate sound restores video audio routing while respecting volume");
            group.Clear(); check(group.Windows.Count == 0, "closing audio presentation releases all mascot windows");
        }
        var broken = Path.Combine(dir, "invalid.wav"); File.WriteAllText(broken, "not audio");
        var rejected = false; try { MascotPackageEditor.ValidateSound(broken); } catch { rejected = true; }
        check(rejected, "corrupt audio is rejected before registration");
        var manifest = Path.Combine(folders.PackageDirectory("bot"), "mascot.json");
        var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        json["mascot"]!["states"]!["completed"]!["sound"] = "../outside.wav";
        File.WriteAllText(manifest, json.ToJsonString());
        rejected = false; try { MascotFolderImport.Read(manifest); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "sound import rejects folder traversal");

        var legacyPath = Path.Combine(dir, "audio-migration.json");
        var legacy = new MascotLibrary { FolderLayoutVersion = 1, Installed = new() { bot } };
        foreach (var media in bot.States.Values) media.Sound = null;
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(legacy, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var old = new MascotConfiguration(); old.Global.SoundEnabled = false;
        var migrated = new LibraryStore(legacyPath, configuration: old);
        check(migrated.Library.AudioLayoutVersion == 1 && migrated.Library.Installed[0].Settings(MascotState.Completed).Volume == 0 && migrated.Library.Installed[0].For(MascotState.Completed).Sound is not null, "legacy global sound and mute migrate into individual settings");
        migrated.Library.Installed[0].Settings(MascotState.Completed).Volume = 1; migrated.Save();
        check(new LibraryStore(legacyPath, configuration: old).Library.Installed[0].Settings(MascotState.Completed).Volume == 1, "removed global mute cannot reapply after migration");

        var samples = Environment.GetEnvironmentVariable("MASCOT_SOUND_SAMPLES");
        if (!string.IsNullOrWhiteSpace(samples))
        {
            var fresh = TestLibrary.Create(Path.Combine(dir, "fresh-sounds.json"));
            var output = new MascotFolderLibrary(samples);
            foreach (var mascot in fresh.Library.Installed.Where(m => m.Id != "original")) output.SavePackage(mascot);
        }
    }
}
