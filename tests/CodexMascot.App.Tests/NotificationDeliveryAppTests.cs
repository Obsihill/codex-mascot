using System.Reflection;
using System.Windows.Threading;
using CodexMascot.App;
using CodexMascot.Core;

internal static class NotificationDeliveryAppTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Do not show the main window: Loaded would start real agent monitoring.
        // All configuration/journal writes stay in the test executable directory.
        var window = new MainWindow();
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        void Dismiss(AgentKind kind) => typeof(MainWindow).GetMethod("DismissAgentNotification", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { kind });
        try
        {
            Field<DispatcherTimer>("_foregroundTimer").Stop();
            var manager = Field<CustomizationManager>("_customization");
            manager.Configuration.Global.SoundEnabled = false;
            manager.Configuration.Global.ShowIdle = false;
            manager.Configuration.Global.AlwaysOnTop = false;
            manager.Configuration.Monitor.AutoIncludeNewProjects = true;
            manager.Configuration.Monitor.AutoIncludeNewChats = true;
            var store = Field<LibraryStore>("_library");
            store.Library.Selected.Clear();
            var source = store.Library.Installed.First();
            store.Library.Select(source.Id); store.Library.Select(source.Id);
            foreach (var mascot in store.Library.Selected)
                foreach (var state in new[] { MascotState.Completed, MascotState.NeedsAttention, MascotState.Failed })
                {
                    mascot.Settings(state).Volume = 0;
                    mascot.Settings(state).HoldUntilClick = true;
                    mascot.Settings(state).ImageDurationMs = 2000;
                }
            var group = Field<MascotPresentationGroup>("_presentations");
            var prefix = Guid.NewGuid().ToString("N");
            var now = DateTimeOffset.UtcNow;
            void Send(AgentKind agent, CodexEventKind kind, string thread, int time, string? request = null) =>
                window.ReceiveMonitoredEvent(agent, new(kind, agent == AgentKind.Claude ? "Claude record" : "Codex record",
                    prefix + thread, thread, RequestId: request, OccurredAt: now.AddSeconds(time)) { ProjectPath = AppContext.BaseDirectory });

            Send(AgentKind.Codex, CodexEventKind.ApprovalRequired, "A", 0, "approval-a");
            check(group.IsPresenting && group.State == MascotState.NeedsAttention, "approval is displayed through MainWindow");
            Dismiss(AgentKind.Codex);
            check(!group.IsPresenting && Field<StatusAggregator>("_aggregator").State == MascotState.NeedsAttention,
                "confirmation hides popup without approving task");
            Send(AgentKind.Codex, CodexEventKind.ApprovalRequired, "B", 1, "approval-b");
            check(group.IsPresenting && group.Windows.Count == 2, "new approval displays every selected mascot after dismissal");
            Send(AgentKind.Claude, CodexEventKind.TurnCompleted, "C", 2);
            check(group.IsPresenting && group.State == MascotState.Completed && Field<AgentKind>("_popupAgent") == AgentKind.Claude,
                "Claude completion replaces pending Codex approval and retains correct activation target");
            var previous = group.Windows[0];
            var deadline = Field<DateTimeOffset>("_foregroundDismissAfter");
            Send(AgentKind.Claude, CodexEventKind.TurnCompleted, "C", 3);
            check(ReferenceEquals(previous, group.Windows[0]) && deadline == Field<DateTimeOffset>("_foregroundDismissAfter"),
                "duplicate completion neither replays nor resets visible duration");
            Send(AgentKind.Codex, CodexEventKind.TurnCompleted, "D", 4);
            check(!ReferenceEquals(previous, group.Windows[0]) && group.Windows.All(w => w.IsPresenting) &&
                Field<AgentKind>("_popupAgent") == AgentKind.Codex, "new same-state completion replays all mascots and updates activation target");
            check(!MainWindow.ShouldDismissForForeground(group.State, true, DateTimeOffset.UtcNow, Field<DateTimeOffset>("_foregroundDismissAfter")),
                "fresh notification still receives its minimum foreground display duration");
            Dismiss(AgentKind.Codex);
            Send(AgentKind.Claude, CodexEventKind.TurnCompleted, "E", 5);
            check(group.IsPresenting && group.State == MascotState.Completed, "later result remains displayable after foreground-style acknowledgement");
        }
        finally { window.ExitApplication(); }
    }
}
