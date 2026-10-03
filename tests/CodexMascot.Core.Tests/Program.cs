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
        Equal(MascotState.Interrupted, popup.Resolve(MascotState.Interrupted, true), "interruption overrides held completion");
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
        TestRecentLimit();
        TestProjectSelection();
        TestNotificationRegressions();
        NotificationDeliveryTests.Run((condition, message) => Equal(true, condition, message));
        TestTranscriptQuestions();
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
        var failed = DesktopEventParser.ParseTranscript(Line("turn_failed", "t"), identity)!;
        Equal(CodexEventKind.TurnCompleted, failed.Kind, "explicit failure terminates turn");
        Equal(MascotState.Failed, new StatusAggregator().Apply(failed).State, "explicit failure reaches mascot state");
        Equal(MascotState.Idle, new StatusAggregator().Apply(failed with { IsReplay = true }).State, "historical failure stays silent");
        Equal(null, DesktopEventParser.ParseTranscript(Line("error", "t"), identity), "unspecified error is not guessed to be terminal");
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
    private static void TestTranscriptQuestions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mascot-question-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sessions"));
        try
        {
            var path = Path.Combine(dir, "sessions", "fixture.jsonl");
            string Call(string name, string id) => JsonSerializer.Serialize(new { type = "response_item",
                timestamp = DateTimeOffset.UtcNow, payload = new { type = "function_call", name, call_id = id, arguments = "PRIVATE QUESTION" } });
            string Output(string id, string output) => JsonSerializer.Serialize(new { type = "response_item",
                timestamp = DateTimeOffset.UtcNow, payload = new { type = "function_call_output", call_id = id, output } });
            File.WriteAllText(path, """{"type":"session_meta","payload":{"id":"question-session"}}""" + "\n" +
                Line("task_started", "old") + "\n" + Call("request_user_input_async", "old") + "\n" + Line("task_complete", "old") + "\n");
            var monitor = new DesktopSessionMonitor(dir, Path.Combine(dir, "no-hooks"));
            var jobs = new StatusAggregator();
            var events = new List<CodexEvent>();
            monitor.EventReceived += (_, e) => { events.Add(e); jobs.Apply(e); };
            monitor.Poll();
            Equal(MascotState.Idle, jobs.State, "historical question does not alert");
            void Append(string line) { File.AppendAllText(path, line + "\n"); monitor.Poll(); }
            Append(Line("task_started", "live"));
            Append(Call("request_user_input_async", "async"));
            Equal(MascotState.NeedsAttention, jobs.State, "actual async question shape alerts without hooks");
            var count = events.Count;
            Append(Call("request_user_input_async", "async"));
            Equal(count, events.Count, "duplicate question is ignored");
            Append(Output("unrelated", "{}"));
            Equal(MascotState.NeedsAttention, jobs.State, "unrelated tool result does not answer question");
            Append(Output("async", "{\"accepted\":true}"));
            Equal(MascotState.NeedsAttention, jobs.State, "async delivery acknowledgment is not an answer");
            Append(Line("task_complete", "live"));
            Equal(MascotState.NeedsAttention, jobs.State, "async question survives end of assistant turn");
            jobs.Acknowledge();
            Equal(MascotState.NeedsAttention, jobs.State, "click does not answer question");
            Append(Line("user_message", "live"));
            Equal(MascotState.Idle, jobs.State, "user follow-up clears async question after turn ends");
            Append(Line("task_started", "sync-turn"));
            Append(Call("functions.request_user_input", "sync"));
            Equal(MascotState.NeedsAttention, jobs.State, "synchronous question alerts");
            Append(Output("sync", "{\"answers\":{}}"));
            Equal(MascotState.Running, jobs.State, "synchronous answer resumes running");
            Append(Call("some_request_user_input_fake", "fake"));
            Equal(MascotState.Running, jobs.State, "unrelated tool names are ignored");
            Append(Call("request_user_input_async", "rejected"));
            Append(Output("rejected", "{\"accepted\":false}"));
            Equal(MascotState.Running, jobs.State, "rejected async request is cleared");
            Append(Call("request_user_input_async", "failure"));
            Append(Line("turn_failed", "sync-turn"));
            Equal(MascotState.Failed, jobs.State, "failure clears pending async question");
            Equal(false, events.Any(e => e.Message?.Contains("PRIVATE") == true), "question text is not copied to events");
        }
        finally { Directory.Delete(dir, true); }
    }
    private static void TestNotificationRegressions()
    {
        var jobs = new StatusAggregator();
        var popup = new CompletionPopupPolicy();
        popup.Observe(jobs.Apply(E(CodexEventKind.TurnCompleted, job: "complete")).Jobs);
        jobs.Apply(E(CodexEventKind.TurnCompleted, job: "interrupt", t: 1, status: "interrupted"));
        Equal(MascotState.Interrupted, popup.Resolve(jobs.State, true), "another task's completion cannot mask interruption");
        jobs.Acknowledge("interrupt");
        Equal(MascotState.Completed, jobs.State, "pending completion survives interruption acknowledgement");
        jobs.Apply(E(CodexEventKind.TurnCompleted, job: "failure", t: 2, status: "failed"));
        Equal(MascotState.Failed, jobs.State, "failure remains above completed results");
        jobs.Apply(E(CodexEventKind.ApprovalRequired, job: "approval", t: 3));
        Equal(MascotState.NeedsAttention, jobs.State, "approval remains highest priority");

        var dir = Path.Combine(Path.GetTempPath(), "mascot-hook-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var adapter in new[] { AgentAdapters.Codex, AgentAdapters.Claude })
            {
                var hookDir = Path.Combine(dir, adapter.Kind.ToString());
                Directory.CreateDirectory(hookDir);
                var monitor = new DesktopSessionMonitor(Path.Combine(dir, "missing-home"), hookDir, adapter);
                var received = new List<CodexEvent>();
                monitor.EventReceived += (_, e) => received.Add(e);
                monitor.Poll();
                var path = Path.Combine(hookDir, "approval.json");
                File.WriteAllText(path, JsonSerializer.Serialize(new {
                    session_id = "fixture", hook_event_name = adapter.Kind == AgentKind.Codex ? "PermissionRequest" : "Notification",
                    tool_name = "Bash", timestamp = DateTimeOffset.UtcNow
                }));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
                monitor.Poll();
                Equal(1, received.Count, adapter.Kind + " hook received without transcript directory");
                Equal(MascotState.NeedsAttention, new StatusAggregator().Apply(received.Single()).State, adapter.Kind + " hook raises attention");
                monitor.Poll();
                Equal(1, received.Count, adapter.Kind + " hook is not replayed on next poll");
            }
        }
        finally { Directory.Delete(dir, true); }
    }
    private static void TestProjectSelection()
    {
        var config = new MonitorConfiguration();
        var root = Path.Combine(Path.GetTempPath(), "project-watch-test-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "A"); var b = Path.Combine(root, "B");
        Equal(true, ProjectWatchPolicy.Observe(config, a), "new project discovered");
        Equal(true, ProjectWatchPolicy.Allows(config, a), "automatic monitoring defaults to enabled");
        Equal(false, ProjectWatchPolicy.Observe(config, a.ToUpperInvariant() + Path.DirectorySeparatorChar), "same path and case variants deduplicate");
        config.Projects.Single().Enabled = false;
        Equal(false, ProjectWatchPolicy.Allows(config, a), "explicitly excluded project stays excluded with auto enabled");
        config.AutoIncludeNewProjects = false;
        ProjectWatchPolicy.Observe(config, b);
        Equal(false, ProjectWatchPolicy.Allows(config, b), "newly discovered project excluded while auto is off");
        config.Projects.Single(p => p.Path == b).Enabled = true;
        Equal(true, ProjectWatchPolicy.Allows(config, b), "manually enabled project works while auto is off");
        Equal(false, ProjectWatchPolicy.Allows(config, b + "-other"), "project selection never matches a sibling prefix");
        Equal(false, ProjectWatchPolicy.Allows(config, Path.Combine(b, "nested")), "nested working directories are separately selectable projects");
        config.AutoIncludeNewProjects = true;
        Equal(false, ProjectWatchPolicy.Allows(config, a), "reenabling auto never undoes an explicit exclusion");
        Equal(false, ProjectWatchPolicy.Allows(config, null), "unknown project events cannot bypass the project filter");
        Equal(false, ProjectWatchPolicy.Observe(config, "relative-path"), "invalid relative project path is not saved");
        Directory.CreateDirectory(Path.Combine(root, "sessions"));
        try
        {
            File.WriteAllText(Path.Combine(root, "sessions", "test.jsonl"), JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = "fixture", cwd = a, source = "vscode" } }) + "\n");
            Equal(a, ProjectWatchPolicy.Discover(root, AgentAdapters.Codex, 100).Single(), "project picker discovers transcript headers without running an agent");
            File.WriteAllText(Path.Combine(root, "session_index.jsonl"), JsonSerializer.Serialize(new { id = "fixture", thread_name = "Test chat title" }) + "\n");
            var chat = ChatWatchPolicy.Discover(root, AgentAdapters.Codex, 100).Single();
            Equal("Test chat title", chat.Title, "chat picker uses title index");
            Equal("fixture", chat.Id, "chat picker keeps session ID");
            var chats = new MonitorConfiguration { AutoIncludeNewChats = false };
            ChatWatchPolicy.Observe(chats, AgentKind.Codex, "test", a);
            Equal(false, ChatWatchPolicy.Allows(chats, AgentKind.Codex, "test", a), "new chat starts excluded");
            chats.Chats.Single().Enabled = true;
            Equal(true, ChatWatchPolicy.Allows(chats, AgentKind.Codex, "test", a), "individual chat can be enabled");
            Equal(false, ChatWatchPolicy.Allows(chats, AgentKind.Claude, "test", a), "agent identity separates chats");
            chats.Chats.Single().Enabled = false; chats.AutoIncludeNewChats = true;
            Equal(false, ChatWatchPolicy.Allows(chats, AgentKind.Codex, "test", a), "explicit exclusion survives automatic monitoring enabled");
            Equal(true, ChatWatchPolicy.Allows(chats, AgentKind.Codex, "sibling", a), "same-folder sibling remains monitored");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void TestRecentLimit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mascot-limit-test-" + Guid.NewGuid().ToString("N"));
        var sessions = Path.Combine(dir, "sessions"); Directory.CreateDirectory(sessions);
        try
        {
            string Write(string id, int age)
            {
                var path = Path.Combine(sessions, id + ".jsonl");
                File.WriteAllText(path, JsonSerializer.Serialize(new { type = "session_meta", payload = new { id, cwd = "C:/Fixture", source = "vscode" } }) + "\n" + Line("task_started", id) + "\n");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-age)); return path;
            }
            Write("older", 10); var active = Write("active", 1);
            var monitor = new DesktopSessionMonitor(dir, Path.Combine(dir, "hooks"), recentSessionLimit: 1);
            var events = new List<CodexEvent>(); MonitorHealth? health = null;
            monitor.EventReceived += (_, e) => events.Add(e); monitor.HealthChanged += (_, e) => health = e;
            monitor.Poll();
            Equal(1, health!.Sessions, "recent cap limits initially read transcripts");
            Equal(false, events.Any(e => e.ThreadId == "older"), "old transcripts are not replayed outside cap");
            Write("newer", 0); Rescan(); monitor.Poll();
            Equal(2, health.Sessions, "active transcript remains watched outside recent cap");
            File.AppendAllText(active, Line("task_complete", "active") + "\n"); monitor.Poll();
            Equal(true, events.Any(e => e.ThreadId == "active" && e.Kind == CodexEventKind.TurnCompleted && !e.IsReplay), "active task outside cap still reports live completion");
            File.SetLastWriteTimeUtc(active, DateTime.UtcNow.AddMinutes(-5)); Rescan(); monitor.Poll();
            Equal(1, health.Sessions, "finished old transcript is evicted from polling set");
            var completionCount = events.Count(e => e.ThreadId == "active" && e.Kind == CodexEventKind.TurnCompleted && !e.IsReplay);
            File.SetLastWriteTimeUtc(active, DateTime.UtcNow.AddSeconds(1)); Rescan(); monitor.Poll();
            Equal(completionCount, events.Count(e => e.ThreadId == "active" && e.Kind == CodexEventKind.TurnCompleted && !e.IsReplay), "recent re-entry never replays an old completion as a new alert");
            File.AppendAllText(active, Line("task_started", "resumed") + "\n" + Line("task_complete", "resumed") + "\n"); monitor.Poll();
            Equal(completionCount + 1, events.Count(e => e.ThreadId == "active" && e.Kind == CodexEventKind.TurnCompleted && !e.IsReplay), "recent re-entry reads and reports new appended turns");
            Equal(1, new DesktopSessionMonitor(dir, dir, recentSessionLimit: -1).RecentSessionLimit, "recent cap has safe lower bound");
            Equal(1000, new DesktopSessionMonitor(dir, dir, recentSessionLimit: int.MaxValue).RecentSessionLimit, "recent cap has safe upper bound");
            void Rescan() => typeof(DesktopSessionMonitor).GetField("_nextScan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(monitor, DateTimeOffset.MinValue);
        }
        finally { Directory.Delete(dir, true); }
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
