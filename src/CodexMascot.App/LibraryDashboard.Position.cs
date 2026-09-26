using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexMascot.Core;

namespace CodexMascot.App;

public partial class LibraryDashboard
{
    private void Position_OnClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        StopTest(); _history.BreakMerge();
        var m = _current; var scope = _activeState; var state = PreviewEvent;
        var global = LibraryStore.Placement(m, _manager.Configuration.Global, state);
        global.KeepCompletedVisibleUntilClick = false; global.ClickThrough = false;
        var bounds = OverlayWindow.PlacementBounds(global);
        var panel = new StackPanel { Margin = new Thickness(24) };
        var coordinates = new Grid();
        foreach (var width in new[] { new GridLength(1, GridUnitType.Star), new GridLength(18), new GridLength(1, GridUnitType.Star) })
            coordinates.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var x = new NumericDragInput { Name = "PositionX", Label = "X", Value = bounds.Left, Minimum = int.MinValue, Maximum = int.MaxValue };
        var y = new NumericDragInput { Name = "PositionY", Label = "Y", Value = bounds.Top, Minimum = int.MinValue, Maximum = int.MaxValue };
        Grid.SetColumn(y, 2);
        coordinates.Children.Add(x); coordinates.Children.Add(y);
        panel.Children.Add(coordinates);
        var window = Dialog("위치 선택", panel);
        var error = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(error);
        OverlayWindow? overlay = null;
        void UpdateCoordinates(Point point)
        {
            x.Value = point.X; y.Value = point.Y; error.Visibility = Visibility.Collapsed;
        }
        void PreviewCoordinates()
        {
            global.Position = "custom"; global.CustomLeft = x.Value; global.CustomTop = y.Value; global.MonitorDevice = null;
            overlay?.ApplyGlobal(global);
        }
        void CommitCoordinates()
        {
            if (!SavePosition(m.Id, scope, x.Value, y.Value))
            { error.Text = "위치를 저장하지 못했습니다."; error.Visibility = Visibility.Visible; }
            else error.Visibility = Visibility.Collapsed;
        }
        void FinishInputs()
        {
            foreach (var input in new[] { x, y })
                if (input.IsScrubbing || !input.TryCommitText()) input.CancelEdit();
        }
        x.ValuePreviewed += (_, _) => PreviewCoordinates(); y.ValuePreviewed += (_, _) => PreviewCoordinates();
        x.ValueCommitted += (_, _) => CommitCoordinates(); y.ValueCommitted += (_, _) => CommitCoordinates();
        window.Loaded += (_, _) =>
        {
            // Create after ShowDialog disables other windows, so both this dialog and
            // its owned placement preview remain interactive during the modal session.
            overlay = new OverlayWindow { PlacementMode = true, Owner = window };
            overlay.ApplyGlobal(global);
            overlay.PlacementDragStarted += (_, _) => FinishInputs();
            overlay.PlacementPositionChanged += (_, point) => UpdateCoordinates(point);
            overlay.PositionSaved += (_, _) =>
            {
                if (!SavePosition(m.Id, scope, global.CustomLeft ?? 0, global.CustomTop ?? 0))
                { error.Text = "위치를 저장하지 못했습니다."; error.Visibility = Visibility.Visible; }
            };
            var config = scope is null ? new StateConfiguration() : LibraryStore.Playback(m, state, _manager.Configuration.For(state));
            config.ShowDurationMs = 0; config.Volume = 0;
            var path = scope is null ? _store.CoverPath(m, _manager) : _store.MediaPath(m, _manager, state);
            overlay.ShowState(state, config, path, false);
        };
        window.Closing += (_, _) => { FinishInputs(); overlay?.CompletePlacementDrag(); };
        window.Closed += (_, _) => { overlay?.Close(); overlay = null; };
        try { window.ShowDialog(); }
        finally { overlay?.Close(); _history.BreakMerge(); }
    }
}
