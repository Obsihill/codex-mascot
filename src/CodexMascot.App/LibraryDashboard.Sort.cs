using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CodexMascot.App;

public partial class LibraryDashboard
{
    private LibraryUsage _usage = null!;
    private readonly DispatcherTimer _usageTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private sealed record SortChoice(string Name, string Key);
    private void InitializeSort()
    {
        _usage = new LibraryUsage(_store.UsagePath); _usage.Track(_store.Library.Selected);
        _loading = true;
        InstalledSort.DisplayMemberPath = nameof(SortChoice.Name); InstalledSort.SelectedValuePath = nameof(SortChoice.Key);
        InstalledSort.ItemsSource = new[] { new SortChoice("이름", "name"), new SortChoice("설치 날짜", "installed"), new SortChoice("선호", "preference") };
        InstalledSort.SelectedValue = _store.Library.InstalledSort; _loading = false;
        _usageTimer.Tick += (_, _) =>
        {
            SaveUsage();
            if (_store.Library.InstalledSort == "preference" && IsVisible && Mouse.LeftButton != MouseButtonState.Pressed && _editorDialog is null && !VolumeInput.IsEditing && !SpeedInput.IsEditing && !DurationInput.IsEditing)
                RefreshLists();
        };
        _usageTimer.Start();
        if (_usage.Warning is not null) Feedback.Text = _usage.Warning;
    }
    private void SaveUsage()
    {
        try { _usage?.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Feedback.Text = "선호 기록 저장 실패: " + e.Message; }
    }
    private void Sort_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _store is null || InstalledSort.SelectedItem is not SortChoice sort) return;
        Commit("설치됨 정렬 · " + sort.Name, () => _store.Library.InstalledSort = sort.Key);
        RefreshLists();
    }
}
