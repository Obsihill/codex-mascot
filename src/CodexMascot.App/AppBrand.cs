namespace CodexMascot.App;

public static class AppBrand
{
    internal static WindowBrandTheme WindowTheme { get; } = new();
    internal static readonly System.Uri IconUri = new("pack://application:,,,/AgentMascot;component/Branding/App.ico");
    internal static System.Uri TrayIconUri => TrayIconFor(TrayThemeIcon.ReadDarkMode());
    internal static System.Uri TrayIconFor(bool dark) => new("pack://application:,,,/AgentMascot;component/Branding/" + (dark ? "TrayDark.ico" : "TrayLight.ico"));
    internal static System.Drawing.Icon CreateTrayIcon() => CreateTrayIcon(TrayThemeIcon.ReadDarkMode());
    internal static System.Drawing.Icon CreateTrayIcon(bool dark)
    {
        using var stream = System.Windows.Application.GetResourceStream(TrayIconFor(dark))!.Stream;
        using var icon = new System.Drawing.Icon(stream, 32, 32);
        return (System.Drawing.Icon)icon.Clone();
    }
    public const string Name = "Agent Mascot";
    public static string OpenLabel => Loc.F("{0} 열기", Name);
    public static string SettingsTitle => Loc.T("설정");
    public static string NotificationSettingsTitle => Name + Loc.T(" · 공통 알림 설정");
}

// A running window (and its taskbar button) can change icons independently of
// the executable's fixed Explorer/shortcut icon.
internal sealed class WindowBrandTheme : System.ComponentModel.INotifyPropertyChanged
{
    private bool? _dark;
    public System.Windows.Media.ImageSource Icon { get; private set; } =
        System.Windows.Media.Imaging.BitmapFrame.Create(AppBrand.TrayIconFor(TrayThemeIcon.ReadDarkMode()));
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    internal void Update(bool dark)
    {
        if (_dark == dark) return;
        _dark = dark;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(AppBrand.TrayIconFor(dark));
        PropertyChanged?.Invoke(this, new(nameof(Icon)));
    }
}
