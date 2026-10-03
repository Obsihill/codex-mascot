using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexMascot.App;
using CodexMascot.Core;

internal static class StudioInteractionTests
{
    public static void Run(Action<bool, string> check, string dir)
    {
        var context = SynchronizationContext.Current;
        try { RunCore(check, dir); }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
    private static void RunCore(Action<bool, string> check, string dir)
    {
        var file = Path.Combine(dir, "studio-interaction.json");
        var store = TestLibrary.Create(file); var manager = new CustomizationManager();
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, manager);
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowInTaskbar = false, ShowActivated = false };
        T Control<T>(string name) => (T)dashboard.FindName(name);
        void Click(string name) => Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            host.Show(); Pump();
            store.Library.Selected.Clear();
            dashboard.AddMascot("bot"); var firstId = store.Library.Selected.Last().Id;
            dashboard.AddMascot("bot"); var secondId = store.Library.Selected.Last().Id;
            check(Control<ListBox>("InstalledList").SelectedItem is null && Control<ListBox>("SelectedList").SelectedIndex == 1, "inspector highlights only the selected copy being edited");
            LibraryMascot Second() => store.Library.Find(secondId)!;
            Control<NumericDragInput>("VolumeInput").CommitValue(.25);
            Control<NumericDragInput>("SpeedInput").CommitValue(1.5);
            check(firstId != secondId && store.Library.Find(firstId)!.Settings(MascotState.Completed).Volume == 1 && store.Library.Installed.Single(m => m.Id == "bot").Volume == 1, "duplicate selection has independent identity and settings, leaving template unchanged");
            check(Control<ListBox>("SelectedList").Items.Cast<object>().Select(c => c.GetType().GetProperty("Name")!.GetValue(c)).Distinct().Count() == 2, "duplicate cards have distinguishable numbered labels");
            var loaded = new LibraryStore(file);
            check(loaded.Library.Selected.Count == 2 && loaded.Library.Find(secondId)!.Settings(MascotState.Completed).Volume == .25, "duplicate selection settings and IDs survive restart");
            check(SameImage(Control<Image>("PreviewImage").Source, LibraryDashboard.LoadThumbnail(store.CoverPath(Second(), manager))), "whole-scope inspector displays the cover image");
            Click("StateSettingsButton");
            check(!SameImage(Control<Image>("PreviewImage").Source, LibraryDashboard.LoadThumbnail(store.CoverPath(Second(), manager))), "individual scope displays state artwork rather than the cover");
            check(Control<WrapPanel>("StatePlaybackOptions").IsVisible && dashboard.FindName("SoundCheck") is null, "individual scope has playback and loop without sound checkbox");
            Click("StateSettingsButton");
            var settingsActions = Control<StackPanel>("SelectedSettingsActions");
            var resetAction = Control<Button>("ResetSettingsButton");
            var applyAction = Control<Button>("ApplySettingsButton");
            check(!Control<WrapPanel>("StatePlaybackOptions").IsVisible && settingsActions.IsVisible &&
                  ReferenceEquals(settingsActions.Children[0], resetAction) && ReferenceEquals(settingsActions.Children[1], applyAction) &&
                  resetAction.Content?.ToString() == "" && applyAction.Content?.ToString() == "" &&
                  Pencil.GetIcon(resetAction) == PencilIconKind.Reset && Pencil.GetIcon(applyAction) == PencilIconKind.Apply &&
                  resetAction.ToolTip is ToolTip { Content: string resetTip } && resetTip.StartsWith("설정 초기화 ·") &&
                  applyAction.ToolTip is ToolTip { Content: string applyTip } && applyTip.StartsWith("설정 적용 ·"),
                "selected mascots show adjacent icon-only reset and apply controls with tooltips");
            check(SameImage(Control<Image>("PreviewImage").Source, LibraryDashboard.LoadThumbnail(store.CoverPath(Second(), manager))), "returning to whole scope restores the cover");

            var beforeDrag = store.Snapshot(); OverlayWindow? placement = null;
            OpenPosition(dashboard, (dialog, overlay, x, y) =>
            {
                placement = overlay;
                var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                check(!panel.Children.OfType<Button>().Any() && overlay.PlacementMode && overlay.IsVisible && IsWindowEnabled(new WindowInteropHelper(overlay).Handle), "position click immediately opens an enabled drag preview without a second button");
                check(!dashboard.IsTesting, "placement mode is separate from timed playback tests");
                var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
                var p1 = new Point(area.Left + 100, area.Top + 100);
                var p2 = new Point(area.Left + 140, area.Top + 130);
                overlay.BeginPlacementDrag(); Move(overlay, p1);
                check(new Point(x.Value, y.Value) == overlay.DesktopCenter, "X/Y follow the media center during native dragging");
                Move(overlay, p2);
                check(new Point(x.Value, y.Value) == overlay.DesktopCenter && store.Snapshot() == beforeDrag, "continued dragging updates center coordinates without generating per-pixel saves");
                overlay.CompletePlacementDrag();
                check(Second().Settings(MascotState.Completed).CustomLeft == overlay.DesktopCenter.X && Second().Settings(MascotState.Completed).Position == "custom-center" && store.Library.Find(firstId)!.Settings(MascotState.Completed).CustomLeft is null, "drag release saves only inspected selected instance's center");
                var settled = store.Snapshot();
                overlay.BeginPlacementDrag(); Move(overlay, p1); Move(overlay, p2); overlay.CompletePlacementDrag();
                check(store.Snapshot() == settled, "cancelled drag returning to start does not persist a new edit");
                var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
                if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(dialog, Path.ChangeExtension(screenshot, ".live-position.png"));
            });
            check(placement is not null && !placement.IsVisible, "closing coordinates also closes placement preview");
            dashboard.ReplayHistory(false);
            check(store.Snapshot() == beforeDrag, "one entire placement drag is one undo entry");
            dashboard.ReplayHistory(true);
            OpenPosition(dashboard, (_, overlay, x, y) =>
            {
                x.BeginTextEdit(); x.Editor.Text = "180"; x.TryCommitText();
                y.BeginTextEdit(); y.Editor.Text = "210"; y.TryCommitText(); Pump();
                check(Second().Settings(MascotState.Completed).CustomLeft == 180 && Second().Settings(MascotState.Completed).CustomTop == 210, "typing coordinates automatically persists and moves placement preview");
                var expected = OverlayWindow.PlacementBounds(LibraryStore.Placement(Second(), manager.Configuration.Global, MascotState.Completed));
                check(overlay.DesktopPosition == new Point(expected.Left, expected.Top), "coordinate input updates the actual placement window");
                var before = store.Snapshot();
                x.BeginPointer(0); x.MovePointer(60, false);
                check(x.Value == 240 && Math.Abs(overlay.DesktopCenter.X - 240) < 1 && store.Snapshot() == before, "numeric coordinate drag moves preview center live without saving before release");
                x.CancelEdit();
                check(Math.Abs(overlay.DesktopCenter.X - 180) < 1 && store.Snapshot() == before, "cancelled coordinate scrub restores original preview center");
                x.BeginPointer(0); x.MovePointer(30, false); x.EndPointer();
                check(Second().Settings(MascotState.Completed).CustomLeft == 210, "coordinate scrub release persists final value");
            });
            dashboard.ReplayHistory(false);
            check(Second().Settings(MascotState.Completed).CustomLeft == 180, "coordinate scrub is one undo step");
            var beforeSize = store.Snapshot();
            var originalGlobalScale = manager.Configuration.Global.Scale;
            check(LibraryStore.Placement(Second(), manager.Configuration.Global, MascotState.Completed).Scale == originalGlobalScale, "legacy size inherits the existing global configuration");
            OpenPosition(dashboard, (dialog, overlay, x, y) =>
            {
                var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                var size = panel.Children.OfType<NumericDragInput>().Single(n => n.Name == "PositionScale");
                dialog.UpdateLayout();
                check(size.ShowValueFill && size.Minimum == .4 && size.Maximum == 3 && size.TranslatePoint(new Point(), panel).Y > y.TranslatePoint(new Point(0, y.ActualHeight), panel).Y, "gray size bar sits below the shared X/Y row and supports 40 to 300 percent");
                var fixedCenter = overlay.DesktopCenter; var fixedCoordinates = new Point(x.Value, y.Value);
                size.BeginPointer(0); size.MovePointer(40, false); Pump();
                check(store.Snapshot() == beforeSize && Math.Abs(overlay.Width - 260 * size.Value) < 2, "size scrub resizes the actual preview before persisting");
                check((overlay.DesktopCenter - fixedCenter).Length < 1 && new Point(x.Value, y.Value) == fixedCoordinates, "resizing fixes both the actual media center and the displayed coordinates");
                size.CancelEdit(); Pump();
                check(size.Value == originalGlobalScale && store.Snapshot() == beforeSize, "cancelling size drag restores preview without saving");
                size.BeginTextEdit(); size.Editor.Text = "160%"; size.TryCommitText(); Pump();
                check(CustomizationManager.States.All(s => Second().Settings(s).Scale == 1.6) && Second().Scale == 1.6, "whole-scope size saves all states and the inspected copy's default");
                check(store.Library.Find(firstId)!.Scale is null && store.Library.Installed.Single(m => m.Id == "bot").Scale is null && manager.Configuration.Global.Scale == originalGlobalScale, "size edits leave sibling copies, installed template and legacy global size unchanged");
                var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
                if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(dialog, Path.ChangeExtension(screenshot, ".size.png"));
            });
            check(new LibraryStore(file).Library.Find(secondId)!.Settings(MascotState.Completed).Scale == 1.6, "per-copy size survives save and reload");
            dashboard.ReplayHistory(false); check(store.Snapshot() == beforeSize, "size edit is one reversible history entry");
            dashboard.ReplayHistory(true); check(Second().Scale == 1.6, "redo restores size");
            Click("StateSettingsButton");
            OpenPosition(dashboard, (dialog, _, _, _) =>
            {
                var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                panel.Children.OfType<NumericDragInput>().Single().CommitValue(.8);
            });
            check(Second().Settings(MascotState.Completed).Scale == .8 && Second().Settings(MascotState.Running).Scale == 1.6, "individual size affects only the selected event");
            Click("StateSettingsButton");
            check(Control<Button>("PositionButton").Content.ToString() == "위치 크기 변경 ??", "mixed sizes mark the placement button with double question marks");
            OpenPosition(dashboard, (dialog, _, _, _) =>
            {
                var size = ((StackPanel)((ScrollViewer)dialog.Content).Content).Children.OfType<NumericDragInput>().Single();
                check(size.DisplayText == "??" && size.ValueFillFraction == 0, "mixed size hides fill and displays double question marks");
                size.CommitValue(1.2);
            });
            check(CustomizationManager.States.All(s => Second().Settings(s).Scale == 1.2), "whole-scope size unifies mixed event sizes");
            var installedTemplate = store.Library.Installed.Single(m => m.Id == "bot");
            installedTemplate.Volume = .62; installedTemplate.Speed = 1.4; installedTemplate.Scale = .9;
            installedTemplate.Position = "top-left"; installedTemplate.Loop = true;
            installedTemplate.Events = new() { "running", "completed" };
            foreach (var state in CustomizationManager.States)
            {
                var settings = installedTemplate.Settings(state);
                settings.Volume = .62; settings.Speed = 1.4; settings.Scale = .9;
                settings.Position = "top-left"; settings.Loop = true;
                settings.HideBehindTaskbar = false; settings.HoldUntilClick = true;
                settings.ImageDurationMs = 1750;
            }
            Second().For(MascotState.Completed).Sound = null;
            Second().For(MascotState.Completed).Sounds.Clear();
            var beforeReset = store.Snapshot(); var cover = Second().CoverImage;
            SettingsTransferTestHelpers.QueueResponse(check, "설정 초기화 확인", false);
            Click("ResetSettingsButton");
            check(store.Snapshot() == beforeReset, "cancelling reset leaves selected and installed settings unchanged");
            SettingsTransferTestHelpers.QueueResponse(check, "설정 초기화 확인", true);
            Click("ResetSettingsButton");
            check(CustomizationManager.States.All(s => Second().Settings(s).Volume == .62 && Second().Settings(s).Speed == 1.4 &&
                  Second().Settings(s).Scale == .9 && Second().Settings(s).Position == "top-left" && Second().Settings(s).Loop &&
                  !Second().Settings(s).HideBehindTaskbar && Second().Settings(s).HoldUntilClick == true &&
                  Second().Settings(s).ImageDurationMs == 1750) &&
                  Second().Volume == .62 && Second().Speed == 1.4 && Second().Scale == .9 &&
                  Second().Events.SequenceEqual(installedTemplate.Events),
                "reset restores the installed mascot's actual overall and per-state settings, not app defaults");
            check(Second().CoverImage == cover && Second().For(MascotState.Completed).SoundCandidates().Count == 0 &&
                  installedTemplate.Settings(MascotState.Completed).Volume == .62 &&
                  store.Library.Selected.Count == 2 && Control<TextBlock>("Feedback").Text.Contains("초기화"),
                "reset preserves media, the installed original and other selections");
            dashboard.ReplayHistory(false);
            check(store.Snapshot() == beforeReset, "reset is fully reversible in one undo step");
            var selectedBeforeApply = Second();
            var templateBeforeApply = store.Library.Installed.Single(m => m.Id == "bot");
            var otherVolume = store.Library.Find(firstId)!.Settings(MascotState.Completed).Volume;
            var templateImage = templateBeforeApply.For(MascotState.Completed).Image;
            var beforeApply = store.Snapshot();
            SettingsTransferTestHelpers.QueueResponse(check, "설정 적용 확인", false);
            Click("ApplySettingsButton");
            check(store.Snapshot() == beforeApply, "cancelling apply leaves the installed template unchanged");
            SettingsTransferTestHelpers.QueueResponse(check, "설정 적용 확인", true);
            Click("ApplySettingsButton");
            check(templateBeforeApply.Volume == selectedBeforeApply.Volume && templateBeforeApply.Speed == selectedBeforeApply.Speed &&
                  templateBeforeApply.Scale == selectedBeforeApply.Scale && templateBeforeApply.Events.SequenceEqual(selectedBeforeApply.Events) &&
                  CustomizationManager.States.All(s => templateBeforeApply.Settings(s).Volume == selectedBeforeApply.Settings(s).Volume &&
                      templateBeforeApply.Settings(s).HideBehindTaskbar == selectedBeforeApply.Settings(s).HideBehindTaskbar) &&
                  templateBeforeApply.For(MascotState.Completed).Image == templateImage &&
                  templateBeforeApply.For(MascotState.Completed).SoundCandidates().Count > 0 &&
                  store.Library.Find(firstId)!.Settings(MascotState.Completed).Volume == otherVolume,
                "apply copies only settings to the installed template, preserving media and other selected copies");
            check(new LibraryStore(file).Library.Installed.Single(m => m.Id == "bot").Settings(MascotState.Completed).Volume ==
                  selectedBeforeApply.Settings(MascotState.Completed).Volume, "applied template settings survive restart");
            dashboard.ReplayHistory(false);
            check(store.Snapshot() == beforeApply, "apply is reversible in one undo step");
            dashboard.RemoveMascot(secondId);
            check(store.Library.Selected.Count == 1 && store.Library.Selected.Single().Id == firstId, "removing a duplicate removes only that exact instance");
            dashboard.ReplayHistory(false);
            check(store.Library.Find(secondId)!.Settings(MascotState.Completed).Volume == .25, "undo restores removed duplicate with its own settings");

            TestGroup(check, store, manager);
            var snapshotPath = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(snapshotPath)) LibraryFeatureTests.Capture(dashboard, Path.ChangeExtension(snapshotPath, ".duplicates.png"));
        }
        finally { dashboard.Shutdown(); host.Close(); }

        var editFile = Path.Combine(dir, "installed-edit.json");
        var editStore = TestLibrary.Create(editFile);
        var editDashboard = new LibraryDashboard(); editDashboard.Initialize(editStore, manager);
        var editHost = new Window { Content = editDashboard, Width = 1220, Height = 900, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            editHost.Show(); Pump();
            editDashboard.AddMascot("bot");
            var selected = editStore.Library.Selected.Single(m => m.SourceId == "bot");
            selected.Settings(MascotState.Completed).Volume = .37;
            var editButton = (Button)editDashboard.FindName("EditMascotButton");
            check(editButton.Visibility == Visibility.Collapsed, "installed-package editing is hidden for independently configured selected copies");
            var installedList = (ListBox)editDashboard.FindName("InstalledList");
            installedList.SelectedItem = installedList.Items.Cast<object>().Single(c =>
                ((LibraryMascot)c.GetType().GetProperty("Mascot")!.GetValue(c)!).Id == "bot");
            editHost.UpdateLayout();
            var resetButton = (Button)editDashboard.FindName("ResetSettingsButton");
            var testButton = (Button)editDashboard.FindName("TestButton");
            check(resetButton.Visibility == Visibility.Collapsed &&
                  ((Button)editDashboard.FindName("ApplySettingsButton")).Visibility == Visibility.Collapsed &&
                  editButton.Visibility == Visibility.Visible &&
                  editButton.Content.ToString() == "마스코트 수정" &&
                  editButton.TranslatePoint(new Point(), testButton).Y >= testButton.ActualHeight,
                "installed mascots hide Reset settings and show Edit mascot below Test");
            var beforeHiddenReset = editStore.Snapshot();
            resetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(editStore.Snapshot() == beforeHiddenReset, "installed mascots cannot reset settings through a hidden button event");
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows.OfType<MascotPackageEditor>().Single(w => w.IsVisible);
                check(editor.Title == "마스코트 수정" && editStore.Library.Installed.Single(m => m.Id == "bot").Name == "민트 봇",
                    "edit button opens the installed mascot's media editor without saving on entry");
                editor.Close();
            }));
            editButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var original = editStore.Library.Installed.Single(m => m.Id == "bot");
            var beforeEdit = editStore.Snapshot();
            var media = CustomizationManager.States.ToDictionary(MascotConfiguration.StateKey, s => original.For(s).Image);
            media["cover"] = original.CoverImage;
            media["completed"] = original.For(MascotState.Failed).Image;
            var replacement = MascotPackageEditor.BuildPackage("Renamed bot", media, new HashSet<string> { "completed" }, manager, original);
            check(editStore.Snapshot() == beforeEdit && replacement.Id == original.Id && replacement.InstalledAt == original.InstalledAt,
                "preparing an edit leaves the installed package unchanged and preserves its identity");
            check(editDashboard.UpdateInstalledMascot(replacement) && selected.Name == "Renamed bot" &&
                  selected.For(MascotState.Completed).Image == replacement.For(MascotState.Completed).Image &&
                  selected.Settings(MascotState.Completed).Volume == .37 && editStore.Library.Installed.Single(m => m.Id == "bot").Name == "Renamed bot",
                "editing installed media updates selected copies without resetting their playback settings");
            var reloadedEdit = new LibraryStore(editFile);
            check(reloadedEdit.Library.Installed.Single(m => m.Id == "bot").Name == "Renamed bot" &&
                  reloadedEdit.Library.Selected.Single(m => m.SourceId == "bot").Settings(MascotState.Completed).Volume == .37,
                "installed edits and selected-instance settings survive restart");
        }
        finally { editDashboard.Shutdown(); editHost.Close(); }

        var legacyFile = Path.Combine(dir, "muted-duplicates.json");
        File.WriteAllText(legacyFile, """{"installed":[{"id":"bot","media":"builtin:bot","volume":0.8,"states":{"completed":{"image":"builtin:bot/completed","soundEnabled":false}}}],"selectedIds":["bot","bot"]}""");
        var migrated = new LibraryStore(legacyFile);
        check(migrated.Library.Selected.Count == 2 && migrated.Library.Selected.Select(m => m.Id).Distinct().Count() == 2 && migrated.Library.Selected.All(m => m.Settings(MascotState.Completed).Volume == 0 && m.For(MascotState.Completed).SoundEnabled), "legacy duplicate IDs migrate to independent copies and old mute becomes zero volume");
        migrated.Save();
        check(new LibraryStore(legacyFile).Library.Selected.Count == 2 && !File.ReadAllText(legacyFile).Contains("selectedIds"), "legacy migration runs once without duplicate growth");
    }
    private static void TestGroup(Action<bool, string> check, LibraryStore store, CustomizationManager manager)
    {
        using var group = new MascotPresentationGroup();
        var copies = store.Library.Selected.ToArray();
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var fixture = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
        for (var i = 0; i < copies.Length; i++)
        {
            var config = copies[i].Settings(MascotState.Completed);
            config.Position = "custom"; config.CustomLeft = area.Left + 60 + i * 160; config.CustomTop = area.Top + 80;
            if (!string.IsNullOrWhiteSpace(fixture)) copies[i].For(MascotState.Completed).Image = fixture;
        }
        group.Show(store, manager, MascotState.Completed, false);
        check(group.Windows.Count == copies.Length && group.Windows.All(w => w.IsPresenting && !w.PlacementMode), "every selected duplicate presents simultaneously in an independent locked window");
        check(group.Windows.Select(w => w.DesktopPosition).Distinct().Count() == copies.Length, "simultaneous copies use independent positions");
        check(group.Windows.Select(w => w.Width).Distinct().Count() == copies.Length, "simultaneous copies apply their independent sizes to actual playback windows");
        if (!string.IsNullOrWhiteSpace(fixture))
        {
            for (var attempt = 0; attempt < 50 && group.Windows.Any(w => ((MediaElement)w.FindName("MascotVideo")).NaturalVideoWidth == 0); attempt++) Pump(100);
            check(group.Windows.All(w => ((MediaElement)w.FindName("MascotVideo")).Source is not null && ((MediaElement)w.FindName("MascotVideo")).NaturalVideoWidth > 0 && w.LastImageError is null), "duplicate MP4s both decode in simultaneous independent media players");
        }
        var firstWindow = group.Windows[0];
        copies[1].Events.Remove("completed"); group.RemoveIneligible(store.Library);
        check(group.Windows.Count == 1 && ReferenceEquals(group.Windows[0], firstWindow) && firstWindow.IsPresenting, "excluding an event closes only its instance without restarting siblings");
        copies[1].Events.Add("completed");
        group.Show(store, manager, MascotState.Failed, false);
        check(!firstWindow.IsVisible && group.Windows.Count == 2 && group.Windows.All(w => w.DisplayedState == MascotState.Failed), "new event replaces all old windows with matching state artwork");
        var active = group.Windows.ToArray(); var clicked = 0;
        group.Clicked += (_, _) => { clicked++; group.Clear(); };
        firstWindow = active[0];
        firstWindow.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        firstWindow.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        check(clicked == 1 && !group.IsPresenting && active.All(w => !w.IsVisible), "acknowledging a shared notification closes every window and player");
    }
    private static void OpenPosition(LibraryDashboard dashboard, Action<Window, OverlayWindow, NumericDragInput, NumericDragInput> action)
    {
        Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "위치 크기 변경");
            try
            {
                var overlay = Application.Current.Windows.OfType<OverlayWindow>().Single(w => w.Owner == dialog);
                var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                var row = panel.Children.OfType<Grid>().Single();
                action(dialog, overlay, row.Children.OfType<NumericDragInput>().Single(t => t.Name == "PositionX"), row.Children.OfType<NumericDragInput>().Single(t => t.Name == "PositionY"));
            }
            catch (Exception e) { failure = e; }
            finally { dialog.Close(); }
        }));
        ((Button)dashboard.FindName("PositionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure is not null) throw failure;
    }
    private static bool SameImage(ImageSource? a, ImageSource? b) => a is not null && b is not null && Pixels(a).SequenceEqual(Pixels(b));
    private static byte[] Pixels(ImageSource source)
    {
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, 64, 64));
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var buffer = new byte[64 * 64 * 4]; bitmap.CopyPixels(buffer, 64 * 4, 0); return buffer;
    }
    private static void Move(OverlayWindow overlay, Point point)
    { SetWindowPos(new WindowInteropHelper(overlay).Handle, IntPtr.Zero, (int)point.X, (int)point.Y, 0, 0, 0x0001 | 0x0004 | 0x0010); }
    private static void Pump(int ms = 0)
    {
        var frame = new DispatcherFrame();
        if (ms == 0) Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        else { var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); }
        Dispatcher.PushFrame(frame);
    }
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
}
