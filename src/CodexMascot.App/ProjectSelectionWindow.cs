using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CodexMascot.Core;

namespace CodexMascot.App;

internal sealed class ProjectSelectionWindow : Window
{
    internal bool Accepted { get; private set; }
    internal CheckBox AutoInclude { get; }
    internal NumericDragInput RecentLimit { get; }
    internal Button Confirm { get; }
    internal Button Cancel { get; }
    internal Dictionary<string, CheckBox> ProjectChoices { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, CheckBox> ChatChoices { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Expander> ProjectGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChatWatchEntry> _chats = new(StringComparer.Ordinal);
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = PencilPalette.Muted, Margin = new Thickness(0, 8, 0, 8) };
    private readonly CustomizationManager _manager;
    private bool _closed;
    internal ProjectSelectionWindow(CustomizationManager manager, Func<Task<IReadOnlyList<ChatWatchEntry>>>? discover = null)
    {
        _manager = manager;
        Title = Loc.T("프로젝트 선택"); Width = 760; Height = 660; MinWidth = 580; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = PencilFonts.Handwriting; FontSize = 20;
        Background = PencilPalette.Paper; Foreground = PencilPalette.Ink;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AgentMascot;component/PencilTheme.xaml", UriKind.Relative) });
        PencilWindow.Apply(this);
        var monitor = manager.Configuration.Monitor;
        var root = new DockPanel { Margin = new Thickness(24) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Confirm = new Button { Content = Loc.T("확인"), IsDefault = true, MinWidth = 100, Margin = new Thickness(4) };
        Confirm.SetBinding(BackgroundProperty, new Binding(nameof(PencilThemeColors.Button)) { Source = PencilPalette.Current });
        Confirm.SetBinding(ForegroundProperty, new Binding(nameof(PencilThemeColors.OnButton)) { Source = PencilPalette.Current });
        Confirm.SetBinding(BorderBrushProperty, new Binding(nameof(PencilThemeColors.Button)) { Source = PencilPalette.Current });
        Cancel = new Button { Content = Loc.T("취소"), IsCancel = true, MinWidth = 100, Margin = new Thickness(4) };
        footer.Children.Add(Confirm); footer.Children.Add(Cancel); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var options = new StackPanel(); DockPanel.SetDock(options, Dock.Top); root.Children.Add(options);
        AutoInclude = new CheckBox { Content = Loc.T("새 채팅 자동 감시"), IsChecked = monitor.AutoIncludeNewChats, Margin = new Thickness(0, 0, 0, 12) };
        options.Children.Add(AutoInclude);
        options.Children.Add(new TextBlock { Text = Loc.T("폴더를 펼쳐 채팅별 감시를 선택하세요. 새 채팅 자동 감시를 끄면 직접 체크한 채팅만 감시합니다."), TextWrapping = TextWrapping.Wrap, Foreground = PencilPalette.Muted, Margin = new Thickness(0, 0, 0, 12) });
        RecentLimit = new NumericDragInput { Label = Loc.T("최근 기록 수 (에이전트별)"), Minimum = 1, Maximum = 1000, DecimalPlaces = 0, Value = monitor.RecentSessionLimit, UnitsPerPixel = 1 };
        options.Children.Add(RecentLimit);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        Button ActionButton(string label, Action action)
        { var b = new Button { Content = Loc.T(label), Margin = new Thickness(0, 0, 8, 0) }; b.Click += (_, _) => action(); actions.Children.Add(b); return b; }
        ActionButton("프로젝트 폴더 추가", () =>
        {
            using var browser = new System.Windows.Forms.FolderBrowserDialog { Description = Loc.T("감시할 프로젝트 폴더를 선택하세요."), UseDescriptionForTitle = true };
            if (browser.ShowDialog() == System.Windows.Forms.DialogResult.OK) AddProject(browser.SelectedPath, true);
        });
        ActionButton("모두 감시", () => { foreach (var c in ProjectChoices.Values.Concat(ChatChoices.Values)) c.IsChecked = true; });
        ActionButton("모두 제외", () => { foreach (var c in ProjectChoices.Values.Concat(ChatChoices.Values)) c.IsChecked = false; });
        options.Children.Add(actions); options.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        foreach (var project in monitor.Projects.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)) AddProject(project.Path, project.Enabled);
        foreach (var chat in monitor.Chats) AddChat(chat);
        UpdateCount();
        Confirm.Click += (_, _) => Accept(); Cancel.Click += (_, _) => Close();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) => _closed = true;
        if (discover is not null) Loaded += async (_, _) =>
        {
            try
            {
                var found = await discover();
                if (_closed) return;
                foreach (var chat in found) AddChat(chat);
                UpdateCount();
            }
            catch (Exception ex) { if (!_closed) _status.Text = Loc.T("프로젝트 목록을 읽지 못했습니다: ") + ex.Message; }
        };
        Content = root;
    }
    internal void AddProject(string path, bool enabled)
    {
        var normalized = ProjectWatchPolicy.Normalize(path);
        if (normalized is null || ProjectChoices.ContainsKey(normalized)) return;
        var label = new StackPanel { MaxWidth = Math.Max(200, (ActualWidth > 0 ? ActualWidth : Width) - 110) };
        SizeChanged += (_, _) => label.MaxWidth = Math.Max(200, ActualWidth - 110);
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
        check.Checked += (_, _) => { foreach (var c in _chats.Where(c => string.Equals(c.Value.ProjectPath, normalized, StringComparison.OrdinalIgnoreCase))) ChatChoices[c.Key].IsChecked = true; };
        check.Unchecked += (_, _) => { foreach (var c in _chats.Where(c => string.Equals(c.Value.ProjectPath, normalized, StringComparison.OrdinalIgnoreCase))) ChatChoices[c.Key].IsChecked = false; };
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
        SizeChanged += (_, _) => label.MaxWidth = Math.Max(200, ActualWidth - 155);
        label.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        label.Children.Add(new TextBlock { Text = chat.Agent + " · " + chat.Id, FontSize = 14, Foreground = PencilPalette.Muted, TextWrapping = TextWrapping.Wrap });
        var check = new CheckBox { Content = label, ContentTemplate = null, IsChecked = enabled, Margin = new Thickness(0, 6, 0, 6), ToolTip = chat.Id };
        System.Windows.Automation.AutomationProperties.SetName(check, text + " · " + chat.Agent + " · " + chat.Id);
        _chats[key] = new() { Agent = chat.Agent, Id = chat.Id, ProjectPath = path, Title = title ?? "", Enabled = enabled };
        ChatChoices[key] = check;
        ((StackPanel)ProjectGroups[path].Content).Children.Add(check);
        check.Checked += (_, _) => {
            // Enabling one child must not enable its excluded siblings.
            if (ProjectChoices[path].IsChecked != true)
            {
                var states = _chats.Where(c => string.Equals(c.Value.ProjectPath, path, StringComparison.OrdinalIgnoreCase)).ToDictionary(c => c.Key, c => ChatChoices[c.Key].IsChecked);
                ProjectChoices[path].IsChecked = true;
                foreach (var state in states) ChatChoices[state.Key].IsChecked = state.Value;
            }
        };
        UpdateCount();
    }
    private void UpdateCount() => _status.Text = ProjectChoices.Count == 0 ? Loc.T("발견된 프로젝트가 없습니다. 폴더를 직접 추가할 수 있습니다.") : Loc.F("프로젝트 {0}개 · 채팅 {1}개", ProjectChoices.Count, ChatChoices.Count);
    private void Accept()
    {
        if (!RecentLimit.TryCommitText()) return;
        var monitor = _manager.Configuration.Monitor;
        var originalChats = monitor.Chats; var originalChatAuto = monitor.AutoIncludeNewChats;
        var original = monitor.Projects; var originalAuto = monitor.AutoIncludeNewProjects; var originalLimit = monitor.RecentSessionLimit; var originalFilter = monitor.ProjectFilter;
        try
        {
            // Preserve discoveries made by the monitor while this draft was open.
            var selected = ProjectChoices.Select(p => new ProjectWatchEntry { Path = p.Key, Enabled = p.Value.IsChecked == true }).ToList();
            selected.AddRange(monitor.Projects.Where(p => !ProjectChoices.ContainsKey(p.Path)).Select(p => new ProjectWatchEntry { Path = p.Path, Enabled = p.Enabled && AutoInclude.IsChecked == true }));
            monitor.Projects = selected; monitor.AutoIncludeNewProjects = AutoInclude.IsChecked == true;
            monitor.AutoIncludeNewChats = AutoInclude.IsChecked == true;
            monitor.Chats = _chats.Select(p => new ChatWatchEntry { Agent = p.Value.Agent, Id = p.Value.Id, ProjectPath = p.Value.ProjectPath, Title = p.Value.Title, Enabled = ChatChoices[p.Key].IsChecked == true }).ToList();
            monitor.Chats.AddRange(originalChats.Where(c => !ChatChoices.ContainsKey(ChatWatchPolicy.Key(c.Agent, c.Id)))
                .Select(c => new ChatWatchEntry { Agent = c.Agent, Id = c.Id, ProjectPath = c.ProjectPath, Title = c.Title, Enabled = c.Enabled && monitor.AutoIncludeNewChats }));
            monitor.RecentSessionLimit = (int)Math.Clamp(RecentLimit.Value, 1, 1000); monitor.ProjectFilter = null;
            _manager.Save(); Accepted = true; Close();
        }
        catch (Exception ex) { monitor.Chats = originalChats; monitor.AutoIncludeNewChats = originalChatAuto; monitor.Projects = original; monitor.AutoIncludeNewProjects = originalAuto; monitor.RecentSessionLimit = originalLimit; monitor.ProjectFilter = originalFilter; _status.Text = ex.Message; }
    }
}
