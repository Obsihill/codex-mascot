using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodexMascot.App;
using CodexMascot.Core;

internal static class LibrarySettingsTests
{
    public static void Run(Action<bool, string> check, string dir)
    {
        var context = SynchronizationContext.Current;
        var store = new LibraryStore(Path.Combine(dir, "state-settings.json"));
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, new CustomizationManager());
        var window = new Window { Content = dashboard, Width = 1220, Height = 900, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); window.UpdateLayout();
            T Control<T>(string name) => (T)dashboard.FindName(name);
            void Click(string name) => Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Toggle(string name, bool? value)
            { var c = Control<CheckBox>(name); c.IsChecked = value; c.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); }
            LibraryMascot Mascot() => store.Library.Installed.Single(m => m.Id == "original");
            var volume = Control<NumericDragInput>("VolumeInput");
            check(volume.Maximum == 2 && volume.Value == 1 && new GlobalConfiguration().MasterVolume == 1, "volume range is 0-200 with new library/master defaults at 100");
            var states = Control<ComboBox>("PreviewState");
            check(states.Items.Count == 6 && states.Visibility == Visibility.Collapsed && states.Items.Cast<object>().All(s => !s.ToString()!.Contains("대표")), "cover cannot be selected as playback state and whole scope starts collapsed");
            Click("StateSettingsButton"); volume.CommitValue(2);
            Control<NumericDragInput>("SpeedInput").CommitValue(1.5);
            Toggle("LoopCheck", true); Toggle("PlayCheck", false);
            dashboard.SavePosition("original", MascotState.Completed, 30, 60);
            check(Mascot().Settings(MascotState.Completed).Volume == 2 && Mascot().Settings(MascotState.Running).Volume == 1, "individual volume changes affect only selected state");
            var saved = new LibraryStore(Path.Combine(dir, "state-settings.json")).Library.Installed.Single(m => m.Id == "original");
            check(saved.Settings(MascotState.Completed).Volume == 2 && !saved.Events.Contains("completed"), "per-state boosted gain and playback toggle survive reload");
            check(LibraryStore.Placement(saved, new(), MascotState.Completed).CustomLeft == 30 && LibraryStore.Placement(saved, new(), MascotState.Running).Position != "custom", "live placement resolves the correct state's coordinates");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(dashboard, Path.ChangeExtension(screenshot, ".state.png"));
            Click("StateSettingsButton");
            check(volume.DisplayText == "?" && Control<NumericDragInput>("SpeedInput").DisplayText == "?", "whole scope shows question marks for mixed numeric values");
            check(Control<WrapPanel>("StatePlaybackOptions").Visibility == Visibility.Collapsed && dashboard.FindName("SoundCheck") is null && Control<Button>("PositionButton").Content.ToString()!.Contains('?'), "whole scope hides playback toggles and sound toggle is absent");
            if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(dashboard, Path.ChangeExtension(screenshot, ".mixed.png"));
            var mixedSnapshot = store.Snapshot();
            volume.BeginPointer(0); volume.MovePointer(40, false); volume.CancelEdit();
            check(volume.DisplayText == "?" && store.Snapshot() == mixedSnapshot, "cancelled mixed numeric drag restores question mark without changing state settings");
            volume.CommitValue(1.25);
            check(CustomizationManager.States.All(s => Mascot().Settings(s).Volume == 1.25) && Mascot().Settings(MascotState.Completed).Speed == 1.5 && Mascot().Settings(MascotState.Running).Speed == 1, "whole-scope volume changes only that field across states");
            check(dashboard.TryHistoryGesture(Key.Z, ModifierKeys.Control) && Mascot().Settings(MascotState.Completed).Volume == 2 && Mascot().Settings(MascotState.Running).Volume == 1, "Ctrl Z restores mixed per-state values");
            check(Control<TextBlock>("Feedback").Text.Contains("실행 취소") && Control<TextBlock>("Feedback").Text.Contains("볼륨"), "bottom history describes reverted setting");
            check(dashboard.TryHistoryGesture(Key.Y, ModifierKeys.Control) && Mascot().Settings(MascotState.Completed).Volume == 1.25, "Ctrl Y redoes setting");
            dashboard.TryHistoryGesture(Key.Z, ModifierKeys.Control);
            check(dashboard.TryHistoryGesture(Key.Z, ModifierKeys.Control | ModifierKeys.Shift) && Mascot().Settings(MascotState.Running).Volume == 1.25, "Ctrl Shift Z also redoes setting");
            check(!dashboard.TryHistoryGesture(Key.Z, ModifierKeys.None) && !dashboard.TryHistoryGesture(Key.Z, ModifierKeys.Control | ModifierKeys.Alt), "plain typing and unrelated key chords are not intercepted");
            volume.BeginPointer(0); volume.MovePointer(50, false); volume.MovePointer(150, false);
            check(volume.Value == 2 && Mascot().Settings(MascotState.Completed).Volume == 1.25, "numeric drag previews without persisting each movement");
            volume.EndPointer();
            dashboard.ReplayHistory(false);
            check(Mascot().Settings(MascotState.Completed).Volume == 1.25, "one numeric drag gesture undoes in one step");
            volume.CommitValue(.75);
            check(!dashboard.ReplayHistory(true), "new change after undo clears redo branch");
            Click("StateSettingsButton");
            check(Control<WrapPanel>("StatePlaybackOptions").IsVisible && !Control<Button>("ResetSettingsButton").IsVisible, "individual scope exposes playback and repeat only");
            Toggle("LoopCheck", false);
            check(!Mascot().Settings(MascotState.Completed).Loop, "individual looping can be turned off");
            Click("StateSettingsButton");
            var ratio = store.Library.InstalledPanelRatio;
            dashboard.SavePanelRatio(.7);
            check(new LibraryStore(Path.Combine(dir, "state-settings.json")).Library.InstalledPanelRatio == .7, "panel divider ratio persists");
            dashboard.ReplayHistory(false);
            check(store.Library.InstalledPanelRatio == ratio, "divider resize can be undone");
            dashboard.ReplayHistory(true);
            check(Control<RowDefinition>("InstalledRow").Height.Value == .7, "redo restores actual panel row ratio");
            dashboard.AddMascot("bot"); dashboard.ReplayHistory(false);
            check(!store.Library.Selected.Any(m => m.SourceId == "bot"), "undo removes newly selected mascot");
            dashboard.ReplayHistory(true); dashboard.DeleteMascot("bot"); dashboard.ReplayHistory(false);
            check(store.Library.Selected.Any(m => m.SourceId == "bot") && store.Library.Installed.Any(m => m.Id == "bot"), "undo delete restores installed package and selection together");
            foreach (var id in store.Library.Installed.Select(m => m.Id).ToArray()) dashboard.DeleteMascot(id);
            dashboard.ReplayHistory(false);
            check(store.Library.Installed.Count == 1 && Control<Button>("TestButton").IsEnabled && Control<Image>("PreviewImage").Source is not null, "undo last deletion rebinds preview and editor");
        }
        finally { dashboard.Shutdown(); window.Close(); SynchronizationContext.SetSynchronizationContext(context); }

        var history = new LibraryHistory(store);
        var before = store.Snapshot();
        try { history.Commit("failed edit", () => { store.Library.Selected.Clear(); throw new IOException("test"); }); }
        catch (IOException) { }
        check(store.Snapshot() == before && history.Undo() is null, "failed edit rolls back snapshot without history entry");
        history.Commit("register sample", () => store.Library.Installed.Add(new LibraryMascot { Id = "new" }));
        history.Undo(); check(store.Library.Installed.All(m => m.Id != "new"), "registration metadata can be undone");
        history.Redo(); check(store.Library.Installed.Any(m => m.Id == "new"), "registration metadata can be redone");
    }
}
