using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CodexMascot.Core;

namespace CodexMascot.App;

internal sealed class ProjectSelectionWindow : Window
{
    internal bool Accepted { get; private set; }
    internal CheckBox AutoInclude { get; }
    internal NumericDragInput RecentLimit { get; }
    internal Button Confirm { get; }
    internal Button Cancel { get; }
    internal Button MonitorAll { get; }
    internal Button ExcludeAll { get; }
    internal Dictionary<string, CheckBox> ProjectChoices { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, CheckBox> ChatChoices { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Expander> ProjectGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChatWatchEntry> _chats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SizeChangedEventHandler> _projectResizers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SizeChangedEventHandler> _chatResizers = new(StringComparer.Ordinal);
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = PencilPalette.Muted, Margin = new Thickness(0, 8, 0, 8) };
    private readonly CustomizationManager _manager;
    private readonly Func<int, Task<IReadOnlyList<ChatWatchEntry>>>? _discover;
    private readonly DispatcherTimer _discoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private int _discoveryRevision;
    private bool _closed;
    private bool _updatingChecks;
    internal event EventHandler? SelectionChanged;
    internal ProjectSelectionWindow(CustomizationManager manager, Func<int, Task<IReadOnlyList<ChatWatchEntry>>>? discover = null)
    {
        _manager = manager;
        _discover = discover;
        _manager.BeginEdit();
        Title = Loc.T("감시 프로젝트 선택"); Width = 760; Height = 660; MinWidth = 580; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = PencilFonts.Handwriting; FontSize = 20;
        Background = PencilPalette.Paper; Foreground = PencilPalette.Ink;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AgentMascot;component/PencilTheme.xaml", UriKind.Relative) });
        PencilWindow.Apply(this);
        var monitor = manager.Configuration.Monitor;
        var root = new DockPanel { Margin = new Thickness(24) };
        var footer = new DockPanel { LastChildFill = true };
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Confirm = new Button { Content = Loc.T("확인"), IsDefault = true, MinWidth = 100, Margin = new Thickness(4) };
        Confirm.SetBinding(BackgroundProperty, new Binding(nameof(PencilThemeColors.Button)) { Source = PencilPalette.Current });
        Confirm.SetBinding(ForegroundProperty, new Binding(nameof(PencilThemeColors.OnButton)) { Source = PencilPalette.Current });
        Confirm.SetBinding(BorderBrushProperty, new Binding(nameof(PencilThemeColors.Button)) { Source = PencilPalette.Current });
        Cancel = new Button { Content = Loc.T("취소"), IsCancel = true, MinWidth = 100, Margin = new Thickness(4) };
        footerButtons.Children.Add(Confirm); footerButtons.Children.Add(Cancel);
        DockPanel.SetDock(footerButtons, Dock.Right); footer.Children.Add(footerButtons);
        AutoInclude = new CheckBox { Content = Loc.T("새 채팅 자동 감시"), IsChecked = monitor.AutoIncludeNewChats,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(AutoInclude);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var options = new StackPanel(); DockPanel.SetDock(options, Dock.Top); root.Children.Add(options);
        options.Children.Add(new TextBlock { Text = Loc.T("폴더를 펼쳐 채팅별 감시를 선택하세요. 새 채팅 자동 감시를 끄면 직접 체크한 채팅만 감시합니다."), TextWrapping = TextWrapping.Wrap, Foreground = PencilPalette.Muted, Margin = new Thickness(0, 0, 0, 12) });
        RecentLimit = new NumericDragInput { Label = Loc.T("최근 기록 수 (에이전트별)"), Icon = PencilIconKind.Duration, ShowValueFill = true,
            Minimum = 1, Maximum = 1000, DecimalPlaces = 0, Value = monitor.RecentSessionLimit, UnitsPerPixel = 1 };
        options.Children.Add(RecentLimit);
        AutoInclude.Checked += (_, _) => PreviewSelection();
        AutoInclude.Unchecked += (_, _) => PreviewSelection();
        RecentLimit.ValuePreviewed += (_, _) => { PreviewSelection(); ScheduleDiscovery(); };
        _discoveryTimer.Tick += async (_, _) =>
        {
            _discoveryTimer.Stop();
            await RefreshDiscoveredAsync(_discoveryRevision);
        };
        var summary = new Grid { Margin = new Thickness(0, 12, 0, 4) };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.Margin = new Thickness(0); _status.VerticalAlignment = VerticalAlignment.Center;
        summary.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button ActionButton(string label, string explanation, PencilIconKind icon, Action action)
        {
            var tip = new ToolTip { Content = Loc.T(label) + " · " + Loc.T(explanation),
                Style = (Style)FindResource("CompactPencilToolTip") };
            var button = new Button { Content = "", Width = 40, Height = 40, Padding = new Thickness(8),
                Margin = new Thickness(4, 0, 0, 0), ToolTip = tip };
            Pencil.SetIcon(button, icon);
            System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(label));
            System.Windows.Automation.AutomationProperties.SetHelpText(button, Loc.T(explanation));
            ToolTipService.SetInitialShowDelay(button, 200);
            ToolTipService.SetShowDuration(button, 10000);
            button.Click += (_, _) => action(); actions.Children.Add(button); return button;
        }
        MonitorAll = ActionButton("모두 감시", "표시된 항목 전부 켜기", PencilIconKind.MonitorAll,
            () => { foreach (var c in ProjectChoices.Values.Concat(ChatChoices.Values)) c.IsChecked = true; });
        ExcludeAll = ActionButton("모두 제외", "표시된 항목 전부 끄기", PencilIconKind.ExcludeAll,
            () => { foreach (var c in ProjectChoices.Values.Concat(ChatChoices.Values)) c.IsChecked = false; });
        Grid.SetColumn(actions, 1); summary.Children.Add(actions); options.Children.Add(summary);
        root.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        UpdateCount();
        Confirm.Click += (_, _) => Accept(); Cancel.Click += (_, _) => Close();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) =>
        {
            _closed = true;
            _discoveryTimer.Stop();
            _discoveryRevision++;
            if (!Accepted) { _manager.CancelEdit(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
        };
        if (_discover is not null) Loaded += async (_, _) => await RefreshDiscoveredAsync(++_discoveryRevision);
        Content = root;
    }
    private void ScheduleDiscovery()
    {
        if (_discover is null || _closed) return;
        _discoveryRevision++;
        _discoveryTimer.Stop();
        _discoveryTimer.Start();
    }
    private async Task RefreshDiscoveredAsync(int revision)
    {
        if (_discover is null || _closed) return;
        var limit = (int)Math.Clamp(RecentLimit.Value, 1, 1000);
        try
        {
            var found = await _discover(limit);
            if (_closed || revision != _discoveryRevision) return;
            ReconcileChoices(found);
        }
        catch (Exception ex)
        {
            if (!_closed && revision == _discoveryRevision) _status.Text = Loc.T("프로젝트 목록을 읽지 못했습니다: ") + ex.Message;
        }
    }
    private void ReconcileChoices(IReadOnlyList<ChatWatchEntry> found)
    {
        // Commit visible check states into the draft before hiding entries. They
        // remain available if the user increases the limit again or presses OK.
        PreviewSelection();
        var desired = found.Where(c => !string.IsNullOrWhiteSpace(c.Id) && ProjectWatchPolicy.Normalize(c.ProjectPath) is not null)
            .DistinctBy(c => ChatWatchPolicy.Key(c.Agent, c.Id)).ToArray();
        var paths = desired.ToDictionary(c => ChatWatchPolicy.Key(c.Agent, c.Id), c => ProjectWatchPolicy.Normalize(c.ProjectPath)!, StringComparer.Ordinal);
        foreach (var key in ChatChoices.Keys.Where(key => !paths.TryGetValue(key, out var path) ||
                     !string.Equals(_chats[key].ProjectPath, path, StringComparison.OrdinalIgnoreCase)).ToArray())
            RemoveChat(key);
        foreach (var chat in desired) AddChat(chat);
        PreviewSelection();
        UpdateCount();
    }
    private void RemoveChat(string key)
    {
        var path = _chats[key].ProjectPath;
        ((StackPanel)ProjectGroups[path].Content).Children.Remove(ChatChoices[key]);
        if (_chatResizers.Remove(key, out var resize)) SizeChanged -= resize;
        ChatChoices.Remove(key); _chats.Remove(key);
        if (_chats.Values.Any(c => string.Equals(c.ProjectPath, path, StringComparison.OrdinalIgnoreCase))) return;
        _rows.Children.Remove(ProjectGroups[path]);
        if (_projectResizers.Remove(path, out resize)) SizeChanged -= resize;
        ProjectGroups.Remove(path); ProjectChoices.Remove(path);
    }
    internal void AddProject(string path, bool enabled)
    {
        var normalized = ProjectWatchPolicy.Normalize(path);
        if (normalized is null || ProjectChoices.ContainsKey(normalized)) return;
        var label = new StackPanel { MaxWidth = Math.Max(200, (ActualWidth > 0 ? ActualWidth : Width) - 110) };
        SizeChangedEventHandler projectResize = (_, _) => label.MaxWidth = Math.Max(200, ActualWidth - 110);
        SizeChanged += projectResize; _projectResizers[normalized] = projectResize;
        label.Children.Add(new TextBlock { Text = Path.GetFileName(normalized) is { Length: > 0 } name ? name : normalized });
        label.Children.Add(new TextBlock { Text = normalized, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = PencilPalette.Muted });
        // The shared checkbox style uses a text-only content template. This row
        // supplies real controls for a wrapped two-line project label instead.
        var check = new CheckBox { Content = label, ContentTemplate = null, ToolTip = normalized, IsChecked = enabled, Margin = new Thickness(0, 8, 10, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(check, normalized);
        ProjectChoices.Add(normalized, check);
        var children = new StackPanel { Margin = new Thickness(24, 0, 0, 8) };
        check.Content = Loc.T("폴더 전체 감시");
        children.Children.Add(check);
        var group = new Expander { Header = label, Content = children, IsExpanded = false, Foreground = PencilPalette.Ink };
        ProjectGroups.Add(normalized, group); _rows.Children.Add(group);
        void SetChildren(bool value)
        {
            _updatingChecks = true;
            try { foreach (var c in _chats.Where(c => string.Equals(c.Value.ProjectPath, normalized, StringComparison.OrdinalIgnoreCase))) ChatChoices[c.Key].IsChecked = value; }
            finally { _updatingChecks = false; }
            PreviewSelection();
        }
        check.Checked += (_, _) => SetChildren(true);
        check.Unchecked += (_, _) => SetChildren(false);
        UpdateCount();
    }
    internal void AddChat(ChatWatchEntry chat)
    {
        var path = ProjectWatchPolicy.Normalize(chat.ProjectPath);
        if (path is null || string.IsNullOrWhiteSpace(chat.Id)) return;
        var key = ChatWatchPolicy.Key(chat.Agent, chat.Id);
        if (ChatChoices.TryGetValue(key, out var existing))
        {
            if (!string.IsNullOrWhiteSpace(chat.Title))
            {
                _chats[key].Title = chat.Title;
                ((TextBlock)((StackPanel)existing.Content).Children[0]).Text = chat.Title;
            }
            return;
        }
        var monitor = _manager.Configuration.Monitor;
        var saved = monitor.Chats.FirstOrDefault(c => c.Agent == chat.Agent && c.Id == chat.Id);
        var project = monitor.Projects.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        AddProject(path, project?.Enabled ?? monitor.AutoIncludeNewProjects);
        var enabled = (saved?.Enabled ?? AutoInclude.IsChecked == true) && ProjectChoices[path].IsChecked == true;
        var title = !string.IsNullOrWhiteSpace(chat.Title) ? chat.Title : saved?.Title;
        var text = string.IsNullOrWhiteSpace(title) ? Loc.T("제목 없는 채팅") : title;
        var label = new StackPanel { MaxWidth = Math.Max(200, Width - 155) };
        SizeChangedEventHandler chatResize = (_, _) => label.MaxWidth = Math.Max(200, ActualWidth - 155);
        SizeChanged += chatResize; _chatResizers[key] = chatResize;
        label.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        label.Children.Add(new TextBlock { Text = chat.Agent + " · " + chat.Id, FontSize = 14, Foreground = PencilPalette.Muted, TextWrapping = TextWrapping.Wrap });
        var check = new CheckBox { Content = label, ContentTemplate = null, IsChecked = enabled, Margin = new Thickness(0, 6, 0, 6), ToolTip = chat.Id };
        System.Windows.Automation.AutomationProperties.SetName(check, text + " · " + chat.Agent + " · " + chat.Id);
        _chats[key] = new() { Agent = chat.Agent, Id = chat.Id, ProjectPath = path, Title = title ?? "", Enabled = enabled };
        ChatChoices[key] = check;
        ((StackPanel)ProjectGroups[path].Content).Children.Add(check);
        check.Checked += (_, _) => {
            if (_updatingChecks) return;
            // Enabling one child must not enable its excluded siblings.
            if (ProjectChoices[path].IsChecked != true)
            {
                var states = _chats.Where(c => string.Equals(c.Value.ProjectPath, path, StringComparison.OrdinalIgnoreCase)).ToDictionary(c => c.Key, c => ChatChoices[c.Key].IsChecked);
                _updatingChecks = true;
                try
                {
                    ProjectChoices[path].IsChecked = true;
                    foreach (var state in states) ChatChoices[state.Key].IsChecked = state.Value;
                }
                finally { _updatingChecks = false; }
            }
            PreviewSelection();
        };
        check.Unchecked += (_, _) => PreviewSelection();
        UpdateCount();
    }
    private void UpdateCount() => _status.Text = ProjectChoices.Count == 0 ? Loc.T("현재 감시 범위에 채팅 기록이 없습니다.") : Loc.F("프로젝트 {0}개 · 채팅 {1}개", ProjectChoices.Count, ChatChoices.Count);
    private void PreviewSelection()
    {
        if (_updatingChecks || _closed) return;
        var monitor = _manager.Configuration.Monitor;
        // Keep choices outside this recent scan, including observations made while
        // the picker is open. Only the visible subset is edited here.
        monitor.Projects = ProjectChoices.Select(p => new ProjectWatchEntry { Path = p.Key, Enabled = p.Value.IsChecked == true })
            .Concat(monitor.Projects.Where(p => !ProjectChoices.ContainsKey(p.Path))).ToList();
        monitor.Chats = _chats.Select(p => new ChatWatchEntry { Agent = p.Value.Agent, Id = p.Value.Id, ProjectPath = p.Value.ProjectPath,
            Title = p.Value.Title, Enabled = ChatChoices[p.Key].IsChecked == true })
            .Concat(monitor.Chats.Where(c => !ChatChoices.ContainsKey(ChatWatchPolicy.Key(c.Agent, c.Id)))).ToList();
        monitor.AutoIncludeNewProjects = monitor.AutoIncludeNewChats = AutoInclude.IsChecked == true;
        monitor.RecentSessionLimit = (int)Math.Clamp(RecentLimit.Value, 1, 1000);
        monitor.ProjectFilter = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Accept()
    {
        if (!RecentLimit.TryCommitText()) return;
        try { PreviewSelection(); _manager.CommitEdit(() => { }); Accepted = true; Close(); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
}
