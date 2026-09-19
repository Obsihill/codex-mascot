using System.Diagnostics;
using System.Text;
using CodexMascot.Core;

namespace CodexMascot.App;

public sealed class CodexCliClient : ICodexClient
{
    private Process? _process;
    private Task? _reader;
    private string _id = "";
    private string _turn = "";
    private bool _stopping;
    public string? ExecutablePath { get; set; }
    public bool ReadOnly { get; set; }
    public event EventHandler<CodexEvent>? EventReceived;
    public bool IsRunning => _process is { HasExited: false };
    public async Task<string> StartTaskAsync(string dir, string prompt, string? model, CancellationToken cancellationToken = default)
    {
        await StopAsync();
        _stopping = false;
        _id = "cli-" + Guid.NewGuid().ToString("N"); _turn = Guid.NewGuid().ToString("N");
        var info = new ProcessStartInfo(ExecutablePath ?? CodexExecutable.Resolve())
        {
            WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-a", "never", "exec", "--json", "--sandbox", ReadOnly ? "read-only" : "workspace-write" }) info.ArgumentList.Add(arg);
        if (!string.IsNullOrWhiteSpace(model)) { info.ArgumentList.Add("--model"); info.ArgumentList.Add(model); }
        info.ArgumentList.Add("-");
        var process = Process.Start(info) ?? throw new IOException("CLI 시작 실패");
        _process = process;
        await process.StandardInput.WriteLineAsync(prompt.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        _reader = Read(process, dir);
        return _id;
    }
    private async Task Read(Process process, string dir)
    {
        var stderr = process.StandardError.ReadToEndAsync();
        var completed = false;
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                var e = CodexEventNormalizer.FromJsonLine(line, _id);
                if (e is null) continue;
                if (e.Kind == CodexEventKind.ThreadStarted) _id = e.ThreadId ?? _id;
                if (e.Kind == CodexEventKind.TurnCompleted) completed = true;
                EventReceived?.Invoke(this, e with { ThreadId = _id, TurnId = _turn, ProjectPath = dir });
            }
            await process.WaitForExitAsync();
            var error = await stderr;
            if (!completed) EventReceived?.Invoke(this, new(CodexEventKind.TurnCompleted, "CLI JSON", _id, _turn,
                Status: _stopping ? "interrupted" : "failed",
                Message: _stopping ? "사용자가 CLI를 중단함" : "종료 이벤트 없이 CLI 종료: " + error[..Math.Min(error.Length, 300)]) { ProjectPath = dir });
        }
        catch (Exception e) { EventReceived?.Invoke(this, new(CodexEventKind.Warning, "CLI JSON", _id, Message: e.Message)); }
    }
    public Task InterruptAsync(string threadId, string turnId, CancellationToken cancellationToken = default) => StopAsync();
    public Task ApproveAsync(string requestId, bool accept, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("CLI 모드는 승인 대화 없이 지정한 샌드박스 안에서만 실행됩니다."));
    public async Task StopAsync()
    {
        var p = _process;
        if (p is null) return;
        _stopping = true;
        try { if (!p.HasExited) { p.Kill(true); await p.WaitForExitAsync(); } }
        catch (InvalidOperationException) { }
        if (_reader is not null) await _reader;
        p.Dispose(); _process = null; _reader = null;
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
