using System.Diagnostics;
using System.Reflection;
using CodexMascot.App;

internal static class BrandingTests
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(MainWindow).Assembly;
        check(TrayThemeIcon.IsDarkValue(0) && !TrayThemeIcon.IsDarkValue(1) && !TrayThemeIcon.IsDarkValue(null) && !TrayThemeIcon.IsDarkValue("bad"), "shell theme handles dark, light, missing and invalid values");
        check(AppBrand.TrayIconFor(true).ToString().EndsWith("TrayDark.ico") && AppBrand.TrayIconFor(false).ToString().EndsWith("TrayLight.ico"), "dark uses mas-cat artwork and light uses mas-cat-black artwork resources");
        var window = new System.Windows.Window();
        PencilWindow.Apply(window);
        AppBrand.WindowTheme.Update(false);
        check(ReferenceEquals(window.Icon, AppBrand.WindowTheme.Icon), "window icon follows theme binding");
        var lightWindowIcon = window.Icon;
        AppBrand.WindowTheme.Update(true);
        check(!ReferenceEquals(lightWindowIcon, window.Icon) && ReferenceEquals(window.Icon, AppBrand.WindowTheme.Icon), "running window icon changes with shell theme");
        check(AppBrand.IconUri.ToString().EndsWith("Branding/App.ico"), "executable and shortcut artwork remains fixed");
        AppBrand.WindowTheme.Update(false); window.Close();
        using (var darkIcon = AppBrand.CreateTrayIcon(true))
        using (var lightIcon = AppBrand.CreateTrayIcon(false))
        {
            check(darkIcon.Width == 32 && lightIcon.Width == 32, "both theme resources decode at tray size");
            using var darkData = new System.IO.MemoryStream(); using var lightData = new System.IO.MemoryStream();
            darkIcon.Save(darkData); lightIcon.Save(lightData);
            check(!darkData.ToArray().SequenceEqual(lightData.ToArray()), "theme variants contain distinct artwork");
        }
        using (var tray = new System.Windows.Forms.NotifyIcon())
        {
            var dark = false;
            using var theme = new TrayThemeIcon(tray, () => dark);
            var first = tray.Icon;
            theme.Refresh();
            check(ReferenceEquals(first, tray.Icon), "unchanged theme reuses existing icon");
            dark = true; theme.Refresh();
            check(!ReferenceEquals(first, tray.Icon), "running tray swaps icon after theme change");
            var second = tray.Icon;
            dark = false; theme.Refresh();
            check(!ReferenceEquals(second, tray.Icon), "running tray switches back to light mode");
            var last = tray.Icon;
            theme.Dispose(); dark = true; theme.Refresh();
            check(ReferenceEquals(last, tray.Icon), "disposed watcher no longer changes icon");
            tray.Icon = null; last?.Dispose();
        }
        check(AppBrand.Name == "Agent Mascot" && AppBrand.OpenLabel == "Agent Mascot 열기", "app and tray branding use Agent Mascot");
        check(assembly.GetName().Name == "AgentMascot", "application assembly uses the renamed executable identity");
        check(assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title == "Agent Mascot" && assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product == "Agent Mascot", "assembly title and product use Agent Mascot");
        var info = FileVersionInfo.GetVersionInfo(assembly.Location);
        check(info.ProductName == "Agent Mascot" && info.FileDescription == "Agent Mascot", "Windows file metadata uses Agent Mascot");
        const string executable = @"C:\Program Files\Agent Mascot\AgentMascot.exe";
        const string legacy = "\"C:\\Program Files\\Agent Mascot\\CodexMascot.App.exe\" --tray";
        check(StartupRegistration.ReplacementCommand(legacy, executable) == "\"" + executable + "\" --tray", "startup rename preserves quoted paths and tray argument");
        check(StartupRegistration.ReplacementCommand(legacy.ToLowerInvariant(), executable) is not null, "startup rename matches Windows paths case-insensitively");
        check(StartupRegistration.ReplacementCommand(null, executable) is null, "rename does not enable an absent startup registration");
        check(StartupRegistration.ReplacementCommand("\"C:\\Another installation\\CodexMascot.App.exe\" --tray", executable) is null, "rename does not redirect a different installation");
        check(StartupRegistration.ReplacementCommand(legacy + " --custom", executable) is null, "rename preserves custom startup commands");
        check(StartupRegistration.ReplacementCommand(legacy, @"C:\Program Files\Agent Mascot\CodexMascot.App.Tests.exe") is null, "test harness cannot rewrite real startup entries");
    }
}
