namespace CodexMascot.Core;

// A new turn may replace a task's completed state before the user clicks its
// notification. Keep that notification independently of the live task state.
public sealed class CompletionPopupPolicy
{
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    public void Observe(IEnumerable<JobSnapshot> jobs)
    {
        foreach (var job in jobs)
            if (job.HasUnreadResult && job.State == MascotState.Completed) _pending.Add(job.ThreadId);
    }

    public MascotState Resolve(MascotState state, bool keepUntilClick)
        => keepUntilClick && _pending.Count > 0 && state is not (MascotState.NeedsAttention or MascotState.Failed)
            ? MascotState.Completed : state;

    public void Acknowledge(string? threadId = null)
    {
        if (threadId is null) _pending.Clear();
        else _pending.Remove(threadId);
    }
}
