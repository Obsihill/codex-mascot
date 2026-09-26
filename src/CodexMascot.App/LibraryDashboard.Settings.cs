using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CodexMascot.Core;

namespace CodexMascot.App;

public partial class LibraryDashboard
{
    private LibraryHistory _history = null!;
    private MascotState? _activeState;
    private IEnumerable<MascotState> EditStates => _activeState is { } state ? new[] { state } : CustomizationManager.States;
    private string ScopeName => _activeState is { } state ? MainWindow.StateName(state) : "전체";
    private string ChangeLabel(string field) => DisplayName(_current!) + " · " + ScopeName + " · " + field;
    private static T? Common<T>(IEnumerable<T> values) where T : struct
    { var array = values.Distinct().Take(2).ToArray(); return array.Length == 1 ? array[0] : null; }

    private void RefreshInspector()
    {
        if (_current is null) return;
        _loading = true;
        DetailName.Text = DisplayName(_current);
        var states = EditStates.ToArray();
        var settings = states.Select(_current.Settings).ToArray();
        var volume = Common(settings.Select(s => s.Volume));
        var speed = Common(settings.Select(s => s.Speed));
        VolumeInput.Value = volume ?? settings[0].Volume; VolumeInput.IsMixed = volume is null;
        SpeedInput.Value = speed ?? settings[0].Speed; SpeedInput.IsMixed = speed is null;
        var imageStates = states.Where(s => !MascotMedia.IsVideo(_current.For(s).Image)).ToArray();
        DurationInput.Visibility = imageStates.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (imageStates.Length > 0)
        {
            var durations = imageStates.Select(s => LibraryStore.Playback(_current, s, _manager.Configuration.For(s)).ShowDurationMs).ToArray();
            var duration = Common(durations); DurationInput.Value = duration ?? durations[0]; DurationInput.IsMixed = duration is null;
        }
        SetToggle(LoopCheck, "반복", Common(settings.Select(s => s.Loop)));
        SetToggle(PlayCheck, "재생", Common(states.Select(s => _current.Events.Contains(MascotConfiguration.StateKey(s)))));
        StatePlaybackOptions.Visibility = _activeState is null ? Visibility.Collapsed : Visibility.Visible;
        ResetSettingsButton.Visibility = _activeState is null ? Visibility.Visible : Visibility.Collapsed;
        var samePosition = settings.Select(s => (s.Position, s.MonitorDevice, s.CustomLeft, s.CustomTop)).Distinct().Count() == 1;
        PositionButton.Content = samePosition ? "위치 선택" : "위치 선택 ?";
        _loading = false;
    }
    private static void SetToggle(CheckBox check, string name, bool? value)
    { check.IsChecked = value; check.Content = value is null ? name + " ?" : name; }
    private void StateSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        _history.BreakMerge(); StopTest();
        _activeState = _activeState is null ? ((PreviewChoice)PreviewState.SelectedItem).State : null;
        StateSettingsButton.Content = _activeState is null ? "상태별 설정" : "전체로";
        ScopeLabel.Text = _activeState is null ? "전체" : "상태별";
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
        catch (Exception ex) { RebindAfterRestore(); Feedback.Text = "저장 실패: " + ex.Message; return false; }
    }
    private void ResetSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || _activeState is not null) return;
        StopTest(); _history.BreakMerge();
        var m = _current;
        Commit(DisplayName(m) + " · 설정 초기화", () =>
        {
            var defaults = new LibraryMascot();
            m.Volume = defaults.Volume; m.Speed = defaults.Speed; m.Loop = defaults.Loop;
            m.Position = defaults.Position; m.CustomLeft = m.CustomTop = null; m.MonitorDevice = null;
            m.Events = defaults.Events;
            foreach (var state in CustomizationManager.States)
            { m.For(state).Playback = new MascotPlaybackSettings(); m.For(state).SoundEnabled = true; }
        });
        RefreshInspector(); ShowPreview();
    }
    private void Volume_OnChanged(object? sender, EventArgs e)
    {
        if (_loading || _current is null) return;
        var m = _current; var states = EditStates.ToArray(); var value = VolumeInput.Value;
        Commit(ChangeLabel("볼륨 변경"), () => { foreach (var state in states) m.Settings(state).Volume = value; if (_activeState is null) m.Volume = value; });
        RefreshInspector();
    }
    private void Speed_OnChanged(object? sender, EventArgs e)
    {
        if (_loading || _current is null) return;
        var m = _current; var states = EditStates.ToArray(); var value = SpeedInput.Value;
        Commit(ChangeLabel("재생 속도 변경"), () => { foreach (var state in states) m.Settings(state).Speed = value; if (_activeState is null) m.Speed = value; });
        PreviewVideo.SpeedRatio = m.Settings(PreviewEvent).Speed; RefreshInspector();
    }
    private void Duration_OnChanged(object? sender, EventArgs e)
    {
        if (_loading || _current is null) return;
        var m = _current; var states = EditStates.Where(s => !MascotMedia.IsVideo(m.For(s).Image)).ToArray();
        var value = (int)Math.Round(DurationInput.Value);
        Commit(ChangeLabel("이미지 재생시간 변경"), () => { foreach (var state in states) m.Settings(state).ImageDurationMs = value; });
        StopTest(); RefreshInspector();
    }
    private void Loop_OnClick(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null || _activeState is null) return;
        var m = _current; var value = LoopCheck.IsChecked == true; var states = EditStates.ToArray();
        Commit(ChangeLabel("반복 " + (value ? "켜기" : "끄기")), () => { foreach (var state in states) m.Settings(state).Loop = value; if (_activeState is null) m.Loop = value; });
        RefreshInspector(); ShowPreview();
    }
    private void Play_OnClick(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null || _activeState is null) return;
        var m = _current; var value = PlayCheck.IsChecked == true; var states = EditStates.ToArray();
        Commit(ChangeLabel("재생 " + (value ? "켜기" : "끄기")), () =>
        {
            foreach (var state in states)
            {
                var key = MascotConfiguration.StateKey(state);
                if (value) { if (!m.Events.Contains(key)) m.Events.Add(key); } else m.Events.Remove(key);
            }
        });
        StopTest(); RefreshInspector();
    }
    internal bool SavePosition(string id, MascotState? state, double x, double y)
    {
        var m = _store.Library.Find(id);
        if (m is null) return false;
        var states = state is { } selectedState ? new[] { selectedState } : CustomizationManager.States;
        var result = Commit(DisplayName(m) + " · " + (state is { } st ? MainWindow.StateName(st) : "전체") + " · 위치 변경", () =>
        {
            foreach (var s in states)
            {
                var settings = m.Settings(s); settings.Position = "custom"; settings.MonitorDevice = null; settings.CustomLeft = x; settings.CustomTop = y;
            }
            if (state is null) { m.Position = "custom"; m.MonitorDevice = null; m.CustomLeft = x; m.CustomTop = y; }
        });
        RefreshInspector(); return result || states.All(s => m.Settings(s).CustomLeft == x && m.Settings(s).CustomTop == y);
    }
    private void ApplyPanelRatio()
    {
        InstalledRow.Height = new GridLength(_store.Library.InstalledPanelRatio, GridUnitType.Star);
        SelectedRow.Height = new GridLength(1 - _store.Library.InstalledPanelRatio, GridUnitType.Star);
    }
    internal void SavePanelRatio(double ratio)
    { Commit("설치됨 / 선택됨 높이 비율 변경", () => _store.Library.InstalledPanelRatio = Math.Clamp(ratio, .2, .8)); ApplyPanelRatio(); }
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
            if (description is null) { Feedback.Text = redo ? "다시 실행할 변경이 없습니다." : "실행 취소할 변경이 없습니다."; return false; }
            RebindAfterRestore(); LibraryChanged?.Invoke(this, EventArgs.Empty);
            Feedback.Text = (redo ? "다시 실행 · " : "실행 취소 · ") + description; return true;
        }
        catch (Exception ex) { Feedback.Text = "설정 복원 실패: " + ex.Message; return false; }
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
