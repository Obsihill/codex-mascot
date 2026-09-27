using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexMascot.App;
using CodexMascot.Core;

internal static class SequenceTest
{
    internal static void Run(Action<bool, string> check, string dir)
    {
        var context = SynchronizationContext.Current;
        var designData = new LibraryDashboardDesignData();
        check(designData.Installed.Count == 2 && designData.Selected.Count == 1 && designData.Preview.Width > 0, "designer samples load packaged images without initializing the app library");
        var store = new LibraryStore(Path.Combine(dir, "sequence.json"));
        foreach (var m in store.Library.Installed.Concat(store.Library.Selected))
            foreach (var state in CustomizationManager.States) { m.Settings(state).Volume = 0; m.Settings(state).ImageDurationMs = 10000; m.Settings(state).Loop = true; }
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, new CustomizationManager());
        check(dashboard.DataContext is not LibraryDashboardDesignData, "designer data does not replace runtime library data");
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowActivated = false, ShowInTaskbar = false };
        var toggle = (Button)dashboard.FindName("TestButton");
        try
        {
            host.Show(); host.UpdateLayout();
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var state in CustomizationManager.States)
            {
                check(dashboard.IsTesting && dashboard.TestingState == state, "whole test visits state in order: " + state);
                dashboard.AdvanceTest();
            }
            check(!dashboard.IsTesting && (string)toggle.Content == "테스트", "whole sequence finishes and resets test toggle");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dashboard.AdvanceTest();
            check(!dashboard.IsTesting, "stop cancels every remaining state");
            var video = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
            if (!string.IsNullOrWhiteSpace(video))
            {
                store.Library.Installed[0].For(MascotState.Idle).Image = video;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var overlay = Application.Current.Windows.OfType<OverlayWindow>().Single(w => w.IsVisible);
                ((MediaElement)overlay.FindName("MascotVideo")).RaiseEvent(new RoutedEventArgs(MediaElement.MediaEndedEvent));
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                check(dashboard.TestingState == MascotState.Running, "whole test advances on video end even when saved looping is enabled");
                dashboard.StopTest();
            }
            foreach (var name in new[] { "InstalledList", "SelectedList" })
            {
                var list = (ListBox)dashboard.FindName(name); list.UpdateLayout();
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                var label = Descendants(item).OfType<TextBlock>().First(t => t.Name == "CardCaption");
                check(label.Parent is Border background && background.Name == "CardCaptionBackground" && background.Background is SolidColorBrush brush && brush.Color.R == 255 && brush.Color.G == 255 && brush.Color.B == 255 && brush.Color.A is > 0 and < 255, "card caption has separate editable translucent white box: " + name);
                check(((Border)label.Parent).Margin == new Thickness(5, 0, 5, 7) && ((Border)label.Parent).Style.Triggers.OfType<DataTrigger>().Any(t => t.EnterActions.Count > 0 && t.ExitActions.Count > 0), "caption uses inset margins and reversible hover fade: " + name);
            }
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(dashboard, Path.ChangeExtension(screenshot, ".caption-bars.png"));
        }
        finally { dashboard.Shutdown(); host.Close(); SynchronizationContext.SetSynchronizationContext(context); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
