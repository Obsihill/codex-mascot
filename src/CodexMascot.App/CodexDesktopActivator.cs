using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexMascot.App;

public enum DesktopActivationResult { Activated, NotFound, AttentionRequested }

public static class CodexDesktopActivator
{
    public static DesktopActivationResult TryActivate()
        => ActivateWindow(FindWindow(), new NativeWindowActivation());

    // Enumerate in desktop Z order so a previously used Codex window wins.
    // The Windows Store Codex desktop can be named ChatGPT.exe; its package path
    // distinguishes it from the separate ChatGPT application and the Codex CLI.
    internal static IntPtr FindWindow()
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
                if (!process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase) &&
                    !process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)) return true;
                if (!IsDesktopExecutable(process.MainModule?.FileName)) return true;
                candidate = window;
                return false;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
            { return true; } // A window can close or become inaccessible during enumeration.
        }, IntPtr.Zero);
        return candidate;
    }

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
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
}
