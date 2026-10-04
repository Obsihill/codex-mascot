using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using CodexMascot.Core;

namespace CodexMascot.App;

internal sealed class MascotPackageEditor : Window
{
    private readonly CustomizationManager _manager;
    private readonly LibraryMascot? _original;
    private readonly Dictionary<string, string?> _paths = new();
    private readonly Dictionary<string, string[]> _soundChoices = new();
    private readonly HashSet<string> _changed = new();
    private readonly TextBox _name = new() { Margin = new Thickness(0, 6, 0, 18) };
    private readonly TextBlock _error = new() { Foreground = PencilPalette.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly Image _libraryPreview = new() { Width = 140, Height = 126, Stretch = Stretch.Uniform, Margin = new Thickness(0, 12, 0, 8) };
    private readonly TextBlock _libraryName = new() { TextAlignment = TextAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 16) };
    private readonly Button _confirmButton = new() { Name = "ApplyMascotButton", MinWidth = 100 };
    private TabControl? _registrationTabs;
    private string? _librarySource;
    private bool _applied, _discardConfirmed;
    public LibraryMascot? Result { get; private set; }
    public MascotPackageEditor(CustomizationManager manager, LibraryMascot? original)
    {
        _manager = manager; _original = original;
        FontFamily = PencilFonts.Handwriting; FontSize = 20;
        Title = original is null ? Loc.T("마스코트 등록") : Loc.T("마스코트 수정");
        Width = 520; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = PencilPalette.Paper; Foreground = PencilPalette.Ink;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AgentMascot;component/PencilTheme.xaml", UriKind.Relative) });
        if (original is null)
        {
            WindowStyle = WindowStyle.None;
            Loaded += (_, _) => PencilPalette.BindTree(this);
            Closing += Registration_Closing;
            PencilEnterFeedback.Attach(this, _confirmButton);
        }
        else PencilWindow.Apply(this);
        _confirmButton.Content = Loc.T(original is null ? "적용" : "저장");
        _confirmButton.Background = PencilPalette.Button;
        _confirmButton.Foreground = PencilPalette.OnButton;
        _confirmButton.BorderBrush = PencilPalette.Button;
        var root = new StackPanel { Margin = new Thickness(24) };
        var scroller = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = original is null ? new PencilBorder { Child = scroller, Background = PencilPalette.Paper,
            BorderBrush = PencilPalette.Line, BorderThickness = new Thickness(1) } : scroller;
        var panel = new StackPanel { Margin = new Thickness(8, 16, 8, 0) };
        if (original is null)
        {
            _registrationTabs = new TabControl { Name = "RegistrationTabs" };
            _registrationTabs.Items.Add(new TabItem { Header = Loc.T("라이브러리"), Content = BuildLibraryPanel() });
            _registrationTabs.Items.Add(new TabItem { Header = Loc.T("직접 만들기"), Content = panel });
            _registrationTabs.SelectionChanged += (_, e) =>
            {
                if (!ReferenceEquals(e.Source, _registrationTabs)) return;
                _error.Text = ""; RefreshConfirmAvailability();
            };
            root.Children.Add(_registrationTabs);
        }
        else root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = Loc.T("이름") }); _name.Text = original?.Name ?? ""; panel.Children.Add(_name);
        AddSlot(panel, "cover", Loc.T("대표 이미지"), original?.CoverImage);
        foreach (var state in CustomizationManager.States)
        {
            AddSlot(panel, MascotConfiguration.StateKey(state), MainWindow.StateName(state), original?.For(state).Image);
            AddSoundSlot(panel, MascotConfiguration.StateKey(state) + ".sound", original?.For(state).SoundCandidates().ToArray() ?? Array.Empty<string>());
        }
        _confirmButton.Click += (_, _) =>
        {
            try
            {
                Result = _registrationTabs?.SelectedIndex == 0
                    ? _librarySource is null ? null : MascotFolderImport.Read(_librarySource)
                    : BuildPackage(_name.Text, _paths, _changed, _manager, _original, _soundChoices);
                if (Result is not null) { _applied = true; DialogResult = true; }
            }
            catch (Exception ex) { _applied = false; _error.Text = ex.Message; }
        };
        root.Children.Add(_error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        if (original is null)
        {
            var close = new Button { Name = "CloseMascotButton", Content = Loc.T("닫기"), MinWidth = 100,
                Margin = new Thickness(0, 0, 8, 0) };
            close.Click += (_, _) => Close();
            actions.Children.Add(close);
        }
        actions.Children.Add(_confirmButton); root.Children.Add(actions);
        RefreshConfirmAvailability();
    }
    private bool HasPendingRegistrationChanges()
        => _librarySource is not null || !string.IsNullOrEmpty(_name.Text) || _changed.Count > 0;

    private void Registration_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_applied || _discardConfirmed || !HasPendingRegistrationChanges()) return;
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        _discardConfirmed = true;
    }

    private bool ConfirmDiscard()
    {
        var dialog = new Window { Title = Loc.T("닫기 확인"), Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = PencilFonts.Handwriting, FontSize = 20,
            Background = PencilPalette.Paper, Foreground = PencilPalette.Ink, Resources = Resources };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = Loc.T("변경 중인 작업이 있습니다. 닫으시겠습니까?"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var no = new Button { Name = "KeepMascotEditingButton", Content = Loc.T("아니요"), IsCancel = true,
            MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
        var yes = new Button { Name = "DiscardMascotChangesButton", Content = Loc.T("네"), MinWidth = 100,
            Background = PencilPalette.Button, Foreground = PencilPalette.OnButton, BorderBrush = PencilPalette.Button };
        no.Click += (_, _) => dialog.DialogResult = false;
        yes.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(no); actions.Children.Add(yes); panel.Children.Add(actions);
        dialog.Content = new PencilBorder { Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Background = PencilPalette.Paper, BorderBrush = PencilPalette.Line, BorderThickness = new Thickness(1) };
        dialog.PreviewKeyDown += (_, e) => { if (e.Key is Key.Return or Key.Enter) e.Handled = true; };
        dialog.Loaded += (_, _) => PencilPalette.BindTree(dialog);
        return dialog.ShowDialog() == true;
    }
    private FrameworkElement BuildLibraryPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(8, 16, 8, 0) };
        var pick = new Button { Name = "ChooseLibraryFolderButton", Content = Loc.T("라이브러리 폴더 선택") };
        Pencil.SetIcon(pick, PencilIconKind.Folder);
        pick.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("mascot.json이 있는 마스코트 폴더 선택"), Multiselect = false };
            if (dialog.ShowDialog(this) == true) LoadLibrary(dialog.FolderName);
        };
        panel.Children.Add(pick);
        var preview = new StackPanel(); preview.Children.Add(_libraryPreview); preview.Children.Add(_libraryName);
        preview.Children.Add(new TextBlock { Text = Loc.T("폴더 또는 mascot.json 놓기"), TextAlignment = TextAlignment.Center, Foreground = PencilPalette.Muted, Margin = new Thickness(0, 0, 0, 12) });
        var drop = new PencilBorder { Name = "LibraryImportDropZone", Child = preview, Background = PencilPalette.Surface, BorderBrush = PencilPalette.Line,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), AllowDrop = true, Margin = new Thickness(0, 16, 0, 0) };
        drop.DragOver += (_, e) => { e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } ? DragDropEffects.Copy : DragDropEffects.None; drop.BorderBrush = e.Effects == DragDropEffects.Copy ? PencilPalette.Emphasis : PencilPalette.Line; e.Handled = true; };
        drop.DragLeave += (_, _) => drop.BorderBrush = PencilPalette.Line;
        drop.Drop += (_, e) =>
        {
            drop.BorderBrush = PencilPalette.Line;
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) LoadLibrary(files[0]);
            else _error.Text = Loc.T("마스코트 폴더를 하나씩 선택하세요.");
        };
        panel.Children.Add(drop);
        return panel;
    }
    private void RefreshConfirmAvailability()
        => _confirmButton.IsEnabled = _registrationTabs is null || _registrationTabs.SelectedIndex > 0 ||
            _registrationTabs.SelectedIndex == 0 && _librarySource is not null;
    internal bool LoadLibrary(string path)
    {
        _librarySource = null; RefreshConfirmAvailability(); _libraryPreview.Source = null; _libraryName.Text = ""; _error.Text = "";
        try
        {
            var mascot = MascotFolderImport.Read(path);
            _libraryPreview.Source = LibraryDashboard.LoadThumbnail(mascot.CoverImage);
            _libraryName.Text = mascot.Name; _librarySource = path; RefreshConfirmAvailability(); return true;
        }
        catch (Exception ex) { _error.Text = ex.Message; return false; }
    }
    private void AddSlot(StackPanel panel, string key, string label, string? path)
    {
        _paths[key] = path;
        var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(42) }); row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var image = new Image { Height = 34, Width = 34, Stretch = Stretch.Uniform };
        var box = new PencilBorder { Background = PencilPalette.Inset, Child = image }; row.Children.Add(box);
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) }; Grid.SetColumn(caption, 1); row.Children.Add(caption);
        var pick = new Button { Content = key == "cover" ? Loc.T("이미지") : Loc.T("이미지 / 영상"),
            Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(pick, 2); row.Children.Add(pick);
        var clear = new Button { Content = "", ToolTip = Loc.T("제거"), Width = 30,
            Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Pencil.SetIcon(clear, PencilIconKind.Remove);
        System.Windows.Automation.AutomationProperties.SetName(clear, label + " " + Loc.T("제거"));
        Grid.SetColumn(clear, 3); row.Children.Add(clear);
        void Refresh()
        {
            var value = _paths[key];
            var resolved = value?.StartsWith("builtin:", StringComparison.Ordinal) == true ? value : _manager.ResolveAsset(value);
            try { image.Source = LibraryDashboard.LoadThumbnail(resolved); }
            catch (Exception) { image.Source = null; }
            // Videos are selected by filename but are not opened or played in this dialog.
            caption.Text = label + (MascotMedia.IsVideo(value) ? "  ▶" : "");
        }
        Refresh();
        pick.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = label,
                Filter = key == "cover" ? Loc.T("이미지|*.png;*.gif;*.webp") : Loc.T("이미지 / 영상|*.png;*.gif;*.webp;*.mp4;*.m4v;*.mov;*.wmv;*.avi") };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                ValidateMedia(dialog.FileName, key == "cover");
                _paths[key] = dialog.FileName; _changed.Add(key); Refresh(); _error.Text = "";
            }
            catch (Exception ex) { _error.Text = ex.Message; }
        };
        clear.Click += (_, _) => { _paths[key] = null; _changed.Add(key); Refresh(); _error.Text = ""; };
        panel.Children.Add(row);
    }
    private void AddSoundSlot(StackPanel panel, string key, string[] paths)
    {
        _soundChoices[key] = paths;
        var row = new DockPanel { Margin = new Thickness(42, 0, 0, 6) };
        var pick = new Button { Content = Loc.T("소리 추가"), Name = key.Replace(".", "_") + "Pick", Padding = new Thickness(8, 3, 8, 3) };
        var clear = new Button { Content = Loc.T("제거"), Padding = new Thickness(8, 3, 8, 3) };
        Pencil.SetIcon(pick, PencilIconKind.Volume); Pencil.SetIcon(clear, PencilIconKind.Remove);
        DockPanel.SetDock(clear, Dock.Right); row.Children.Add(clear);
        DockPanel.SetDock(pick, Dock.Right); row.Children.Add(pick);
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 6, 0) };
        row.Children.Add(name);
        void Refresh()
        {
            var values = _soundChoices[key];
            name.Text = values.Length switch { 0 => Loc.T("별도 소리 없음"), 1 => Path.GetFileName(values[0]), _ => Loc.F("소리 {0}개 · 랜덤", values.Length) };
            name.ToolTip = string.Join("\n", values.Select(Path.GetFileName));
        }
        pick.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("소리 선택 — 여러 개 선택 시 랜덤 재생"), Filter = Loc.T("소리|*.wav;*.mp3"), Multiselect = true };
            if (dialog.ShowDialog(this) != true) return;
            try { _soundChoices[key] = AppendSoundChoices(_soundChoices[key], dialog.FileNames); _changed.Add(key); Refresh(); _error.Text = ""; }
            catch (Exception ex) { _error.Text = ex.Message; }
        };
        clear.Click += (_, _) => { _soundChoices[key] = Array.Empty<string>(); _changed.Add(key); Refresh(); };
        Refresh(); panel.Children.Add(row);
    }
    internal static string[] AppendSoundChoices(IEnumerable<string> current, IEnumerable<string> added)
    {
        var selected = added.ToArray();
        foreach (var path in selected) ValidateSound(path);
        return current.Concat(selected).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static LibraryMascot BuildPackage(string name, IReadOnlyDictionary<string, string?> paths, IReadOnlySet<string> changed,
        CustomizationManager manager, LibraryMascot? original = null, IReadOnlyDictionary<string, string[]>? soundChoices = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException(Loc.T("이름을 입력하세요."));
        var keys = new[] { "cover" }.Concat(CustomizationManager.States.Select(MascotConfiguration.StateKey)).ToArray();
        // Validate the whole package before copying any file. Cancelling never imports assets.
        foreach (var key in keys)
        {
            if (!paths.TryGetValue(key, out var path) || string.IsNullOrWhiteSpace(path)) continue;
            if (path.StartsWith("builtin:", StringComparison.Ordinal)) continue;
            ValidateMedia(manager.ResolveAsset(path) ?? path, key == "cover");
        }
        var selectedSounds = new Dictionary<string, string[]>();
        foreach (var state in CustomizationManager.States)
        {
            var key = MascotConfiguration.StateKey(state) + ".sound";
            var sounds = soundChoices is not null && soundChoices.TryGetValue(key, out var choices) ? choices :
                paths.TryGetValue(key, out var value) ? (string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value }) :
                original?.For(state).SoundCandidates().ToArray() ?? Array.Empty<string>();
            selectedSounds[key] = sounds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var sound in selectedSounds[key]) ValidateSound(manager.ResolveAsset(sound) ?? sound);
        }
        var result = original is null ? new LibraryMascot { InstalledAt = DateTimeOffset.UtcNow } :
            JsonSerializer.Deserialize<LibraryMascot>(JsonSerializer.Serialize(original))!;
        result.Name = name.Trim();
        foreach (var key in keys)
        {
            var path = paths.GetValueOrDefault(key);
            if (key == "cover") { result.CoverImage = path; continue; }
            var media = result.States.GetValueOrDefault(key) ?? new LibraryEventMedia();
            media.Image = path; media.Sound = null; media.Sounds = selectedSounds[key + ".sound"].ToList();
            if (changed.Contains(key)) { media.SpriteColumns = 1; media.SpriteRows = 1; }
            result.States[key] = media;
        }
        // Edits are copied by LibraryHistory.Commit, so a cancelled or failed
        // edit cannot change the installed package before the library update.
        return original is null ? new MascotFolderLibrary(AppPaths.LibraryDirectory).SavePackage(result) : result;
    }
    internal static void ValidateMedia(string path, bool cover)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(Loc.T("파일을 찾을 수 없습니다."), path);
        if (cover && MascotMedia.IsVideo(path)) throw new InvalidOperationException(Loc.T("대표 이미지는 이미지 파일을 선택하세요."));
        if (!MascotMedia.IsVideo(path)) MascotImageLoader.Load(path, new());
    }
    internal static void ValidateSound(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(Loc.T("소리 파일을 찾을 수 없습니다."), path);
        if (Path.GetExtension(path).ToLowerInvariant() is not (".wav" or ".mp3")) throw new InvalidDataException(Loc.T("WAV 또는 MP3 소리를 선택하세요."));
        using var reader = new NAudio.Wave.AudioFileReader(path);
        if (reader.TotalTime <= TimeSpan.Zero || reader.Read(new float[1024], 0, 1024) == 0) throw new InvalidDataException(Loc.T("재생 가능한 소리가 없습니다."));
    }
}
