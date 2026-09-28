using Microsoft.Win32;

namespace CodexMascot.App;

internal static class StartupRegistration
{
    // Keep the existing registration identity so upgrades do not add a second entry.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexMascot";
    internal static bool IsEnabledFor(string? command, string executable) =>
        string.Equals(command, "\"" + executable + "\" --tray", StringComparison.OrdinalIgnoreCase);
    internal static string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }
    internal static void RestoreCommand(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (command is null) key.DeleteValue(ValueName, false); else key.SetValue(ValueName, command);
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\" --tray");
        else key.DeleteValue(ValueName, false);
    }

    internal static void RefreshRenamedExecutable()
    {
        var executable = Environment.ProcessPath;
        if (executable is null || !Path.GetFileName(executable).Equals("AgentMascot.exe", StringComparison.OrdinalIgnoreCase)) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is not null && ReplacementCommand(key.GetValue(ValueName) as string, executable) is { } replacement)
            key.SetValue(ValueName, replacement);
    }

    internal static string? ReplacementCommand(string? registeredCommand, string executable)
    {
        if (!Path.GetFileName(executable).Equals("AgentMascot.exe", StringComparison.OrdinalIgnoreCase)) return null;
        var legacyPath = Path.Combine(Path.GetDirectoryName(executable)!, "CodexMascot.App.exe");
        // Only update our exact legacy command in this installation; leave other
        // installations, custom commands and disabled startup registrations alone.
        return string.Equals(registeredCommand, "\"" + legacyPath + "\" --tray", StringComparison.OrdinalIgnoreCase)
            ? "\"" + executable + "\" --tray" : null;
    }
}
