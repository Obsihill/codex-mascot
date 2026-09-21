namespace CodexMascot.Core;

public sealed record MonitorHealth(int Sessions, DateTimeOffset? LastEvent, long HookEvents, string Message);

// Read-only observer for the current local JSONL format of one agent. Hooks are a supplementary
// source. No resume calls, database writes, prompt storage, or agent process control.
public sealed class DesktopSessionMonitor
{
    private sealed class Tracked
    {
        public required string Path;
        public required TranscriptContext Context;
        public required JsonlTail Tail;
        public string? ActiveTurn;
        public bool Stale;
        public DateTime LastWrite;
        public DateTimeOffset LastActivity;
        public HashSet<string> ClosedTurns = new(StringComparer.Ordinal);
        public SessionIdentity Identity => Context.Identity;
    }
    private readonly Dictionary<string, Tracked> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hooksSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private DateTimeOffset _nextScan;
    private DateTimeOffset? _lastEvent;
    private long _hookCount;
    private bool _initialized;
    public event EventHandler<CodexEvent>? EventReceived;
    public event EventHandler<MonitorHealth>? HealthChanged;
    public string CodexHome { get; }
    public string HookDirectory { get; }
    public IAgentAdapter Adapter { get; }
    public AgentKind Kind => Adapter.Kind;
    public DesktopSessionMonitor(string codexHome, string hookDirectory, IAgentAdapter? adapter = null)
    { CodexHome = codexHome; HookDirectory = hookDirectory; Adapter = adapter ?? AgentAdapters.Codex; }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Poll(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { HealthChanged?.Invoke(this, new(_files.Count, _lastEvent, _hookCount, "읽기 오류: " + e.Message)); }
            try { await Task.Delay(650, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Poll()
    {
        var sessionsRoot = Adapter.SessionsRoot(CodexHome);
        if (!Directory.Exists(sessionsRoot))
        {
            HealthChanged?.Invoke(this, new(0, _lastEvent, _hookCount, Adapter.MissingRootMessage));
            return;
        }
        if (DateTimeOffset.UtcNow >= _nextScan)
        {
            var recent = new DirectoryInfo(sessionsRoot).EnumerateFiles("*.jsonl", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(100).ToArray();
            foreach (var file in recent)
            {
                if (_files.ContainsKey(file.FullName)) continue;
                try { Discover(file, !_initialized || file.LastWriteTimeUtc < _started.UtcDateTime); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            _initialized = true;
            _nextScan = DateTimeOffset.UtcNow.AddSeconds(3);
        }
        foreach (var entry in _files.Values.ToArray())
        {
            try
            {
                if (!File.Exists(entry.Path))
                {
                    Emit(new(CodexEventKind.ThreadClosed, Adapter.TranscriptSource, entry.Identity.Id, Message: "기록 파일 이동 또는 보관됨") { ProjectPath = entry.Identity.Cwd, IsReplay = true });
                    _files.Remove(entry.Path);
                    continue;
                }
                var info = new FileInfo(entry.Path);
                if (info.LastWriteTimeUtc != entry.LastWrite || info.Length != entry.Tail.Offset)
                {
                    foreach (var line in entry.Tail.Read(entry.Path)) ProcessLine(entry, line, false);
                    entry.LastWrite = info.LastWriteTimeUtc;
                }
                if (entry.ActiveTurn is not null && !entry.Stale && DateTimeOffset.UtcNow - entry.LastActivity > TimeSpan.FromMinutes(5))
                {
                    entry.Stale = true;
                    Emit(new(CodexEventKind.ThreadStatusChanged, Adapter.TranscriptSource, entry.Identity.Id,
                        Status: "unknown", Message: "5분간 기록 갱신 없음 · 완료로 판단하지 않음") { ProjectPath = entry.Identity.Cwd, IsReplay = true });
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        ReadHooks();
        HealthChanged?.Invoke(this, new(_files.Count, _lastEvent, _hookCount,
            _files.Count == 0 ? Adapter.DisplayName + ": 읽을 수 있는 로컬 작업 기록이 없습니다."
                : Adapter.DisplayName + " 기록 감시 중 · 목록은 최근 100개 기록 기준"));
    }

    private void Discover(FileInfo file, bool replay)
    {
        var head = ReadHead(file.FullName);
        var identity = Adapter.ReadIdentity(head);
        if (identity is null) return;
        var context = new TranscriptContext { Identity = identity };
        var entry = new Tracked { Path = file.FullName, Context = context,
            Tail = new JsonlTail(Math.Max(0, file.Length - 4 * 1024 * 1024)), LastWrite = file.LastWriteTimeUtc };
        _files[file.FullName] = entry;
        Emit(new(CodexEventKind.ThreadStarted, Adapter.TranscriptSource, identity.Id, OccurredAt: DateTimeOffset.MinValue)
            { ProjectPath = identity.Cwd, IsReplay = true });
        var lines = entry.Tail.Read(file.FullName);
        if (!replay) { foreach (var line in lines) ProcessLine(entry, line, false); return; }
        // Reduce history to the last lifecycle state; old completions never produce notifications.
        var events = lines.Select(line => Adapter.ParseTranscript(line, context, true)).Where(e => e is not null).Cast<CodexEvent>().ToArray();
        var lifecycle = events.LastOrDefault(e => e.Kind is CodexEventKind.TurnStarted or CodexEventKind.TurnCompleted);
        if (lifecycle is not null)
        {
            entry.ActiveTurn = lifecycle.Kind == CodexEventKind.TurnStarted ? lifecycle.TurnId : null;
            if (lifecycle.Kind == CodexEventKind.TurnCompleted && lifecycle.TurnId is not null) entry.ClosedTurns.Add(lifecycle.TurnId);
            Emit(lifecycle);
        }
        var last = events.LastOrDefault();
        entry.LastActivity = last?.Time ?? new DateTimeOffset(file.LastWriteTimeUtc);
        if (last is not null && last != lifecycle && lifecycle?.Kind != CodexEventKind.TurnCompleted)
        {
            entry.ActiveTurn = last.TurnId;
            Emit(last with { Kind = lifecycle is null ? CodexEventKind.TurnStarted : last.Kind });
        }
    }

    // Codex states its identity on the first record; Claude needs a few lines before cwd appears.
    private IReadOnlyList<string> ReadHead(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var head = new List<string>();
        for (var i = 0; i < Adapter.HeadLines; i++)
        {
            var line = reader.ReadLine();
            if (line is null) break;
            head.Add(line);
        }
        return head;
    }

    private void ProcessLine(Tracked entry, string line, bool replay)
    {
        var e = Adapter.ParseTranscript(line, entry.Context, replay);
        if (e is null) return;
        entry.LastActivity = replay ? e.Time : DateTimeOffset.UtcNow;
        if (e.TurnId is not null && entry.ClosedTurns.Contains(e.TurnId)) return;
        // A long-running turn may start before the bounded initial tail window.
        if (e.Kind == CodexEventKind.ItemCompleted && entry.ActiveTurn is null && e.TurnId is not null)
        {
            entry.ActiveTurn = e.TurnId;
            Emit(e with { Kind = CodexEventKind.TurnStarted, Message = "진행 기록 발견" });
        }
        if (e.Kind == CodexEventKind.TurnStarted) entry.ActiveTurn = e.TurnId;
        if (e.Kind == CodexEventKind.TurnCompleted)
        {
            entry.ActiveTurn = null;
            if (e.TurnId is not null) { if (entry.ClosedTurns.Count > 128) entry.ClosedTurns.Clear(); entry.ClosedTurns.Add(e.TurnId); }
        }
        entry.Stale = false;
        Emit(e);
    }
    private void Emit(CodexEvent e)
    {
        if (!e.IsReplay) _lastEvent = DateTimeOffset.UtcNow;
        EventReceived?.Invoke(this, e);
    }
    private void ReadHooks()
    {
        if (!Directory.Exists(HookDirectory)) return;
        foreach (var file in new DirectoryInfo(HookDirectory).EnumerateFiles("*.json").OrderBy(f => f.Name))
        {
            if (_hooksSeen.Contains(file.FullName)) continue;
            if (file.LastWriteTimeUtc < _started.UtcDateTime) { _hooksSeen.Add(file.FullName); continue; }
            try
            {
                if (file.Length > 16384) { _hooksSeen.Add(file.FullName); continue; }
                var e = Adapter.ParseHook(File.ReadAllText(file.FullName), false);
                _hooksSeen.Add(file.FullName);
                if (e is null) continue;
                _hookCount++;
                Emit(e);
            }
            catch (IOException) { }
        }
    }
}
