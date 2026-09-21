using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexMascot.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CodexMascot.App;

public partial class SettingsWindow : Window
{
    private readonly CustomizationManager _manager;
    private readonly OverlayWindow _overlay;
    private readonly SoundPlayerService _sound;
    private readonly TextBlock _feedback = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightBlue, Margin = new Thickness(8) };
    private readonly StackPanel _editor = new();
    private readonly System.Windows.Controls.Image _preview = new() { Width = 180, Height = 150, Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    private MascotState _state = MascotState.Completed;
    private CancellationTokenSource? _tests;
    private int _testGeneration;
    private bool _closed;
    public SettingsWindow(CustomizationManager manager, OverlayWindow overlay, SoundPlayerService sound)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        _manager = manager; _overlay = overlay; _sound = sound;
        _sound.Feedback += SoundFeedback;
        BuildUi();
        Closed += (_, _) =>
        {
            _closed = true; StopTests(); _overlay.PlacementMode = false;
            _sound.Feedback -= SoundFeedback;
        };
    }
    private void SoundFeedback(object? sender, string text) { if (!_closed) _feedback.Text = text; }
    private void BuildUi()
    {
        if (_editor.Parent is Panel oldEditorParent) oldEditorParent.Children.Remove(_editor);
        if (_feedback.Parent is Panel oldFeedbackParent) oldFeedbackParent.Children.Remove(_feedback);
        Root.Children.Clear(); Root.RowDefinitions.Clear();
        Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Root.Children.Add(new TextBlock { Text = "캐릭터 · 사운드 · 표시 위치", FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        var content = new StackPanel();
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); Root.Children.Add(scroll);
        content.Children.Add(Text("설정은 자동 저장됩니다. 고른 파일은 assets 폴더로 복사되므로 원본을 옮겨도 유지됩니다."));
        var global = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        global.Children.Add(Check("소리 사용", _manager.Configuration.Global.SoundEnabled, v => _manager.Configuration.Global.SoundEnabled = v));
        global.Children.Add(Check("항상 위", _manager.Configuration.Global.AlwaysOnTop, v => _manager.Configuration.Global.AlwaysOnTop = v));
        global.Children.Add(Check("클릭 통과", _manager.Configuration.Global.ClickThrough, v => _manager.Configuration.Global.ClickThrough = v));
        global.Children.Add(Check("대기 캐릭터 표시", _manager.Configuration.Global.ShowIdle, v => _manager.Configuration.Global.ShowIdle = v));
        content.Children.Add(global);
        content.Children.Add(Check("완료 팝업을 클릭할 때까지 유지", _manager.Configuration.Global.KeepCompletedVisibleUntilClick, v =>
        { _manager.Configuration.Global.KeepCompletedVisibleUntilClick = v; BuildEditor(); }));
        content.Children.Add(Text("켜면 완료 팝업이 시간 제한 없이 유지됩니다. 캐릭터 클릭 또는 ×로 닫으세요. 이 팝업에는 클릭 통과가 적용되지 않습니다."));
        var clickOptions = new WrapPanel();
        clickOptions.Children.Add(Check("팝업 클릭 시 Codex 데스크톱 창 열기", _manager.Configuration.Global.BringCodexToFrontOnClick, v => _manager.Configuration.Global.BringCodexToFrontOnClick = v));
        clickOptions.Children.Add(Button("Codex 창 열기 테스트", () =>
        {
            _feedback.Text = CodexDesktopActivator.TryActivate() switch
            {
                DesktopActivationResult.Activated => "Codex 데스크톱 창을 앞으로 가져왔습니다.",
                DesktopActivationResult.AttentionRequested => "Windows가 창 전환을 제한했습니다. 작업 표시줄의 Codex 아이콘을 클릭하세요.",
                _ => "Codex 데스크톱을 먼저 실행하세요. 실제 알림 클릭 시에는 Mascot 작업 창을 대신 엽니다."
            };
        }));
        content.Children.Add(clickOptions);
        content.Children.Add(Text("실행 중인 Codex 창을 엽니다. 최소화된 창은 복원하며, 여러 창이면 가장 앞쪽 Codex 창을 선택합니다. ×는 알림만 닫습니다."));
        content.Children.Add(SliderRow("전체 볼륨", 0, 1, _manager.Configuration.Global.MasterVolume, v => _manager.Configuration.Global.MasterVolume = v));
        content.Children.Add(SliderRow("캐릭터 크기", .4, 3, _manager.Configuration.Global.Scale, v => _manager.Configuration.Global.Scale = v));
        var positions = new WrapPanel();
        var screens = new ComboBox { Width = 220, Margin = new Thickness(4) };
        foreach (var screen in Forms.Screen.AllScreens) screens.Items.Add(new ComboBoxItem
            { Content = screen.DeviceName + (screen.Primary ? " (기본)" : ""), Tag = screen.DeviceName });
        screens.SelectedIndex = Math.Max(0, Forms.Screen.AllScreens.ToList().FindIndex(s => s.DeviceName == _manager.Configuration.Global.MonitorDevice));
        screens.SelectionChanged += (_, _) => { _manager.Configuration.Global.MonitorDevice = (screens.SelectedItem as ComboBoxItem)?.Tag as string; Save(); };
        positions.Children.Add(screens);
        var position = new ComboBox { Width = 150, Margin = new Thickness(4) };
        foreach (var (key, label) in new[] { ("bottom-right", "오른쪽 아래"), ("bottom-left", "왼쪽 아래"), ("top-right", "오른쪽 위"), ("top-left", "왼쪽 위"), ("center", "화면 중앙"), ("custom", "드래그한 위치") })
        {
            var item = new ComboBoxItem { Content = label, Tag = key };
            position.Items.Add(item);
            if (key == _manager.Configuration.Global.Position) position.SelectedItem = item;
        }
        position.SelectionChanged += (_, _) => { _manager.Configuration.Global.Position = (position.SelectedItem as ComboBoxItem)?.Tag as string ?? "bottom-right"; Save(); };
        positions.Children.Add(position);
        positions.Children.Add(Button("위치 표시 · 드래그", () =>
        {
            StopTests();
            _overlay.PlacementMode = true;
            _overlay.ApplyGlobal(_manager.Configuration.Global);
            var cfg = _manager.Configuration.For(_state);
            _overlay.ShowState(_state, new StateConfiguration { Loop = true, SpriteColumns = cfg.SpriteColumns, SpriteRows = cfg.SpriteRows, FrameDurationMs = cfg.FrameDurationMs }, _manager.ResolveImage(_state));
            _feedback.Text = "캐릭터를 드래그하세요. 위치가 자동 저장됩니다. 닫기(×) 또는 테스트 중지로 배치를 끝냅니다.";
        }));
        content.Children.Add(positions);
        content.Children.Add(Text("기본 위치는 선택한 모니터의 오른쪽 아래입니다. 배치 중에는 클릭 통과가 일시 해제됩니다."));
        content.Children.Add(Check("Windows 로그인 시 시작", _manager.Configuration.Global.StartWithWindows, SetStartup));
        content.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10) });
        var stateRow = new WrapPanel();
        stateRow.Children.Add(Text("꾸밀 상태"));
        var states = new ComboBox { Width = 180, Margin = new Thickness(4) };
        foreach (var state in CustomizationManager.States)
            states.Items.Add(new ComboBoxItem { Content = MainWindow.StateName(state), Tag = state });
        states.SelectedIndex = 3;
        states.SelectionChanged += (_, _) => { StopTests(); _state = (MascotState)((ComboBoxItem)states.SelectedItem).Tag; BuildEditor(); };
        stateRow.Children.Add(states);
        stateRow.Children.Add(Button("모든 상태 테스트", RunAllTests));
        stateRow.Children.Add(Button("테스트 중지", StopTests));
        content.Children.Add(stateRow);
        content.Children.Add(_editor);
        Grid.SetRow(_feedback, 2); Root.Children.Add(_feedback);
        BuildEditor();
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(Button("assets 폴더 열기", _manager.OpenAssetsFolder));
        footer.Children.Add(Button("테마 내보내기", Export));
        footer.Children.Add(Button("테마 가져오기", Import));
        footer.Children.Add(Button("기본 테마 복구", () => { StopTests(); _manager.Reset(); _editor.Children.Clear(); BuildUi(); Save(); }));
        footer.Children.Add(Button("닫기", Close));
        Grid.SetRow(footer, 3); Root.Children.Add(footer);
    }
    private void BuildEditor()
    {
        _editor.Children.Clear();
        var cfg = _manager.Configuration.For(_state);
        var imagePath = Text(cfg.Image ?? "이미지 없음");
        var soundPath = Text(cfg.Sound ?? "사운드 없음");
        imagePath.TextTrimming = TextTrimming.CharacterEllipsis; soundPath.TextTrimming = TextTrimming.CharacterEllipsis;
        _editor.Children.Add(_preview);
        var imageRow = new WrapPanel();
        imageRow.Children.Add(Button("이미지 선택", () =>
        {
            var dialog = new OpenFileDialog { Filter = "이미지 (GIF, PNG, WebP)|*.gif;*.png;*.webp", InitialDirectory = AppPaths.ImagesDirectory };
            if (dialog.ShowDialog(this) != true) return;
            Try(() =>
            {
                MascotImageLoader.Load(dialog.FileName, cfg);
                cfg.Image = _manager.ImportAsset(dialog.FileName, false); Save(); imagePath.Text = cfg.Image;
                UpdatePreview(); Preview(false);
            });
        }));
        imageRow.Children.Add(Button("이미지만 테스트", () => Preview(false)));
        _editor.Children.Add(imageRow); _editor.Children.Add(imagePath);
        var soundRow = new WrapPanel();
        soundRow.Children.Add(Button("사운드 선택", () =>
        {
            var dialog = new OpenFileDialog { Filter = "사운드 (WAV, MP3)|*.wav;*.mp3", InitialDirectory = AppPaths.SoundsDirectory };
            if (dialog.ShowDialog(this) == true) Try(() => { cfg.Sound = _manager.ImportAsset(dialog.FileName, true); Save(); soundPath.Text = cfg.Sound; });
        }));
        soundRow.Children.Add(Button("소리만 테스트", PlaySound));
        soundRow.Children.Add(Button("소리 제거", () => { cfg.Sound = null; Save(); soundPath.Text = "사운드 없음"; }));
        soundRow.Children.Add(Button("이미지 + 소리", () => Preview(true)));
        _editor.Children.Add(soundRow); _editor.Children.Add(soundPath);
        var values = new WrapPanel();
        var duration = Number("표시 시간 ms (0=계속)", cfg.ShowDurationMs, 0, 600000, v => cfg.ShowDurationMs = v);
        var held = _state == MascotState.Completed && _manager.Configuration.Global.KeepCompletedVisibleUntilClick;
        if (held) _editor.Children.Add(Text("해당 앱이 활성화되면 아래 표시 시간이 지난 뒤 완료 알림을 닫습니다(0이면 4초). 캐릭터를 직접 클릭하면 즉시 닫습니다."));
        values.Children.Add(duration);
        values.Children.Add(Check("애니메이션 반복", cfg.Loop, v => cfg.Loop = v));
        values.Children.Add(Number("스프라이트 가로 칸", cfg.SpriteColumns, 1, 32, v => { cfg.SpriteColumns = v; UpdatePreview(); }));
        values.Children.Add(Number("세로 칸", cfg.SpriteRows, 1, 32, v => { cfg.SpriteRows = v; UpdatePreview(); }));
        values.Children.Add(Number("칸당 ms", cfg.FrameDurationMs, 20, 5000, v => cfg.FrameDurationMs = v));
        _editor.Children.Add(values);
        _editor.Children.Add(SliderRow("이 상태의 볼륨", 0, 1, cfg.Volume, v => cfg.Volume = v));
        UpdatePreview();
    }
    private void UpdatePreview()
    {
        try
        {
            var path = _manager.ResolveImage(_state);
            _preview.Source = path is null ? null : MascotImageLoader.Load(path, _manager.Configuration.For(_state))[0].Bitmap;
        }
        catch (Exception ex) { _preview.Source = null; _feedback.Text = "이미지 오류: " + ex.Message; }
    }
    private void Preview(bool sound)
    {
        StopTests(); _overlay.PlacementMode = false;
        ShowAndPlay(_state, sound);
    }
    private void ShowAndPlay(MascotState state, bool sound)
    {
        _overlay.ApplyGlobal(_manager.Configuration.Global);
        _overlay.ShowState(state, _manager.Configuration.For(state), _manager.ResolveImage(state));
        _feedback.Text = _overlay.LastImageError is null ? MainWindow.StateName(state) + " 이미지 표시 · " + _overlay.DescribePosition() : "대체 이미지 표시: " + _overlay.LastImageError;
        if (sound) PlaySound(state);
    }
    private void PlaySound() { _sound.Stop(); PlaySound(_state); }
    private void PlaySound(MascotState state)
    {
        if (!_manager.Configuration.Global.SoundEnabled) { _feedback.Text = "음소거 상태입니다. '소리 사용'을 켜 주세요."; return; }
        var cfg = _manager.Configuration.For(state);
        if (cfg.Sound is null) { _feedback.Text = "이 상태는 사운드가 없습니다."; return; }
        _sound.Play(_manager.ResolveAsset(cfg.Sound), cfg.Volume * _manager.Configuration.Global.MasterVolume);
    }
    public async void RunAllTests()
    {
        StopTests();
        var generation = _testGeneration;
        var cts = new CancellationTokenSource(); _tests = cts;
        try
        {
            foreach (var state in CustomizationManager.States)
            {
                cts.Token.ThrowIfCancellationRequested();
                ShowAndPlay(state, true);
                await Task.Delay(2000, cts.Token);
            }
            _feedback.Text = "전체 테스트 종료. 이미지 표시와 스피커 소리를 확인해 주세요.";
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (generation == _testGeneration) { _tests = null; _overlay.HideMascot(); _sound.Stop(); }
            cts.Dispose();
        }
    }
    private void StopTests()
    { _testGeneration++; _tests?.Cancel(); _tests = null; _overlay.PlacementMode = false; _overlay.HideMascot(); _sound.Stop(); }
    public void StopPreview() => StopTests();
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
        if (dialog.ShowDialog(this) == true) Try(() =>
        {
            // Build a replacement archive first; an existing theme remains recoverable.
            var temp = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            _manager.Export(temp);
            if (File.Exists(dialog.FileName)) File.Copy(dialog.FileName, dialog.FileName + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
            File.Move(temp, dialog.FileName, true);
            _feedback.Text = "테마 저장: " + dialog.FileName;
        });
    }
    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "Mascot 테마|*.zip" };
        if (dialog.ShowDialog(this) == true) Try(() => { StopTests(); _manager.ImportTheme(dialog.FileName); _editor.Children.Clear(); BuildUi(); Save(); });
    }
    private void Save() { Try(() => { _manager.Save(); _overlay.ApplyGlobal(_manager.Configuration.Global); }); }
    private void Try(Action action) { try { action(); } catch (Exception e) { _feedback.Text = e.Message; } }
    private CheckBox Check(string text, bool value, Action<bool> change)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Foreground = Brushes.White, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => Try(() => { change(box.IsChecked == true); Save(); });
        return box;
    }
    private FrameworkElement Number(string title, int value, int min, int max, Action<int> change)
    {
        var row = new StackPanel { Margin = new Thickness(4) }; row.Children.Add(Text(title));
        var input = new TextBox { Text = value.ToString(), Width = 120 };
        input.LostFocus += (_, _) => { if (int.TryParse(input.Text, out var v) && v >= min && v <= max) { change(v); Save(); } else { input.Text = value.ToString(); _feedback.Text = title + ": " + min + " ~ " + max; } };
        row.Children.Add(input); return row;
    }
    private FrameworkElement SliderRow(string title, double min, double max, double value, Action<double> change)
    {
        var row = new WrapPanel(); row.Children.Add(Text(title));
        var number = Text(value.ToString("0.00"));
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 230, Margin = new Thickness(8) };
        slider.ValueChanged += (_, _) => { change(slider.Value); number.Text = slider.Value.ToString("0.00"); Save(); };
        row.Children.Add(slider); row.Children.Add(number); return row;
    }
    private static TextBlock Text(string text) => new() { Text = text, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) };
    private static Button Button(string title, Action action)
    { var button = new Button { Content = title }; button.Click += (_, _) => action(); return button; }
}
