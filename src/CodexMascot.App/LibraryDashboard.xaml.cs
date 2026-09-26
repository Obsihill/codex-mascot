using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMascot.Core;

namespace CodexMascot.App;

public partial class LibraryDashboard : UserControl
{
    private const string DragFormat = "CodexMascot.LibraryItem", SelectedDragFormat = "CodexMascot.SelectedItem";
    private LibraryStore _store = null!;
    private CustomizationManager _manager = null!;
    private LibraryMascot? _current;
    private bool _loading;
    private Point _dragStart;
    private string? _dragId;
    private readonly DispatcherTimer _previewTimer = new();
    private IReadOnlyList<MascotFrame> _previewFrames = Array.Empty<MascotFrame>();
    private int _previewFrame;
    private Window? _editorDialog;
    private OverlayWindow? _testOverlay;
    private readonly SoundPlayerService _testSound = new();
    private readonly DispatcherTimer _testTimeout = new() { Interval = TimeSpan.FromSeconds(10) };
    public event EventHandler? SettingsRequested;
    public event EventHandler? LibraryChanged;
    private sealed record PreviewChoice(string Name, MascotState State);
    private sealed record Card(LibraryMascot Mascot, ImageSource? Thumbnail, string Name);
    private MascotState PreviewEvent => _activeState ?? MascotState.Completed;
    internal bool IsTesting => _testOverlay is not null;

    public LibraryDashboard()
    {
        InitializeComponent();
        PreviewState.DisplayMemberPath = nameof(PreviewChoice.Name);
        foreach (var state in CustomizationManager.States) PreviewState.Items.Add(new PreviewChoice(MainWindow.StateName(state), state));
        PreviewState.SelectedIndex = 3;
        _previewTimer.Tick += (_, _) =>
        {
            if (_previewFrames.Count == 0 || _current is null) return;
            if (_previewFrame == _previewFrames.Count - 1 && !_current.Settings(PreviewEvent).Loop) { _previewTimer.Stop(); return; }
            _previewFrame = (_previewFrame + 1) % _previewFrames.Count;
            PreviewImage.Source = _previewFrames[_previewFrame].Bitmap;
            _previewTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, _previewFrames[_previewFrame].DelayMs / _current.Settings(PreviewEvent).Speed));
        };
        PreviewVideo.MediaEnded += (_, _) => { if (_current?.Settings(PreviewEvent).Loop == true && IsVisible) { PreviewVideo.Position = TimeSpan.Zero; PreviewVideo.Play(); } };
        PreviewVideo.MediaFailed += (_, e) => { if (PreviewVideo.Source is not null) ShowError(e.ErrorException.Message); };
        _testTimeout.Tick += (_, _) => StopTest();
        IsVisibleChanged += (_, _) => { if (!IsVisible) { StopPreview(); StopTest(); } else if (_current is not null) ShowPreview(); };
        Unloaded += (_, _) => { StopPreview(); StopTest(); };
    }
    public void Initialize(LibraryStore store, CustomizationManager manager)
    {
        _store = store; _manager = manager;
        _history = new LibraryHistory(store); ApplyPanelRatio();
        InitializeSort();
        RefreshLists(); InstalledList.SelectedIndex = 0;
        if (store.Warning is not null) Feedback.Text = store.Warning;
    }
    private Card ToCard(LibraryMascot m)
    {
        ImageSource? thumbnail = null;
        try { thumbnail = LoadThumbnail(_store.CoverPath(m, _manager)); }
        catch (Exception) { /* Missing media must not prevent editing the package. */ }
        return new(m, thumbnail, DisplayName(m));
    }
    private string DisplayName(LibraryMascot m)
    {
        if (m.SourceId is null) return m.Name;
        var copies = _store.Library.Selected.Where(c => c.SourceId == m.SourceId).ToList();
        return copies.Count > 1 ? m.Name + " · " + (copies.FindIndex(c => c.Id == m.Id) + 1) : m.Name;
    }
    internal static ImageSource? LoadThumbnail(string? path)
        => path is null ? null : path.StartsWith("builtin:", StringComparison.Ordinal)
            ? DemoMascotArtwork.Create(path[8..]) : MascotMedia.IsVideo(path) ? null : MascotImageLoader.Load(path, new())[0].Bitmap;
    private void RefreshLists()
    {
        var top = (InstalledList.SelectedItem as Card)?.Mascot.Id;
        var bottom = (SelectedList.SelectedItem as Card)?.Mascot.Id;
        _loading = true;
        var all = _usage.Sort(_store.Library).Select(ToCard).ToList();
        InstalledSort.SelectedValue = _store.Library.InstalledSort;
        InstalledList.ItemsSource = all;
        SelectedList.ItemsSource = _store.Library.Selected.Select(ToCard).ToList();
        InstalledList.SelectedItem = all.FirstOrDefault(c => c.Mascot.Id == top);
        SelectedList.SelectedItem = SelectedList.Items.Cast<Card>().FirstOrDefault(c => c.Mascot.Id == bottom);
        InstalledCount.Text = all.Count.ToString(); SelectedCount.Text = _store.Library.Selected.Count.ToString();
        _loading = false; UpdateButtons();
    }
    private void UpdateButtons()
    {
        if (_store is null) return;
        DeleteButton.IsEnabled = InstalledList.SelectedItem is Card;
        EditorPanel.IsEnabled = PreviewState.IsEnabled = StateSettingsButton.IsEnabled = _current is not null;
    }
    private void Installed_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_loading && InstalledList.SelectedItem is Card card) Inspect(card.Mascot); UpdateButtons(); }
    private void Selected_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_loading && SelectedList.SelectedItem is Card card) Inspect(card.Mascot); UpdateButtons(); }
    private void Inspect(LibraryMascot m)
    {
        VolumeInput.CancelEdit(); SpeedInput.CancelEdit(); DurationInput.CancelEdit();
        StopTest(); _history?.BreakMerge(); _current = m; _loading = true;
        InstalledList.SelectedItem = InstalledList.Items.Cast<Card>().FirstOrDefault(c => c.Mascot.Id == m.Id);
        SelectedList.SelectedItem = SelectedList.Items.Cast<Card>().FirstOrDefault(c => c.Mascot.Id == m.Id);
        DetailName.Text = DisplayName(m);
        _loading = false; RefreshInspector(); UpdateButtons(); ShowPreview();
    }
    private void CardAdd_OnClick(object sender, RoutedEventArgs e)
    {
        _dragId = null; e.Handled = true;
        if (sender is Button { DataContext: Card card }) AddMascot(card.Mascot.Id);
    }
    internal bool AddMascot(string id)
    {
        var mascot = _store.Library.Installed.FirstOrDefault(m => m.Id == id);
        if (mascot is null) return false;
        if (!Commit(mascot.Name + " 선택", () => _store.Library.Select(id))) return false;
        var selectedId = _store.Library.Selected.Last().Id;
        RefreshLists(); SelectedList.SelectedItem = SelectedList.Items.Cast<Card>().First(c => c.Mascot.Id == selectedId); return true;
    }
    internal bool RemoveMascot(string id)
    {
        var mascot = _store.Library.Selected.FirstOrDefault(m => m.Id == id);
        if (mascot is null) return false;
        if (!Commit(DisplayName(mascot) + " 선택 해제", () => _store.Library.Selected.Remove(mascot))) return false;
        StopTest(); RefreshLists();
        if (_current?.Id == id)
            Inspect(_store.Library.Selected.FirstOrDefault(m => m.SourceId == mascot.SourceId) ?? _store.Library.Installed.First(m => m.Id == mascot.SourceId));
        return true;
    }
    private void CardRemove_OnClick(object sender, RoutedEventArgs e)
    {
        _dragId = null; e.Handled = true;
        if (sender is Button { DataContext: Card card }) RemoveMascot(card.Mascot.Id);
    }
    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (InstalledList.SelectedItem is not Card card) return;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = card.Name + "을(를) 삭제할까요?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "목록에서 제거합니다. 이미지·영상 파일은 유지됩니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var window = Dialog("마스코트 삭제", panel);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "취소", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var confirm = new Button { Name = "ConfirmDelete", Content = "삭제", Foreground = Brushes.Firebrick };
        cancel.Click += (_, _) => window.Close();
        confirm.Click += (_, _) => { if (DeleteMascot(card.Mascot.Id)) window.Close(); };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); panel.Children.Add(buttons);
        window.ShowDialog();
    }
    internal bool DeleteMascot(string id)
    {
        var index = _store.Library.Installed.FindIndex(m => m.Id == id);
        if (index < 0) return false;
        var mascot = _store.Library.Installed[index];
        if (!Commit(mascot.Name + " 삭제", () => { _store.Library.Installed.RemoveAt(index); _store.Library.Selected.RemoveAll(m => m.SourceId == id); })) return false;
        if (_current?.Id == id || _current?.SourceId == id)
        {
            StopTest(); StopPreview(); _current = null;
            DetailName.Text = ""; PreviewError.Visibility = Visibility.Collapsed;
        }
        RefreshLists();
        if (_current is null && InstalledList.Items.Count > 0) InstalledList.SelectedIndex = Math.Min(index, InstalledList.Items.Count - 1);
        return true;
    }
    private static ListBoxItem? ItemAt(ListBox list, object source) => ItemsControl.ContainerFromElement(list, source as DependencyObject) as ListBoxItem;
    private static bool IsCardAction(object source)
    {
        for (var current = source as DependencyObject; current is not null;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (current is ListBoxItem) break;
        }
        return false;
    }
    private void Installed_OnDoubleClick(object sender, MouseButtonEventArgs e)
    { if (!IsCardAction(e.OriginalSource) && ItemAt(InstalledList, e.OriginalSource)?.Content is Card card) { _dragId = null; AddMascot(card.Mascot.Id); e.Handled = true; } }
    private void Selected_OnDoubleClick(object sender, MouseButtonEventArgs e)
    { if (!IsCardAction(e.OriginalSource) && ItemAt(SelectedList, e.OriginalSource)?.Content is Card card) { _dragId = null; RemoveMascot(card.Mascot.Id); e.Handled = true; } }
    private void List_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragId = null;
        if (IsCardAction(e.OriginalSource)) return;
        var list = (ListBox)sender; _dragStart = e.GetPosition(list);
        _dragId = (ItemAt(list, e.OriginalSource)?.Content as Card)?.Mascot.Id;
    }
    private void List_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragId is null) return;
        var list = (ListBox)sender; var p = e.GetPosition(list);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var id = _dragId; _dragId = null;
        var card = list.Items.Cast<Card>().FirstOrDefault(c => c.Mascot.Id == id);
        if (card is null) return;
        var source = list.ItemContainerGenerator.ContainerFromItem(card) as ListBoxItem;
        var selected = list == SelectedList;
        var released = false; var cancelled = false; var inside = true;
        var ghost = new DragPreviewWindow(card.Thumbnail, card.Name);
        var oldOpacity = source?.Opacity ?? 1;
        QueryContinueDragEventHandler query = (_, args) =>
        {
            ghost.FollowCursor();
            if (args.EscapePressed) { cancelled = true; args.Action = DragAction.Cancel; args.Handled = true; }
            else if ((args.KeyStates & DragDropKeyStates.LeftMouseButton) == 0)
            {
                released = true;
                inside = new Rect(0, 0, DropZone.ActualWidth, DropZone.ActualHeight).Contains(DropZone.PointFromScreen(DragPreviewWindow.CursorPoint()));
                // Leaving the selected region is a local removal, never an external drop.
                if (selected) { args.Action = DragAction.Cancel; args.Handled = true; }
            }
        };
        GiveFeedbackEventHandler feedback = (_, _) => ghost.FollowCursor();
        try
        {
            if (source is not null) source.Opacity = .3;
            ghost.Show(); ghost.FollowCursor();
            list.QueryContinueDrag += query; list.GiveFeedback += feedback;
            DragDrop.DoDragDrop(list, new DataObject(selected ? SelectedDragFormat : DragFormat, id), selected ? DragDropEffects.Move : DragDropEffects.Copy);
        }
        finally
        {
            list.QueryContinueDrag -= query; list.GiveFeedback -= feedback;
            ghost.Close(); if (source is not null) source.Opacity = oldOpacity;
            DropZone.Background = Brushes.White;
        }
        if (selected) CompleteSelectionDrag(id, released, cancelled, inside);
    }
    internal bool CompleteSelectionDrag(string id, bool released, bool cancelled, bool insideSelected)
        => released && !cancelled && !insideSelected && RemoveMascot(id);
    private void Selected_OnDragOver(object sender, DragEventArgs e)
    {
        var selected = e.Data.GetData(SelectedDragFormat) is string selectedId && _store.Library.Selected.Any(m => m.Id == selectedId);
        var add = e.Data.GetData(DragFormat) is string id && _store.Library.Installed.Any(m => m.Id == id);
        e.Effects = selected ? DragDropEffects.Move : add ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.Background = selected || add ? new SolidColorBrush(Color.FromRgb(231, 246, 240)) : Brushes.White; e.Handled = true;
    }
    private void Selected_OnDragLeave(object sender, DragEventArgs e) => DropZone.Background = Brushes.White;
    private void Selected_OnDrop(object sender, DragEventArgs e)
    { DropZone.Background = Brushes.White; ReceiveDrop(e.Data); e.Handled = true; }
    internal bool ReceiveDrop(IDataObject data) => data.GetData(DragFormat) is string id && AddMascot(id);

    private void PreviewState_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_activeState is null || PreviewState.SelectedItem is not PreviewChoice choice) return;
        _activeState = choice.State; _history?.BreakMerge(); StopTest(); RefreshInspector(); ShowPreview();
    }
    private void ShowPreview()
    {
        StopPreview(); PreviewError.Visibility = Visibility.Collapsed;
        if (_current is null || !IsVisible) return;
        try
        {
            // The whole-scope inspector is a package preview, not a playback state.
            if (_activeState is null)
            {
                PreviewImage.Source = LoadThumbnail(_store.CoverPath(_current, _manager))
                    ?? throw new FileNotFoundException("대표 이미지 없음");
                return;
            }
            var path = _store.MediaPath(_current, _manager, PreviewEvent);
            if (path is null) throw new FileNotFoundException("이미지 없음");
            if (path.StartsWith("builtin:", StringComparison.Ordinal)) PreviewImage.Source = DemoMascotArtwork.Create(path[8..]);
            else if (MascotMedia.IsVideo(path))
            { PreviewVideo.Visibility = Visibility.Visible; PreviewVideo.Source = new Uri(path); PreviewVideo.SpeedRatio = _current.Settings(PreviewEvent).Speed; PreviewVideo.Play(); }
            else
            {
                var cfg = LibraryStore.Playback(_current, PreviewEvent, _manager.Configuration.For(PreviewEvent));
                _previewFrames = MascotImageLoader.Load(path, cfg);
                _previewFrame = 0; PreviewImage.Source = _previewFrames[0].Bitmap;
                if (_previewFrames.Count > 1) { _previewTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, _previewFrames[0].DelayMs / cfg.PlaybackSpeed)); _previewTimer.Start(); }
            }
        }
        catch (Exception e) { ShowError(e.Message); }
    }
    private void ShowError(string message) { PreviewError.Text = message; PreviewError.Visibility = Visibility.Visible; }
    private void StopPreview()
    { _previewTimer.Stop(); _previewFrames = Array.Empty<MascotFrame>(); PreviewVideo.Close(); PreviewVideo.Source = null; PreviewVideo.Visibility = Visibility.Collapsed; PreviewImage.Source = null; }
    private void Test_OnClick(object sender, RoutedEventArgs e) { if (IsTesting) StopTest(); else RunTest(); }
    private void RunTest()
    {
        if (_current is null) return;
        StopTest(); var mascot = _current;
        var global = LibraryStore.Placement(mascot, _manager.Configuration.Global, PreviewEvent);
        global.KeepCompletedVisibleUntilClick = false; global.ClickThrough = false;
        _testOverlay = new OverlayWindow();
        _testOverlay.AudioError += (_, message) => Feedback.Text = message;
        _testOverlay.ApplyGlobal(global); _testOverlay.Clicked += (_, _) => StopTest();
        var state = PreviewEvent;
        var cfg = LibraryStore.Playback(mascot, state, _manager.Configuration.For(state));
        var path = _store.MediaPath(mascot, _manager, state);
        _testTimeout.Interval = TimeSpan.FromMilliseconds(!MascotMedia.IsVideo(path) && cfg.ShowDurationMs > 0 ? Math.Clamp(cfg.ShowDurationMs, 200, 10000) : 10000);
        cfg.ShowDurationMs = 0; // The owner timer closes the test and resets the toggle together.
        var sound = mascot.For(state).SoundEnabled;
        _testOverlay.ShowState(state, cfg, path, sound);
        if (sound && !MascotMedia.IsVideo(path) && global.SoundEnabled)
            _testSound.Play(_manager.ResolveAsset(cfg.Sound), cfg.Volume * global.MasterVolume, cfg.PlaybackSpeed);
        TestButton.Content = "■ 중지"; _testTimeout.Start();
    }
    public void StopTest()
    { _testTimeout.Stop(); _testSound.Stop(); _testOverlay?.Close(); _testOverlay = null; TestButton.Content = "▶ 테스트"; }
    public void Shutdown() { _usageTimer.Stop(); SaveUsage(); _editorDialog?.Close(); StopTest(); StopPreview(); _testSound.Dispose(); }
    private void Register_OnClick(object sender, RoutedEventArgs e)
    {
        StopTest();
        var editor = new MascotPackageEditor(_manager, null) { Owner = Window.GetWindow(this), Resources = Resources };
        _editorDialog = editor;
        try
        {
            if (editor.ShowDialog() != true || editor.Result is not { } result) return;
            foreach (var state in CustomizationManager.States) result.Settings(state);
            if (!Commit(result.Name + " 등록", () => _store.Library.Installed.Add(result))) return;
            RefreshLists(); InstalledList.SelectedItem = InstalledList.Items.Cast<Card>().First(c => c.Mascot.Id == result.Id);
        }
        finally { _editorDialog = null; }
    }
    private void Settings_OnClick(object sender, RoutedEventArgs e) { StopTest(); SettingsRequested?.Invoke(this, EventArgs.Empty); }
    private Window Dialog(string title, StackPanel panel)
    {
        _editorDialog?.Close();
        var window = new Window { Title = title, Width = 420, SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height - 40,
            ResizeMode = ResizeMode.NoResize, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White, Foreground = new SolidColorBrush(Color.FromRgb(38, 50, 64)), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        window.Resources = Resources; window.PreviewKeyDown += History_OnKeyDown;
        _editorDialog = window; window.Closed += (_, _) => _editorDialog = null; return window;
    }
}
