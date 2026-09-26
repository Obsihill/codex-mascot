using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMascot.App;
using CodexMascot.Core;

internal static class LibraryImportDurationTests
{
    public static void Run(Action<bool, string> check, string dir)
    {
        var context = SynchronizationContext.Current;
        try
        {
            var source = new LibraryStore(Path.Combine(dir, "import-source.json"));
            var cat = source.Library.Installed.Single(m => m.Id == "cat");
            cat.Settings(MascotState.Completed).Volume = 1.7;
            cat.Settings(MascotState.Completed).ImageDurationMs = 2700;
            cat.Settings(MascotState.Failed).Loop = true; cat.Events.Remove("running"); source.Save();
            var folder = new MascotFolderLibrary(source.LibraryDirectory).PackageDirectory("cat");
            ValidateImports(check, folder);
            Registration(check, dir, folder);
            Duration(check, dir);
        }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
    private static void ValidateImports(Action<bool, string> check, string folder)
    {
        var manifest = Path.Combine(folder, "mascot.json");
        var original = File.ReadAllText(manifest);
        var beforeFiles = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        var first = MascotFolderImport.Read(folder); var second = MascotFolderImport.Read(manifest);
        check(first.Id != "cat" && first.Id != second.Id && first.SourceId is null && first.InstalledAt > DateTimeOffset.UtcNow.AddMinutes(-1), "folder and manifest imports create fresh local identities and installation timestamps");
        check(first.Name == "살구 고양이" && first.States.Count == 6 && first.Settings(MascotState.Completed).Volume == 1.7 && first.Settings(MascotState.Completed).ImageDurationMs == 2700 && first.Settings(MascotState.Failed).Loop && !first.Events.Contains("running"), "folder import preserves package name, media, event exclusions and per-state playback defaults");
        check(File.ReadAllText(manifest) == original && beforeFiles.SequenceEqual(Directory.GetFiles(folder, "*", SearchOption.AllDirectories)), "reading and previewing a library is read-only");
        void Reject(Action<JsonObject> mutate, string description)
        {
            var node = JsonNode.Parse(original)!.AsObject(); mutate(node); File.WriteAllText(manifest, node.ToJsonString());
            var rejected = false;
            try { MascotFolderImport.Read(folder); } catch (Exception) { rejected = true; }
            finally { File.WriteAllText(manifest, original); }
            check(rejected, description);
        }
        Reject(n => n["version"] = 2, "unsupported package version rejected");
        Reject(n => n["version"] = "1", "incorrect manifest value types rejected");
        Reject(n => n["format"] = "unrelated", "unrelated JSON is not a mascot package");
        Reject(n => n["mascot"]!["name"] = "", "empty imported name rejected");
        Reject(n => n["mascot"]!["states"]!.AsObject().Remove("failed"), "incomplete state sets rejected");
        Reject(n => n["mascot"]!["coverImage"] = "../outside.png", "parent-directory traversal rejected");
        Reject(n => n["mascot"]!["coverImage"] = ".. /outside.png", "Windows trailing-space traversal aliases rejected");
        Reject(n => n["mascot"]!["coverImage"] = first.CoverImage, "absolute media paths rejected");
        Reject(n => n["mascot"]!["coverImage"] = "media/file.png:secret", "NTFS alternate data stream paths rejected");
        Reject(n => n["mascot"]!["coverImage"] = "media/missing.png", "missing file rejected before registration");
        var invalidPng = Path.Combine(folder, "media", "invalid.png"); File.WriteAllText(invalidPng, "not an image");
        Reject(n => n["mascot"]!["coverImage"] = "media/invalid.png", "invalid image bytes rejected before copying");
        var invalidVideo = Path.Combine(folder, "media", "cover.mp4"); File.WriteAllBytes(invalidVideo, new byte[] { 1 });
        Reject(n => n["mascot"]!["coverImage"] = "media/cover.mp4", "representative cover cannot be a video");
        File.WriteAllText(manifest, new string(' ', 1024 * 1024 + 1));
        var oversized = false;
        try { MascotFolderImport.Read(folder); } catch (InvalidDataException) { oversized = true; }
        finally { File.WriteAllText(manifest, original); }
        check(oversized, "manifest size is bounded");
        var bounds = JsonNode.Parse(original)!;
        bounds["mascot"]!["states"]!["completed"]!["playback"]!["imageDurationMs"] = 900000;
        bounds["mascot"]!["states"]!["completed"]!["playback"]!["volume"] = 999;
        File.WriteAllText(manifest, bounds.ToJsonString());
        var bounded = MascotFolderImport.Read(folder);
        check(bounded.Settings(MascotState.Completed).ImageDurationMs == 600000 && bounded.Settings(MascotState.Completed).Volume == 2, "imported numeric settings are constrained to supported ranges");
        File.WriteAllText(manifest, original);
        var video = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
        if (!string.IsNullOrWhiteSpace(video))
        {
            File.Copy(video, Path.Combine(folder, "media", "completed.mp4"));
            var node = JsonNode.Parse(original)!; node["mascot"]!["states"]!["completed"]!["image"] = "media/completed.mp4";
            File.WriteAllText(manifest, node.ToJsonString());
            check(MascotFolderImport.Read(folder).For(MascotState.Completed).Image!.EndsWith("completed.mp4"), "state videos import without changing their media type");
            File.WriteAllText(manifest, original);
        }
    }
    private static void Registration(Action<bool, string> check, string dir, string folder)
    {
        var file = Path.Combine(dir, "import-ui.json"); var store = new LibraryStore(file);
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, new CustomizationManager());
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            host.Show(); Pump(); var initial = store.Snapshot(); var count = store.Library.Installed.Count;
            var mediaBefore = Directory.GetFiles(store.LibraryDirectory, "*", SearchOption.AllDirectories).Length;
            OpenRegistration(dashboard, editor =>
            {
                var tabs = Descendants(editor).OfType<TabControl>().Single();
                check(((TabItem)tabs.SelectedItem).Header?.ToString() == "라이브러리", "registration opens directly to simple library import");
                var button = Descendants(editor).OfType<Button>().Single(b => b.Name == "ImportLibraryButton");
                check(!button.IsEnabled && Descendants(editor).OfType<Border>().Any(b => b.Name == "LibraryImportDropZone" && b.AllowDrop), "registration accepts folder drops and cannot submit without a valid package");
                Capture(editor, "import-empty");
                check(editor.LoadLibrary(folder) && button.IsEnabled && Descendants(editor).OfType<TextBlock>().Any(t => t.Text == "살구 고양이"), "folder selection loads cover/name and enables registration");
                Capture(editor, "import-ready");
                tabs.SelectedIndex = 1; editor.UpdateLayout();
                check(Descendants(editor).OfType<TextBox>().Any(), "manual file-by-file creation remains available on its own tab");
                Capture(editor, "import-manual"); editor.Close();
            });
            check(store.Snapshot() == initial && Directory.GetFiles(store.LibraryDirectory, "*", SearchOption.AllDirectories).Length == mediaBefore, "cancelling after folder preview writes no installed entry or copied assets");
            OpenRegistration(dashboard, editor =>
            {
                check(!editor.LoadLibrary(Path.GetDirectoryName(folder)!) && editor.Result is null, "invalid folder displays an error without closing the dialog");
                check(editor.LoadLibrary(Path.Combine(folder, "mascot.json")), "valid manifest selection recovers after an error");
                Descendants(editor).OfType<Button>().Single(b => b.Name == "ImportLibraryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            var added = store.Library.Installed.Last();
            check(store.Library.Installed.Count == count + 1 && added.Id != "cat" && added.Name == "살구 고양이" && store.Library.Selected.Count == 1, "registration adds installed package without changing active selections");
            check(added.CoverImage!.StartsWith(store.LibraryDirectory, StringComparison.OrdinalIgnoreCase) && !added.CoverImage.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && File.Exists(added.CoverImage), "registration copies all files into its own managed folder instead of retaining external references");
            check(new LibraryStore(file).Library.Find(added.Id)!.Settings(MascotState.Completed).ImageDurationMs == 2700, "imported package and duration persist across restart");
            dashboard.ReplayHistory(false);
            check(store.Library.Installed.Count == count && File.Exists(added.CoverImage), "registration is one undo step and never deletes the source or recoverable copied files");
            dashboard.ReplayHistory(true);
            check(store.Library.Find(added.Id) is not null, "redo restores imported package");
        }
        finally { dashboard.Shutdown(); host.Close(); }
    }
    private static void Duration(Action<bool, string> check, string dir)
    {
        var file = Path.Combine(dir, "image-duration.json"); var store = new LibraryStore(file); var manager = new CustomizationManager();
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, manager);
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            host.Show(); Pump(); dashboard.AddMascot("bot"); var id = store.Library.Selected.Last().Id;
            LibraryMascot Mascot() => store.Library.Find(id)!;
            var duration = (NumericDragInput)dashboard.FindName("DurationInput");
            var states = (ComboBox)dashboard.FindName("PreviewState");
            void Click(string name) => ((Button)dashboard.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(duration.IsVisible && duration.DisplayText == "?", "whole image duration reflects mixed inherited event timings");
            duration.CommitValue(2300);
            check(CustomizationManager.States.All(s => Mascot().Settings(s).ImageDurationMs == 2300) && duration.DisplayText == "2.3초", "whole image duration stores milliseconds and displays seconds across all image states");
            Click("StateSettingsButton"); duration.CommitValue(3700);
            check(Mascot().Settings(MascotState.Completed).ImageDurationMs == 3700 && Mascot().Settings(MascotState.Running).ImageDurationMs == 2300, "state-specific image duration leaves other states unchanged");
            Click("StateSettingsButton"); check(duration.DisplayText == "?", "returning to whole scope shows mixed durations");
            var snapshot = store.Snapshot(); duration.BeginPointer(0); duration.MovePointer(20, false);
            check(store.Snapshot() == snapshot, "duration scrub does not persist intermediate values");
            duration.EndPointer(); dashboard.ReplayHistory(false);
            check(store.Snapshot() == snapshot && duration.DisplayText == "?", "entire duration drag undoes in a single step");
            duration.CommitValue(0);
            check(LibraryStore.Playback(Mascot(), MascotState.Completed, new() { ShowDurationMs = 800 }).ShowDurationMs == 0, "zero duration is retained as unlimited image display");
            dashboard.ReplayHistory(false); Click("StateSettingsButton"); duration.CommitValue(200);
            Click("TestButton"); check(dashboard.IsTesting, "image duration test starts");
            Pump(650); check(!dashboard.IsTesting, "image test stops automatically at the configured duration and resets its button");
            duration.CommitValue(7500);
            check(MascotPresentationGroup.CompletionVisibleMilliseconds(store, manager) == 7500, "foreground acknowledgment honors the longest selected image duration");
            Capture(dashboard, "duration");
            var video = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
            if (!string.IsNullOrWhiteSpace(video))
            {
                Mascot().For(MascotState.Completed).Image = video; store.Save();
                states.SelectedIndex = 1; states.SelectedIndex = 3;
                check(!duration.IsVisible && LibraryStore.Playback(Mascot(), MascotState.Completed, new() { ShowDurationMs = 5100 }).ShowDurationMs == 5100, "video states hide image duration and ignore its stored override");
                Click("StateSettingsButton"); duration.CommitValue(3300);
                check(Mascot().Settings(MascotState.Completed).ImageDurationMs == 7500 && Mascot().Settings(MascotState.Running).ImageDurationMs == 3300, "whole duration edit changes image states only, leaving video settings untouched");
            }
            check(new LibraryStore(file).Library.Find(id)!.Settings(MascotState.Running).ImageDurationMs is not null, "duration survives reload and independent selected settings");
            if (states.IsVisible) Click("StateSettingsButton");
            Click("ResetSettingsButton");
            check(CustomizationManager.States.All(s => Mascot().Settings(s).ImageDurationMs is null), "reset restores legacy/default event timing");
            dashboard.ReplayHistory(false);
            check(Mascot().Settings(MascotState.Running).ImageDurationMs is not null, "duration reset is undoable");
            // Actual notification timers, not just the test button.
            store.Library.Selected.RemoveAll(m => m.Id != id);
            Mascot().Settings(MascotState.Failed).ImageDurationMs = 200;
            using var group = new MascotPresentationGroup();
            group.Show(store, manager, MascotState.Failed, false);
            Pump(700); check(!group.IsPresenting, "live image notification hides after its per-mascot duration");
            Mascot().Settings(MascotState.NeedsAttention).ImageDurationMs = 200;
            group.Show(store, manager, MascotState.NeedsAttention, false);
            Pump(700); check(group.IsPresenting, "approval-required notifications still wait for a click despite image duration");
        }
        finally { dashboard.Shutdown(); host.Close(); }
    }
    private static void OpenRegistration(LibraryDashboard dashboard, Action<MascotPackageEditor> action)
    {
        Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var editor = Application.Current.Windows.OfType<MascotPackageEditor>().Single();
            try { action(editor); }
            catch (Exception e) { failure = e; }
            finally { if (editor.IsVisible) editor.Close(); }
        }));
        ((Button)dashboard.FindName("RegisterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure is not null) throw failure;
    }
    private static void Capture(FrameworkElement element, string name)
    {
        var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(element, Path.ChangeExtension(screenshot, "." + name + ".png"));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Pump(int ms = 0)
    {
        var frame = new DispatcherFrame();
        if (ms == 0) Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        else
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start();
        }
        Dispatcher.PushFrame(frame);
    }
}
