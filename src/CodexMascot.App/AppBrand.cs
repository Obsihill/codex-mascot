namespace CodexMascot.App;

public static class AppBrand
{
    internal static readonly System.Uri IconUri = new("pack://application:,,,/AgentMascot;component/Branding/App.ico");
    internal static readonly System.Uri TrayIconUri = new("pack://application:,,,/AgentMascot;component/Branding/Tray.ico");
    internal static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = System.Windows.Application.GetResourceStream(TrayIconUri)!.Stream;
        using var icon = new System.Drawing.Icon(stream, 32, 32);
        return (System.Drawing.Icon)icon.Clone();
    }
    public const string Name = "Agent Mascot";
    public const string OpenLabel = Name + " 열기";
    public const string SettingsTitle = Name + " · 앱 설정";
    public const string NotificationSettingsTitle = Name + " · 공통 알림 설정";
}
