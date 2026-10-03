using System.Windows;
using System.Windows.Controls;
using CodexMascot.App;
using CodexMascot.Core;

internal static class TaskbarOcclusionTests
{
    internal static void Options(Action<bool, string> check, string dir)
    {
        var file = System.IO.Path.Combine(dir, "taskbar-options.json");
        var store = TestLibrary.Create(file);
        var manager = new CustomizationManager();
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, manager);
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            host.Show(); host.UpdateLayout();
            var list = (ListBox)dashboard.FindName("SelectedList"); list.SelectedIndex = 0;
            var card = list.SelectedItem;
            var id = ((LibraryMascot)card.GetType().GetProperty("Mascot")!.GetValue(card)!).Id;
            LibraryMascot Mascot() => store.Library.Find(id)!;
            var toggle = (CheckBox)dashboard.FindName("TaskbarCheck");
            void Set(bool value) { toggle.IsChecked = value; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); }
            void Scope() => ((Button)dashboard.FindName("StateSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(toggle.IsVisible && toggle.IsChecked == true, "whole-scope taskbar checkbox defaults on");
            var editor = (StackPanel)dashboard.FindName("EditorPanel");
            var speed = (FrameworkElement)dashboard.FindName("SpeedInput");
            host.UpdateLayout();
            var visibleOptions = editor.Children.Cast<UIElement>().Where(c => c.Visibility == Visibility.Visible).ToArray();
            check(Array.IndexOf(visibleOptions, toggle) == Array.IndexOf(visibleOptions, speed) + 1,
                "whole scope places taskbar checkbox immediately after playback speed");
            Set(false);
            check(CustomizationManager.States.All(s => !Mascot().Settings(s).HideBehindTaskbar), "whole-scope checkbox applies to all states");
            Scope();
            ((ComboBox)dashboard.FindName("PreviewState")).SelectedIndex = 3;
            check(toggle.IsVisible && toggle.IsChecked == false, "state-scope checkbox displays selected setting");
            host.UpdateLayout();
            var hold = (FrameworkElement)dashboard.FindName("HoldCheck");
            var options = (FrameworkElement)dashboard.FindName("StatePlaybackOptions");
            check(editor.Children.IndexOf(toggle) == editor.Children.IndexOf(options) + 1 &&
                toggle.TranslatePoint(new Point(), editor).Y >= hold.TranslatePoint(new Point(0, hold.ActualHeight), editor).Y,
                "state scope places taskbar checkbox on the line below hold-until-click");
            Set(true);
            check(Mascot().Settings(MascotState.Completed).HideBehindTaskbar && !Mascot().Settings(MascotState.Running).HideBehindTaskbar, "state toggle does not change other states");
            check(LibraryStore.Placement(Mascot(), manager.Configuration.Global, MascotState.Completed).HideBehindTaskbar &&
                !LibraryStore.Placement(Mascot(), manager.Configuration.Global, MascotState.Running).HideBehindTaskbar, "overlay placement receives per-state taskbar preference");
            Scope();
            check(toggle.IsChecked is null, "whole-scope checkbox shows mixed state values");
            Set(false);
            dashboard.ReplayHistory(false);
            check(toggle.IsChecked is null && Mascot().Settings(MascotState.Completed).HideBehindTaskbar, "undo restores mixed taskbar settings");
            dashboard.ReplayHistory(true);
            check(toggle.IsChecked == false && CustomizationManager.States.All(s => !Mascot().Settings(s).HideBehindTaskbar), "redo restores whole-scope change");
            var saved = new LibraryStore(file).Library.Find(id)!;
            check(CustomizationManager.States.All(s => !saved.Settings(s).HideBehindTaskbar), "taskbar preference persists on reload");
            SettingsTransferTestHelpers.QueueResponse(check, "설정 초기화 확인", true);
            ((Button)dashboard.FindName("ResetSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(toggle.IsChecked == true, "reset restores default taskbar occlusion");
        }
        finally { dashboard.Shutdown(); host.Close(); }
        var content = new System.Windows.Controls.Grid { Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 20, 20)) };
        var window = new Window { Content = content };
        using (var occlusion = new TaskbarOcclusion(window, content))
        {
            occlusion.Enabled = false;
            check(content.Clip is null, "disabling occlusion removes existing clip immediately");
        }
        window.Close();
    }
    internal static void Run(Action<bool, string> check)
    {
        var size = new Size(260, 280);
        check(TaskbarOcclusion.CreateClip(size, Array.Empty<Rect>()) is null, "hidden taskbars leave mascot unclipped");
        check(TaskbarOcclusion.CreateClip(size, new[] { new Rect(500, 0, 40, 1000) }) is null, "other monitor's nonoverlapping taskbar does not clip mascot");
        var bottom = TaskbarOcclusion.CreateClip(size, new[] { new Rect(-1000, 240, 2000, 40) })!;
        check(bottom.FillContains(new Point(130, 230)) && !bottom.FillContains(new Point(130, 260)), "bottom taskbar occludes only overlapping mascot pixels");
        var sides = TaskbarOcclusion.CreateClip(size, new[] { new Rect(-20, -20, 50, 500), new Rect(230, 0, 50, 500), new Rect(0, -20, 260, 40) })!;
        check(!sides.FillContains(new Point(10, 100)) && !sides.FillContains(new Point(250, 100)) && !sides.FillContains(new Point(130, 10)) && sides.FillContains(new Point(130, 100)), "multiple taskbars and negative local coordinates are excluded");
        var full = TaskbarOcclusion.CreateClip(size, new[] { new Rect(-100, -100, 1000, 1000) })!;
        check(!full.FillContains(new Point(130, 140)) && full.IsFrozen, "fully covered mascot is invisible and geometry is frozen");
        check(TaskbarOcclusion.CreateClip(size, new[] { new Rect(0, 280, 260, 40) }) is null, "nonoverlapping taskbar edge does not cut mascot");
    }
}
