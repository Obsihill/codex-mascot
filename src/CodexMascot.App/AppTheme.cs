using System.Windows;
using System.Windows.Threading;

namespace CodexMascot.App;

internal sealed class AppTheme : IDisposable
{
    private static bool _registered;
    private bool _disposed;
    private readonly Func<bool> _readDark;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    internal AppTheme(Func<bool>? readDark = null)
    {
        _readDark = readDark ?? TrayThemeIcon.ReadAppDarkMode;
        Refresh(); _timer.Tick += OnTick; _timer.Start();
    }
    private void OnTick(object? sender, EventArgs e) => Refresh();
    internal void Refresh() { if (!_disposed) Apply(_readDark()); }
    internal static void Apply(bool dark)
    {
        if (!_registered)
        {
            _registered = true;
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) => PencilPalette.BindLocal((DependencyObject)sender)));
        }
        if (PencilPalette.Current.IsDark == dark) return;
        // Bind programmatic controls before changing colors so their semantic role
        // (muted text, error, surface, etc.) survives either direction of switching.
        if (Application.Current is { } app)
            foreach (Window window in app.Windows) PencilPalette.BindTree(window);
        PencilPalette.Current.SetDark(dark);
    }
    public void Dispose() { _disposed = true; _timer.Stop(); _timer.Tick -= OnTick; }
}
