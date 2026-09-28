using System.Diagnostics;
using System.IO;
using System.Windows;
using CodexMascot.App;

internal static class InstanceActivationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var context = SynchronizationContext.Current;
        var channel = "AgentMascot.Tests." + Guid.NewGuid().ToString("N");
        var requests = 0;
        var earlyClient = InstanceActivation.NotifyAsync(channel);
        using (var listener = new InstanceActivation(() => Interlocked.Increment(ref requests), channel))
        {
            check(earlyClient.GetAwaiter().GetResult() && Volatile.Read(ref requests) == 1, "launch request waits for an instance still starting up");
            check(InstanceActivation.NotifyAsync(channel).GetAwaiter().GetResult() && Volatile.Read(ref requests) == 2, "repeated launches reuse the existing activation listener");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(InstanceActivationTests).Assembly.Location);
            start.ArgumentList.Add("--activate-instance-test"); start.ArgumentList.Add(channel);
            using var child = Process.Start(start)!;
            var exited = child.WaitForExit(10000);
            if (!exited) child.Kill();
            check(exited && child.ExitCode == 0 && Volatile.Read(ref requests) == 3, "second OS process requests activation and exits without a second UI");
        }
        check(!InstanceActivation.NotifyAsync(channel + ".missing", 100).GetAwaiter().GetResult(), "unavailable older instance has a bounded activation timeout");
        var window = new Window { Title = "Instance activation test", Width = 400, Height = 300, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); window.Hide();
            InstanceActivation.Restore(window);
            check(window.IsVisible && window.WindowState == WindowState.Normal, "hidden tray window is restored");
            window.WindowState = WindowState.Minimized;
            InstanceActivation.Restore(window);
            check(window.IsVisible && window.WindowState == WindowState.Normal, "minimized window is restored");
            window.ShowActivated = true;
            window.WindowState = WindowState.Maximized; window.Hide();
            InstanceActivation.Restore(window);
            check(window.IsVisible && window.WindowState == WindowState.Maximized, "restoring a hidden maximized window preserves its size mode");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(context); }
    }
}
