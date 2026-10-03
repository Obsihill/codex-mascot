using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMascot.App;

internal static class AppThemeTests
{
    internal static void Run(Action<bool, string> check, string dir)
    {
        var dark = false;
        using var theme = new AppTheme(() => dark);
        var store = TestLibrary.Create(System.IO.Path.Combine(dir, "dark-theme-library.json"));
        var manager = new CustomizationManager();
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, manager);
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowActivated = false, ShowInTaskbar = false };
        PencilWindow.Apply(host);
        SettingsWindow? settings = null;
        try
        {
            host.Show(); Pump(host);
            var original = store.Snapshot();
            var lightSurface = PencilPalette.Surface;
            var lightLogo = ((Image)dashboard.FindName("FooterMascotArt")).Source;
            var speed = (NumericDragInput)dashboard.FindName("SpeedInput");
            var value = speed.Value;
            dark = true; theme.Refresh(); Pump(host);
            check(PencilPalette.Current.IsDark && PencilPalette.Surface.Color.R < 64, "app mode selects dark surfaces");
            var paper = (DrawingGroup)PencilPalette.Paper.Drawing;
            foreach (var brush in new[] { PencilPalette.Surface, PencilPalette.Inset, PencilPalette.ValueFill,
                PencilPalette.Selected, PencilPalette.Badge, PencilPalette.Ink, PencilPalette.Muted, PencilPalette.Line }
                .Concat(paper.Children.OfType<GeometryDrawing>().Select(d => (SolidColorBrush)d.Brush)))
                check(brush.Color.R == brush.Color.G && brush.Color.G == brush.Color.B, "dark neutral surfaces and text have no green tint: " + brush.Color);
            foreach (var (name, expected) in new[] { ("TestButton", PencilPalette.AccentButton), ("RegisterButton", PencilPalette.Button) })
            {
                var button = (Button)dashboard.FindName(name);
                var color = expected.Color;
                check(ReferenceEquals(button.Background, expected) && color.G < 100 && color.G > color.R && color.G > color.B,
                    name + " uses a dark green fill");
                check(ReferenceEquals(button.Foreground, PencilPalette.OnButton) && Contrast(PencilPalette.OnButton.Color, color) >= 4.5,
                    name + " retains readable light text");
            }
            check(ReferenceEquals(dashboard.Background, PencilPalette.Paper) && ReferenceEquals(dashboard.Foreground, PencilPalette.Ink), "existing dashboard background and text update live");
            check(!ReferenceEquals(lightLogo, ((Image)dashboard.FindName("FooterMascotArt")).Source), "dark mode uses bright footer logo");
            check(ReferenceEquals(speed.Editor.Foreground, PencilPalette.Ink) && speed.Value == value && store.Snapshot() == original, "numeric fields recolor without changing settings");
            var fill = (LinearGradientBrush)((Border)speed.Content).Background;
            check(fill.GradientStops[0].Color == PencilPalette.ValueFill.Color && fill.GradientStops[3].Color == PencilPalette.Surface.Color, "numeric gradient is rebuilt for dark mode");
            foreach (var (foreground, background) in new[] { (PencilPalette.Ink, PencilPalette.Surface), (PencilPalette.Muted, PencilPalette.Surface),
                (PencilPalette.OnButton, PencilPalette.Button), (PencilPalette.OnButton, PencilPalette.AccentButton),
                (PencilPalette.Accent, PencilPalette.Surface),
                (PencilPalette.OrangeInk, PencilPalette.OrangeSurface), (PencilPalette.Danger, PencilPalette.Surface) })
                check(Contrast(foreground.Color, background.Color) >= 4.5, "dark text contrast at least 4.5:1 for " + foreground.Color);
            settings = new SettingsWindow(manager); settings.Show(); Pump(settings);
            check(ReferenceEquals(settings.Background, PencilPalette.Paper), "new settings window inherits dark theme");
            CheckSectionHeaders(settings, check);
            var screenshotDir = Environment.GetEnvironmentVariable("MASCOT_THEME_SCREENSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(screenshotDir))
            {
                System.IO.Directory.CreateDirectory(screenshotDir);
                LibraryFeatureTests.Capture(host, System.IO.Path.Combine(screenshotDir, "app-dark.png"));
                LibraryFeatureTests.Capture(settings, System.IO.Path.Combine(screenshotDir, "settings-dark.png"));
            }
            dark = false; theme.Refresh(); Pump(host); Pump(settings);
            check(ReferenceEquals(lightSurface, PencilPalette.Surface) && ReferenceEquals(dashboard.Foreground, PencilPalette.Ink), "switching back restores original cached light palette");
            check(ReferenceEquals(lightLogo, ((Image)dashboard.FindName("FooterMascotArt")).Source), "switching back restores black logo");
            check(ReferenceEquals(settings.Background, PencilPalette.Paper), "open settings window switches back to light");
            CheckSectionHeaders(settings, check);
            if (!string.IsNullOrWhiteSpace(screenshotDir))
            {
                LibraryFeatureTests.Capture(host, System.IO.Path.Combine(screenshotDir, "app-light.png"));
                LibraryFeatureTests.Capture(settings, System.IO.Path.Combine(screenshotDir, "settings-light.png"));
            }
            theme.Dispose(); dark = true; theme.Refresh();
            check(!PencilPalette.Current.IsDark, "disposed theme watcher stops updating");
        }
        finally { settings?.Close(); dashboard.Shutdown(); host.Close(); AppTheme.Apply(false); }
    }
    private static void Pump(Window window)
    { window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    private static void CheckSectionHeaders(SettingsWindow settings, Action<bool, string> check)
    {
        var page = (StackPanel)((ScrollViewer)((TabItem)settings.Pages.Items[0]).Content).Content;
        var groups = page.Children.OfType<Grid>().ToArray();
        check(groups.Length == 2, "general settings retain startup and mascot sections");
        foreach (var group in groups)
        {
            var header = group.Children.OfType<PencilBorder>().Single(b => b.Name == "SectionHeader");
            var body = group.Children.OfType<PencilBorder>().Single(b => b != header);
            check(header.BorderThickness == new Thickness(1) && ReferenceEquals(header.BorderBrush, PencilPalette.Line)
                && ReferenceEquals(header.Background, PencilPalette.Paper), "section heading has a themed pencil outline");
            var content = (StackPanel)header.Child;
            check(content.Children.OfType<PencilIcon>().Count() == 1 && content.Children.OfType<TextBlock>().Count() == 1,
                "section outline encloses both icon and label");
            var rows = (StackPanel)body.Child;
            check(header.TransformToAncestor(group).TransformBounds(new Rect(header.RenderSize)).Bottom
                <= rows.TransformToAncestor(group).TransformBounds(new Rect(rows.RenderSize)).Top,
                "outlined heading does not overlap settings rows");
        }
    }
    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte channel) { var n = channel / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        var x = Luminance(a); var y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
