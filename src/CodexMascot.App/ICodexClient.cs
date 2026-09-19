using CodexMascot.Core;

namespace CodexMascot.App;

public interface ICodexClient : IAsyncDisposable
{
    event EventHandler<CodexEvent>? EventReceived;
    bool IsRunning { get; }
    Task<string> StartTaskAsync(string workingDirectory, string prompt, string? model, CancellationToken cancellationToken = default);
    Task InterruptAsync(string threadId, string turnId, CancellationToken cancellationToken = default);
    Task ApproveAsync(string requestId, bool accept, CancellationToken cancellationToken = default);
    Task StopAsync();
}
