namespace CodexMascot.Core;

public static class NotificationPresentationPolicy
{
    public static MascotState? SelectState(AggregationResult result, MascotState? displayedState,
        bool holdUntilConfirmed, string? notificationThreadId)
    {
        // New events replace the visible notification and restart its display
        // duration, even when another task has the same or a higher-priority state.
        if (result.NotificationState is { } notification) return notification;

        var attentionResolved = displayedState == MascotState.NeedsAttention &&
            !result.Jobs.Any(j => j.ThreadId == notificationThreadId && j.NeedsAttention);
        if (!result.StateChanged && !attentionResolved) return null;
        if (holdUntilConfirmed && !attentionResolved && displayedState is
            (MascotState.Completed or MascotState.Failed or MascotState.Interrupted or MascotState.NeedsAttention)) return null;

        // Unread results / pending approvals remain in the task list. They must
        // never resurrect a dismissed popup or choose the next popup's owner.
        if (result.Jobs.Any(j => j.State == MascotState.Running)) return MascotState.Running;
        if (result.Jobs.Any(j => j.State == MascotState.Disconnected)) return MascotState.Disconnected;
        return MascotState.Idle;
    }
}
