using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            var source = TestLibrary.Create(Path.Combine(dir, "import-source.json"));
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
        var withoutState = JsonNode.Parse(original)!.AsObject();
        withoutState["mascot"]!["states"]!.AsObject().Remove("failed");
        File.WriteAllText(manifest, withoutState.ToJsonString());
        check(MascotFolderImport.Read(folder).For(MascotState.Failed).Image is null,
            "missing state media imports as an empty state");
        File.WriteAllText(manifest, original);
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
        var file = Path.Combine(dir, "import-ui.json"); var store = TestLibrary.Create(file);
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, new CustomizationManager());
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            host.Show(); Pump(); var initial = store.Snapshot(); var count = store.Library.Installed.Count;
            var mediaBefore = Directory.GetFiles(store.LibraryDirectory, "*", SearchOption.AllDirectories).Length;
            OpenRegistration(dashboard, editor =>
            {
                Descendants(editor).OfType<Button>().Single(b => b.Name == "CloseMascotButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!editor.IsVisible && !Application.Current.Windows.OfType<Window>().Any(w => w.Title == "닫기 확인" && w.IsVisible),
                    "closing an untouched registration needs no discard prompt");
            });
            OpenRegistration(dashboard, editor =>
            {
                var tabs = Descendants(editor).OfType<TabControl>().Single();
                check(((TabItem)tabs.SelectedItem).Header?.ToString() == "라이브러리", "registration opens directly to simple library import");
                var button = Descendants(editor).OfType<Button>().Single(b => b.Name == "ApplyMascotButton");
                var actions = ((StackPanel)((ScrollViewer)((PencilBorder)editor.Content).Child).Content).Children.OfType<StackPanel>().Single();
                check(editor.WindowStyle == WindowStyle.None && editor.FontFamily == PencilFonts.Handwriting &&
                      actions.HorizontalAlignment == HorizontalAlignment.Right &&
                      actions.Children.OfType<Button>().Select(b => b.Name).SequenceEqual(new[] { "CloseMascotButton", "ApplyMascotButton" }) &&
                      !button.IsEnabled && !button.IsDefault && button.Content?.ToString() == "적용" &&
                      button.Background == PencilPalette.Button &&
                      Descendants(editor).OfType<Border>().Any(b => b.Name == "LibraryImportDropZone" && b.AllowDrop),
                    "titleless registration has Close left and one Apply right, disabled until a valid library package is chosen");
                Capture(editor, "import-empty");
                check(editor.LoadLibrary(folder) && button.IsEnabled && Descendants(editor).OfType<TextBlock>().Any(t => t.Text == "살구 고양이"), "folder selection loads cover/name and enables registration");
                Capture(editor, "import-ready");
                tabs.SelectedIndex = 1; editor.UpdateLayout();
                check(button.IsEnabled && Descendants(editor).OfType<Button>().Count(b => b.Name == "ApplyMascotButton") == 1 &&
                      Descendants(editor).OfType<TextBox>().Any(),
                    "manual file-by-file creation shares the same single Apply button");
                Capture(editor, "import-manual");
                QueueDiscardResponse(check, false);
                actions.Children.OfType<Button>().Single(b => b.Name == "CloseMascotButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(editor.IsVisible && editor.Result is null, "No keeps unsaved registration open");
                QueueDiscardResponse(check, true);
                actions.Children.OfType<Button>().Single(b => b.Name == "CloseMascotButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!editor.IsVisible, "Yes closes unsaved registration without applying");
            });
            check(store.Snapshot() == initial && Directory.GetFiles(store.LibraryDirectory, "*", SearchOption.AllDirectories).Length == mediaBefore, "cancelling after folder preview writes no installed entry or copied assets");
            OpenRegistration(dashboard, editor =>
            {
                var tabs = Descendants(editor).OfType<TabControl>().Single();
                tabs.SelectedIndex = 1;
                ((StackPanel)((TabItem)tabs.Items[1]).Content).Children.OfType<TextBox>().Single().Text = "draft mascot";
                QueueDiscardResponse(check, true);
                Descendants(editor).OfType<Button>().Single(b => b.Name == "CloseMascotButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!editor.IsVisible && editor.Result is null, "typing a draft name also requires discard confirmation");
            });
            OpenRegistration(dashboard, editor =>
            {
                check(!editor.LoadLibrary(Path.GetDirectoryName(folder)!) && editor.Result is null, "invalid folder displays an error without closing the dialog");
                check(editor.LoadLibrary(Path.Combine(folder, "mascot.json")), "valid manifest selection recovers after an error");
                var apply = Descendants(editor).OfType<Button>().Single(b => b.Name == "ApplyMascotButton");
                apply.Focus();
                var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor),
                    Environment.TickCount, Key.Return) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                editor.RaiseEvent(enter);
                check(enter.Handled && editor.IsVisible && apply.RenderTransform is ScaleTransform { ScaleX: .96 },
                    "Enter briefly presses Apply without closing registration immediately");
                Pump(560);
                check(!editor.IsVisible, "registration applies after the half-second Enter feedback");
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
        var file = Path.Combine(dir, "image-duration.json"); var store = TestLibrary.Create(file); var manager = new CustomizationManager();
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, manager);
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            host.Show(); Pump(); dashboard.AddMascot("bot"); var id = store.Library.Selected.Last().Id;
            LibraryMascot Mascot() => store.Library.Find(id)!;
            var duration = (NumericDragInput)dashboard.FindName("DurationInput");
            var states = (ComboBox)dashboard.FindName("PreviewState");
            void Click(string name) => ((Button)dashboard.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var beforeOverallEdit = store.Snapshot();
            check(!duration.IsVisible && !duration.IsEnabled && !duration.CommitValue(2300) && store.Snapshot() == beforeOverallEdit, "whole scope hides and disables image duration without changing saved timings");
            Click("StateSettingsButton");
            check(duration.IsVisible && duration.Value == manager.Configuration.For(MascotState.Completed).ShowDurationMs && !duration.IsMixed, "image state shows its own inherited duration");
            duration.CommitValue(2300);
            check(Mascot().Settings(MascotState.Completed).ImageDurationMs == 2300 && Mascot().Settings(MascotState.Running).ImageDurationMs is null && duration.DisplayText == "2.3초", "duration input changes only the selected image and displays seconds");
            states.SelectedIndex = 1; duration.CommitValue(3700);
            check(Mascot().Settings(MascotState.Running).ImageDurationMs == 3700 && Mascot().Settings(MascotState.Completed).ImageDurationMs == 2300, "each image has an independent duration");
            duration.BeginTextEdit(); duration.Editor.Text = "9"; states.SelectedIndex = 3;
            check(!duration.IsEditing && duration.Value == 2300 && Mascot().Settings(MascotState.Running).ImageDurationMs == 3700, "switching image states cancels uncommitted text instead of applying it to another image");
            Click("StateSettingsButton"); beforeOverallEdit = store.Snapshot();
            check(!duration.IsVisible && !duration.CommitValue(0) && store.Snapshot() == beforeOverallEdit, "returning to whole scope hides differing image times without overwriting them");
            Capture(dashboard, "duration-overall");
            Click("StateSettingsButton");
            var snapshot = store.Snapshot(); duration.BeginPointer(0); duration.MovePointer(20, false);
            check(store.Snapshot() == snapshot, "duration scrub does not persist intermediate values");
            duration.EndPointer(); dashboard.ReplayHistory(false);
            check(store.Snapshot() == snapshot && duration.Value == 2300, "individual image duration drag undoes in a single step");
            duration.CommitValue(0);
            check(LibraryStore.Playback(Mascot(), MascotState.Completed, new() { ShowDurationMs = 800 }).ShowDurationMs == 0, "zero duration is retained as unlimited image display");
            dashboard.ReplayHistory(false); duration.CommitValue(200);
            Click("TestButton"); check(dashboard.IsTesting, "image duration test starts");
            Pump(650); check(!dashboard.IsTesting, "image test stops automatically at the configured duration and resets its button");
            duration.CommitValue(7500);
            check(MascotPresentationGroup.CompletionVisibleMilliseconds(store, manager) == 7500, "foreground acknowledgment honors the longest selected image duration");
            Mascot().Settings(MascotState.NeedsAttention).ImageDurationMs = 6200;
            check(MascotPresentationGroup.NotificationVisibleMilliseconds(store, manager, MascotState.NeedsAttention) == 6200, "question notification uses its own image duration");
            var questionTime = DateTimeOffset.UtcNow;
            check(!MainWindow.ShouldDismissForForeground(MascotState.NeedsAttention, true, questionTime, questionTime.AddMilliseconds(6200)), "foreground Codex does not instantly dismiss question");
            check(MainWindow.ShouldDismissForForeground(MascotState.NeedsAttention, true, questionTime.AddMilliseconds(6200), questionTime.AddMilliseconds(6200)), "foreground Codex dismisses question after visible duration");
            Capture(dashboard, "duration");
            var video = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
            if (!string.IsNullOrWhiteSpace(video))
            {
                Mascot().For(MascotState.Completed).Image = video; store.Save();
                states.SelectedIndex = 1; states.SelectedIndex = 3;
                check(!duration.IsVisible && !duration.IsEnabled && !duration.CommitValue(3300) && LibraryStore.Playback(Mascot(), MascotState.Completed, new() { ShowDurationMs = 5100 }).ShowDurationMs == 5100, "video states hide and disable image duration and ignore its stored override");
                states.SelectedIndex = 1;
                check(duration.IsVisible && duration.Value == 3700, "switching from video to image restores that image's duration control");
                duration.CommitValue(3300);
                check(Mascot().Settings(MascotState.Completed).ImageDurationMs == 7500 && Mascot().Settings(MascotState.Running).ImageDurationMs == 3300, "individual image edits leave video settings untouched");
            }
            check(new LibraryStore(file).Library.Find(id)!.Settings(MascotState.Running).ImageDurationMs is not null, "duration survives reload and independent selected settings");
            if (states.IsVisible) Click("StateSettingsButton");
            SettingsTransferTestHelpers.QueueResponse(check, "설정 초기화 확인", true);
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
            Mascot().Settings(MascotState.NeedsAttention).HoldUntilClick = true;
            group.Show(store, manager, MascotState.NeedsAttention, false);
            Pump(700); check(group.IsPresenting, "checked approval notification waits for a click despite image duration");
            Mascot().Settings(MascotState.NeedsAttention).HoldUntilClick = false;
            group.ApplyPreferences(store, manager);
            Pump(700); check(!group.IsPresenting, "unchecking approval hold restores the image timeout");
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
    private static void QueueDiscardResponse(Action<bool, string> check, bool discard)
    {
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.IsVisible && w.Title == "닫기 확인");
            var frame = (PencilBorder)dialog.Content;
            var panel = (StackPanel)((ScrollViewer)frame.Child).Content;
            var actions = panel.Children.OfType<StackPanel>().Single();
            check(dialog.WindowStyle == WindowStyle.None && dialog.FontFamily == PencilFonts.Handwriting &&
                  panel.Children.OfType<TextBlock>().Single().Text == "변경 중인 작업이 있습니다. 닫으시겠습니까?" &&
                  actions.Children.OfType<Button>().Select(b => b.Name)
                      .SequenceEqual(new[] { "KeepMascotEditingButton", "DiscardMascotChangesButton" }) &&
                  !actions.Children.OfType<Button>().Any(b => b.IsDefault),
                "discard confirmation is titleless, localized and requires an explicit Yes or No");
            actions.Children.OfType<Button>().Single(b => b.Name ==
                (discard ? "DiscardMascotChangesButton" : "KeepMascotEditingButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
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
