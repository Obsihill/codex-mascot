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
    private readonly CompletionPopupPolicy _completionPopup = new();
    private readonly CustomizationManager _customization = new();
    private readonly OverlayWindow _overlay = new();
    private readonly SoundPlayerService _sound = new();
    private readonly CodexAppServerClient _server = new();
    private readonly CodexCliClient _cli = new();
    private readonly ObservableCollection<JobRow> _jobs = new();
    private readonly ObservableCollection<ApprovalRow> _approvals = new();
    private readonly Forms.NotifyIcon _tray;
    private CancellationTokenSource? _monitorCancel;
    private Task? _monitorTask;
    private ICodexClient? _launcherClient;
    private string? _ownedThread, _ownedTurn;
    private bool _closing, _launcherRunning;
    private SettingsWindow? _settings;
    private MonitorHealth? _health;
    private string _filter = "";
    private readonly string _journal = Path.Combine(AppPaths.ConfigDirectory, "events.jsonl");

    public MainWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        JobsListView.ItemsSource = _jobs;
        ApprovalsListBox.ItemsSource = _approvals;
        var config = _customization.Configuration;
        CodexHomeTextBox.Text = config.Monitor.CodexHome ?? HookIntegration.DefaultCodexHome;
        FilterTextBox.Text = config.Monitor.ProjectFilter ?? "";
        CodexExeTextBox.Text = config.Monitor.CodexExecutable ?? "";
        ProjectPathComboBox.ItemsSource = config.Monitor.RecentProjects;
        ProjectPathComboBox.Text = config.Monitor.RecentProjects.FirstOrDefault() ?? Environment.CurrentDirectory;
        _server.EventReceived += OwnEvent;
        _cli.EventReceived += OwnEvent;
        _overlay.Clicked += Overlay_OnClicked;
        _overlay.Dismissed += (_, _) =>
        {
            if (_settings is not null) { _settings.StopPreview(); return; }
            _aggregator.Acknowledge(); _completionPopup.Acknowledge(); RefreshJobs(); _sound.Stop();
        };
        _server.GetUserInput = async parameters => await Dispatcher.InvokeAsync(() => InputRequestWindow.Ask(this, parameters));
        _overlay.PositionSaved += (_, _) => { _customization.Save(); UpdatePositionText(); };
        _sound.Feedback += (_, text) => Log(text);
        _tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "Codex Mascot", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("작업 창 표시", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add("캐릭터 숨기기", null, (_, _) => Dispatcher.Invoke(_overlay.HideMascot));
        menu.Items.Add("설정", null, (_, _) => Dispatcher.Invoke(() => OpenSettings(false)));
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
        ApplyConfiguration();
        Loaded += async (_, _) =>
        {
            if (Environment.GetCommandLineArgs().Contains("--tray")) Hide();
            if (config.Monitor.AutoStart) await StartMonitoring();
            if (_customization.LoadWarning is not null) Log(_customization.LoadWarning);
        };
    }

    private async Task StartMonitoring()
    {
        MonitorButton.IsEnabled = false;
        try
        {
            await StopMonitoring();
            var home = Path.GetFullPath(CodexHomeTextBox.Text.Trim());
            _filter = FilterTextBox.Text.Trim();
            _customization.Configuration.Monitor.CodexHome = home;
            _customization.Configuration.Monitor.ProjectFilter = _filter;
            _customization.Save();
            _aggregator.Clear();
            _completionPopup.Acknowledge();
            _overlay.HideMascot();
            RefreshJobs();
            var monitor = new DesktopSessionMonitor(home, HookIntegration.EventDirectory);
            monitor.EventReceived += (_, e) => Dispatch(() =>
            {
                if (MatchesProject(e.ProjectPath)) HandleEvent(e);
            });
            monitor.HealthChanged += (_, health) => Dispatch(() =>
            {
                _health = health;
                ConnectionTextBlock.Text = health.Message + " · " + health.Sessions + "개 작업 · 최근 수신 " +
                    (health.LastEvent?.ToLocalTime().ToString("HH:mm:ss") ?? "새 이벤트 대기");
                HookTextBlock.Text = health.HookEvents > 0 ? "Hook 수신 확인 · " + health.HookEvents + "개 이벤트. 승인/답변은 원래 Codex에서 진행하세요."
                    : "Hook 수신 전 · 시작/종료는 기록 감시로 표시합니다. 승인·질문 알림은 Hook 신뢰 검토 후 사용할 수 있습니다.";
                WriteHealth();
            });
            _monitorCancel = new CancellationTokenSource();
            var ct = _monitorCancel.Token;
            _monitorTask = Task.Run(() => monitor.RunAsync(ct));
            Log("감시 시작: " + home);
        }
        catch (Exception ex) { Log("감시 시작 실패: " + ex.Message); ConnectionTextBlock.Text = "연결 확인 필요"; }
        finally { MonitorButton.IsEnabled = true; }
    }
    private async Task StopMonitoring()
    {
        _monitorCancel?.Cancel();
        if (_monitorTask is not null) await _monitorTask;
        _monitorCancel?.Dispose(); _monitorCancel = null; _monitorTask = null;
    }
    private bool MatchesProject(string? project)
    {
        if (string.IsNullOrWhiteSpace(_filter)) return true;
        if (project is null) return false;
        var prefix = _filter.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return project.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            project.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    private async void Monitor_OnClick(object sender, RoutedEventArgs e) => await StartMonitoring();
    private async void PauseMonitor_OnClick(object sender, RoutedEventArgs e)
        { await StopMonitoring(); _aggregator.Clear(); _completionPopup.Acknowledge(); RefreshJobs(); _overlay.HideMascot(); ConnectionTextBlock.Text = "감시 정지됨 · Codex 작업은 계속됩니다."; }
    private void BrowseHome_OnClick(object sender, RoutedEventArgs e) => BrowseInto(CodexHomeTextBox, "Codex 데이터 폴더 (.codex)를 선택하세요.");
    private void BrowseFilter_OnClick(object sender, RoutedEventArgs e) => BrowseInto(FilterTextBox, "감시할 프로젝트 폴더를 선택하세요.");
    private void ClearFilter_OnClick(object sender, RoutedEventArgs e) { FilterTextBox.Clear(); _ = StartMonitoring(); }
    private static void BrowseInto(System.Windows.Controls.TextBox box, string title)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = title, UseDescriptionForTitle = true, SelectedPath = box.Text };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) box.Text = dialog.SelectedPath;
    }
    private void InstallHooks_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = HookIntegration.Install(Path.GetFullPath(CodexHomeTextBox.Text.Trim()));
            HookTextBlock.Text = "설정 저장됨. Codex CLI의 /hooks에서 Mascot 항목을 검토·신뢰한 뒤 새 메시지를 보내세요. 기존 작업은 다시 열어야 적용될 수 있습니다.";
            Log("Hook 설정: " + path + " · 기존 설정은 백업했습니다. '수신 확인'은 실제 이벤트가 도착한 뒤 표시됩니다.");
        }
        catch (Exception ex) { Log("Hook 설정 실패: " + ex.Message); }
    }
    private void RemoveHooks_OnClick(object sender, RoutedEventArgs e)
    {
        try { HookIntegration.Uninstall(Path.GetFullPath(CodexHomeTextBox.Text.Trim())); Log("Mascot Hook만 해제했습니다. 원본은 .mascot-backup 파일로 복구할 수 있습니다."); }
        catch (Exception ex) { Log(ex.Message); }
    }
    private void OwnEvent(object? sender, CodexEvent e) => Dispatch(() =>
    {
        if (e.Kind == CodexEventKind.ThreadStarted) _ownedThread = e.ThreadId;
        if (e.Kind == CodexEventKind.TurnStarted) { _ownedThread = e.ThreadId; _ownedTurn = e.TurnId; }
        if (e.Kind is CodexEventKind.ApprovalRequired or CodexEventKind.UserInputRequired && e.RequestId is not null)
            if (!_approvals.Any(a => a.Id == e.RequestId)) _approvals.Add(new(e.RequestId, e.Message ?? e.ItemType ?? "Codex 요청"));
        if (e.Kind == CodexEventKind.ServerRequestResolved)
            foreach (var a in _approvals.Where(a => a.Id == e.RequestId).ToArray()) _approvals.Remove(a);
        if (e.Kind == CodexEventKind.TurnCompleted)
        { _ownedTurn = null; _approvals.Clear(); SetLauncherBusy(false); }
        if (e.Kind == CodexEventKind.ConnectionChanged)
        { LauncherTextBlock.Text = e.Message ?? e.Status; if (e.Status == "disconnected") SetLauncherBusy(false); return; }
        HandleEvent(e with { ProjectPath = e.ProjectPath ?? ProjectPathComboBox.Text });
    });
    private void HandleEvent(CodexEvent e)
    {
        var result = _aggregator.Apply(e);
        _completionPopup.Observe(result.Jobs);
        RefreshJobs();
        if (!e.IsReplay && e.Kind != CodexEventKind.ItemCompleted)
        {
            try { File.AppendAllText(_journal, JsonSerializer.Serialize(new { time = e.Time, e.SourceId, e.ThreadId, kind = e.Kind.ToString(), state = result.State.ToString() }) + Environment.NewLine); }
            catch (IOException) { }
        }
        if (result.StateChanged && _settings is null) Present(result.State, result.ShouldNotify);
    }
    private void Present(MascotState state, bool sound)
    {
        var resolved = _completionPopup.Resolve(state, _customization.Configuration.Global.KeepCompletedVisibleUntilClick);
        if (resolved == MascotState.Completed && _customization.Configuration.Global.KeepCompletedVisibleUntilClick &&
            _overlay.IsPresenting && _overlay.DisplayedState == MascotState.Completed) return;
        sound &= resolved == state;
        state = resolved;
        if (state is MascotState.Disconnected or MascotState.Connecting ||
            state == MascotState.Idle && !_customization.Configuration.Global.ShowIdle)
        { _overlay.HideMascot(); return; }
        var cfg = _customization.Configuration.For(state);
        _overlay.ShowState(state, cfg, _customization.ResolveImage(state));
        if (sound && _customization.Configuration.Global.SoundEnabled &&
            state is MascotState.Completed or MascotState.Failed or MascotState.NeedsAttention)
            _sound.Play(_customization.ResolveAsset(cfg.Sound), cfg.Volume * _customization.Configuration.Global.MasterVolume);
    }
    private void RefreshJobs()
    {
        var selected = (JobsListView.SelectedItem as JobRow)?.Id;
        _jobs.Clear();
        foreach (var j in _aggregator.Jobs)
            _jobs.Add(new(j.ThreadId, StateName(j.State), (j.ProjectPath is null ? "(프로젝트 없음)" : Path.GetFileName(j.ProjectPath.TrimEnd('\\','/'))) + " · " + j.ThreadId[..Math.Min(8, j.ThreadId.Length)],
                j.LastUpdated == DateTimeOffset.MinValue ? "—" : j.LastUpdated.ToLocalTime().ToString("HH:mm:ss"),
                j.Source, j.LastMessage ?? "", j.ProjectPath));
        if (selected is not null) JobsListView.SelectedItem = _jobs.FirstOrDefault(j => j.Id == selected);
        StatusTextBlock.Text = StateName(_aggregator.State);
        _tray.Text = "Codex Mascot · " + StateName(_aggregator.State);
    }
    internal static string StateName(MascotState state) => state switch
    {
        MascotState.Running => "작업 중", MascotState.NeedsAttention => "확인/승인 필요", MascotState.Completed => "응답 완료",
        MascotState.Failed => "실패", MascotState.Interrupted => "중단", MascotState.Disconnected => "상태 확인 필요",
        MascotState.Connecting => "연결 중", _ => "대기 중"
    };
    private void Acknowledge(string? id)
    { _aggregator.Acknowledge(id); _completionPopup.Acknowledge(id); RefreshJobs(); Present(_aggregator.State, false); }
    private void Overlay_OnClicked(object? sender, EventArgs e)
    {
        // Preview clicks must not acknowledge real tasks or leave the test loop running.
        if (_settings is not null) { _settings.StopPreview(); return; }
        _aggregator.Acknowledge(); _completionPopup.Acknowledge(); RefreshJobs(); _sound.Stop();
        if (!_customization.Configuration.Global.BringCodexToFrontOnClick) { ShowMain(); return; }
        var result = CodexDesktopActivator.TryActivate();
        if (result == DesktopActivationResult.NotFound)
        {
            ShowMain();
            Log("실행 중인 Codex 데스크톱 창을 찾지 못해 Mascot 작업 창을 열었습니다.");
        }
        else if (result == DesktopActivationResult.AttentionRequested)
            Log("Windows가 창 전환을 허용하지 않아 Codex 작업 표시줄 아이콘으로 알렸습니다.");
    }
    private void Acknowledge_OnClick(object sender, RoutedEventArgs e) { if (JobsListView.SelectedItem is JobRow j) Acknowledge(j.Id); }
    private void AcknowledgeAll_OnClick(object sender, RoutedEventArgs e) => Acknowledge(null);
    private void CopyId_OnClick(object sender, RoutedEventArgs e)
    { if (JobsListView.SelectedItem is JobRow j) { Clipboard.SetText(j.Id); Log("작업 ID를 복사했습니다."); } }
    private void BrowseProject_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "새 작업을 실행할 프로젝트", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) ProjectPathComboBox.Text = dialog.SelectedPath;
    }
    private void BrowseCodex_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Codex 실행 파일|codex.exe|실행 파일|*.exe" };
        if (dialog.ShowDialog(this) == true) { CodexExeTextBox.Text = dialog.FileName; _customization.Configuration.Monitor.CodexExecutable = dialog.FileName; _customization.Save(); }
    }
    private async void Start_OnClick(object sender, RoutedEventArgs e)
    {
        if (_launcherRunning) return;
        try
        {
            var dir = ProjectPathComboBox.Text.Trim();
            if (!Directory.Exists(dir) || string.IsNullOrWhiteSpace(PromptTextBox.Text)) throw new InvalidOperationException("프로젝트 폴더와 프롬프트를 입력하세요.");
            var exe = CodexExecutable.Resolve(CodexExeTextBox.Text.Trim());
            _server.ExecutablePath = exe; _cli.ExecutablePath = exe;
            _server.ReadOnly = ApprovalComboBox.SelectedIndex == 1;
            _cli.ReadOnly = ApprovalComboBox.SelectedIndex == 1;
            _launcherClient = BackendComboBox.SelectedIndex == 0 ? _server : _cli;
            SetLauncherBusy(true);
            _ownedThread = await _launcherClient.StartTaskAsync(dir, PromptTextBox.Text.Trim(), ModelTextBox.Text.Trim());
            var recent = _customization.Configuration.Monitor.RecentProjects;
            recent.Remove(dir); recent.Insert(0, dir);
            if (recent.Count > 10) recent.RemoveRange(10, recent.Count - 10);
            _customization.Save();
            LauncherTextBlock.Text = "새 작업을 시작했습니다. 상태는 작업 감시 탭에서 볼 수 있습니다.";
        }
        catch (Exception ex) { SetLauncherBusy(false); LauncherTextBlock.Text = "실행 실패: " + ex.Message + " (자동 재실행하지 않습니다.)"; }
    }
    private async void Stop_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_launcherClient is not null && _ownedThread is not null && _ownedTurn is not null)
                await _launcherClient.InterruptAsync(_ownedThread, _ownedTurn);
            else if (_launcherClient is not null) await _launcherClient.StopAsync();
        }
        catch (Exception ex) { Log(ex.Message); }
    }
    private async void Approve_OnClick(object sender, RoutedEventArgs e) => await Reply(true);
    private async void Decline_OnClick(object sender, RoutedEventArgs e) => await Reply(false);
    private async Task Reply(bool accept)
    {
        if (ApprovalsListBox.SelectedItem is not ApprovalRow request || _launcherClient is null) return;
        try { await _launcherClient.ApproveAsync(request.Id, accept); _approvals.Remove(request); }
        catch (Exception ex) { Log("요청 처리 실패: " + ex.Message); }
    }
    private void SetLauncherBusy(bool busy)
    { _launcherRunning = busy; StartButton.IsEnabled = !busy; StopButton.IsEnabled = busy; }
    private void Test_OnClick(object sender, RoutedEventArgs e) => OpenSettings(true);
    private void Settings_OnClick(object sender, RoutedEventArgs e) => OpenSettings(false);
    private void OpenSettings(bool test)
    {
        if (_settings is not null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(_customization, _overlay, _sound) { Owner = this };
        _settings.Closed += (_, _) => { _settings = null; ApplyConfiguration(); Present(_aggregator.State, false); };
        _settings.Show();
        if (test) _settings.RunAllTests();
    }
    private void ApplyConfiguration()
    { _overlay.ApplyGlobal(_customization.Configuration.Global); UpdatePositionText(); }
    private void UpdatePositionText() => PositionTextBlock.Text = "캐릭터 위치: " + _overlay.DescribePosition() + " · 설정에서 위치 미리보기/드래그 가능";
    private void Hide_OnClick(object sender, RoutedEventArgs e) => Hide();
    private void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Diagnostics_OnClick(object sender, RoutedEventArgs e)
    { WriteHealth(); Process.Start(new ProcessStartInfo(AppPaths.ConfigDirectory) { UseShellExecute = true }); }
    private void WriteHealth()
    {
        try { File.WriteAllText(Path.Combine(AppPaths.ConfigDirectory, "connection-status.json"), JsonSerializer.Serialize(new
            { time = DateTimeOffset.Now, codexHome = CodexHomeTextBox.Text, filter = _filter, health = _health, state = _aggregator.State.ToString(), jobs = _jobs }, new JsonSerializerOptions { WriteIndented = true })); }
        catch (IOException) { }
    }
    private void Log(string message) { if (!_closing) LogTextBlock.Text = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message; }
    private void Dispatch(Action action) { if (!_closing) _ = Dispatcher.InvokeAsync(() => { if (!_closing) action(); }); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            _monitorCancel?.Cancel();
            _settings?.Close();
            _overlay.Close();
            _sound.Dispose();
            _tray.Visible = false; _tray.Dispose();
            _ = _server.DisposeAsync(); _ = _cli.DisposeAsync();
        }
        base.OnClosing(e);
    }
    private sealed record JobRow(string Id, string State, string Project, string Updated, string Source, string Message, string? FullPath);
    private sealed record ApprovalRow(string Id, string Label);
}
