using Microsoft.Win32;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexMascot.App;

// Follow the Windows shell theme (taskbar), not the independent app color mode.
internal sealed class TrayThemeIcon : IDisposable
{
    private readonly Forms.NotifyIcon _tray;
    private readonly Func<bool> _readDark;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool? _dark;
    private bool _disposed;

    internal TrayThemeIcon(Forms.NotifyIcon tray, Func<bool>? readDark = null)
    {
        _tray = tray; _readDark = readDark ?? ReadDarkMode;
        Refresh();
        _timer.Tick += OnTick;
        _timer.Start();
    }
    private void OnTick(object? sender, EventArgs e) => Refresh();
    internal void Refresh()
    {
        if (_disposed) return;
        var dark = _readDark();
        AppBrand.WindowTheme.Update(dark);
        if (_dark == dark) return;
        var icon = AppBrand.CreateTrayIcon(dark);
        var previous = _tray.Icon;
        _tray.Icon = icon;
        _dark = dark;
        previous?.Dispose();
    }
    internal static bool IsDarkValue(object? value) => value is int mode && mode == 0;
    internal static bool ReadDarkMode() => ReadMode("SystemUsesLightTheme");
    internal static bool ReadAppDarkMode() => ReadMode("AppsUseLightTheme");
    private static bool ReadMode(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return IsDarkValue(key?.GetValue(valueName));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return false; }
    }
    public void Dispose()
    {
        _disposed = true;
        _timer.Stop(); _timer.Tick -= OnTick;
        // The NotifyIcon owner disposes its final icon when the application exits.
    }
}
