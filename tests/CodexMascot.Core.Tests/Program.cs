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
    private static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--probe")
        {
            var probeAggregator = new StatusAggregator();
            var m = new DesktopSessionMonitor(args[1], Path.Combine(Path.GetTempPath(), "mascot-nonexistent-hook-probe"));
            var live = 0;
            m.EventReceived += (_, e) => { probeAggregator.Apply(e); if (!e.IsReplay) live++; };
            for (var i = 0; i < 6; i++) { m.Poll(); Thread.Sleep(650); }
            var report = JsonSerializer.Serialize(new { state = probeAggregator.State.ToString(), liveEvents = live,
                jobs = probeAggregator.Jobs.Select(j => new { id = j.ThreadId, state = j.State.ToString(), cwd = j.ProjectPath, updated = j.LastUpdated }) }, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(report);
            if (args.Length > 2) File.WriteAllText(args[2], report);
            return;
        }
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
        }
        finally { Directory.Delete(dir, true); }
        Console.WriteLine("PASS: " + _assertions + " assertions (aggregation, duplicate/late events, hooks, JSONL, real monitor fixture).");
    }
    private static string Line(string type, string turn) => JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow,
        type = "event_msg", payload = new { type, turn_id = turn } });
}
