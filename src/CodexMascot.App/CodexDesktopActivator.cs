using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CodexMascot.Core;

namespace CodexMascot.App;

public enum DesktopActivationResult { Activated, NotFound, AttentionRequested }

public static class CodexDesktopActivator
{
    public static bool IsForeground(AgentKind kind)
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var pid) == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return Matches(kind, process.ProcessName) && IsDesktopExecutable(kind, process.MainModule?.FileName);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
        { return false; }
    }
    public static DesktopActivationResult TryActivate(AgentKind kind = AgentKind.Codex)
        => ActivateWindow(FindWindow(kind), new NativeWindowActivation());

    // Enumerate in desktop Z order so a previously used agent window wins.
    // The Windows Store Codex desktop can be named ChatGPT.exe and the Claude desktop
    // Claude.exe; their package paths distinguish them from the separate ChatGPT
    // application and from the CLI, which runs inside a console host we never steal.
    internal static IntPtr FindWindow(AgentKind kind = AgentKind.Codex)
    {
        var candidate = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
            var className = new StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() == "ConsoleWindowClass") return true;
            if (GetWindowThreadProcessId(window, out var pid) == 0) return true;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (!Matches(kind, process.ProcessName)) return true;
                if (!IsDesktopExecutable(kind, process.MainModule?.FileName)) return true;
                candidate = window;
                return false;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
            { return true; } // A window can close or become inaccessible during enumeration.
        }, IntPtr.Zero);
        return candidate;
    }

    private static bool Matches(AgentKind kind, string name) => kind == AgentKind.Claude
        ? name.Equals("Claude", StringComparison.OrdinalIgnoreCase)
        : name.Equals("Codex", StringComparison.OrdinalIgnoreCase) || name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase);

    internal static bool IsDesktopExecutable(AgentKind kind, string? path)
        => kind == AgentKind.Claude ? IsClaudeDesktopExecutable(path) : IsDesktopExecutable(path);

    internal static bool IsDesktopExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var parts = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var name = parts[^1];
        if (!name.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) || p.Equals("resources", StringComparison.OrdinalIgnoreCase)))
            return false;
        return name.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase) ||
            parts.Any(p => p.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)) ||
            path.Replace('/', '\\').Contains(@"\OpenAI\Codex\", StringComparison.OrdinalIgnoreCase);
    }

    // The Claude desktop ships as a Store package (Claude_<version>\app\Claude.exe) or as a
    // per-user install under AnthropicClaude. The node based CLI never matches: it runs as
    // node.exe inside a console host, and bin/resources paths are excluded either way.
    internal static bool IsClaudeDesktopExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('/', '\\');
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (!parts[^1].Equals("Claude.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || p.Equals("resources", StringComparison.OrdinalIgnoreCase)
            || p.Equals("node_modules", StringComparison.OrdinalIgnoreCase))) return false;
        return parts.Any(p => p.StartsWith("Claude_", StringComparison.OrdinalIgnoreCase)
            || p.Equals("AnthropicClaude", StringComparison.OrdinalIgnoreCase))
            || normalized.Contains(@"\Programs\Claude\", StringComparison.OrdinalIgnoreCase);
    }

    internal static DesktopActivationResult ActivateWindow(IntPtr window, IWindowActivation api)
    {
        if (window == IntPtr.Zero) return DesktopActivationResult.NotFound;
        // Restore only minimized windows: maximized windows must keep their size.
        if (api.IsMinimized(window)) api.Restore(window);
        if (api.TryForeground(window)) return DesktopActivationResult.Activated;
        api.RequestAttention(window);
        return DesktopActivationResult.AttentionRequested;
    }

    internal interface IWindowActivation
    {
        bool IsMinimized(IntPtr window);
        void Restore(IntPtr window);
        bool TryForeground(IntPtr window);
        void RequestAttention(IntPtr window);
    }
    private sealed class NativeWindowActivation : IWindowActivation
    {
        public bool IsMinimized(IntPtr window) => IsIconic(window);
        public void Restore(IntPtr window) => ShowWindowAsync(window, 9); // SW_RESTORE
        public bool TryForeground(IntPtr window) => SetForegroundWindow(window);
        public void RequestAttention(IntPtr window)
        {
            var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = window, Flags = 2, Count = 3 };
            FlashWindowEx(ref info);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags, Count, Timeout; }
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
}
