using System.Diagnostics;
using System.Reflection;
using CodexMascot.App;

internal static class BrandingTests
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(MainWindow).Assembly;
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
