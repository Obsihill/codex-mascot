using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexMascot.Core;
using Microsoft.Win32;

namespace CodexMascot.App;

public partial class SettingsWindow : Window
{
    private readonly CustomizationManager _manager;
    private readonly TextBlock _feedback = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightBlue, Margin = new Thickness(4, 12, 4, 0) };
    private readonly StackPanel _stateOptions = new();
    private MascotState _state = MascotState.Completed;
    public event EventHandler? Saved;

    public SettingsWindow(CustomizationManager manager)
    {
        InitializeComponent(); _manager = manager; MaxHeight = SystemParameters.WorkArea.Height;
        BuildUi();
    }
    private void BuildUi()
    {
        if (_stateOptions.Parent is Panel old) old.Children.Remove(_stateOptions);
        if (_feedback.Parent is Panel previous) previous.Children.Remove(_feedback);
        Root.Children.Clear(); Root.RowDefinitions.Clear();
        Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Root.Children.Add(new TextBlock { Text = "공통 알림 설정", FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) });
        var content = new StackPanel();
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); Root.Children.Add(scroll);
        var global = _manager.Configuration.Global;
        content.Children.Add(Check("전체 알림 소리 사용", global.SoundEnabled, v => global.SoundEnabled = v));
        content.Children.Add(Check("항상 위", global.AlwaysOnTop, v => global.AlwaysOnTop = v));
        content.Children.Add(Check("클릭 통과", global.ClickThrough, v => global.ClickThrough = v));
        content.Children.Add(Check("대기 캐릭터 표시", global.ShowIdle, v => global.ShowIdle = v));
        content.Children.Add(Check("완료 팝업을 클릭할 때까지 유지", global.KeepCompletedVisibleUntilClick, v => global.KeepCompletedVisibleUntilClick = v));
        content.Children.Add(Check("팝업 클릭 시 해당 Codex / Claude 창 열기", global.BringCodexToFrontOnClick, v => global.BringCodexToFrontOnClick = v));
        content.Children.Add(Check("Windows 로그인 시 시작", global.StartWithWindows, SetStartup));
        content.Children.Add(Number("모든 마스코트 크기", global.Scale, .4, 3, 2, .01, v => global.Scale = v));
        content.Children.Add(new Separator { Margin = new Thickness(0, 16, 0, 12) });
        var states = new ComboBox { Name = "NotificationState", Width = 190, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4) };
        foreach (var state in CustomizationManager.States) states.Items.Add(new ComboBoxItem { Content = MainWindow.StateName(state), Tag = state });
        states.SelectedIndex = Array.IndexOf(CustomizationManager.States, _state);
        states.SelectionChanged += (_, _) => { _state = (MascotState)((ComboBoxItem)states.SelectedItem).Tag; BuildStateOptions(); };
        content.Children.Add(states); content.Children.Add(_stateOptions); BuildStateOptions();
        content.Children.Add(_feedback);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(Button("assets 폴더 열기", _manager.OpenAssetsFolder));
        footer.Children.Add(Button("테마 내보내기", Export));
        footer.Children.Add(Button("테마 가져오기", Import));
        footer.Children.Add(Button("닫기", Close));
        Grid.SetRow(footer, 2); Root.Children.Add(footer);
    }
    private void BuildStateOptions()
    {
        _stateOptions.Children.Clear();
        var cfg = _manager.Configuration.For(_state);
        var row = new WrapPanel();
        row.Children.Add(Button("공통 알림음 선택", () =>
        {
            var picker = new OpenFileDialog { Filter = "사운드 (WAV, MP3)|*.wav;*.mp3", InitialDirectory = AppPaths.SoundsDirectory };
            if (picker.ShowDialog(this) == true) Try(() => { cfg.Sound = _manager.ImportAsset(picker.FileName, true); Save(); BuildStateOptions(); });
        }));
        row.Children.Add(Button("알림음 제거", () => { cfg.Sound = null; Save(); BuildStateOptions(); }));
        _stateOptions.Children.Add(row);
        _stateOptions.Children.Add(new TextBlock { Text = cfg.Sound is null ? "알림음 없음" : Path.GetFileName(cfg.Sound), Foreground = Brushes.LightGray, Margin = new Thickness(8), TextTrimming = TextTrimming.CharacterEllipsis });
    }
    private FrameworkElement Number(string label, double value, double min, double max, int decimals, double sensitivity, Action<double> change)
    {
        var input = new NumericDragInput { Label = label, Value = value, Minimum = min, Maximum = max, DecimalPlaces = decimals, UnitsPerPixel = sensitivity, Margin = new Thickness(4, 8, 4, 8), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Width = 420 };
        input.ValueCommitted += (_, _) => { change(input.Value); Save(); };
        return input;
    }
    private CheckBox Check(string text, bool value, Action<bool> change)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Foreground = Brushes.White, Margin = new Thickness(8) };
        box.Click += (_, _) => Try(() => { change(box.IsChecked == true); Save(); }); return box;
    }
    private void Save() => Try(() => { _manager.Save(); Saved?.Invoke(this, EventArgs.Empty); });
    private void Try(Action action) { try { action(); } catch (Exception e) { _feedback.Text = e.Message; } }
    private void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("CodexMascot", "\"" + Environment.ProcessPath + "\" --tray");
        else key.DeleteValue("CodexMascot", false);
        _manager.Configuration.Global.StartWithWindows = enabled;
    }
    private void Export()
    {
        var dialog = new SaveFileDialog { Filter = "Mascot 테마|*.zip", FileName = "my-mascot-theme.zip", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        Try(() =>
        {
            var temp = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp"; _manager.Export(temp);
            if (File.Exists(dialog.FileName)) File.Copy(dialog.FileName, dialog.FileName + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
            File.Move(temp, dialog.FileName, true); _feedback.Text = "테마 저장: " + dialog.FileName;
        });
    }
    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "Mascot 테마|*.zip" };
        if (dialog.ShowDialog(this) == true) Try(() => { _manager.ImportTheme(dialog.FileName); BuildUi(); Save(); });
    }
    private static Button Button(string title, Action action)
    { var button = new Button { Content = title }; button.Click += (_, _) => action(); return button; }
}
