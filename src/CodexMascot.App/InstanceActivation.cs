using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;

namespace CodexMascot.App;

// A current-user-only channel shared by builds in the same Windows session.
// The mutex in App remains the authority for which process may create windows.
internal sealed class InstanceActivation : IDisposable
{
    internal static string ChannelName => "AgentMascot.Activate." + Process.GetCurrentProcess().SessionId + "." + WindowsIdentity.GetCurrent().User!.Value;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;

    internal InstanceActivation(Action activate, string? channel = null)
        => _listener = Listen(channel ?? ChannelName, activate, _stop.Token);

    private static async Task Listen(string channel, Action activate, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(channel, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var token = deadline.Token;
                await pipe.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), token).ConfigureAwait(false);
                var command = new byte[1];
                await pipe.ReadExactlyAsync(command, token).ConfigureAwait(false);
                if (command[0] != 1) continue;
                activate();
                await pipe.WriteAsync(new byte[] { 1 }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { if (stop.IsCancellationRequested) return; }
            catch (IOException)
            {
                if (stop.IsCancellationRequested) return;
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }

    internal static async Task<bool> NotifyAsync(string? channel = null, int timeoutMs = 5000)
    {
        using var deadline = new CancellationTokenSource(timeoutMs);
        try
        {
            using var pipe = new NamedPipeClientStream(".", channel ?? ChannelName,
                PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            var pid = new byte[4];
            await pipe.ReadExactlyAsync(pid, deadline.Token).ConfigureAwait(false);
            // A user-launched second process can grant the existing process foreground access.
            AllowSetForegroundWindow(BitConverter.ToInt32(pid));
            await pipe.WriteAsync(new byte[] { 1 }, deadline.Token).ConfigureAwait(false);
            var acknowledgement = new byte[1];
            await pipe.ReadExactlyAsync(acknowledgement, deadline.Token).ConfigureAwait(false);
            return acknowledgement[0] == 1;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        { return false; }
    }

    internal static void Restore(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate(); window.Focus();
    }

    public void Dispose()
    {
        _stop.Cancel();
        // Listener never waits for the UI thread; shutdown cannot deadlock it.
        _ = _listener.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
