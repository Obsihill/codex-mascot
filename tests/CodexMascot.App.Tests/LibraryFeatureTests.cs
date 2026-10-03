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
        var store = TestLibrary.Create(file, new GlobalConfiguration { Position = "top-left", CustomLeft = 120 });
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
        check(placement.MonitorDevice == "test-screen" && placement.Position == "center" && placement.MasterVolume == 1 && global.MasterVolume == .4 && global.MonitorDevice == "default", "per-mascot placement ignores retired global volume without overwriting preferences");
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
        slots.Remove("failed");
        var partial = MascotPackageEditor.BuildPackage("Incomplete", slots, new HashSet<string>(), manager);
        check(partial.For(MascotState.Failed).Image is null && partial.CoverImage is not null,
            "registration permits an omitted state without borrowing another state's image");
        var empty = MascotPackageEditor.BuildPackage("Name only", new Dictionary<string, string?>(), new HashSet<string>(), manager);
        check(empty.CoverImage is null && empty.States.Count == 6 && empty.States.Values.All(m => m.Image is null) &&
              MascotFolderImport.Read(new MascotFolderLibrary(AppPaths.LibraryDirectory).PackageDirectory(empty.Id)).CoverImage is null,
            "name-only registration creates a portable package with no placeholder media");
        store.Library.Installed.Add(empty); store.Library.Select(empty.Id); store.Save();
        var emptyReloaded = new LibraryStore(file);
        check(emptyReloaded.Library.Installed.Single(m => m.Id == empty.Id).CoverImage is null &&
              emptyReloaded.Library.Installed.Single(m => m.Id == empty.Id).States.Values.All(m => m.Image is null) &&
              !emptyReloaded.Library.Eligible(MascotState.Completed).Any(m => m.SourceId == empty.Id),
            "empty media stays empty after reload and cannot open a missing-media notification");
        var rejected = false;
        try { MascotPackageEditor.BuildPackage(" ", new Dictionary<string, string?>(), new HashSet<string>(), manager); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "a mascot name is still required");
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
            installed.SelectedItem = installed.Items.Cast<object>().Single(c =>
                ((LibraryMascot)c.GetType().GetProperty("Mascot")!.GetValue(c)!).Id == empty.Id);
            check(!((Button)dashboard.FindName("TestButton")).IsEnabled &&
                  ((Image)dashboard.FindName("PreviewImage")).Source is null &&
                  ((TextBlock)dashboard.FindName("PreviewError")).Visibility == Visibility.Collapsed,
                "name-only mascots show a quiet empty preview and cannot start an empty media test");
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
                var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "위치 크기 변경");
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
            check(positioned.Position == "custom-center" && positioned.CustomLeft == -120 && positioned.CustomTop == 240, "position dialog persists signed desktop center coordinates");
            var toggle = (Button)dashboard.FindName("TestButton");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(dashboard.IsTesting && (string)toggle.Content == "중지" && Pencil.GetIcon(toggle) == PencilIconKind.Stop, "test button switches both label and pencil icon into stop while playing");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!dashboard.IsTesting && (string)toggle.Content == "테스트" && Pencil.GetIcon(toggle) == PencilIconKind.Play, "same button stops and resets test label and pencil icon");
            var videoFixture = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
            if (!string.IsNullOrWhiteSpace(videoFixture))
            {
                var inspected = store.Library.Installed.Single(m => m.Id == "ghost");
                var originalImage = inspected.For(MascotState.Completed).Image;
                inspected.For(MascotState.Completed).Image = videoFixture;
                if (((ComboBox)dashboard.FindName("PreviewState")).Visibility != Visibility.Visible)
                    ((Button)dashboard.FindName("StateSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((ComboBox)dashboard.FindName("PreviewState")).SelectedIndex = 3;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var testOverlay = Application.Current.Windows.OfType<OverlayWindow>().Single(w => w.IsVisible);
                var video = (MediaElement)testOverlay.FindName("MascotVideo");
                check(video.Source is not null && video.Volume == 0, "zero-volume event suppresses actual MP4 test audio");
                var timeout = (DispatcherTimer)typeof(LibraryDashboard).GetField("_testTimeout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dashboard)!;
                check(!timeout.IsEnabled, "video tests never start the ten-second image timeout");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!dashboard.IsTesting && !testOverlay.IsVisible, "test toggle closes video overlay");
                var originalLoop = inspected.Settings(MascotState.Completed).Loop;
                inspected.Settings(MascotState.Completed).Loop = false;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                testOverlay = Application.Current.Windows.OfType<OverlayWindow>().Single(w => w.IsVisible);
                video = (MediaElement)testOverlay.FindName("MascotVideo");
                video.RaiseEvent(new RoutedEventArgs(MediaElement.MediaEndedEvent)); Pump();
                check(!dashboard.IsTesting && !testOverlay.IsVisible && (string)toggle.Content == "테스트", "video end closes test and restores button without waiting for timeout");
                inspected.Settings(MascotState.Completed).Loop = true;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                testOverlay = Application.Current.Windows.OfType<OverlayWindow>().Single(w => w.IsVisible);
                ((MediaElement)testOverlay.FindName("MascotVideo")).RaiseEvent(new RoutedEventArgs(MediaElement.MediaEndedEvent)); Pump();
                check(dashboard.IsTesting && !timeout.IsEnabled, "looped video remains active until explicitly stopped");
                dashboard.StopTest(); inspected.Settings(MascotState.Completed).Loop = originalLoop;
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
        var oldCodexHome = manager.Configuration.Monitor.CodexHome;
        var oldClaudeHome = manager.Configuration.Monitor.ClaudeHome;
        manager.Configuration.Monitor.CodexHome = Path.Combine(dir, "fixture-codex-home");
        manager.Configuration.Monitor.ClaudeHome = Path.Combine(dir, "fixture-claude-home");
        manager.Configuration.Monitor.AutoStart = false; manager.Save();
        var main = new MainWindow();
        try
        {
            main.Show(); Pump();
            var mainManager = (CustomizationManager)typeof(MainWindow).GetField("_customization", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!;
            var allowedProject = Path.Combine(dir, "watched-project");
            var blockedProject = Path.Combine(dir, "excluded-project");
            var futureProject = Path.Combine(dir, "new-project");
            mainManager.Configuration.Monitor.AutoIncludeNewProjects = false;
            mainManager.Configuration.Monitor.Projects = new()
            {
                new() { Path = allowedProject, Enabled = true }, new() { Path = blockedProject, Enabled = false }
            };
            main.ReceiveMonitoredEvent(AgentKind.Codex, new(CodexEventKind.ThreadStarted, "Codex 기록", "included") { ProjectPath = allowedProject, IsReplay = true });
            mainManager.Configuration.Monitor.Chats.Add(new() { Agent = AgentKind.Codex, Id = "excluded-test-chat", ProjectPath = allowedProject, Enabled = false });
            main.ReceiveMonitoredEvent(AgentKind.Codex, new(CodexEventKind.ThreadStarted, "Codex 기록", "excluded-test-chat") { ProjectPath = allowedProject, IsReplay = true });
            main.ReceiveMonitoredEvent(AgentKind.Codex, new(CodexEventKind.TurnStarted, "Codex Hook", "excluded-test-chat", "turn") { IsReplay = true });
            main.ReceiveMonitoredEvent(AgentKind.Codex, new(CodexEventKind.ThreadStarted, "Codex 기록", "blocked") { ProjectPath = blockedProject, IsReplay = true });
            main.ReceiveMonitoredEvent(AgentKind.Codex, new(CodexEventKind.ThreadStarted, "Codex 기록", "future") { ProjectPath = futureProject, IsReplay = true });
            main.ReceiveMonitoredEvent(AgentKind.Codex, new(CodexEventKind.TurnStarted, "Codex Hook", "blocked", "turn") { IsReplay = true });
            main.ReceiveMonitoredEvent(AgentKind.Claude, new(CodexEventKind.ThreadStarted, "Claude Hook", "unknown") { IsReplay = true });
            check(main.JobsListView.Items.Count == 1, "excluded projects, new blocked projects, and pathless hooks cannot enter the job list");
            check(mainManager.Configuration.Monitor.Projects.Any(p => p.Path == futureProject && !p.Enabled), "new blocked project is discoverable for later manual inclusion");
            main.ReceiveMonitoredEvent(AgentKind.Codex, new(CodexEventKind.ThreadStatusChanged, "Codex Hook", "included", Status: "idle") { IsReplay = true });
            check(main.JobsListView.Items.Count == 1, "included project accepts pathless follow-up events through the session mapping");
            check(main.Title == "Agent Mascot" && ((TextBlock)main.Template.FindName("WindowTitle", main)).Text == "Agent Mascot", "native and pencil caption titles use Agent Mascot");
            var tray = (System.Windows.Forms.NotifyIcon)typeof(MainWindow).GetField("_tray", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!;
            check(tray.Text.StartsWith("Agent Mascot", StringComparison.Ordinal) && tray.ContextMenuStrip!.Items[0].Text == "Agent Mascot 열기", "tray tooltip and open menu use Agent Mascot");
            typeof(MainWindow).GetMethod("RefreshJobs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(main, null);
            check(tray.Text.StartsWith("Agent Mascot · ", StringComparison.Ordinal), "tray status updates retain Agent Mascot branding");
            CaptureDialog(main, "window");
            PencilWindowTests.Execute(PencilWindowTests.Caption(main, "Maximize"));
            CaptureDialog(main, "window-maximized");
            PencilWindowTests.Execute(PencilWindowTests.Caption(main, "Maximize"));
            PencilWindowTests.Execute(PencilWindowTests.Caption(main, "Close"));
            check(!main.IsVisible, "pencil caption close hides studio without terminating it");
            main.Tray_OnMouseDoubleClick(null, new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Right, 2, 0, 0, 0)); Pump();
            check(!main.IsVisible, "tray right double-click does not open studio");
            main.Tray_OnMouseDoubleClick(null, new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 2, 0, 0, 0)); Pump();
            check(main.IsVisible && ((LibraryDashboard)main.FindName("Dashboard")).IsVisible, "tray left double-click reopens mascot studio");
            main.WindowState = WindowState.Minimized;
            main.Tray_OnMouseDoubleClick(null, new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 2, 0, 0, 0)); Pump();
            check(main.WindowState == WindowState.Normal && Application.Current.Windows.OfType<MainWindow>().Count() == 1, "tray double-click restores minimized studio without another instance");
            main.OpenWorkspaceSettings(); Pump();
            check(main.Icon is not null, "studio window uses packaged mascot icon");
            var footerArt = (Image)((LibraryDashboard)main.FindName("Dashboard")).FindName("FooterMascotArt");
            check(footerArt.Source is not null && !footerArt.IsHitTestVisible && !footerArt.Focusable, "footer artwork is packaged and cannot intercept interaction");
            check(AppBrand.TrayIconUri != AppBrand.IconUri, "tray artwork resource remains independent of executable icon");
            using (var trayIcon = AppBrand.CreateTrayIcon()) check(trayIcon.Width == 32 && trayIcon.Height == 32, "tray icon loads packaged 32px artwork independently of its stream");
            var settings = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "설정");
            var settingsTabs = ((SettingsWindow)settings).Pages;
            check(settingsTabs.Items.Cast<TabItem>().Select(t => (string)t.Header).SequenceEqual(new[] { "일반", "알림", "연결", "정보", "언어 / Language" }), "studio settings includes language and excludes legacy file and sound controls");
            CaptureDialog(settings, "settings-overview");
            settingsTabs.SelectedIndex = 2; Pump();
            var list = (ListView)main.FindName("JobsListView");
            check(main.FindName("HooksToggle") is null && main.FindName("StartButton") is null && main.FindName("PromptTextBox") is null, "manual hook toggle and task launcher are removed");
            check(!list.Focusable && !list.IsTabStop && list.ItemContainerStyle.Setters.OfType<Setter>().Any(s => s.Property == UIElement.IsHitTestVisibleProperty && Equals(s.Value, false)), "monitor list cannot receive row clicks or keyboard selection");
            var themeScreenshots = Environment.GetEnvironmentVariable("MASCOT_THEME_SCREENSHOT_DIR");
            var previousDark = PencilPalette.Current.IsDark;
            foreach (var dark in new[] { true, false })
            {
                AppTheme.Apply(dark); Pump();
                var headers = VisualChildren(list).OfType<GridViewColumnHeader>().ToArray();
                check(headers.Length > 0 && headers.All(h => ReferenceEquals(h.Background, PencilPalette.Inset) && ReferenceEquals(h.Foreground, PencilPalette.Ink)), "monitor headers follow the app theme without native white gradients");
                if (!string.IsNullOrWhiteSpace(themeScreenshots))
                { Directory.CreateDirectory(themeScreenshots); Capture(settings, Path.Combine(themeScreenshots, "connections-" + (dark ? "dark" : "light") + ".png")); }
            }
            AppTheme.Apply(previousDark);
            check(settings.IsVisible && ((Grid)settings.Content).IsVisible, "connection controls open in settings window");
            check(PencilWindowTests.Caption(settings, "Close").IsVisible, "connection settings use pencil window chrome");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) Capture(settings, Path.ChangeExtension(screenshot, ".connections.png"));
            settings.Close(); main.OpenWorkspaceSettings(); Pump();
            check(Application.Current.Windows.OfType<Window>().Count(w => w.Title == "설정") == 1, "settings can reopen without losing or duplicating controls");
            Application.Current.Windows.OfType<SettingsWindow>().Single().Close();
            var projectsButton = (Button)main.Dashboard.FindName("ProjectSelectionButton");
            var footer = (StackPanel)main.Dashboard.FindName("FooterActions");
            check(projectsButton.Content?.ToString() == "감시 프로젝트 선택" && footer.Children.IndexOf(projectsButton) < footer.Children.IndexOf((UIElement)main.Dashboard.FindName("AppSettingsButton")), "monitored-project selector is left of settings in the studio footer");
            main.ReceiveMonitoredEvent(AgentKind.Codex, new(CodexEventKind.TurnStarted, "Codex Hook", "excluded-test-chat", "current") { ProjectPath = allowedProject, IsReplay = true });
            main.ReceiveMonitoredEvent(AgentKind.Codex, new(CodexEventKind.TurnStarted, "Codex Hook", "blocked", "current") { ProjectPath = blockedProject, IsReplay = true });
            projectsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            check(Application.Current.Windows.OfType<ProjectSelectionWindow>().Count() == 1, "project button opens the project selection window");
            var picker = Application.Current.Windows.OfType<ProjectSelectionWindow>().Single();
            for (var attempt = 0; attempt < 30 && picker.ProjectChoices.Count == 0; attempt++)
            { System.Threading.Thread.Sleep(10); Pump(); }
            check(picker.ProjectChoices.ContainsKey(allowedProject) && picker.ProjectChoices.ContainsKey(blockedProject) &&
                  !picker.ProjectChoices.ContainsKey(futureProject), "picker shows actively monitored chats outside the recent file scan, including excluded chats, but hides saved inactive history");
            picker.Close();
        }
        finally { main.ExitApplication(); manager.Configuration.Monitor.AutoStart = autoStart; manager.Configuration.Monitor.CodexHome = oldCodexHome; manager.Configuration.Monitor.ClaudeHome = oldClaudeHome; manager.Save(); }
    }
    private static void CaptureDialog(Window dialog, string suffix)
    {
        var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(screenshot)) Capture(dialog, Path.ChangeExtension(screenshot, "." + suffix + ".png"));
    }
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in VisualChildren(child)) yield return nested;
        }
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
