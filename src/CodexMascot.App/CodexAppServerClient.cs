using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexMascot.Core;

namespace CodexMascot.App;

public sealed class CodexAppServerClient : ICodexClient
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, JsonElement> _requests = new();
    private readonly SemaphoreSlim _startup = new(1);
    private readonly SemaphoreSlim _write = new(1);
    private Process? _process;
    private CancellationTokenSource? _lifetime;
    private Task? _reader;
    private int _id;
    private string? _thread, _turn;
    public string? ExecutablePath { get; set; }
    public bool ReadOnly { get; set; }
    public Func<JsonElement, Task<object?>>? GetUserInput { get; set; }
    public event EventHandler<CodexEvent>? EventReceived;
    public bool IsRunning => _process is { HasExited: false };

    public async Task<string> StartTaskAsync(string workingDirectory, string prompt, string? model, CancellationToken cancellationToken = default)
    {
        await EnsureStarted(cancellationToken);
        var thread = await Request("thread/start", new
        {
            cwd = workingDirectory, approvalPolicy = "on-request",
            sandbox = ReadOnly ? "readOnly" : "workspaceWrite"
        }, cancellationToken);
        _thread = thread.FindNestedString("thread", "id") ?? throw new InvalidDataException("thread/start 응답에 작업 ID가 없습니다.");
        var args = new Dictionary<string, object?> { ["threadId"] = _thread, ["input"] = new[] { new { type = "text", text = prompt } } };
        if (!string.IsNullOrWhiteSpace(model)) args["model"] = model;
        var turn = await Request("turn/start", args, cancellationToken);
        _turn = turn.FindNestedString("turn", "id");
        return _thread;
    }
    public async Task InterruptAsync(string threadId, string turnId, CancellationToken cancellationToken = default)
        => await Request("turn/interrupt", new { threadId, turnId }, cancellationToken);
    public async Task ApproveAsync(string requestId, bool accept, CancellationToken cancellationToken = default)
    {
        if (!_requests.TryGetValue(requestId, out var request)) throw new InvalidOperationException("이미 해결되었거나 만료된 요청입니다.");
        var method = request.GetStringOrNull("method");
        object result;
        if (method == "item/tool/requestUserInput")
            result = accept && GetUserInput is not null ? await GetUserInput(request.GetProperty("params")) ?? new { answers = new { } } : new { answers = new { } };
        else if (method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval")
            result = new { decision = accept ? "accept" : "decline" };
        else throw new NotSupportedException("이 승인 형식은 아직 지원하지 않습니다: " + method);
        await Write(new { id = request.GetProperty("id"), result }, cancellationToken);
        _requests.TryRemove(requestId, out _);
    }
    private async Task EnsureStarted(CancellationToken ct)
    {
        await _startup.WaitAsync(ct);
        try
        {
            if (IsRunning) return;
            var info = new ProcessStartInfo(ExecutablePath ?? CodexExecutable.Resolve())
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            info.ArgumentList.Add("app-server");
            _process = Process.Start(info) ?? throw new InvalidOperationException("Codex 프로세스를 시작하지 못했습니다.");
            _lifetime = new CancellationTokenSource();
            var process = _process;
            _reader = Task.Run(() => ReadLoop(process, _lifetime.Token));
            _ = DrainStderr(process, _lifetime.Token);
            try
            {
                await Request("initialize", new { clientInfo = new { name = "codex_mascot", title = "Codex Mascot", version = "0.2.0" } }, ct);
                await Write(new { method = "initialized", @params = new { } }, ct);
            }
            catch { await StopAsync(); throw; }
            Emit(new(CodexEventKind.ConnectionChanged, "app-server", Status: "connected"));
        }
        finally { _startup.Release(); }
    }
    private async Task<JsonElement> Request(string method, object args, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _id);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            await Write(new { id, method, @params = args }, timeout.Token);
            return await completion.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException(method + " 응답이 25초 안에 오지 않았습니다. 중복 실행 방지를 위해 자동 재시도하지 않습니다."); }
        finally { _pending.TryRemove(id, out _); }
    }
    private async Task Write(object value, CancellationToken ct)
    {
        await _write.WaitAsync(ct);
        try
        {
            var process = _process;
            if (process is null || process.HasExited) throw new IOException("Codex 연결이 종료되었습니다.");
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
        }
        finally { _write.Release(); }
    }
    private async Task ReadLoop(Process process, CancellationToken ct)
    {
        string reason = "Codex 프로세스 연결 종료";
        try
        {
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id))
                {
                    if (!root.TryGetProperty("method", out _))
                    {
                        if (id.TryGetInt32(out var n) && _pending.TryGetValue(n, out var completion))
                        {
                            if (root.TryGetProperty("error", out var error)) completion.TrySetException(new InvalidOperationException(error.ToString()));
                            else completion.TrySetResult(root.GetProperty("result").Clone());
                        }
                        continue;
                    }
                    _requests[id.ToString()] = root.Clone();
                }
                var e = CodexEventNormalizer.FromJsonLine(line, "app-server");
                if (e is null) continue;
                if (e.Kind == CodexEventKind.TurnStarted) { _thread = e.ThreadId; _turn = e.TurnId; }
                if (e.Kind == CodexEventKind.TurnCompleted) _turn = null;
                if (e.Kind == CodexEventKind.ServerRequestResolved && e.RequestId is not null) _requests.TryRemove(e.RequestId, out _);
                Emit(e);
            }
        }
        catch (OperationCanceledException) { reason = "연결 닫힘"; }
        catch (Exception ex) { reason = ex.Message; }
        finally
        {
            foreach (var p in _pending.Values) p.TrySetException(new IOException(reason));
            _requests.Clear();
            if (_turn is not null && !ct.IsCancellationRequested)
                Emit(new(CodexEventKind.ThreadStatusChanged, "app-server", _thread, Status: "disconnected", Message: reason));
            Emit(new(CodexEventKind.ConnectionChanged, "app-server", Status: "disconnected", Message: reason));
        }
    }
    private static async Task DrainStderr(Process process, CancellationToken ct)
    {
        try { while (await process.StandardError.ReadLineAsync(ct) is not null) { } }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
    }
    public async Task StopAsync()
    {
        var p = _process;
        _lifetime?.Cancel();
        if (p is not null)
        {
            try { if (!p.HasExited) { p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
        }
        if (_reader is not null) await _reader;
        p?.Dispose(); _process = null; _reader = null;
        _lifetime?.Dispose(); _lifetime = null;
        _turn = null;
    }
    public async ValueTask DisposeAsync() => await StopAsync();
    private void Emit(CodexEvent e) => EventReceived?.Invoke(this, e);
}
