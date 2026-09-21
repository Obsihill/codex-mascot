using System.Text;
using System.Text.Json;
using CodexMascot.Core;

internal static class Program
{
    private static int _assertions;
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UtcNow;
    private static CodexEvent E(CodexEventKind kind, string job = "a", string turn = "1", int t = 0, string? status = null, string? request = null)
        => new(kind, "test", job, turn, Status: status, RequestId: request, OccurredAt: Epoch.AddSeconds(t));
    private static void Equal<T>(T expected, T actual, string label)
    { _assertions++; if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", actual " + actual); }
    private static void TestCompletionPopup()
    {
        var jobs = new StatusAggregator();
        var popup = new CompletionPopupPolicy();
        jobs.Apply(E(CodexEventKind.TurnStarted));
        popup.Observe(jobs.Apply(E(CodexEventKind.TurnCompleted, t: 1)).Jobs);
        popup.Observe(jobs.Apply(E(CodexEventKind.TurnStarted, turn: "2", t: 2)).Jobs);
        Equal(MascotState.Completed, popup.Resolve(jobs.State, true), "held completion survives next turn on same task");
        Equal(MascotState.Running, popup.Resolve(jobs.State, false), "disabled hold follows live status");
        Equal(MascotState.NeedsAttention, popup.Resolve(MascotState.NeedsAttention, true), "approval overrides held completion");
        Equal(MascotState.Failed, popup.Resolve(MascotState.Failed, true), "failure overrides held completion");
        Equal(MascotState.Completed, popup.Resolve(MascotState.Idle, true), "held completion reappears after higher-priority state resolves");
        popup.Observe(jobs.Apply(E(CodexEventKind.TurnCompleted, job: "b", t: 3)).Jobs);
        popup.Acknowledge("a");
        Equal(MascotState.Completed, popup.Resolve(MascotState.Idle, true), "acknowledging one task keeps another completion");
        popup.Acknowledge("b");
        Equal(MascotState.Running, popup.Resolve(MascotState.Running, true), "acknowledging all completed tasks releases popup");
        popup.Observe(jobs.Jobs);
        popup.Acknowledge();
        Equal(MascotState.Idle, popup.Resolve(MascotState.Idle, true), "global acknowledgement clears retained completions");
        jobs = new StatusAggregator();
        popup.Observe(jobs.Apply(E(CodexEventKind.TurnCompleted) with { IsReplay = true }).Jobs);
        Equal(MascotState.Idle, popup.Resolve(jobs.State, true), "historical completions do not latch a popup");
    }
    private static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--probe")
        {
            var probeAggregator = new StatusAggregator();
            // --probe <home> [reportPath] [--claude]
            var adapter = args.Contains("--claude") ? AgentAdapters.Claude : AgentAdapters.Codex;
            var m = new DesktopSessionMonitor(args[1], Path.Combine(Path.GetTempPath(), "mascot-nonexistent-hook-probe"), adapter);
            var live = 0;
            m.EventReceived += (_, e) => { probeAggregator.Apply(e); if (!e.IsReplay) live++; };
            for (var i = 0; i < 6; i++) { m.Poll(); Thread.Sleep(650); }
            var report = JsonSerializer.Serialize(new { state = probeAggregator.State.ToString(), liveEvents = live,
                jobs = probeAggregator.Jobs.Select(j => new { id = j.ThreadId, state = j.State.ToString(), cwd = j.ProjectPath, updated = j.LastUpdated }) }, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(report);
            if (args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal)) File.WriteAllText(args[2], report);
            return;
        }
        TestCompletionPopup();
        var a = new StatusAggregator();
        Equal(MascotState.Running, a.Apply(E(CodexEventKind.TurnStarted)).State, "start");
        Equal(MascotState.Completed, a.Apply(E(CodexEventKind.TurnCompleted, t: 1, status: "completed")).State, "complete");
        Equal(false, a.Apply(E(CodexEventKind.TurnCompleted, t: 2, status: "completed")).ShouldNotify, "duplicate completion");
        a.Acknowledge();
        Equal(MascotState.Idle, a.State, "ack");
        Equal(MascotState.Idle, a.Apply(E(CodexEventKind.TurnCompleted, t: 3)).State, "duplicate after ack");
        Equal(MascotState.Idle, a.Apply(E(CodexEventKind.ItemCompleted, t: 4)).State, "late item after terminal");
        Equal(MascotState.Idle, a.Apply(E(CodexEventKind.TurnStarted, t: 5)).State, "late start same closed turn");
        Equal(MascotState.Running, a.Apply(E(CodexEventKind.TurnStarted, turn: "2", t: 6)).State, "next turn");
        Equal(MascotState.Completed, a.Apply(E(CodexEventKind.TurnCompleted, job: "b", t: 7)).State, "unread outranks running");
        Equal(false, a.Apply(E(CodexEventKind.ItemStarted, turn: "2", t: 8)).ShouldNotify, "unrelated item does not replay completion");
        a.Apply(E(CodexEventKind.ApprovalRequired, turn: "2", t: 9, request: "r1"));
        a.Apply(E(CodexEventKind.ApprovalRequired, turn: "2", t: 10, request: "r2"));
        a.Acknowledge();
        Equal(MascotState.NeedsAttention, a.State, "ack never approves");
        Equal(MascotState.NeedsAttention, a.Apply(E(CodexEventKind.ServerRequestResolved, turn: "2", t: 11, request: "r1")).State, "remaining approval");
        Equal(MascotState.Running, a.Apply(E(CodexEventKind.ServerRequestResolved, turn: "2", t: 12, request: "r2")).State, "all requests resolved");
        Equal(MascotState.Running, a.Apply(E(CodexEventKind.Warning, turn: "2", t: 13)).State, "warning is not terminal failure");
        Equal(MascotState.Failed, a.Apply(E(CodexEventKind.TurnCompleted, turn: "2", t: 14, status: "failed")).State, "failure");
        a.Acknowledge();
        Equal(MascotState.Interrupted, a.Apply(E(CodexEventKind.TurnCompleted, turn: "3", t: 15, status: "interrupted")).State, "interrupt");
        a.Clear();
        var replay = a.Apply(E(CodexEventKind.TurnCompleted) with { IsReplay = true });
        Equal(MascotState.Idle, replay.State, "history completion is not unread");
        Equal(false, replay.ShouldNotify, "history is silent");

        var identity = DesktopEventParser.ReadIdentity("""{"type":"session_meta","payload":{"id":"s","cwd":"C:/Project","originator":"Codex Desktop","source":"vscode"}}""")!;
        Equal("s", identity.Id, "desktop metadata");
        Equal(null, DesktopEventParser.ReadIdentity("""{"type":"session_meta","payload":{"id":"s","source":{"subagent":{}}}}"""), "exclude subagents");
        var parsed = DesktopEventParser.ParseTranscript(Line("task_started", "t"), identity)!;
        Equal(CodexEventKind.TurnStarted, parsed.Kind, "actual task_started format");
        Equal(CodexEventKind.TurnCompleted, DesktopEventParser.ParseTranscript(Line("task_complete", "t"), identity)!.Kind, "actual task_complete format");
        Equal("interrupted", DesktopEventParser.ParseTranscript(Line("turn_aborted", "t"), identity)!.Status, "actual abort");
        Equal(null, DesktopEventParser.ParseTranscript("{bad", identity), "bad JSON");
        Equal(null, DesktopEventParser.ParseTranscript("""{"type":"event_msg"}""", identity), "missing payload");
        Equal(null, DesktopEventParser.ReadIdentity("""{"type":"session_meta","payload":null}"""), "null metadata");
        var delayed = new StatusAggregator();
        delayed.Apply(E(CodexEventKind.TurnStarted, turn: "new"));
        Equal(MascotState.Running, delayed.Apply(E(CodexEventKind.TurnCompleted, turn: "old", t: 1)).State, "delayed completion of another turn");
        var hook = DesktopEventParser.ParseHook("""{"hook_event_name":"PermissionRequest","session_id":"s","turn_id":"t","tool_name":"Bash","cwd":"C:/Project"}""")!;
        Equal(CodexEventKind.ApprovalRequired, hook.Kind, "hook attention");
        Equal("hook:Bash", hook.RequestId, "hook correlation");
        Equal(null, DesktopEventParser.ParseHook("""{"hook_event_name":"Stop"}"""), "reject missing session");
        var cli = CodexEventNormalizer.FromJsonLine("""{"type":"thread.started","thread_id":"cli-id"}""", "source")!;
        Equal("cli-id", cli.ThreadId, "CLI snake case");
        Equal("failed", CodexEventNormalizer.FromJsonLine("""{"type":"turn.failed","error":{"message":"auth failed"}}""", "source")!.Status, "CLI failed");

        // Claude Code writes no lifecycle event, so the turn is inferred from prompt and stop reason.
        var claudeHead = new[] { ClaudeQueue(), ClaudePrompt("p1") };
        var claudeIdentity = ClaudeEventParser.ReadIdentity(claudeHead)!;
        Equal("claude-session", claudeIdentity.Id, "claude session id from the first record");
        Equal("C:/Project", claudeIdentity.Cwd, "claude cwd from a later record");
        Equal(null, ClaudeEventParser.ReadIdentity(new[] { "{bad", """{"type":"user"}""" }), "claude file without a session id");
        var claude = new TranscriptContext { Identity = claudeIdentity };
        var claudeStart = ClaudeEventParser.ParseTranscript(claudeHead[1], claude)!;
        Equal(CodexEventKind.TurnStarted, claudeStart.Kind, "claude human prompt starts a turn");
        Equal("p1", claudeStart.TurnId, "claude turn id comes from promptId");
        Equal("C:/Project", claudeStart.ProjectPath, "claude project path");
        Equal(CodexEventKind.ItemCompleted, ClaudeEventParser.ParseTranscript(ClaudeAssistant("tool_use", "req-1"), claude)!.Kind, "claude tool step is progress");
        Equal(null, ClaudeEventParser.ParseTranscript(ClaudeAssistant("tool_use", "req-1"), claude), "one response written as several records reports once");
        Equal(CodexEventKind.ItemCompleted, ClaudeEventParser.ParseTranscript(ClaudeToolResult(), claude)!.Kind, "claude tool result is progress");
        Equal(null, ClaudeEventParser.ParseTranscript(ClaudeSubagent(), claude), "claude subagent records excluded");
        Equal(null, ClaudeEventParser.ParseTranscript("""{"type":"system","subtype":"stop_hook_summary"}""", claude), "claude system record ignored");
        Equal(null, ClaudeEventParser.ParseTranscript("{bad", claude), "claude bad JSON");
        Equal(CodexEventKind.ItemCompleted, ClaudeEventParser.ParseTranscript(ClaudeAssistant("max_tokens"), claude)!.Kind, "claude never guesses a result from max_tokens");
        var claudeEnd = ClaudeEventParser.ParseTranscript(ClaudeAssistant("end_turn"), claude)!;
        Equal(CodexEventKind.TurnCompleted, claudeEnd.Kind, "claude end_turn completes the turn");
        Equal("completed", claudeEnd.Status, "claude completion status");
        Equal("p1", claudeEnd.TurnId, "claude completion keeps the prompt turn id");
        Equal(null, claude.CurrentTurn, "claude turn closes");
        // Tool output that quotes the interrupt marker must not be read as an interruption.
        Equal(CodexEventKind.ItemCompleted, ClaudeEventParser.ParseTranscript(ClaudeQuotedMarker(true), claude)!.Kind, "grep output quoting the marker is not an interruption");
        Equal(CodexEventKind.ItemCompleted, ClaudeEventParser.ParseTranscript(ClaudeQuotedMarker(false), claude)!.Kind, "the marker mid-text is not an interruption");
        Equal("interrupted", ClaudeEventParser.ParseTranscript(ClaudeInterrupt(), claude)!.Status, "claude interruption from the transcript");
        var claudeHook = ClaudeEventParser.ParseHook("""{"hook_event_name":"Notification","session_id":"cs","cwd":"C:/Project","tool_name":"Bash"}""")!;
        Equal(CodexEventKind.ApprovalRequired, claudeHook.Kind, "claude notification needs attention");
        Equal(null, claudeHook.TurnId, "claude hooks carry no turn id");
        Equal(CodexEventKind.UserInputRequired, ClaudeEventParser.ParseHook("""{"hook_event_name":"PreToolUse","session_id":"cs","tool_name":"AskUserQuestion"}""")!.Kind, "claude question tool");
        Equal(null, ClaudeEventParser.ParseHook("""{"hook_event_name":"PreToolUse","session_id":"cs","tool_name":"Bash"}"""), "ordinary tool is not a question");
        Equal(null, ClaudeEventParser.ParseHook("""{"hook_event_name":"SubagentStop","session_id":"cs"}"""), "subagent stop is not the user's result");
        Equal(null, ClaudeEventParser.ParseHook("""{"hook_event_name":"Stop"}"""), "claude hook without a session is rejected");
        Equal("completed", ClaudeEventParser.ParseHook("""{"hook_event_name":"Stop","session_id":"cs"}""")!.Status, "claude stop completes");
        // Hooks stay supplementary: the record monitor supplies the turn, the hook the approval.
        var claudeJobs = new StatusAggregator();
        claudeJobs.Apply(claudeStart);
        Equal(MascotState.NeedsAttention, claudeJobs.Apply(ClaudeEventParser.ParseHook("""{"hook_event_name":"Notification","session_id":"claude-session"}""")!).State, "claude notification raises attention");
        Equal(MascotState.Running, claudeJobs.Apply(ClaudeEventParser.ParseHook("""{"hook_event_name":"PostToolUse","session_id":"claude-session","tool_name":"Bash"}""")!).State, "a tool that ran answers the prompt in front of it");
        Equal(MascotState.Completed, claudeJobs.Apply(ClaudeEventParser.ParseHook("""{"hook_event_name":"Stop","session_id":"claude-session"}""")!).State, "claude stop hook completes the turn");

        var dir = Path.Combine(Path.GetTempPath(), "mascot-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "tail.jsonl");
            var tail = new JsonlTail();
            File.WriteAllText(path, "{\"msg\":\"한", new UTF8Encoding(false));
            Equal(0, tail.Read(path).Count, "partial record withheld");
            File.AppendAllText(path, "글\"}\n", new UTF8Encoding(false));
            Equal("{\"msg\":\"한글\"}", tail.Read(path).Single(), "UTF8 tail");
            Equal(0, tail.Read(path).Count, "no duplicate reads");
            File.WriteAllText(path, "{}\n", new UTF8Encoding(false));
            Equal("{}", tail.Read(path).Single(), "truncation");
            Equal(true, tail.WasTruncated, "truncation detected");

            var sessions = Path.Combine(dir, "sessions"); Directory.CreateDirectory(sessions);
            var log = Path.Combine(sessions, "rollout.jsonl");
            File.WriteAllText(log, """{"type":"session_meta","payload":{"id":"session","cwd":"C:/Project","source":"vscode"}}""" + "\n" + Line("task_started","old") + "\n" + Line("task_complete","old") + "\n");
            var monitor = new DesktopSessionMonitor(dir, Path.Combine(dir, "hooks"));
            var observed = new List<CodexEvent>();
            var state = new StatusAggregator();
            monitor.EventReceived += (_, e) => { observed.Add(e); state.Apply(e); };
            monitor.Poll();
            Equal(MascotState.Idle, state.State, "bootstrap history");
            Equal(true, observed.All(e => e.IsReplay), "bootstrap events silent");
            File.AppendAllText(log, Line("task_started", "new") + "\n");
            monitor.Poll();
            Equal(MascotState.Running, state.State, "live append start");
            File.AppendAllText(log, Line("task_complete", "new") + "\n");
            monitor.Poll();
            Equal(MascotState.Completed, state.State, "live append end");
            var count = observed.Count; monitor.Poll();
            Equal(count, observed.Count, "no repeated polling events");
            File.AppendAllText(log, "{bad}\n" + Line("task_started","next") + "\n");
            monitor.Poll();
            Equal(MascotState.Running, state.State, "recover malformed record");

            var projects = Path.Combine(dir, "projects", "C--Project"); Directory.CreateDirectory(projects);
            var claudeLog = Path.Combine(projects, "session.jsonl");
            File.WriteAllText(claudeLog, ClaudeQueue() + "\n" + ClaudePrompt("old") + "\n" + ClaudeAssistant("end_turn") + "\n");
            var claudeMonitor = new DesktopSessionMonitor(dir, Path.Combine(dir, "claude-hooks"), AgentAdapters.Claude);
            var claudeState = new StatusAggregator();
            var claudeEvents = new List<CodexEvent>();
            claudeMonitor.EventReceived += (_, e) => { claudeEvents.Add(e); claudeState.Apply(e); };
            claudeMonitor.Poll();
            Equal(MascotState.Idle, claudeState.State, "claude bootstrap history");
            Equal(true, claudeEvents.All(e => e.IsReplay), "claude bootstrap events silent");
            File.AppendAllText(claudeLog, ClaudePrompt("new") + "\n");
            claudeMonitor.Poll();
            Equal(MascotState.Running, claudeState.State, "claude live prompt");
            File.AppendAllText(claudeLog, ClaudeAssistant("tool_use") + "\n" + ClaudeSubagent() + "\n");
            claudeMonitor.Poll();
            Equal(MascotState.Running, claudeState.State, "claude tool step keeps running and ignores subagents");
            File.AppendAllText(claudeLog, ClaudeAssistant("end_turn") + "\n");
            claudeMonitor.Poll();
            Equal(MascotState.Completed, claudeState.State, "claude live completion");
            var claudeCount = claudeEvents.Count; claudeMonitor.Poll();
            Equal(claudeCount, claudeEvents.Count, "no repeated claude polling events");
        }
        finally { Directory.Delete(dir, true); }
        Console.WriteLine("PASS: " + _assertions + " assertions (aggregation, duplicate/late events, hooks, JSONL, real monitor fixture).");
    }
    private static string Line(string type, string turn) => JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow,
        type = "event_msg", payload = new { type, turn_id = turn } });
    private static string ClaudeQueue() => JsonSerializer.Serialize(new { type = "queue-operation", operation = "enqueue",
        timestamp = DateTimeOffset.UtcNow, sessionId = "claude-session" });
    private static string ClaudePrompt(string promptId) => JsonSerializer.Serialize(new { type = "user",
        sessionId = "claude-session", cwd = "C:/Project", version = "2.1.275", promptId, uuid = promptId,
        isSidechain = false, origin = new { kind = "human" }, message = new { role = "user", content = "fixture prompt" },
        timestamp = DateTimeOffset.UtcNow });
    private static string ClaudeAssistant(string stopReason, string? requestId = null) => JsonSerializer.Serialize(new { type = "assistant",
        sessionId = "claude-session", cwd = "C:/Project", isSidechain = false, requestId,
        message = new { role = "assistant", stop_reason = stopReason, content = new[] { new { type = "text", text = "ok" } } },
        timestamp = DateTimeOffset.UtcNow });
    private static string ClaudeToolResult() => JsonSerializer.Serialize(new { type = "user",
        sessionId = "claude-session", cwd = "C:/Project", isSidechain = false, toolUseResult = new { ok = true },
        message = new { role = "user", content = new[] { new { type = "tool_result", content = "done" } } },
        timestamp = DateTimeOffset.UtcNow });
    private static string ClaudeSubagent() => JsonSerializer.Serialize(new { type = "assistant",
        sessionId = "claude-session", cwd = "C:/Project", isSidechain = true,
        message = new { role = "assistant", stop_reason = "end_turn", content = new[] { new { type = "text", text = "sub" } } },
        timestamp = DateTimeOffset.UtcNow });
    // Left: a tool result whose output contains the marker. Right: a text block that mentions it mid-sentence.
    private static string ClaudeQuotedMarker(bool asToolResult) => asToolResult
        ? JsonSerializer.Serialize(new { type = "user", sessionId = "claude-session", cwd = "C:/Project",
            isSidechain = false, toolUseResult = new { ok = true },
            message = new { role = "user", content = new[] { new { type = "tool_result", content = "2 matches: [Request interrupted by user]" } } },
            timestamp = DateTimeOffset.UtcNow })
        : JsonSerializer.Serialize(new { type = "user", sessionId = "claude-session", cwd = "C:/Project",
            isSidechain = false,
            message = new { role = "user", content = new[] { new { type = "text", text = "the log said [Request interrupted by user] earlier" } } },
            timestamp = DateTimeOffset.UtcNow });
    private static string ClaudeInterrupt() => JsonSerializer.Serialize(new { type = "user",
        sessionId = "claude-session", cwd = "C:/Project", isSidechain = false,
        message = new { role = "user", content = new[] { new { type = "text", text = "[Request interrupted by user]" } } },
        timestamp = DateTimeOffset.UtcNow });
}
