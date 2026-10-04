using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace CodexMascot.App;

public partial class SettingsWindow : Window
{
    private readonly CustomizationManager _manager;
    private readonly string _originalLanguage;
    private readonly bool _originalStartup;
    private readonly Action<bool> _applyStartup;
    private readonly Func<Action> _startupUndo;
    private bool _finished;
    internal bool Accepted { get; private set; }
    private readonly TextBlock _feedback = new() { TextWrapping = TextWrapping.Wrap, Foreground = PencilPalette.Accent, Margin = new Thickness(4, 12, 4, 0) };
    private FrameworkElement? _connection;
    private TabItem? _connectionTab;
    private ScrollViewer? _connectionHost;
    private PencilEnterFeedback? _enterFeedback;
    internal TabControl Pages { get; private set; } = null!;
    public event EventHandler? Saved;
    public event EventHandler? PreviewChanged;
    internal event Action? Preparing;
    internal event Action? Accepting;

    public SettingsWindow(CustomizationManager manager, FrameworkElement? connection = null, Action<bool>? applyStartup = null)
    {
        _originalLanguage = Loc.Language; _originalStartup = manager.Configuration.Global.StartWithWindows;
        _applyStartup = applyStartup ?? StartupRegistration.SetEnabled;
        _startupUndo = applyStartup is null ? () => { var command = StartupRegistration.ReadCommand(); return () => StartupRegistration.RestoreCommand(command); }
            : () => () => applyStartup(_originalStartup);
        InitializeComponent(); _manager = manager; _connection = connection; PencilWindow.Apply(this);
        _manager.BeginEdit();
        BuildUi();
        Loc.Changed += LanguageChanged;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        // Rebuild only this form, never the monitor/main window or active tasks.
        Dispatcher.BeginInvoke(new Action(() => { if (!_finished) { Title = AppBrand.SettingsTitle; BuildUi(); } }));
    }
    private void BuildUi()
    {
        _enterFeedback?.Dispose();
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
        var general = Page(Loc.T("일반"));
        general.Children.Add(Section(Loc.T("시작"), PencilIconKind.Play, Choice(Loc.T("Windows 로그인 시 시작"), global.StartWithWindows, SetStartup)));
        general.Children.Add(Section(Loc.T("마스코트 표시"), PencilIconKind.Position,
            Choice(Loc.T("항상 위"), global.AlwaysOnTop, v => global.AlwaysOnTop = v),
            Choice(Loc.T("대기 캐릭터 표시"), global.ShowIdle, v => global.ShowIdle = v)));
        var notifications = Page(Loc.T("알림"));
        notifications.Children.Add(Section(Loc.T("완료 알림"), PencilIconKind.Settings,
            Choice(Loc.T("팝업 클릭 시 해당 Codex / Claude 창 열기"), global.BringCodexToFrontOnClick, v => global.BringCodexToFrontOnClick = v)));
        if (_connection is not null)
        {
            _connectionHost = new ScrollViewer { Content = _connection, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _connectionTab = new TabItem { Header = Loc.T("연결"), Content = _connectionHost };
            Pages.Items.Add(_connectionTab);
        }
        var info = Page(Loc.T("정보"));
        info.Children.Add(Section(AppBrand.Name, PencilIconKind.Settings,
            Row(Loc.T("버전"), new TextBlock { Text = typeof(App).Assembly.GetName().Version?.ToString(3), VerticalAlignment = VerticalAlignment.Center }),
            Row(Loc.T("지원 도구"), new TextBlock { Text = "Codex · Claude", VerticalAlignment = VerticalAlignment.Center }),
            Row(Loc.T("글꼴"), new TextBlock { Text = Loc.T("나눔손글씨 펜 · SIL OFL 1.1"), TextWrapping = TextWrapping.Wrap }),
            Row(Loc.T("설정 저장"), new TextBlock { Text = Loc.T("확인을 누르면 저장됩니다."), VerticalAlignment = VerticalAlignment.Center })));
        var language = Page("언어 / Language");
        var choices = new[] { "ko", "en" };
        var languagePicker = new ComboBox
        {
            Name = "LanguagePicker", ItemsSource = new[] { "한국어", "English" },
            SelectedIndex = Array.IndexOf(choices, Loc.Language), Margin = new Thickness(0)
        };
        languagePicker.SelectionChanged += (_, _) => Try(() =>
        {
            if (_finished || languagePicker.SelectedIndex < 0) return;
            global.Language = choices[languagePicker.SelectedIndex];
            Loc.Configure(global.Language);
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        });
        language.Children.Add(Section(Loc.T("언어"), PencilIconKind.Settings, Row(Loc.T("표시 언어"), languagePicker)));
        language.Children.Add(new TextBlock
        {
            Text = Loc.T("변경 사항은 즉시 미리 적용됩니다. 확인하면 저장하고, 취소하면 원래 설정으로 돌아갑니다."),
            TextWrapping = TextWrapping.Wrap, Foreground = PencilPalette.Muted, Margin = new Thickness(4)
        });
        Pages.SelectedIndex = Math.Clamp(selected, 0, Pages.Items.Count - 1);
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = true };
        var cancel = Button(Loc.T("취소"), Close); cancel.Name = "CancelSettings"; cancel.IsCancel = true; cancel.MinWidth = 100;
        var confirm = Button(Loc.T("확인"), Accept); confirm.Name = "ConfirmSettings"; confirm.MinWidth = 100;
        confirm.SetBinding(BackgroundProperty, new Binding(nameof(PencilThemeColors.Button)) { Source = PencilPalette.Current });
        confirm.SetBinding(ForegroundProperty, new Binding(nameof(PencilThemeColors.OnButton)) { Source = PencilPalette.Current });
        confirm.SetBinding(BorderBrushProperty, new Binding(nameof(PencilThemeColors.Button)) { Source = PencilPalette.Current });
        DockPanel.SetDock(cancel, Dock.Right); DockPanel.SetDock(confirm, Dock.Right);
        footer.Children.Add(confirm); footer.Children.Add(cancel); footer.Children.Add(_feedback);
        Grid.SetRow(footer, 1); Root.Children.Add(footer);
        _enterFeedback = PencilEnterFeedback.Attach(this, confirm);
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
        group.Children.Add(new PencilBorder { Name = "SectionBody", Background = PencilPalette.Surface, BorderBrush = PencilPalette.Line, BorderThickness = new Thickness(1), Padding = new Thickness(14, 26, 14, 10), Margin = new Thickness(0, 16, 0, 0), Child = body });
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new PencilIcon { Kind = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        group.Children.Add(new PencilBorder
        {
            Name = "SectionHeader", Background = PencilPalette.Paper, BorderBrush = PencilPalette.Line,
            BorderThickness = new Thickness(1), Padding = new Thickness(7, 3, 7, 3),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 0, 0, 0), Child = header
        });
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
        var box = new ComboBox { Tag = text, ItemsSource = new[] { Loc.T("켜기"), Loc.T("끄기") }, SelectedIndex = value ? 0 : 1, Margin = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(box, text);
        box.SelectionChanged += (_, _) => Try(() => { if (_finished || box.SelectedIndex < 0) return; change(box.SelectedIndex == 0); PreviewChanged?.Invoke(this, EventArgs.Empty); });
        return Row(text, box);
    }
    internal void Accept() => Try(() =>
    {
        Preparing?.Invoke();
        _manager.Configuration.Global.Language = Loc.Language;
        _manager.CommitEdit(() =>
        {
            Action? undoStartup = null;
            try
            {
                if (_manager.Configuration.Global.StartWithWindows != _originalStartup)
                {
                    undoStartup = _startupUndo();
                    _applyStartup(_manager.Configuration.Global.StartWithWindows);
                }
                Accepting?.Invoke();
            }
            catch { undoStartup?.Invoke(); throw; }
        });
        Accepted = true;
        Saved?.Invoke(this, EventArgs.Empty);
        Close();
    });
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        _finished = true; Loc.Changed -= LanguageChanged;
        if (!Accepted)
        {
            _manager.CancelEdit();
            Loc.Configure(_originalLanguage);
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private void Try(Action action) { try { action(); } catch (Exception e) { _feedback.Text = e.Message; } }
    private void SetStartup(bool enabled)
    {
        _manager.Configuration.Global.StartWithWindows = enabled;
    }
    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(4) };
        if (title.Contains(Loc.T("알림음"))) Pencil.SetIcon(button, title.Contains(Loc.T("제거")) ? PencilIconKind.Remove : PencilIconKind.Volume);
        else if (title.Contains(Loc.T("폴더"))) Pencil.SetIcon(button, PencilIconKind.Folder);
        button.Click += (_, _) => action(); return button;
    }
}
