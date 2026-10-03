using System.Text.Json;
using CodexMascot.Core;

internal static class NotificationDeliveryTests
{
    internal static void Run(Action<bool, string> check)
    {
        var epoch = DateTimeOffset.UtcNow;
        CodexEvent E(CodexEventKind kind, string job, string? turn, int time,
            string? request = null, string? status = null) =>
            new(kind, "Codex record", job, turn, RequestId: request, Status: status, OccurredAt: epoch.AddSeconds(time));
        MascotState? Select(AggregationResult result, MascotState? shown = null, bool hold = false, string? thread = null)
            => NotificationPresentationPolicy.SelectState(result, shown, hold, thread);

        var jobs = new StatusAggregator();
        jobs.Apply(E(CodexEventKind.TurnStarted, "A", "1", 0));
        var first = jobs.Apply(E(CodexEventKind.TurnCompleted, "A", "1", 1));
        check(first.ShouldNotify && Select(first) == MascotState.Completed, "first completion notifies");
        jobs.Apply(E(CodexEventKind.TurnStarted, "B", "2", 2));
        var second = jobs.Apply(E(CodexEventKind.TurnCompleted, "B", "2", 3));
        check(!second.StateChanged && second.ShouldNotify, "new completion notifies without aggregate transition");
        check(Select(second) == MascotState.Completed, "completion after timed hide is shown");
        check(Select(second, MascotState.Completed, true, "A") == MascotState.Completed, "held completion restarts for a different result");
        jobs.Acknowledge("B");
        var duplicate = jobs.Apply(E(CodexEventKind.TurnCompleted, "B", "2", 4));
        check(!duplicate.ShouldNotify && Select(duplicate) is null, "duplicate does not resurrect acknowledged result");

        jobs = new();
        jobs.Apply(E(CodexEventKind.TurnStarted, "A", "1", 0));
        jobs.Apply(E(CodexEventKind.ApprovalRequired, "A", "1", 1, "r1"));
        jobs.Acknowledge("A");
        check(jobs.Jobs.Single().NeedsAttention, "dismissal never approves an actual request");
        var attention = jobs.Apply(E(CodexEventKind.ApprovalRequired, "B", "2", 2, "r2"));
        check(!attention.StateChanged && Select(attention) == MascotState.NeedsAttention, "fresh approval shows after earlier approval was dismissed");
        check(!jobs.Apply(E(CodexEventKind.ApprovalRequired, "B", "2", 3, "r2")).ShouldNotify, "same pending request is deduplicated");
        check(jobs.Apply(E(CodexEventKind.ApprovalRequired, "B", "2", 4, "r3")).ShouldNotify, "another request in same turn notifies");
        var complete = jobs.Apply(E(CodexEventKind.TurnCompleted, "C", "3", 5));
        check(complete.State == MascotState.NeedsAttention && Select(complete, MascotState.NeedsAttention, true, "B") == MascotState.Completed,
            "old pending approval cannot suppress another task's completion");
        var failed = jobs.Apply(E(CodexEventKind.TurnCompleted, "D", "4", 6, status: "failed"));
        check(Select(failed) == MascotState.Failed, "failure delivers its own state despite pending approval");
        var interrupted = jobs.Apply(E(CodexEventKind.TurnCompleted, "E", "5", 7, status: "interrupted"));
        check(Select(interrupted) == MascotState.Interrupted, "interruption delivers its own state");

        jobs = new();
        jobs.Apply(E(CodexEventKind.TurnStarted, "A", "1", 0));
        jobs.Apply(E(CodexEventKind.ApprovalRequired, "A", "1", 1, "r1"));
        jobs.Apply(E(CodexEventKind.TurnStarted, "B", "2", 2));
        jobs.Apply(E(CodexEventKind.ApprovalRequired, "B", "2", 3, "r2"));
        var resolved = jobs.Apply(E(CodexEventKind.ServerRequestResolved, "B", "2", 4, "r2"));
        check(!resolved.StateChanged && Select(resolved, MascotState.NeedsAttention, true, "B") == MascotState.Running,
            "resolved visible approval closes even when another task is still pending");
        jobs = new();
        jobs.Apply(E(CodexEventKind.TurnCompleted, "A", "1", 0));
        var next = jobs.Apply(E(CodexEventKind.TurnStarted, "A", "2", 1));
        check(Select(next, MascotState.Completed, true, "A") is null, "held completion survives next turn until confirmation");
        check(Select(next) == MascotState.Running, "hidden notification permits normal running state");
        jobs = new();
        var replay = jobs.Apply(E(CodexEventKind.TurnCompleted, "A", "1", 0) with { IsReplay = true });
        check(!replay.ShouldNotify && Select(replay) is null, "replay never creates completion notification");

        CodexEvent Hook(string name, int time) => DesktopEventParser.ParseHook(JsonSerializer.Serialize(new {
            session_id = "H", turn_id = "", hook_event_name = name, tool_name = "Bash", timestamp = epoch.AddSeconds(time)
        }))!;
        check(Hook("Stop", 0).TurnId is null, "empty hook turn ID is normalized to absent");
        jobs = new();
        jobs.Apply(E(CodexEventKind.TurnStarted, "H", "real-turn", 0));
        check(jobs.Apply(Hook("PermissionRequest", 1)).NotificationState == MascotState.NeedsAttention,
            "hook lacking turn ID applies to active transcript turn");
        jobs.Apply(Hook("Stop", 2));
        check(!jobs.Apply(E(CodexEventKind.TurnCompleted, "H", "real-turn", 3)).ShouldNotify,
            "transcript terminal after hook terminal does not notify twice");
        jobs = new();
        jobs.Apply(E(CodexEventKind.TurnStarted, "H", "real-turn", 0));
        jobs.Apply(E(CodexEventKind.TurnCompleted, "H", "real-turn", 1));
        check(!jobs.Apply(Hook("Stop", 2)).ShouldNotify, "hook terminal after transcript terminal does not notify twice");
        jobs = new();
        for (var turn = 0; turn < 3; turn++)
        {
            jobs.Apply(Hook("UserPromptSubmit", turn * 4));
            var end = jobs.Apply(Hook("Stop", turn * 4 + 1));
            check(end.NotificationState == MascotState.Completed, "hook-only successive completion " + turn);
            jobs.Acknowledge("H");
            check(!jobs.Apply(Hook("Stop", turn * 4 + 2)).ShouldNotify, "hook-only duplicate stays dismissed " + turn);
        }
        // Defensive normalization also covers callers other than the JSON parser.
        jobs = new();
        jobs.Apply(E(CodexEventKind.TurnStarted, "H", "real-turn", 0));
        check(jobs.Apply(E(CodexEventKind.ApprovalRequired, "H", "  ", 1, "r1")).ShouldNotify,
            "aggregator accepts missing ID from any source");
        jobs = new();
        jobs.Apply(Hook("UserPromptSubmit", 0));
        jobs.Apply(E(CodexEventKind.ThreadStatusChanged, "H", null, 1, status: "systemError"));
        jobs.Apply(Hook("UserPromptSubmit", 2));
        check(jobs.Apply(Hook("Stop", 3)).NotificationState == MascotState.Completed,
            "anonymous turn recovers after system-error terminal status");
    }
}
