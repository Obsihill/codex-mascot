using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexMascot.App;
using CodexMascot.Core;

internal static class StudioControlsTests
{
    public static void Run(Action<bool, string> check, string dir)
    {
        var context = SynchronizationContext.Current;
        try { Numeric(check); Usage(check, dir); SortUi(check, dir); CardActions(check, dir); Settings(check); }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
    private static void Numeric(Action<bool, string> check)
    {
        var input = new NumericDragInput { Label = "볼륨", Value = 1, Minimum = 0, Maximum = 2, DisplayScale = 100, UnitsPerPixel = .5, Suffix = "%", Width = 320 };
        var host = new Window { Content = input, Width = 380, Height = 120, ShowInTaskbar = false, ShowActivated = false };
        var commits = 0; input.ValueCommitted += (_, _) => commits++;
        try
        {
            host.Show(); host.UpdateLayout();
            check(input.Cursor == Cursors.SizeWE && input.DisplayText == "100%" && ((Border)input.Content).Background is not null, "entire numeric row has horizontal drag cursor and hit-testable surface");
            input.BeginPointer(0); input.MovePointer(40, false); input.MovePointer(80, false);
            check(input.Value == 1.4 && commits == 0, "volume scrubbing previews percentage without committing movements");
            input.EndPointer(); check(commits == 1, "release commits once");
            input.BeginPointer(0); input.MovePointer(20, true); input.EndPointer();
            check(input.Value == 1.41 && commits == 2, "shift drag offers ten-times finer adjustment");
            input.BeginPointer(0); input.MovePointer(1000, false); input.EndPointer();
            check(input.Value == 2, "volume drag clamps to 200 percent");
            input.BeginPointer(0); input.MovePointer(-1000, false); input.EndPointer();
            check(input.Value == 0, "volume drag clamps to zero");
            input.BeginPointer(0); input.EndPointer();
            check(input.IsEditing, "click without dragging opens direct text input");
            input.Editor.Text = "125%"; check(input.TryCommitText() && input.Value == 1.25 && !input.IsEditing, "direct percent input parses and commits");
            var count = commits;
            input.BeginTextEdit(); input.Editor.Text = "NaN";
            check(!input.TryCommitText() && input.IsEditing && input.Value == 1.25 && commits == count, "invalid or nonfinite text stays editable without corrupting data");
            input.CancelEdit(); check(!input.IsEditing && commits == count, "cancel restores original value without committing");
            input.BeginTextEdit(); input.Editor.Text = "-50"; input.TryCommitText();
            check(input.Value == 0, "direct input obeys bounds");
            input.Value = 1; input.IsMixed = true;
            input.BeginPointer(0); input.MovePointer(20, false); input.CancelEdit();
            check(input.Value == 1 && input.DisplayText == "?", "cancelled mixed scrub restores mixed state");
            input.BeginTextEdit(); input.Editor.Text = "100"; count = commits; input.TryCommitText();
            check(!input.IsMixed && commits == count + 1, "typing existing base value still unifies mixed values");
            input.Value = .75; check(commits == count + 1, "programmatic refresh never emits a user commit");
            input.BeginPointer(0); input.MovePointer(10, false); input.IsEnabled = false;
            check(input.Value == .75 && !input.IsScrubbing, "disabling mid-drag cancels unsaved preview");
            input.IsEnabled = true;

            var speed = new NumericDragInput { Value = 1, Minimum = .25, Maximum = 3, DecimalPlaces = 2, UnitsPerPixel = .01, Suffix = "×" };
            host.Content = speed;
            speed.BeginPointer(0); speed.MovePointer(25, false); speed.EndPointer();
            check(speed.Value == 1.25 && speed.DisplayText == "1.25×", "speed scrub retains fractional precision");
            speed.BeginTextEdit(); speed.Editor.Text = "2.75×"; speed.TryCommitText();
            check(speed.Value == 2.75, "direct speed input accepts unit suffix");
            speed.BeginTextEdit(); speed.Editor.Text = "Infinity";
            check(!speed.TryCommitText(), "infinite speed rejected"); speed.CancelEdit();
        }
        finally { host.Close(); }
    }
    private static void Usage(Action<bool, string> check, string dir)
    {
        double seconds = 0;
        var path = Path.Combine(dir, "preference.usage.json");
        var usage = new LibraryUsage(path, () => seconds);
        var library = new MascotLibrary { Installed = new()
        {
            new() { Id = "b", Name = "Beta", InstalledAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") },
            new() { Id = "a", Name = "Alpha", InstalledAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z") },
            new() { Id = "c", Name = "Charlie" }
        } };
        library.Selected.Clear();
        string Order() => string.Join(",", usage.Sort(library).Select(m => m.Id));
        library.InstalledSort = "name"; check(Order() == "a,b,c", "name sort is alphabetical");
        library.InstalledSort = "installed"; check(Order() == "a,b,c" && library.Installed[2].InstalledAt is null, "date sort puts newest first and leaves unknown legacy dates unknown");
        library.Installed[1].InstalledAt = library.Installed[0].InstalledAt;
        check(Order() == "b,a,c", "equal install dates preserve stable original order");
        library.Select("b"); library.Select("b"); usage.Track(library.Selected);
        seconds = 30; usage.Save();
        check(usage.Score("b") == 30, "duplicate selected copies count time once per installed source");
        library.Selected.RemoveAt(0); usage.Track(library.Selected); seconds = 45;
        check(usage.Score("b") == 45, "remaining duplicate keeps accruing preference time");
        library.Selected.Clear(); usage.Track(library.Selected); seconds = 55;
        check(usage.Score("b") == 45, "unselected source stops accruing");
        library.Select("a"); usage.Track(library.Selected); seconds = 85; usage.Save();
        library.InstalledSort = "preference"; check(Order() == "b,a,c", "preference sorts by cumulative selected time");
        seconds = 100;
        check(Order() == "a,b,c", "equal preference scores use deterministic name order");
        usage.Save(); var savedScore = usage.Score("a");
        var loaded = new LibraryUsage(path, () => seconds);
        seconds = 10000; check(loaded.Score("a") == savedScore, "preference survives restart without counting offline time");
        loaded.Track(library.Selected); seconds = 20000;
        check(loaded.Score("a") == savedScore + 60, "long suspension gap is capped rather than counted in full");
        var store = new LibraryStore(Path.Combine(dir, "usage-history.json"));
        var history = new LibraryHistory(store);
        history.Commit("sort", () => store.Library.InstalledSort = "name"); history.Undo();
        check(new LibraryUsage(path, () => seconds).Score("a") == savedScore, "undo snapshots do not rewind separately stored preference statistics");
        File.WriteAllText(Path.Combine(dir, "broken-usage.json"), "{broken");
        var broken = new LibraryUsage(Path.Combine(dir, "broken-usage.json"));
        check(broken.Warning is not null && Directory.GetFiles(dir, "broken-usage.json.backup-*").Length == 1, "corrupt usage is backed up and does not prevent opening studio");
    }
    private static void SortUi(Action<bool, string> check, string dir)
    {
        var file = Path.Combine(dir, "sort-ui.json"); var store = new LibraryStore(file);
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, new CustomizationManager());
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            host.Show(); host.UpdateLayout();
            var sort = (ComboBox)dashboard.FindName("InstalledSort"); var list = (ListBox)dashboard.FindName("InstalledList");
            var selected = store.Library.Selected.Select(m => m.Id).ToArray();
            var inspected = ((TextBlock)dashboard.FindName("DetailName")).Text;
            sort.SelectedValue = "name";
            var names = list.Items.Cast<object>().Select(c => (string)c.GetType().GetProperty("Name")!.GetValue(c)!).ToArray();
            check(names.SequenceEqual(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)), "installed list reflects name sort");
            check(new LibraryStore(file).Library.InstalledSort == "name", "sort choice persists across reload");
            check(selected.SequenceEqual(store.Library.Selected.Select(m => m.Id)) && inspected == ((TextBlock)dashboard.FindName("DetailName")).Text && list.SelectedItem is not null, "sorting preserves selected playback order and inspected card");
            dashboard.ReplayHistory(false); check(sort.SelectedValue?.ToString() == "installed", "undo restores sort choice and control");
            dashboard.ReplayHistory(true); check(sort.SelectedValue?.ToString() == "name", "redo reapplies name sort");
            sort.SelectedValue = "preference"; check(store.Library.InstalledSort == "preference", "preference is selectable");
        }
        finally { dashboard.Shutdown(); host.Close(); }
    }
    internal static Button CardButton(ListBox list, string id, string name)
    {
        var item = list.Items.Cast<object>().Single(c => ((LibraryMascot)c.GetType().GetProperty("Mascot")!.GetValue(c)!).Id == id);
        list.ScrollIntoView(item); list.UpdateLayout();
        var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
        return Descendants(container).OfType<Button>().Single(b => b.Name == name);
    }
    private static void CardActions(Action<bool, string> check, string dir)
    {
        var file = Path.Combine(dir, "card-actions.json"); var store = new LibraryStore(file);
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, new CustomizationManager());
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            host.Show(); host.UpdateLayout();
            var installed = (ListBox)dashboard.FindName("InstalledList");
            var selected = (ListBox)dashboard.FindName("SelectedList");
            check(dashboard.FindName("AddButton") is null && dashboard.FindName("RemoveButton") is null && dashboard.FindName("DeleteButton") is Button, "old add/remove footers are gone while installed deletion remains");
            var plus = CardButton(installed, "bot", "CardAddButton");
            check(plus.IsVisible && plus.HorizontalAlignment == HorizontalAlignment.Right && plus.VerticalAlignment == VerticalAlignment.Top && plus.Parent is Grid g && g.Children.OfType<Image>().Any(), "plus is always visible at upper-right of the installed thumbnail");
            var initialCount = store.Library.Selected.Count;
            plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var first = store.Library.Selected.Last().Id;
            check(store.Library.Selected.Count == initialCount + 1 && store.Library.Find(first)!.SourceId == "bot", "card plus adds its own mascot without requiring card selection");
            CardButton(installed, "bot", "CardAddButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var second = store.Library.Selected.Last().Id;
            check(first != second && store.Library.Selected.Count == initialCount + 2, "repeated plus clicks create independent copies");
            var minus = CardButton(selected, first, "CardRemoveButton");
            check(minus.IsVisible && minus.Parent is Grid thumbnail && thumbnail.Height == 64 && minus.HorizontalAlignment == HorizontalAlignment.Right && minus.VerticalAlignment == VerticalAlignment.Top, "minus is on the selected thumbnail, not the footer");
            minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(store.Library.Find(first) is null && store.Library.Find(second) is not null && store.Library.Installed.Any(m => m.Id == "bot"), "minus removes its exact duplicate, not the currently inspected copy or installed package");
            dashboard.ReplayHistory(false);
            check(store.Library.Find(first) is not null, "card minus is undoable");
            dashboard.ReplayHistory(true);
            check(new LibraryStore(file).Library.Find(first) is null && store.Library.Find(second) is not null, "card removal redo persists");
            plus = CardButton(installed, "cat", "CardAddButton");
            var buttonChild = Descendants(plus).OfType<Border>().First();
            var before = store.Snapshot();
            installed.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = buttonChild });
            check(store.Snapshot() == before, "double-click routing on a plus child cannot trigger a second card action");
            var field = typeof(LibraryDashboard).GetField("_dragId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            field.SetValue(dashboard, "bot");
            installed.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = buttonChild });
            check(field.GetValue(dashboard) is null, "pressing a card action clears drag candidate and cannot initiate a card drag");
            minus = CardButton(selected, second, "CardRemoveButton");
            selected.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = Descendants(minus).OfType<Border>().First() });
            check(store.Library.Find(second) is not null, "minus button double-click does not route to card removal");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(dashboard, Path.ChangeExtension(screenshot, ".cards.png"));
        }
        finally { dashboard.Shutdown(); host.Close(); }
    }
    private static void Settings(Action<bool, string> check)
    {
        var window = new SettingsWindow(new CustomizationManager()) { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout();
            var all = Descendants(window).ToArray();
            var buttons = all.OfType<Button>().Select(b => b.Content?.ToString() ?? "").ToArray();
            var checks = all.OfType<CheckBox>().Select(c => c.Content?.ToString() ?? "").ToArray();
            var numbers = all.OfType<NumericDragInput>().Select(n => n.Label).ToArray();
            check(!buttons.Any(b => b.Contains("테스트") || b.Contains("위치") || b.Contains("이미지")) && !numbers.Any(n => n.Contains("볼륨") || n.Contains("속도")), "common settings excludes studio media, preview, position, volume and playback-speed controls");
            check(checks.Contains("전체 알림 소리 사용") && checks.Contains("Windows 로그인 시 시작") && checks.Contains("항상 위"), "global notification and startup options remain accessible");
            check(numbers.Contains("모든 마스코트 크기") && !numbers.Any(n => n.Contains("표시 시간")) && buttons.Contains("공통 알림음 선택"), "common scale and sound remain while image duration is edited only in studio");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(window, Path.ChangeExtension(screenshot, ".settings.png"));
        }
        finally { window.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
