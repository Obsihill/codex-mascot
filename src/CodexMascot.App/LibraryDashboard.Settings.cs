using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Text.Json;
using CodexMascot.Core;

namespace CodexMascot.App;

public partial class LibraryDashboard
{
    private LibraryHistory _history = null!;
    private MascotState? _activeState;
    private IEnumerable<MascotState> EditStates => _activeState is { } state ? new[] { state } : CustomizationManager.States;
    private string ScopeName => _activeState is { } state ? MainWindow.StateName(state) : Loc.T("전체");
    private string ChangeLabel(string field) => DisplayName(_current!) + " · " + ScopeName + " · " + field;
    private static T? Common<T>(IEnumerable<T> values) where T : struct
    { var array = values.Distinct().Take(2).ToArray(); return array.Length == 1 ? array[0] : null; }

    private void RefreshInspector()
    {
        if (_current is null) return;
        _loading = true;
        DetailName.Text = DisplayName(_current);
        PreviewSurface.Height = _activeState is null ? 194 : 146;
        var states = EditStates.ToArray();
        var settings = states.Select(_current.Settings).ToArray();
        var volume = Common(settings.Select(s => s.Volume));
        var speed = Common(settings.Select(s => s.Speed));
        VolumeInput.Value = volume ?? settings[0].Volume; VolumeInput.IsMixed = volume is null;
        SpeedInput.Value = speed ?? settings[0].Speed; SpeedInput.IsMixed = speed is null;
        var enabled = _activeState is null || _current.Events.Contains(MascotConfiguration.StateKey(_activeState.Value));
        foreach (var control in new UIElement[] { VolumeInput, SpeedInput, PositionButton, TestButton, LoopCheck, HoldCheck, TaskbarCheck })
            control.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        TestButton.IsEnabled = enabled && states.Any(s => !string.IsNullOrWhiteSpace(_current.For(s).Image));
        var showDuration = enabled && _activeState is { } selectedState && !MascotMedia.IsVideo(_current.For(selectedState).Image);
        DurationInput.Visibility = showDuration ? Visibility.Visible : Visibility.Collapsed;
        DurationInput.IsEnabled = showDuration;
        DurationInput.IsMixed = false;
        if (showDuration && _activeState is { } imageState)
        {
            DurationInput.Value = LibraryStore.Playback(_current, imageState, _manager.Configuration.For(imageState)).ShowDurationMs;
        }
        SetToggle(LoopCheck, Loc.T("반복"), Common(settings.Select(s => s.Loop)));
        SetToggle(HoldCheck, Loc.T("확인하면 닫힘"), Common(settings.Select(s => s.HoldUntilClick == true)));
        SetToggle(TaskbarCheck, Loc.T("작업표시줄 뒤로"), Common(settings.Select(s => s.HideBehindTaskbar)));
        HoldCheck.ToolTip = Loc.T("마스코트를 클릭하거나 해당 Codex / Claude 창을 열면 확인 처리하고 닫습니다.");
        SetToggle(PlayCheck, Loc.T("재생"), Common(states.Select(s => _current.Events.Contains(MascotConfiguration.StateKey(s)))));
        StatePlaybackOptions.Visibility = _activeState is null ? Visibility.Collapsed : Visibility.Visible;
        SelectedSettingsActions.Visibility = _activeState is null && _current.SourceId is not null ? Visibility.Visible : Visibility.Collapsed;
        ResetSettingsButton.Visibility = ApplySettingsButton.Visibility = SelectedSettingsActions.Visibility;
        EditMascotButton.Visibility = _activeState is null && _current.SourceId is null ? Visibility.Visible : Visibility.Collapsed;
        var samePosition = settings.Select(s => (s.Position, s.MonitorDevice, s.CustomLeft, s.CustomTop)).Distinct().Count() == 1;
        var sameScale = settings.Select(s => s.Scale ?? _manager.Configuration.Global.Scale).Distinct().Count() == 1;
        PositionButton.Content = samePosition && sameScale ? Loc.T("위치 크기 변경") : Loc.T("위치 크기 변경 ??");
        _loading = false;
    }
    private static void SetToggle(CheckBox check, string name, bool? value)
    { check.IsChecked = value; check.Content = value is null ? name + " ??" : name; }
    private void StateSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        DurationInput.CancelEdit();
        _history.BreakMerge(); StopTest();
        _activeState = _activeState is null ? ((PreviewChoice)PreviewState.SelectedItem).State : null;
        StateSettingsButton.Content = _activeState is null ? Loc.T("상태별 설정") : Loc.T("전체로");
        Pencil.SetIcon(StateSettingsButton, _activeState is null ? PencilIconKind.Settings : PencilIconKind.Back);
        ScopeLabel.Text = _activeState is null ? Loc.T("전체") : Loc.T("상태별");
        PreviewState.Visibility = _activeState is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshInspector(); ShowPreview();
    }
    private bool Commit(string description, Action change, string? mergeKey = null)
    {
        try
        {
            if (!_history.Commit(description, change, mergeKey)) return false;
            _usage.Track(_store.Library.Selected);
            Feedback.Text = description; LibraryChanged?.Invoke(this, EventArgs.Empty); return true;
        }
        catch (Exception ex) { RebindAfterRestore(); Feedback.Text = Loc.T("저장 실패: ") + ex.Message; return false; }
    }
    private void ResetSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.SourceId is null || _activeState is not null) return;
        var installed = _store.Library.Installed.FirstOrDefault(source => source.Id == _current.SourceId);
        if (installed is null) return;
        if (!ConfirmSettingsChange("설정 초기화 확인", "선택된 마스코트의 설정을 설치됨 원본의 설정으로 되돌릴까요?")) return;
        StopTest(); _history.BreakMerge();
        var m = _current;
        Commit(DisplayName(m) + Loc.T(" · 설정 초기화"), () => CopySettings(installed, m));
        RefreshInspector(); ShowPreview();
    }
    private void ApplySettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.SourceId is null || _activeState is not null) return;
        var installed = _store.Library.Installed.FirstOrDefault(source => source.Id == _current.SourceId);
        if (installed is null) return;
        if (!ConfirmSettingsChange("설정 적용 확인",
                "현재 선택된 마스코트의 설정을 설치됨 원본에 적용할까요? 다른 선택 항목의 설정은 바뀌지 않습니다.")) return;
        StopTest(); _history.BreakMerge();
        var selected = _current;
        Commit(DisplayName(selected) + Loc.T(" · 설정 적용"), () => CopySettings(selected, installed));
        RefreshInspector(); ShowPreview();
    }
    private static void CopySettings(LibraryMascot source, LibraryMascot target)
    {
        target.Volume = source.Volume; target.Speed = source.Speed; target.Loop = source.Loop;
        target.Scale = source.Scale; target.Position = source.Position;
        target.CustomLeft = source.CustomLeft; target.CustomTop = source.CustomTop;
        target.MonitorDevice = source.MonitorDevice; target.Events = source.Events.ToList();
        foreach (var state in CustomizationManager.States)
        {
            var from = source.For(state); var to = target.For(state);
            to.Playback = from.Playback is null ? null :
                JsonSerializer.Deserialize<MascotPlaybackSettings>(JsonSerializer.Serialize(from.Playback));
            to.SoundEnabled = from.SoundEnabled;
        }
    }
    private bool ConfirmSettingsChange(string title, string message)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = Loc.T(message), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Name = "CancelSettingsChange", Content = Loc.T("취소"), IsCancel = true, IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0) };
        var confirm = new Button { Name = "ConfirmSettingsChange", Content = Loc.T("확인") };
        cancel.Click += (_, _) => Window.GetWindow(cancel)!.DialogResult = false;
        confirm.Click += (_, _) => Window.GetWindow(confirm)!.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(confirm); panel.Children.Add(actions);
        return Dialog(Loc.T(title), panel).ShowDialog() == true;
    }
    private void EditMascot_OnClick(object sender, RoutedEventArgs e)
    {
        if (_current is not { SourceId: null } original || _activeState is not null) return;
        StopTest(); StopPreview();
        var editor = new MascotPackageEditor(_manager, original) { Owner = Window.GetWindow(this), Resources = Resources };
        _editorDialog = editor;
        try
        {
            if (editor.ShowDialog() == true && editor.Result is { } result) UpdateInstalledMascot(result);
        }
        finally { _editorDialog = null; ShowPreview(); }
    }
    internal bool UpdateInstalledMascot(LibraryMascot replacement)
    {
        var original = _store.Library.Installed.FirstOrDefault(m => m.Id == replacement.Id);
        if (original is null || replacement.SourceId is not null) return false;
        foreach (var state in CustomizationManager.States) replacement.Settings(state);
        StopTest(); _history.BreakMerge();
        if (!Commit(replacement.Name + Loc.T(" · 마스코트 수정"), () =>
        {
            var index = _store.Library.Installed.IndexOf(original);
            _store.Library.Installed[index] = replacement;
            foreach (var selected in _store.Library.Selected.Where(m => m.SourceId == replacement.Id))
            {
                selected.Name = replacement.Name;
                selected.CoverImage = replacement.CoverImage;
                foreach (var state in CustomizationManager.States)
                {
                    var source = replacement.For(state); var target = selected.For(state);
                    target.Image = source.Image; target.Sound = source.Sound;
                    target.Sounds = source.Sounds.ToList();
                    target.SpriteColumns = source.SpriteColumns; target.SpriteRows = source.SpriteRows;
                    target.FrameDurationMs = source.FrameDurationMs;
                }
            }
        })) return false;
        RefreshLists(); Inspect(replacement); return true;
    }
    private void Volume_OnChanged(object? sender, EventArgs e)
    {
        if (_loading || _current is null) return;
        var m = _current; var states = EditStates.ToArray(); var value = VolumeInput.Value;
        Commit(ChangeLabel(Loc.T("볼륨 변경")), () => { foreach (var state in states) m.Settings(state).Volume = value; if (_activeState is null) m.Volume = value; });
        RefreshInspector();
    }
    private void Speed_OnChanged(object? sender, EventArgs e)
    {
        if (_loading || _current is null) return;
        var m = _current; var states = EditStates.ToArray(); var value = SpeedInput.Value;
        Commit(ChangeLabel(Loc.T("재생 속도 변경")), () => { foreach (var state in states) m.Settings(state).Speed = value; if (_activeState is null) m.Speed = value; });
        PreviewVideo.SpeedRatio = m.Settings(PreviewEvent).Speed; RefreshInspector();
    }
    private void Duration_OnChanged(object? sender, EventArgs e)
    {
        if (_loading || _current is null || _activeState is not { } state || MascotMedia.IsVideo(_current.For(state).Image)) return;
        var m = _current;
        var value = (int)Math.Round(DurationInput.Value);
        Commit(ChangeLabel(Loc.T("이미지 재생시간 변경")), () => m.Settings(state).ImageDurationMs = value);
        StopTest(); RefreshInspector();
    }
    private void Loop_OnClick(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null || _activeState is null) return;
        var m = _current; var value = LoopCheck.IsChecked == true; var states = EditStates.ToArray();
        Commit(ChangeLabel(Loc.T("반복 ") + (value ? Loc.T("켜기") : Loc.T("끄기"))), () => { foreach (var state in states) m.Settings(state).Loop = value; if (_activeState is null) m.Loop = value; });
        RefreshInspector(); ShowPreview();
    }
    private void Play_OnClick(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null || _activeState is null) return;
        var m = _current; var value = PlayCheck.IsChecked == true; var states = EditStates.ToArray();
        Commit(ChangeLabel(Loc.T("재생 ") + (value ? Loc.T("켜기") : Loc.T("끄기"))), () =>
        {
            foreach (var state in states)
            {
                var key = MascotConfiguration.StateKey(state);
                if (value) { if (!m.Events.Contains(key)) m.Events.Add(key); } else m.Events.Remove(key);
            }
        });
        StopTest(); RefreshInspector();
    }
    private void Hold_OnClick(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null || _activeState is not { } state) return;
        var mascot = _current; var value = HoldCheck.IsChecked == true;
        Commit(ChangeLabel(Loc.T("확인하면 닫힘 ") + (value ? Loc.T("켜기") : Loc.T("끄기"))), () => mascot.Settings(state).HoldUntilClick = value);
        StopTest(); RefreshInspector();
    }
    private void Taskbar_OnClick(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null) return;
        var mascot = _current; var value = TaskbarCheck.IsChecked == true; var states = EditStates.ToArray();
        Commit(ChangeLabel(Loc.T("작업표시줄 뒤로 ") + (value ? Loc.T("켜기") : Loc.T("끄기"))), () =>
        {
            foreach (var state in states) mascot.Settings(state).HideBehindTaskbar = value;
        });
        StopTest(); RefreshInspector();
    }
    internal bool SaveScale(string id, MascotState? state, double scale)
    {
        var m = _store.Library.Find(id);
        if (m is null || !double.IsFinite(scale)) return false;
        scale = Math.Clamp(scale, .4, 3);
        var states = state is { } selectedState ? new[] { selectedState } : CustomizationManager.States;
        var result = Commit(DisplayName(m) + " · " + (state is { } st ? MainWindow.StateName(st) : Loc.T("전체")) + Loc.T(" · 크기 변경"), () =>
        {
            foreach (var s in states)
            {
                var settings = m.Settings(s);
                if (settings.Position != "custom-center")
                {
                    var center = OverlayWindow.PlacementCenter(LibraryStore.Placement(m, _manager.Configuration.Global, s));
                    settings.Position = "custom-center"; settings.MonitorDevice = null;
                    settings.CustomLeft = center.X; settings.CustomTop = center.Y;
                }
                settings.Scale = scale;
            }
            if (state is null)
            {
                if (m.Position != "custom-center")
                {
                    var center = OverlayWindow.PlacementCenter(LibraryStore.Placement(m, _manager.Configuration.Global));
                    m.Position = "custom-center"; m.MonitorDevice = null; m.CustomLeft = center.X; m.CustomTop = center.Y;
                }
                m.Scale = scale;
            }
        });
        RefreshInspector(); return result || states.All(s => m.Settings(s).Scale == scale);
    }
    internal bool SavePosition(string id, MascotState? state, double x, double y)
    {
        var m = _store.Library.Find(id);
        if (m is null) return false;
        var states = state is { } selectedState ? new[] { selectedState } : CustomizationManager.States;
        var result = Commit(DisplayName(m) + " · " + (state is { } st ? MainWindow.StateName(st) : Loc.T("전체")) + Loc.T(" · 위치 변경"), () =>
        {
            foreach (var s in states)
            {
                var settings = m.Settings(s); settings.Position = "custom-center"; settings.MonitorDevice = null; settings.CustomLeft = x; settings.CustomTop = y;
            }
            if (state is null) { m.Position = "custom-center"; m.MonitorDevice = null; m.CustomLeft = x; m.CustomTop = y; }
        });
        RefreshInspector(); return result || states.All(s => m.Settings(s).CustomLeft == x && m.Settings(s).CustomTop == y);
    }
    private void ApplyPanelRatio()
    {
        InstalledRow.Height = new GridLength(_store.Library.InstalledPanelRatio, GridUnitType.Star);
        SelectedRow.Height = new GridLength(1 - _store.Library.InstalledPanelRatio, GridUnitType.Star);
    }
    internal void SavePanelRatio(double ratio)
    { Commit(Loc.T("설치됨 / 선택됨 높이 비율 변경"), () => _store.Library.InstalledPanelRatio = Math.Clamp(ratio, .2, .8)); ApplyPanelRatio(); }
    private void Splitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
    { if (e.Canceled) { ApplyPanelRatio(); return; } SavePanelRatio(InstalledRow.ActualHeight / (InstalledRow.ActualHeight + SelectedRow.ActualHeight)); }
    private void Splitter_OnKeyUp(object sender, KeyEventArgs e)
    { if (e.Key is Key.Up or Key.Down) SavePanelRatio(InstalledRow.ActualHeight / (InstalledRow.ActualHeight + SelectedRow.ActualHeight)); }

    private void History_OnKeyDown(object sender, KeyEventArgs e)
    {
        // While typing, Ctrl+Z belongs to the text box's editing history.
        if (e.OriginalSource is System.Windows.DependencyObject source)
            for (var parent = source; parent is not null; parent = parent is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(parent) : LogicalTreeHelper.GetParent(parent))
                if (parent is NumericDragInput input && input.IsEditing) return;
        if (TryHistoryGesture(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }
    internal bool TryHistoryGesture(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Control) == 0 || (modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0 || _history is null) return false;
        var redo = key == Key.Y || key == Key.Z && (modifiers & ModifierKeys.Shift) != 0;
        if (!redo && key != Key.Z) return false;
        ReplayHistory(redo); return true;
    }
    internal bool ReplayHistory(bool redo)
    {
        VolumeInput.CancelEdit(); SpeedInput.CancelEdit(); DurationInput.CancelEdit();
        _editorDialog?.Close(); StopTest();
        try
        {
            var description = redo ? _history.Redo() : _history.Undo();
            _usage.Track(_store.Library.Selected);
            if (description is null) { Feedback.Text = redo ? Loc.T("다시 실행할 변경이 없습니다.") : Loc.T("실행 취소할 변경이 없습니다."); return false; }
            RebindAfterRestore(); LibraryChanged?.Invoke(this, EventArgs.Empty);
            Feedback.Text = (redo ? Loc.T("다시 실행 · ") : Loc.T("실행 취소 · ")) + description; return true;
        }
        catch (Exception ex) { Feedback.Text = Loc.T("설정 복원 실패: ") + ex.Message; return false; }
    }
    private void RebindAfterRestore()
    {
        var id = _current?.Id;
        _current = id is null ? null : _store.Library.Find(id);
        _current ??= _store.Library.Installed.FirstOrDefault();
        ApplyPanelRatio(); RefreshLists();
        if (_current is not null) Inspect(_current);
        else { StopPreview(); DetailName.Text = ""; }
        UpdateButtons();
    }
}
