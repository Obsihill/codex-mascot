using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexMascot.Core;

namespace CodexMascot.App;

internal sealed class MascotPackageEditor : Window
{
    private readonly CustomizationManager _manager;
    private readonly LibraryMascot? _original;
    private readonly Dictionary<string, string?> _paths = new();
    private readonly HashSet<string> _changed = new();
    private readonly TextBox _name = new() { Margin = new Thickness(0, 6, 0, 18) };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly Image _libraryPreview = new() { Width = 140, Height = 126, Stretch = Stretch.Uniform, Margin = new Thickness(0, 12, 0, 8) };
    private readonly TextBlock _libraryName = new() { TextAlignment = TextAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 16) };
    private readonly Button _importButton = new() { Name = "ImportLibraryButton", Content = "등록", IsEnabled = false, Margin = new Thickness(0, 12, 0, 0) };
    private string? _librarySource;
    public LibraryMascot? Result { get; private set; }
    public MascotPackageEditor(CustomizationManager manager, LibraryMascot? original)
    {
        _manager = manager; _original = original;
        Title = original is null ? "마스코트 등록" : "이미지 구성";
        Width = 520; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White; Foreground = new SolidColorBrush(Color.FromRgb(38, 50, 64));
        var root = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(8, 16, 8, 0) };
        if (original is null)
        {
            var tabs = new TabControl { Name = "RegistrationTabs" };
            tabs.Items.Add(new TabItem { Header = "라이브러리", Content = BuildLibraryPanel() });
            tabs.Items.Add(new TabItem { Header = "직접 만들기", Content = panel });
            tabs.SelectionChanged += (_, _) => _error.Text = "";
            root.Children.Add(tabs);
        }
        else root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = "이름" }); _name.Text = original?.Name ?? ""; panel.Children.Add(_name);
        AddSlot(panel, "cover", "대표 이미지", original?.CoverImage);
        foreach (var state in CustomizationManager.States)
            AddSlot(panel, MascotConfiguration.StateKey(state), MainWindow.StateName(state), original?.For(state).Image);
        var save = new Button { Content = original is null ? "등록" : "저장" };
        save.Click += (_, _) =>
        {
            try { Result = BuildPackage(_name.Text, _paths, _changed, _manager, _original); DialogResult = true; }
            catch (Exception ex) { _error.Text = ex.Message; }
        };
        panel.Children.Add(save);
        root.Children.Add(_error);
    }
    private FrameworkElement BuildLibraryPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(8, 16, 8, 0) };
        var pick = new Button { Name = "ChooseLibraryFolderButton", Content = "라이브러리 폴더 선택" };
        pick.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "mascot.json이 있는 마스코트 폴더 선택", Multiselect = false };
            if (dialog.ShowDialog(this) == true) LoadLibrary(dialog.FolderName);
        };
        panel.Children.Add(pick);
        var preview = new StackPanel(); preview.Children.Add(_libraryPreview); preview.Children.Add(_libraryName);
        preview.Children.Add(new TextBlock { Text = "폴더 또는 mascot.json 놓기", TextAlignment = TextAlignment.Center, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 12) });
        var drop = new Border { Name = "LibraryImportDropZone", Child = preview, Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), AllowDrop = true, Margin = new Thickness(0, 16, 0, 0) };
        drop.DragOver += (_, e) => { e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        drop.Drop += (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) LoadLibrary(files[0]);
            else _error.Text = "마스코트 폴더를 하나씩 선택하세요.";
        };
        panel.Children.Add(drop); panel.Children.Add(_importButton);
        _importButton.Click += (_, _) =>
        {
            if (_librarySource is null) return;
            try { Result = MascotFolderImport.Read(_librarySource); DialogResult = true; }
            catch (Exception ex) { _error.Text = ex.Message; }
        };
        return panel;
    }
    internal bool LoadLibrary(string path)
    {
        _librarySource = null; _importButton.IsEnabled = false; _libraryPreview.Source = null; _libraryName.Text = ""; _error.Text = "";
        try
        {
            var mascot = MascotFolderImport.Read(path);
            _libraryPreview.Source = LibraryDashboard.LoadThumbnail(mascot.CoverImage);
            _libraryName.Text = mascot.Name; _librarySource = path; _importButton.IsEnabled = true; return true;
        }
        catch (Exception ex) { _error.Text = ex.Message; return false; }
    }
    private void AddSlot(StackPanel panel, string key, string label, string? path)
    {
        _paths[key] = path;
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(62) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var image = new Image { Height = 54, Width = 54, Stretch = Stretch.Uniform };
        var box = new Border { Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), CornerRadius = new CornerRadius(8), Child = image }; row.Children.Add(box);
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) }; Grid.SetColumn(caption, 1); row.Children.Add(caption);
        var pick = new Button { Content = "선택", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(pick, 2); row.Children.Add(pick);
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
                Filter = key == "cover" ? "이미지|*.png;*.gif;*.webp" : "이미지 / 영상|*.png;*.gif;*.webp;*.mp4;*.m4v;*.mov;*.wmv;*.avi" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                ValidateMedia(dialog.FileName, key == "cover");
                _paths[key] = dialog.FileName; _changed.Add(key); Refresh(); _error.Text = "";
            }
            catch (Exception ex) { _error.Text = ex.Message; }
        };
        panel.Children.Add(row);
    }
    internal static LibraryMascot BuildPackage(string name, IReadOnlyDictionary<string, string?> paths, IReadOnlySet<string> changed,
        CustomizationManager manager, LibraryMascot? original = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("이름을 입력하세요.");
        var keys = new[] { "cover" }.Concat(CustomizationManager.States.Select(MascotConfiguration.StateKey)).ToArray();
        // Validate the whole package before copying any file. Cancelling never imports assets.
        foreach (var key in keys)
        {
            if (!paths.TryGetValue(key, out var path) || string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("대표 이미지와 모든 상태 이미지를 선택하세요.");
            if (path.StartsWith("builtin:", StringComparison.Ordinal)) continue;
            ValidateMedia(manager.ResolveAsset(path) ?? path, key == "cover");
        }
        var result = new LibraryMascot { Name = name.Trim(), InstalledAt = DateTimeOffset.UtcNow };
        foreach (var key in keys)
        {
            var path = paths[key]!;
            if (key == "cover") { result.CoverImage = path; continue; }
            var old = original?.States.GetValueOrDefault(key);
            result.States[key] = new LibraryEventMedia { Image = path, SoundEnabled = old?.SoundEnabled ?? true,
                SpriteColumns = changed.Contains(key) ? 1 : old?.SpriteColumns ?? 1,
                SpriteRows = changed.Contains(key) ? 1 : old?.SpriteRows ?? 1,
                FrameDurationMs = old?.FrameDurationMs ?? 100 };
        }
        return new MascotFolderLibrary(AppPaths.LibraryDirectory).SavePackage(result);
    }
    internal static void ValidateMedia(string path, bool cover)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("파일을 찾을 수 없습니다.", path);
        if (cover && MascotMedia.IsVideo(path)) throw new InvalidOperationException("대표 이미지는 이미지 파일을 선택하세요.");
        if (!MascotMedia.IsVideo(path)) MascotImageLoader.Load(path, new());
    }
}
