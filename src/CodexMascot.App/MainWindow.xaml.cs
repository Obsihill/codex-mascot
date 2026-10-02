using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using CodexMascot.Core;
using Forms = System.Windows.Forms;

namespace CodexMascot.App;

public partial class MainWindow : Window
{
    private readonly StatusAggregator _aggregator = new();
    private readonly CustomizationManager _customization = new();
    private LibraryStore _library = null!;
    private readonly MascotPresentationGroup _presentations = new();
    private Window? _workspaceSettings;
    private readonly System.Windows.Threading.DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private AgentKind _popupAgent;
    private string? _popupThreadId;
    private DateTimeOffset _foregroundDismissAfter;
    private readonly System.Windows.Threading.DispatcherTimer _monitorEditTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly SemaphoreSlim _monitorGate = new(1, 1);
    private bool _monitorPaused;
    private bool _monitorRefreshRequested;
    private readonly ObservableCollection<JobRow> _jobs = new();
    private readonly Forms.NotifyIcon _tray;
    private readonly TrayThemeIcon _trayTheme;
    private CancellationTokenSource? _monitorCancel;
    private readonly List<Task> _monitorTasks = new();
    private readonly Dictionary<AgentKind, DesktopSessionMonitor> _sessionMonitors = new();
    private bool _closing, _exitRequested;
    private Window? _projectSelection;
    private string? _hookError;
    private readonly Dictionary<AgentKind, MonitorHealth> _health = new();
    private readonly Dictionary<(AgentKind Kind, string Id), string> _projectByThread = new();
    private readonly HashSet<(AgentKind Kind, string Id)> _activeChats = new();
    private readonly string _journal = Path.Combine(AppPaths.ConfigDirectory, "events.jsonl");

    public MainWindow()
    {
        InitializeComponent();
        PencilWindow.Apply(this);
        JobsListView.ItemsSource = _jobs;
        var config = _customization.Configuration;
        if (Environment.ProcessPath is { } executable && Path.GetFileName(executable).Equals("AgentMascot.exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                StartupRegistration.RefreshRenamedExecutable();
                config.Global.StartWithWindows = StartupRegistration.IsEnabledFor(StartupRegistration.ReadCommand(), executable);
                _customization.Save();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
        _library = new LibraryStore(global: config.Global, configuration: config);
        Dashboard.Initialize(_library, _customization);
        Dashboard.SettingsRequested += (_, _) => OpenWorkspaceSettings();
        Dashboard.ProjectsRequested += (_, _) => OpenProjectSelection();
        Dashboard.LibraryChanged += (_, _) =>
        {
            _presentations.RemoveIneligible(_library.Library);
            _presentations.ApplyPreferences(_library, _customization);
        };
        _presentations.Clicked += Overlay_OnClicked;
        _presentations.Feedback += (_, text) => Log(text);
        CodexHomeTextBox.Text = config.Monitor.CodexHome ?? HookIntegration.DefaultHomeFor(AgentKind.Codex);
        ClaudeHomeTextBox.Text = config.Monitor.ClaudeHome ?? HookIntegration.DefaultHomeFor(AgentKind.Claude);
        _monitorPaused = !config.Monitor.AutoStart;
        _monitorEditTimer.Tick += async (_, _) =>
        {
            _monitorEditTimer.Stop();
            await StartMonitoring();
        };
        CodexHomeTextBox.TextChanged += (_, _) => ScheduleMonitorUpdate();
        ClaudeHomeTextBox.TextChanged += (_, _) => ScheduleMonitorUpdate();
        _tray = new Forms.NotifyIcon { Text = AppBrand.Name };
        _trayTheme = new TrayThemeIcon(_tray);
        _tray.Visible = true;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(AppBrand.OpenLabel, null, (_, _) => Dispatcher.BeginInvoke(ShowMain));
        menu.Items.Add(Loc.T("설정"), null, (_, _) => Dispatcher.Invoke(OpenWorkspaceSettings));
        menu.Items.Add(Loc.T("종료"), null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _tray.ContextMenuStrip = menu;
        _tray.MouseDoubleClick += Tray_OnMouseDoubleClick;
        Loc.Changed += RefreshLanguage;
        ApplyConfiguration();
        _foregroundTimer.Tick += (_, _) => DismissForForegroundApp();
        _foregroundTimer.Start();
        Loaded += async (_, _) =>
        {
            if (config.Global.StartWithWindows)
            {
                try { StartupRegistration.RefreshRenamedExecutable(); }
                catch (Exception ex) { Log(Loc.T("Windows 자동 시작 경로를 갱신하지 못했습니다: ") + ex.Message); }
            }
            if (Environment.GetCommandLineArgs().Contains("--tray")) Hide();
            if (config.Monitor.AutoStart) await StartMonitoring();
            if (_customization.LoadWarning is not null) Log(_customization.LoadWarning);
        };
    }

    private string ProviderSetting() => "both";
    private IReadOnlyList<AgentKind> EnabledKinds() => new[] { AgentKind.Codex, AgentKind.Claude }
        .Where(kind => !string.IsNullOrWhiteSpace(kind == AgentKind.Claude ? ClaudeHomeTextBox.Text : CodexHomeTextBox.Text)).ToArray();

    // Preserve empty paths: they explicitly disable monitoring for that agent.
    private string HomeFor(AgentKind kind)
    {
        var text = (kind == AgentKind.Claude ? ClaudeHomeTextBox.Text : CodexHomeTextBox.Text).Trim();
        return text.Length == 0 ? "" : Path.GetFullPath(text);
    }
    private void BrowseClaudeHome_OnClick(object sender, RoutedEventArgs e)
        => BrowseInto(ClaudeHomeTextBox, Loc.T("Claude Code 데이터 폴더 (.claude)를 선택하세요."));

    private async Task StartMonitoring()
    {
        if (_customization.IsEditing) return;
        await _monitorGate.WaitAsync();
        if (_closing || _customization.IsEditing) { _monitorGate.Release(); return; }
        MonitorButton.IsEnabled = false;
        try
        {
            await StopMonitoring();
            var kinds = EnabledKinds();
            _customization.Configuration.Monitor.CodexHome = HomeFor(AgentKind.Codex);
            _customization.Configuration.Monitor.ClaudeHome = HomeFor(AgentKind.Claude);
            _customization.Configuration.Monitor.Provider = ProviderSetting();
            _customization.Save();
            _aggregator.Clear();
            _activeChats.Clear();
            HideMascots();
            _health.Clear();
            RefreshJobs();
            if (_monitorPaused) { ConnectionTextBlock.Text = Loc.T("감시 정지됨 · 원래 도구의 작업은 계속됩니다."); return; }
            if (kinds.Count == 0)
            {
                ConnectionTextBlock.Text = Loc.T("감시 대상 없음 · 데이터 폴더를 확인하세요.");
                HookTextBlock.Text = Loc.T("데이터 폴더가 비어 있는 도구는 감시하지 않습니다.");
                WriteHealth();
                return;
            }
            EnsureAutomaticHooks(kinds);
            _monitorCancel = new CancellationTokenSource();
            var ct = _monitorCancel.Token;
            foreach (var kind in kinds)
            {
                var watched = kind;
                var monitor = new DesktopSessionMonitor(HomeFor(watched), HookIntegration.EventDirectoryFor(watched), AgentAdapters.For(watched), _customization.Configuration.Monitor.RecentSessionLimit);
                _sessionMonitors[watched] = monitor;
                monitor.EventReceived += (_, e) => Dispatch(() =>
                {
                    if (!ct.IsCancellationRequested) ReceiveMonitoredEvent(watched, e);
                });
                monitor.HealthChanged += (_, health) => Dispatch(() => { if (!ct.IsCancellationRequested) { _health[watched] = health; ShowHealth(); } });
                _monitorTasks.Add(Task.Run(() => monitor.RunAsync(ct)));
            }
            Log(Loc.T("감시 시작: ") + string.Join(" · ", kinds.Select(k => AgentAdapters.For(k).DisplayName + " " + HomeFor(k))));
        }
        catch (Exception ex) { Log(Loc.T("감시 시작 실패: ") + ex.Message); ConnectionTextBlock.Text = Loc.T("연결 확인 필요"); }
        finally { MonitorButton.IsEnabled = true; _monitorGate.Release(); }
    }
    private void ScheduleMonitorUpdate()
    {
        _monitorEditTimer.Stop();
        if (_customization.IsEditing) return;
        if (!_closing) _monitorEditTimer.Start();
    }
    private void EnsureAutomaticHooks(IReadOnlyList<AgentKind> kinds)
    {
        var errors = new List<string>();
        foreach (var kind in kinds)
        {
            try
            {
                var home = HomeFor(kind);
                if (Directory.Exists(home)) HookIntegration.EnsureInstalled(home, kind);
            }
            catch (Exception ex) { errors.Add(AgentAdapters.For(kind).DisplayName + ": " + ex.Message); }
        }
        _hookError = errors.Count == 0 ? null : Loc.T("Hook 설정 실패: ") + string.Join(" · ", errors);
    }
    internal void OpenProjectSelection()
    {
        if (_workspaceSettings is not null) { _workspaceSettings.Activate(); return; }
        if (_projectSelection is not null) { _projectSelection.Activate(); return; }
        var monitor = _customization.Configuration.Monitor;
        var homes = EnabledKinds().Select(k => (Kind: k, Home: HomeFor(k))).ToArray();
        var dialog = new ProjectSelectionWindow(_customization, limit =>
        {
            // Capture active chats on the UI thread for each rescan, including
            // turns that started after this picker was opened.
            var activeEntries = monitor.Chats.Where(c => homes.Any(h => h.Kind == c.Agent) && _activeChats.Contains((c.Agent, c.Id)))
                .Select(c => new ChatWatchEntry { Agent = c.Agent, Id = c.Id, ProjectPath = c.ProjectPath, Title = c.Title, Enabled = c.Enabled }).ToArray();
            return Task.Run<IReadOnlyList<ChatWatchEntry>>(() =>
                homes.SelectMany(h => ChatWatchPolicy.Discover(h.Home, AgentAdapters.For(h.Kind), limit))
                    .Concat(activeEntries).DistinctBy(c => ChatWatchPolicy.Key(c.Agent, c.Id)).ToArray());
        }) { Owner = this };
        dialog.SelectionChanged += (_, _) =>
        {
            foreach (var session in _sessionMonitors.Values) session.RecentSessionLimit = monitor.RecentSessionLimit;
            RefreshJobs();
            if (_popupThreadId is { } popupId && _aggregator.Jobs.FirstOrDefault(j => j.ThreadId == popupId) is { } popup && !IsWatched(popup))
                HideMascots();
        };
        _projectSelection = dialog;
        dialog.Closed += (_, _) => { _projectSelection = null; };
        dialog.Show();
    }
    internal void ReceiveMonitoredEvent(AgentKind kind, CodexEvent e)
    {
        if (e.ThreadId is { Length: > 0 } id)
        {
            if (e.Kind == CodexEventKind.TurnStarted) _activeChats.Add((kind, id));
            else if (e.Kind is CodexEventKind.TurnCompleted or CodexEventKind.ThreadClosed ||
                     e.Kind == CodexEventKind.ThreadStatusChanged && e.Status == "idle")
                _activeChats.Remove((kind, id));
        }
        var key = (kind, e.ThreadId ?? e.SourceId);
        var path = ProjectWatchPolicy.Normalize(e.ProjectPath);
        if (path is not null) _projectByThread[key] = path;
        else _projectByThread.TryGetValue(key, out path);
        var monitor = _customization.Configuration.Monitor;
        path ??= monitor.Chats.FirstOrDefault(c => c.Agent == kind && c.Id == key.Item2)?.ProjectPath;
        var changed = ProjectWatchPolicy.Observe(monitor, path);
        changed |= ChatWatchPolicy.Observe(monitor, kind, e.ThreadId, path);
        if (changed)
        {
            try { _customization.Save(); }
            catch (Exception ex) { Log(Loc.T("저장 실패: ") + ex.Message); }
        }
        if (ChatWatchPolicy.Allows(monitor, kind, e.ThreadId, path)) HandleEvent(e with { ProjectPath = path }, kind);
    }
    private void ShowHealth()
    {
        ConnectionTextBlock.Text = string.Join("   |   ", _health.Values.Select(h => Loc.CoreMessage(h.Message) + " · " + h.Sessions +
            Loc.T("개 작업 · 최근 수신 ") + (h.LastEvent?.ToLocalTime().ToString("HH:mm:ss") ?? Loc.T("새 이벤트 대기"))));
        var received = _health.Where(pair => pair.Value.HookEvents > 0).ToArray();
        HookTextBlock.Text = received.Length > 0
            ? Loc.T("Hook 수신 확인 · ") + string.Join(" · ", received.Select(pair => AgentAdapters.For(pair.Key).DisplayName + " " + pair.Value.HookEvents + Loc.T("개")))
              + Loc.T(". 승인/답변은 원래 도구에서 진행하세요.")
            : Loc.T("Hook 수신 전 · 시작/종료는 기록 감시로 표시합니다. 승인·질문 알림은 Hook 설정과 신뢰 검토 후에 사용할 수 있습니다.");
        if (_hookError is not null) HookTextBlock.Text = _hookError;
        WriteHealth();
    }
    private async Task StopMonitoring()
    {
        _monitorCancel?.Cancel();
        if (_monitorTasks.Count > 0) await Task.WhenAll(_monitorTasks);
        _monitorTasks.Clear();
        _sessionMonitors.Clear();
        _monitorCancel?.Dispose(); _monitorCancel = null;
    }
    private async void Monitor_OnClick(object sender, RoutedEventArgs e)
    {
        _monitorEditTimer.Stop(); _monitorPaused = false;
        if (_customization.IsEditing) { _monitorRefreshRequested = true; Log(Loc.T("확인을 누르면 연결 설정에 적용됩니다.")); return; }
        await StartMonitoring();
    }
    private async void PauseMonitor_OnClick(object sender, RoutedEventArgs e)
    {
        _monitorPaused = true;
        if (_customization.IsEditing) { Log(Loc.T("확인을 누르면 연결 설정에 적용됩니다.")); return; }
        await _monitorGate.WaitAsync();
        try { await StopMonitoring(); _aggregator.Clear(); RefreshJobs(); HideMascots(); ConnectionTextBlock.Text = Loc.T("감시 정지됨 · 원래 도구의 작업은 계속됩니다."); }
        finally { _monitorGate.Release(); }
    }
    private void BrowseHome_OnClick(object sender, RoutedEventArgs e) => BrowseInto(CodexHomeTextBox, Loc.T("Codex 데이터 폴더 (.codex)를 선택하세요."));
    private static void BrowseInto(System.Windows.Controls.TextBox box, string title)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = title, UseDescriptionForTitle = true, SelectedPath = box.Text };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) box.Text = dialog.SelectedPath;
    }
    private void HandleEvent(CodexEvent e, AgentKind kind)
    {
        var before = VisibleState();
        var result = _aggregator.Apply(e);
        var visibleJobs = _aggregator.Jobs.Where(IsWatched).ToArray();
        var visibleState = StateFor(visibleJobs);
        var visibleResult = result with { State = visibleState, StateChanged = visibleState != before, Jobs = visibleJobs };
        var presentation = NotificationPresentationPolicy.SelectState(visibleResult,
            _presentations.IsPresenting ? _presentations.State : null,
            _presentations.HasHeldNotifications, _popupThreadId);
        RefreshJobs();
        if (!e.IsReplay && e.Kind != CodexEventKind.ItemCompleted)
        {
            try { File.AppendAllText(_journal, JsonSerializer.Serialize(new { time = e.Time, e.SourceId, e.ThreadId, kind = e.Kind.ToString(), state = result.State.ToString(), notification = result.NotificationState?.ToString(), presentation = presentation?.ToString() }) + Environment.NewLine); }
            catch (IOException) { }
        }
        if (presentation is { } state)
            Present(state, !e.IsReplay, result.ShouldNotify ? kind : null, result.ShouldNotify ? e.ThreadId : null);
    }
    private void Present(MascotState state, bool sound, AgentKind? notificationAgent = null, string? notificationThreadId = null)
    {
        if (state is MascotState.Disconnected or MascotState.Connecting ||
            state == MascotState.Idle && !_customization.Configuration.Global.ShowIdle)
        { HideMascots(); return; }
        _presentations.Show(_library, _customization, state, sound);
        _popupAgent = notificationAgent ?? ActivationTarget(state);
        _popupThreadId = notificationThreadId;
        _foregroundDismissAfter = DateTimeOffset.UtcNow.AddMilliseconds(
            state is MascotState.Completed or MascotState.NeedsAttention or MascotState.Failed or MascotState.Interrupted
                ? MascotPresentationGroup.NotificationVisibleMilliseconds(_library, _customization, state) : 0);
    }
    private void RefreshJobs()
    {
        _jobs.Clear();
        var visible = _aggregator.Jobs.Where(IsWatched).ToArray();
        foreach (var j in visible)
            _jobs.Add(new(j.ThreadId, StateName(j.State), (j.ProjectPath is null ? Loc.T("(프로젝트 없음)") : Path.GetFileName(j.ProjectPath.TrimEnd('\\','/'))) + " · " + j.ThreadId[..Math.Min(8, j.ThreadId.Length)],
                j.LastUpdated == DateTimeOffset.MinValue ? "—" : j.LastUpdated.ToLocalTime().ToString("HH:mm:ss"),
                Loc.T(j.Source), Loc.CoreMessage(j.LastMessage), j.ProjectPath));
        StatusTextBlock.Text = StateName(StateFor(visible));
        _tray.Text = AppBrand.Name + " · " + StateName(StateFor(visible));
    }
    private MascotState VisibleState() => StateFor(_aggregator.Jobs.Where(IsWatched));
    private static MascotState StateFor(IEnumerable<JobSnapshot> jobs)
    {
        var visible = jobs.ToArray();
        if (visible.Any(j => j.NeedsAttention)) return MascotState.NeedsAttention;
        foreach (var state in new[] { MascotState.Failed, MascotState.Interrupted, MascotState.Completed })
            if (visible.Any(j => j.HasUnreadResult && j.State == state)) return state;
        if (visible.Any(j => j.State == MascotState.Running)) return MascotState.Running;
        return visible.Any(j => j.State == MascotState.Disconnected) ? MascotState.Disconnected : MascotState.Idle;
    }
    private bool IsWatched(JobSnapshot job)
    {
        var kind = job.Source.StartsWith("Claude", StringComparison.Ordinal) ? AgentKind.Claude : AgentKind.Codex;
        var path = job.ProjectPath;
        if (path is null) _projectByThread.TryGetValue((kind, job.ThreadId), out path);
        return ChatWatchPolicy.Allows(_customization.Configuration.Monitor, kind, job.ThreadId, path);
    }
    internal static string StateName(MascotState state) => state switch
    {
        MascotState.Running => Loc.T("작업 중"), MascotState.NeedsAttention => Loc.T("확인/승인 필요"), MascotState.Completed => Loc.T("응답 완료"),
        MascotState.Failed => Loc.T("실패"), MascotState.Interrupted => Loc.T("중단"), MascotState.Disconnected => Loc.T("상태 확인 필요"),
        MascotState.Connecting => Loc.T("연결 중"), _ => Loc.T("대기 중")
    };
    // The window to open belongs to whichever agent produced the result being dismissed,
    // so the target is read before acknowledging clears the unread flags.
    private AgentKind ActivationTarget(MascotState state)
    {
        var ranked = _aggregator.Jobs.Where(IsWatched)
            .OrderByDescending(j => j.State == state)
            .ThenByDescending(j => j.NeedsAttention || j.HasUnreadResult)
            .ThenByDescending(j => j.LastUpdated)
            .FirstOrDefault();
        return ranked?.Source.StartsWith("Claude", StringComparison.Ordinal) == true ? AgentKind.Claude : AgentKind.Codex;
    }
    private void Overlay_OnClicked(object? sender, EventArgs e)
    {
        var target = _popupAgent;
        DismissAgentNotification(target);
        if (!_customization.Configuration.Global.BringCodexToFrontOnClick) { ShowMain(); return; }
        var name = AgentAdapters.For(target).DisplayName;
        var result = CodexDesktopActivator.TryActivate(target);
        if (result == DesktopActivationResult.NotFound)
        {
            ShowMain();
            Log(Loc.T("실행 중인 ") + name + Loc.T(" 데스크톱 창을 찾지 못해 Mascot 작업 창을 열었습니다. 터미널에서 실행한 CLI 창은 전환하지 않습니다."));
        }
        else if (result == DesktopActivationResult.AttentionRequested)
            Log(Loc.T("Windows가 창 전환을 허용하지 않아 ") + name + Loc.T(" 작업 표시줄 아이콘으로 알렸습니다."));
    }
    private void DismissForForegroundApp()
    {
        if (_closing || !_presentations.IsPresenting) return;
        if (ShouldDismissForForeground(_presentations.State, CodexDesktopActivator.IsForeground(_popupAgent), DateTimeOffset.UtcNow, _foregroundDismissAfter))
            DismissAgentNotification(_popupAgent);
    }
    internal static bool ShouldDismissForForeground(MascotState state, bool agentIsForeground, DateTimeOffset now, DateTimeOffset dismissAfter)
        => agentIsForeground && now >= dismissAfter && state is MascotState.Completed or MascotState.NeedsAttention or MascotState.Failed or MascotState.Interrupted;
    private void DismissAgentNotification(AgentKind kind)
    {
        foreach (var job in _aggregator.Jobs.Where(j =>
            (j.Source.StartsWith("Claude", StringComparison.Ordinal) ? AgentKind.Claude : AgentKind.Codex) == kind).ToArray())
        {
            _aggregator.Acknowledge(job.ThreadId);
        }
        HideMascots();
        RefreshJobs();
    }
    internal void OpenWorkspaceSettings()
    {
        if (_workspaceSettings is not null) { _workspaceSettings.Activate(); return; }
        if (_projectSelection is not null) { _projectSelection.Activate(); return; }
        _monitorEditTimer.Stop(); _monitorRefreshRequested = false;
        var paused = _monitorPaused;
        var codex = CodexHomeTextBox.Text; var claude = ClaudeHomeTextBox.Text;
        ShellRoot.Children.Remove(WorkspacePanel);
        WorkspacePanel.Visibility = Visibility.Visible;
        var settings = new SettingsWindow(_customization, WorkspacePanel) { Owner = this };
        _workspaceSettings = settings;
        settings.PreviewChanged += (_, _) => ApplyConfiguration();
        settings.Preparing += () =>
        {
            var monitor = _customization.Configuration.Monitor;
            monitor.CodexHome = HomeFor(AgentKind.Codex); monitor.ClaudeHome = HomeFor(AgentKind.Claude);
            monitor.AutoStart = !_monitorPaused;
        };
        settings.Saved += (_, _) => ApplyConfiguration();
        _workspaceSettings.Closed += (_, _) =>
        {
            settings.DetachConnection(); _workspaceSettings = null;
            if (!settings.Accepted)
            {
                _monitorPaused = paused;
                CodexHomeTextBox.Text = codex; ClaudeHomeTextBox.Text = claude;
                _monitorEditTimer.Stop();
            }
            else if (_monitorRefreshRequested || paused != _monitorPaused || codex != CodexHomeTextBox.Text || claude != ClaudeHomeTextBox.Text)
                ScheduleMonitorUpdate();
            _monitorRefreshRequested = false;
            WorkspacePanel.Visibility = Visibility.Collapsed; ShellRoot.Children.Add(WorkspacePanel);
        };
        _workspaceSettings.Show();
    }
    private void OpenSettings()
    {
        OpenWorkspaceSettings();
        if (_workspaceSettings is SettingsWindow settings) settings.Pages.SelectedIndex = 0;
    }
    private void ApplyConfiguration()
    {
        if (_presentations.State == MascotState.Idle && !_customization.Configuration.Global.ShowIdle) _presentations.Clear();
        else if (VisibleState() == MascotState.Idle && _customization.Configuration.Global.ShowIdle && !_presentations.IsPresenting)
            _presentations.Show(_library, _customization, MascotState.Idle, sound: false);
        else _presentations.ApplyPreferences(_library, _customization);
    }
    private void RefreshLanguage(object? sender, EventArgs e)
    {
        if (_closing) return;
        RefreshJobs();
        if (_health.Count > 0) ShowHealth();
        if (_tray.ContextMenuStrip is { } menu)
        {
            menu.Items[0].Text = AppBrand.OpenLabel; menu.Items[1].Text = Loc.T("설정"); menu.Items[2].Text = Loc.T("종료");
        }
    }
    private void Hide_OnClick(object sender, RoutedEventArgs e) => Hide();
    private void HideMascots() { _presentations.Clear(); _popupThreadId = null; }
    internal void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }
    internal void Tray_OnMouseDoubleClick(object? sender, Forms.MouseEventArgs e)
    {
        // Defer activation until the notification-area popup has finished handling the click.
        if (e.Button == Forms.MouseButtons.Left) Dispatcher.BeginInvoke(ShowMain);
    }
    internal void ShowMain()
    {
        if (_closing) return;
        InstanceActivation.Restore(this);
    }
    private void WriteHealth()
    {
        try { File.WriteAllText(Path.Combine(AppPaths.ConfigDirectory, "connection-status.json"), JsonSerializer.Serialize(new
            { time = DateTimeOffset.Now, provider = ProviderSetting(), codexHome = CodexHomeTextBox.Text, claudeHome = ClaudeHomeTextBox.Text,
              projects = _customization.Configuration.Monitor.Projects, health = _health.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
              state = _aggregator.State.ToString(), jobs = _jobs }, new JsonSerializerOptions { WriteIndented = true })); }
        catch (IOException) { }
    }
    private void Log(string message) { if (!_closing) LogTextBlock.Text = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message; }
    private void Dispatch(Action action) { if (!_closing) _ = Dispatcher.InvokeAsync(() => { if (!_closing) action(); }); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        if (!_closing)
        {
            _closing = true;
            Loc.Changed -= RefreshLanguage;
            Dashboard.Shutdown();
            _workspaceSettings?.Close();
            _projectSelection?.Close();
            _foregroundTimer.Stop();
            _monitorEditTimer.Stop();
            _monitorCancel?.Cancel();
            _presentations.Dispose();
            _trayTheme.Dispose();
            _tray.Visible = false; var trayIcon = _tray.Icon; _tray.Dispose(); trayIcon?.Dispose();
        }
        base.OnClosing(e);
    }
    private sealed record JobRow(string Id, string State, string Project, string Updated, string Source, string Message, string? FullPath);
}
