using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexMascot.App;
using CodexMascot.Core;

internal static class LibraryFeatureTests
{
    public static void Run(Action<bool, string> check, string dir)
    {
        var previousContext = SynchronizationContext.Current;
        try { RunCore(check, dir); }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void RunCore(Action<bool, string> check, string dir)
    {
        var file = Path.Combine(dir, "library.json");
        var store = new LibraryStore(file, new GlobalConfiguration { Position = "top-left", CustomLeft = 120 });
        check(store.Library.Installed.Count == 7 && store.Library.Selected.Single().SourceId == "original", "library starts with six samples and preserves legacy mascot");
        check(store.Library.Select("bot") && !store.Library.Select("missing"), "selection validates source IDs");
        var bot = store.Library.Selected.Single(m => m.SourceId == "bot");
        check(bot.States.Count == 6 && bot.States.Values.Select(v => v.Image).Distinct().Count() == 6 && !bot.States.Values.Any(v => v.Image == bot.CoverImage), "sample packages have a separate cover and six distinct state assets");
        bot.Events = new() { "completed" }; bot.Speed = 1.5; bot.Volume = .35; bot.Position = "center";
        foreach (var state in CustomizationManager.States) { bot.Settings(state).Speed = 1.5; bot.Settings(state).Volume = .35; }
        store.Library.Selected.RemoveAll(m => m.SourceId == "original");
        check(store.Library.Eligible(MascotState.Completed).Single() == bot && store.Library.Eligible(MascotState.Running).Count == 0, "event exclusions drive playback eligibility");
        store.Save();
        var restored = new LibraryStore(file);
        var loaded = restored.Library.Selected.Single(m => m.SourceId == "bot");
        check(restored.Library.Selected.Count == 1 && loaded.Speed == 1.5 && loaded.Volume == .35 && loaded.Position == "center", "selection playback and placement survive reload");
        restored.Library.Selected.Clear(); restored.Save();
        check(new LibraryStore(file).Library.Eligible(MascotState.Completed).Count == 0, "empty selection stays empty after restart");
        var global = new GlobalConfiguration { MasterVolume = .4, MonitorDevice = "default" };
        loaded.MonitorDevice = "test-screen";
        var placement = LibraryStore.Placement(loaded, global);
        check(placement.MonitorDevice == "test-screen" && placement.Position == "center" && placement.MasterVolume == .4 && global.MonitorDevice == "default", "per-mascot placement does not overwrite global preferences");
        var playback = LibraryStore.Playback(loaded, MascotState.Completed, new StateConfiguration { ShowDurationMs = 4500 });
        check(playback.PlaybackSpeed == 1.5 && playback.Volume == .35 && playback.ShowDurationMs == 4500, "playback combines mascot controls and event lifetime");
        loaded.For(MascotState.Completed).SoundEnabled = false;
        var muted = LibraryStore.Playback(loaded, MascotState.Completed, new StateConfiguration { Sound = "test.wav" });
        check(muted.Volume == 0 && muted.Sound is null && loaded.For(MascotState.Failed).SoundEnabled, "event mute silences embedded audio and external sound independently");

        var legacyFile = Path.Combine(dir, "legacy-library.json");
        File.WriteAllText(legacyFile, """{"installed":[{"id":"old","name":"old","media":"assets/images/idle.png","volume":0.6}],"selectedIds":["old"]}""");
        var migrated = new LibraryStore(legacyFile); var old = migrated.Library.Installed.Single();
        check(old.Media is null && old.States.Count == 6 && File.Exists(old.CoverImage) && old.Volume == .6 && migrated.Library.Selected.Single().SourceId == "old", "single-file library migrates into folders without losing assets options or selection");
        migrated.Save();
        check(new LibraryStore(legacyFile).Library.Installed.Single().For(MascotState.Completed).Image == old.CoverImage, "folder package migration survives save and reload");

        var manager = new CustomizationManager();
        var slots = new Dictionary<string, string?> { ["cover"] = bot.CoverImage };
        foreach (var pair in bot.States) slots[pair.Key] = pair.Value.Image;
        var package = MascotPackageEditor.BuildPackage("Bundle", slots, new HashSet<string>(), manager, bot);
        check(package.States.Count == 6 && File.ReadAllBytes(manager.ResolveAsset(package.CoverImage)!).SequenceEqual(File.ReadAllBytes(manager.ResolveAsset(bot.CoverImage)!)) && package.For(MascotState.Failed).Image != package.For(MascotState.Completed).Image, "package editor saves a cover plus state-specific media in its own folder");
        slots.Remove("failed"); var rejected = false;
        try { MascotPackageEditor.BuildPackage("Incomplete", slots, new HashSet<string>(), manager); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "registration rejects incomplete mascot packages");
        var fileSlots = new Dictionary<string, string?> { ["cover"] = Path.Combine(AppContext.BaseDirectory, "assets", "images", "idle.png") };
        foreach (var state in CustomizationManager.States)
            fileSlots[MascotConfiguration.StateKey(state)] = manager.ResolveImage(state);
        var importedPackage = MascotPackageEditor.BuildPackage("Files", fileSlots, fileSlots.Keys.ToHashSet(), manager);
        check(manager.ResolveAsset(importedPackage.CoverImage) is not null && importedPackage.States.Values.All(v => manager.ResolveAsset(v.Image) is not null), "registration copies cover and all state media into managed assets");
        check(importedPackage.CoverImage == importedPackage.For(MascotState.Idle).Image, "shared source files are copied only once per package");
        var dashboard = new LibraryDashboard();
        dashboard.Initialize(store, manager);
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            host.Show(); Pump();
            var installed = (ListBox)dashboard.FindName("InstalledList");
            installed.SelectedIndex = 2;
            check(dashboard.AddMascot("cat") && dashboard.AddMascot("cat"), "UI allows duplicate independent selections");
            var volumeInput = (NumericDragInput)dashboard.FindName("VolumeInput"); volumeInput.CommitValue(.25);
            check(new LibraryStore(file).Library.Selected.Last(m => m.SourceId == "cat").Volume == .25 && store.Library.Installed.Single(m => m.Id == "cat").Volume == 1, "inspector saves selected copy without altering installed defaults");
            dashboard.ReceiveDrop(new DataObject("CodexMascot.LibraryItem", "ghost"));
            check(store.Library.Selected.Any(m => m.SourceId == "ghost"), "drop zone adds dragged installed mascot");
            StudioControlsTests.CardButton((ListBox)dashboard.FindName("SelectedList"), store.Library.Selected.Last().Id, "CardRemoveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!store.Library.Selected.Any(m => m.SourceId == "ghost") && store.Library.Installed.Any(m => m.Id == "ghost"), "removing from selected leaves installed asset available");
            check(!dashboard.ReceiveDrop(new DataObject(DataFormats.Text, "unrelated")), "unrelated drag data cannot add mascots");
            dashboard.AddMascot("ghost");
            var ghostId = store.Library.Selected.Last().Id;
            check(!dashboard.CompleteSelectionDrag(ghostId, true, true, false) && store.Library.Selected.Any(m => m.Id == ghostId), "Escape cancels selection removal");
            check(!dashboard.CompleteSelectionDrag(ghostId, true, false, true) && store.Library.Selected.Any(m => m.Id == ghostId), "drop within selected region keeps item");
            check(!dashboard.CompleteSelectionDrag(ghostId, false, false, false), "unfinished drag does not remove item");
            check(dashboard.CompleteSelectionDrag(ghostId, true, false, false) && store.Library.Installed.Any(m => m.Id == "ghost"), "drop outside selected region removes selection but not package");
            dashboard.AddMascot("ghost"); Pump();
            var selectedList = (ListBox)dashboard.FindName("SelectedList");
            var selectedContainer = (ListBoxItem)selectedList.ItemContainerGenerator.ContainerFromItem(selectedList.SelectedItem);
            selectedList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = selectedContainer });
            check(!store.Library.Selected.Any(m => m.SourceId == "ghost"), "double-click selected card removes it");
            var ghost = new DragPreviewWindow(DemoMascotArtwork.Create("bot/cover"), "bot");
            try { ghost.Show(); ghost.FollowCursor(); check(ghost.Opacity == .55 && !ghost.IsHitTestVisible && !ghost.ShowActivated, "drag ghost is translucent click-through and non-activating"); }
            finally { ghost.Close(); }
            check(dashboard.FindName("EventsButton") is null, "separate event dialog is removed");
            var scope = (Button)dashboard.FindName("StateSettingsButton");
            scope.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var completed = (CheckBox)dashboard.FindName("PlayCheck");
            completed.IsChecked = false; completed.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            volumeInput.CommitValue(0);
            check(!new LibraryStore(file).Library.Installed.Single(m => m.Id == "ghost").Events.Contains("completed"), "state inspector persists exclusion for inspected mascot");
            check(new LibraryStore(file).Library.Installed.Single(m => m.Id == "ghost").Settings(MascotState.Completed).Volume == 0 && dashboard.FindName("SoundCheck") is null, "zero volume replaces removed sound toggle");
            scope.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "위치 선택");
                var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                check(!panel.Children.OfType<ComboBox>().Any(), "position dialog has no monitor or preset selectors");
                var row = panel.Children.OfType<Grid>().Single();
                var x = row.Children.OfType<NumericDragInput>().Single(t => t.Name == "PositionX");
                var y = row.Children.OfType<NumericDragInput>().Single(t => t.Name == "PositionY");
                x.CommitValue(-120); y.CommitValue(240);
                dialog.UpdateLayout();
                check(Math.Abs(x.TranslatePoint(new Point(), row).Y - y.TranslatePoint(new Point(), row).Y) < .1 && Grid.GetColumn(x) < Grid.GetColumn(y), "X and Y inputs share one horizontal row");
                CaptureDialog(dialog, "position");
                dialog.Close();
            }));
            ((Button)dashboard.FindName("PositionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var positioned = new LibraryStore(file).Library.Installed.Single(m => m.Id == "ghost");
            check(positioned.Position == "custom" && positioned.CustomLeft == -120 && positioned.CustomTop == 240, "position dialog persists signed desktop coordinates");
            var toggle = (Button)dashboard.FindName("TestButton");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(dashboard.IsTesting && (string)toggle.Content == "■ 중지", "test button toggles into stop while playing");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!dashboard.IsTesting && (string)toggle.Content == "▶ 테스트", "same button stops and resets test");
            var videoFixture = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
            if (!string.IsNullOrWhiteSpace(videoFixture))
            {
                var inspected = store.Library.Installed.Single(m => m.Id == "ghost");
                var originalImage = inspected.For(MascotState.Completed).Image;
                inspected.For(MascotState.Completed).Image = videoFixture;
                ((ComboBox)dashboard.FindName("PreviewState")).SelectedIndex = 3;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var testOverlay = Application.Current.Windows.OfType<OverlayWindow>().Single(w => w.IsVisible);
                var video = (MediaElement)testOverlay.FindName("MascotVideo");
                check(video.Source is not null && video.Volume == 0, "zero-volume event suppresses actual MP4 test audio");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!dashboard.IsTesting && !testOverlay.IsVisible, "test toggle closes video overlay");
                inspected.For(MascotState.Completed).Image = originalImage;
                ((ComboBox)dashboard.FindName("PreviewState")).SelectedIndex = 0;
            }
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<MascotPackageEditor>().Single(); CaptureDialog(dialog, "package"); dialog.Close();
            }));
            check(dashboard.FindName("MediaButton") is null, "image configuration action is removed");
            ((Button)dashboard.FindName("RegisterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot))
            {
                installed.SelectedIndex = 1;
                Capture(dashboard, screenshot);
                host.Width = 1020; host.Height = 760;
                Capture(dashboard, Path.ChangeExtension(screenshot, ".compact.png"));
            }
            store.Library.Installed.Add(importedPackage); dashboard.AddMascot(importedPackage.Id);
            installed.SelectedItem = installed.Items.Cast<object>().Single(c => ((LibraryMascot)c.GetType().GetProperty("Mascot")!.GetValue(c)!).Id == importedPackage.Id);
            var delete = (Button)dashboard.FindName("DeleteButton");
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                Application.Current.Windows.OfType<Window>().Single(w => w.Title == "마스코트 삭제").Close()));
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(store.Library.Installed.Contains(importedPackage) && store.Library.Selected.Any(m => m.SourceId == importedPackage.Id), "cancelled deletion preserves installed and selected lists");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "마스코트 삭제");
                CaptureDialog(dialog, "delete");
                var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(b => b.Name == "ConfirmDelete").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!store.Library.Installed.Contains(importedPackage) && !store.Library.Selected.Any(m => m.SourceId == importedPackage.Id) && !dashboard.IsTesting, "confirmed delete removes package from both lists and stops its test");
            check(new LibraryStore(file).Library.Installed.All(m => m.Id != importedPackage.Id), "deleted package stays deleted after reload");
            check(manager.ResolveAsset(importedPackage.CoverImage) is not null && importedPackage.States.Values.All(v => manager.ResolveAsset(v.Image) is not null), "library deletion preserves media files");
            check(!dashboard.DeleteMascot("missing"), "deleting unknown package is a no-op");
            foreach (var id in store.Library.Installed.Select(m => m.Id).ToArray()) dashboard.DeleteMascot(id);
            check(new LibraryStore(file).Library.Installed.Count == 0 && !delete.IsEnabled && !((Button)dashboard.FindName("TestButton")).IsEnabled && ((Image)dashboard.FindName("PreviewImage")).Source is null, "deleting the last package clears preview and disables actions without recreating samples");
        }
        finally { dashboard.Shutdown(); host.Close(); }
        var autoStart = manager.Configuration.Monitor.AutoStart;
        manager.Configuration.Monitor.AutoStart = false; manager.Save();
        var main = new MainWindow();
        try
        {
            main.Show(); Pump();
            main.Close(); Pump();
            check(!main.IsVisible, "studio close hides window without terminating it");
            main.Tray_OnMouseDoubleClick(null, new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Right, 2, 0, 0, 0)); Pump();
            check(!main.IsVisible, "tray right double-click does not open studio");
            main.Tray_OnMouseDoubleClick(null, new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 2, 0, 0, 0)); Pump();
            check(main.IsVisible && ((LibraryDashboard)main.FindName("Dashboard")).IsVisible, "tray left double-click reopens mascot studio");
            main.WindowState = WindowState.Minimized;
            main.Tray_OnMouseDoubleClick(null, new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 2, 0, 0, 0)); Pump();
            check(main.WindowState == WindowState.Normal && Application.Current.Windows.OfType<MainWindow>().Count() == 1, "tray double-click restores minimized studio without another instance");
            main.OpenWorkspaceSettings(); Pump();
            var settings = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Mascot · 앱 설정");
            check(settings.IsVisible && ((Grid)settings.Content).IsVisible, "connection controls open in settings window");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) Capture(settings, Path.ChangeExtension(screenshot, ".connections.png"));
            settings.Close(); main.OpenWorkspaceSettings(); Pump();
            check(Application.Current.Windows.OfType<Window>().Count(w => w.Title == "Mascot · 앱 설정") == 1, "settings can reopen without losing or duplicating controls");
        }
        finally { main.ExitApplication(); manager.Configuration.Monitor.AutoStart = autoStart; manager.Save(); }
    }
    private static void CaptureDialog(Window dialog, string suffix)
    {
        var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(screenshot)) Capture(dialog, Path.ChangeExtension(screenshot, "." + suffix + ".png"));
    }
    internal static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout(); Pump();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); png.Save(output);
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
