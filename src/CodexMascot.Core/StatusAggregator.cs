namespace CodexMascot.Core;

// Single-threaded: the caller owns synchronization. A click never approves requests.
public sealed class StatusAggregator
{
    private sealed class Job
    {
        public required string Id;
        public string? TurnId, ProjectPath, Message;
        public string Source = "";
        public MascotState State;
        public bool Unread;
        public DateTimeOffset Updated;
        public HashSet<string> Pending = new(StringComparer.Ordinal);
        public HashSet<string> FinishedTurns = new(StringComparer.Ordinal);
    }
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    public IReadOnlyList<JobSnapshot> Jobs => _jobs.Values.OrderByDescending(j => j.Updated)
        .Select(j => new JobSnapshot(j.Id, j.State, j.TurnId, j.Message, j.Unread,
            j.Pending.Count > 0, j.Updated, j.ProjectPath, j.Source)).ToArray();
    public MascotState State => AggregateState();

    public AggregationResult Apply(CodexEvent e)
    {
        var before = State;
        var id = e.ThreadId ?? e.SourceId;
        if (!_jobs.TryGetValue(id, out var j)) _jobs[id] = j = new Job { Id = id };
        j.ProjectPath = e.ProjectPath ?? j.ProjectPath;
        if (e.Time < j.Updated || (e.TurnId is not null && j.FinishedTurns.Contains(e.TurnId)
            && e.Kind is not CodexEventKind.ThreadClosed)) return Result(before, e, false);
        if (j.TurnId is not null && e.TurnId is not null && j.TurnId != e.TurnId &&
            e.Kind is CodexEventKind.TurnCompleted or CodexEventKind.ItemStarted or CodexEventKind.ItemCompleted
                or CodexEventKind.ApprovalRequired or CodexEventKind.UserInputRequired or CodexEventKind.ServerRequestResolved)
            return Result(before, e, false);
        j.Updated = e.Time;
        j.Source = e.SourceId;
        j.Message = e.Message ?? j.Message;
        switch (e.Kind)
        {
            case CodexEventKind.TurnStarted:
                if (e.TurnId != j.TurnId || j.State == MascotState.Idle)
                { j.Pending.Clear(); j.Unread = false; j.State = MascotState.Running; }
                j.TurnId = e.TurnId ?? j.TurnId;
                break;
            case CodexEventKind.ApprovalRequired:
            case CodexEventKind.UserInputRequired:
                j.TurnId = e.TurnId ?? j.TurnId;
                j.Pending.Add(e.RequestId ?? "attention");
                j.State = MascotState.NeedsAttention;
                break;
            case CodexEventKind.ServerRequestResolved:
                if (e.RequestId is not null) j.Pending.Remove(e.RequestId);
                else j.Pending.Clear();
                if (j.Pending.Count == 0 && j.State == MascotState.NeedsAttention)
                    j.State = j.TurnId is null ? MascotState.Idle : MascotState.Running;
                break;
            case CodexEventKind.TurnCompleted:
                if (e.TurnId is not null)
                {
                    if (j.FinishedTurns.Count > 128) j.FinishedTurns.Clear();
                    j.FinishedTurns.Add(e.TurnId);
                }
                j.TurnId = null; j.Pending.Clear(); j.Unread = !e.IsReplay;
                j.State = e.IsReplay ? MascotState.Idle : e.Status switch
                { "failed" => MascotState.Failed, "interrupted" => MascotState.Interrupted, _ => MascotState.Completed };
                break;
            case CodexEventKind.ThreadStatusChanged:
                if (e.Status is "unknown" or "disconnected")
                { if (!j.Unread) { j.State = MascotState.Disconnected; j.Pending.Clear(); } }
                else if (e.Status == "active")
                {
                    if (e.ActiveFlags.Count > 0)
                    { j.Pending.Add("status"); j.State = MascotState.NeedsAttention; }
                    else { j.Pending.Remove("status"); if (!j.Unread && j.Pending.Count == 0) j.State = MascotState.Running; }
                }
                else if (e.Status == "systemError")
                { j.Pending.Clear(); j.State = MascotState.Failed; j.Unread = !e.IsReplay; }
                else if (e.Status == "idle" && !j.Unread)
                { j.Pending.Clear(); j.TurnId = null; j.State = MascotState.Idle; }
                break;
            case CodexEventKind.ItemStarted:
            case CodexEventKind.ItemCompleted:
                if (!j.Unread && j.Pending.Count == 0 && j.TurnId is not null) j.State = MascotState.Running;
                break;
            case CodexEventKind.ThreadClosed:
                j.Pending.Clear(); j.TurnId = null;
                if (!j.Unread) j.State = MascotState.Idle;
                break;
        }
        return Result(before, e, !e.IsReplay);
    }

    public void Acknowledge(string? threadId = null)
    {
        foreach (var j in _jobs.Values.Where(j => threadId is null || j.Id == threadId))
        {
            j.Unread = false;
            if (j.Pending.Count == 0 && j.TurnId is null) j.State = MascotState.Idle;
        }
    }
    public void Clear() => _jobs.Clear();
    private AggregationResult Result(MascotState before, CodexEvent e, bool notify)
        => new(State, State != before, notify && State != before, e.ThreadId, e.Message, Jobs);
    private MascotState AggregateState()
    {
        if (_jobs.Values.Any(j => j.Pending.Count > 0)) return MascotState.NeedsAttention;
        foreach (var state in new[] { MascotState.Failed, MascotState.Completed, MascotState.Interrupted })
            if (_jobs.Values.Any(j => j.Unread && j.State == state)) return state;
        if (_jobs.Values.Any(j => j.State == MascotState.Running)) return MascotState.Running;
        if (_jobs.Values.Any(j => j.State == MascotState.Disconnected)) return MascotState.Disconnected;
        return MascotState.Idle;
    }
}
