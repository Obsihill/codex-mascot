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
        var store = new LibraryStore(file); var manager = new CustomizationManager();
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
            check(!Control<WrapPanel>("StatePlaybackOptions").IsVisible && Control<Button>("ResetSettingsButton").IsVisible, "whole scope omits playback toggles and offers settings reset");
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
                check(x.Value == p1.X && y.Value == p1.Y, "X/Y follow live physical screen position before mouse release");
                Move(overlay, p2);
                check(x.Value == p2.X && y.Value == p2.Y && store.Snapshot() == beforeDrag, "continued dragging updates coordinates without generating per-pixel saves");
                overlay.CompletePlacementDrag();
                check(Second().Settings(MascotState.Completed).CustomLeft == p2.X && store.Library.Find(firstId)!.Settings(MascotState.Completed).CustomLeft is null, "drag release saves only inspected selected instance");
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
                check(x.Value == 240 && overlay.DesktopPosition.X == 240 && store.Snapshot() == before, "numeric coordinate drag moves preview live without saving before release");
                x.CancelEdit();
                check(overlay.DesktopPosition.X == 180 && store.Snapshot() == before, "cancelled coordinate scrub restores original preview");
                x.BeginPointer(0); x.MovePointer(30, false); x.EndPointer();
                check(Second().Settings(MascotState.Completed).CustomLeft == 210, "coordinate scrub release persists final value");
            });
            dashboard.ReplayHistory(false);
            check(Second().Settings(MascotState.Completed).CustomLeft == 180, "coordinate scrub is one undo step");
            var beforeReset = store.Snapshot(); var cover = Second().CoverImage;
            Click("ResetSettingsButton");
            check(CustomizationManager.States.All(s => Second().Settings(s).Volume == 1 && Second().Settings(s).Speed == 1 && Second().Settings(s).Position == "bottom-right" && !Second().Settings(s).Loop), "reset restores all state settings to defaults");
            check(Second().CoverImage == cover && store.Library.Selected.Count == 2 && Control<TextBlock>("Feedback").Text.Contains("초기화"), "reset preserves media and selections and records history description");
            dashboard.ReplayHistory(false);
            check(store.Snapshot() == beforeReset, "reset is fully reversible in one undo step");
            dashboard.RemoveMascot(secondId);
            check(store.Library.Selected.Count == 1 && store.Library.Selected.Single().Id == firstId, "removing a duplicate removes only that exact instance");
            dashboard.ReplayHistory(false);
            check(store.Library.Find(secondId)!.Settings(MascotState.Completed).Volume == .25, "undo restores removed duplicate with its own settings");

            TestGroup(check, store, manager);
            var snapshotPath = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(snapshotPath)) LibraryFeatureTests.Capture(dashboard, Path.ChangeExtension(snapshotPath, ".duplicates.png"));
        }
        finally { dashboard.Shutdown(); host.Close(); }

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
            var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "위치 선택");
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
