using System.Windows;
using System.Windows.Controls;

namespace CodexMascot.App;

public partial class SettingsWindow : Window
{
    private readonly CustomizationManager _manager;
    private readonly TextBlock _feedback = new() { TextWrapping = TextWrapping.Wrap, Foreground = PencilPalette.Accent, Margin = new Thickness(4, 12, 4, 0) };
    private FrameworkElement? _connection;
    private TabItem? _connectionTab;
    private ScrollViewer? _connectionHost;
    internal TabControl Pages { get; private set; } = null!;
    public event EventHandler? Saved;

    public SettingsWindow(CustomizationManager manager, FrameworkElement? connection = null)
    {
        InitializeComponent(); _manager = manager; _connection = connection; PencilWindow.Apply(this);
        BuildUi();
    }
    private void BuildUi()
    {
        var selected = Pages?.SelectedIndex ?? 0;
        if (_connectionHost is not null) _connectionHost.Content = null;
        if (_connectionTab is not null) _connectionTab.Content = null;
        if (_feedback.Parent is Panel previous) previous.Children.Remove(_feedback);
        Root.Children.Clear(); Root.RowDefinitions.Clear();
        Root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Pages = new TabControl { Name = "SettingsPages", Style = (Style)Resources["SettingsTabs"] };
        Root.Children.Add(Pages);
        var global = _manager.Configuration.Global;
        var general = Page("일반");
        general.Children.Add(Section("시작", PencilIconKind.Play, Choice("Windows 로그인 시 시작", global.StartWithWindows, SetStartup)));
        general.Children.Add(Section("마스코트 표시", PencilIconKind.Position,
            Choice("항상 위", global.AlwaysOnTop, v => global.AlwaysOnTop = v),
            Choice("대기 캐릭터 표시", global.ShowIdle, v => global.ShowIdle = v)));
        var notifications = Page("알림");
        notifications.Children.Add(Section("완료 알림", PencilIconKind.Settings,
            Choice("팝업 클릭 시 해당 Codex / Claude 창 열기", global.BringCodexToFrontOnClick, v => global.BringCodexToFrontOnClick = v)));
        if (_connection is not null)
        {
            _connectionHost = new ScrollViewer { Content = _connection, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _connectionTab = new TabItem { Header = "연결", Content = _connectionHost };
            Pages.Items.Add(_connectionTab);
        }
        var info = Page("정보");
        info.Children.Add(Section(AppBrand.Name, PencilIconKind.Settings,
            Row("버전", new TextBlock { Text = typeof(App).Assembly.GetName().Version?.ToString(3), VerticalAlignment = VerticalAlignment.Center }),
            Row("지원 도구", new TextBlock { Text = "Codex · Claude", VerticalAlignment = VerticalAlignment.Center }),
            Row("글꼴", new TextBlock { Text = "나눔손글씨 펜 · SIL OFL 1.1", TextWrapping = TextWrapping.Wrap }),
            Row("설정 저장", new TextBlock { Text = "변경 시 자동 저장", VerticalAlignment = VerticalAlignment.Center })));
        Pages.SelectedIndex = Math.Clamp(selected, 0, Pages.Items.Count - 1);
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = true };
        var close = Button("닫기", Close); close.IsCancel = true; close.MinWidth = 100;
        DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close); footer.Children.Add(_feedback);
        Grid.SetRow(footer, 1); Root.Children.Add(footer);
    }
    internal void DetachConnection()
    {
        if (_connectionHost is not null) _connectionHost.Content = null;
        if (_connectionTab is not null) _connectionTab.Content = null;
        _connection = null;
    }
    private StackPanel Page(string title)
    {
        var content = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        Pages.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        return content;
    }
    private static FrameworkElement Section(string title, PencilIconKind icon, params UIElement[] rows)
    {
        var body = new StackPanel(); foreach (var row in rows) body.Children.Add(row);
        var group = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        group.Children.Add(new PencilBorder { Background = PencilPalette.Surface, BorderBrush = PencilPalette.Line, BorderThickness = new Thickness(1), Padding = new Thickness(14, 20, 14, 10), Margin = new Thickness(0, 12, 0, 0), Child = body });
        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = PencilPalette.Paper, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        header.Children.Add(new PencilIcon { Kind = icon, Width = 18, Height = 18, Margin = new Thickness(4, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 7, 0) }); group.Children.Add(header);
        return group;
    }
    private static Grid Row(string label, FrameworkElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 230 });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 18, 0), Foreground = PencilPalette.Muted };
        row.Children.Add(text); Grid.SetColumn(control, 1); row.Children.Add(control);
        System.Windows.Automation.AutomationProperties.SetLabeledBy(control, text);
        return row;
    }
    private FrameworkElement Choice(string text, bool value, Action<bool> change)
    {
        var box = new ComboBox { Tag = text, ItemsSource = new[] { "켜기", "끄기" }, SelectedIndex = value ? 0 : 1, Margin = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(box, text);
        box.SelectionChanged += (_, _) => Try(() => { change(box.SelectedIndex == 0); Save(); });
        return Row(text, box);
    }
    private void Save() => Try(() => { _manager.Save(); Saved?.Invoke(this, EventArgs.Empty); _feedback.Text = "저장됨"; });
    private void Try(Action action) { try { action(); } catch (Exception e) { _feedback.Text = e.Message; } }
    private void SetStartup(bool enabled)
    {
        StartupRegistration.SetEnabled(enabled);
        _manager.Configuration.Global.StartWithWindows = enabled;
    }
    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(4) };
        if (title.Contains("알림음")) Pencil.SetIcon(button, title.Contains("제거") ? PencilIconKind.Remove : PencilIconKind.Volume);
        else if (title.Contains("폴더")) Pencil.SetIcon(button, PencilIconKind.Folder);
        button.Click += (_, _) => action(); return button;
    }
}
