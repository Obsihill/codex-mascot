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
        var center = OverlayWindow.PlacementCenter(global);
        // Translate legacy top-left/preset positions for this preview only. Merely
        // opening the dialog must not rewrite saved positions or create history.
        global.Position = "custom-center"; global.CustomLeft = center.X; global.CustomTop = center.Y;
        var panel = new StackPanel { Margin = new Thickness(24) };
        var coordinates = new Grid();
        foreach (var width in new[] { new GridLength(1, GridUnitType.Star), new GridLength(18), new GridLength(1, GridUnitType.Star) })
            coordinates.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var x = new NumericDragInput { Name = "PositionX", Label = "X", Icon = PencilIconKind.Position, Value = center.X, Minimum = int.MinValue, Maximum = int.MaxValue };
        var y = new NumericDragInput { Name = "PositionY", Label = "Y", Icon = PencilIconKind.Position, Value = center.Y, Minimum = int.MinValue, Maximum = int.MaxValue };
        Grid.SetColumn(y, 2);
        coordinates.Children.Add(x); coordinates.Children.Add(y);
        panel.Children.Add(coordinates);
        var scale = new NumericDragInput
        {
            Name = "PositionScale", Label = Loc.T("크기"), Value = global.Scale,
            Minimum = .4, Maximum = 3, DisplayScale = 100, UnitsPerPixel = 1, Suffix = "%",
            ShowValueFill = true, Margin = new Thickness(0, 14, 0, 0),
            IsMixed = scope is null && CustomizationManager.States.Select(s => m.Settings(s).Scale ?? _manager.Configuration.Global.Scale).Distinct().Count() > 1
        };
        panel.Children.Add(scale);
        var window = Dialog(Loc.T("위치 크기 변경"), panel);
        var error = new TextBlock { Foreground = PencilPalette.Danger, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(error);
        OverlayWindow? overlay = null;
        void UpdateCoordinates(Point point)
        {
            x.Value = point.X; y.Value = point.Y; error.Visibility = Visibility.Collapsed;
        }
        void PreviewCoordinates()
        {
            global.Position = "custom-center"; global.CustomLeft = x.Value; global.CustomTop = y.Value; global.MonitorDevice = null;
            overlay?.ApplyGlobal(global);
        }
        void CommitCoordinates()
        {
            if (!SavePosition(m.Id, scope, x.Value, y.Value))
            { error.Text = Loc.T("위치를 저장하지 못했습니다."); error.Visibility = Visibility.Visible; }
            else error.Visibility = Visibility.Collapsed;
        }
        void FinishInputs()
        {
            foreach (var input in new[] { x, y, scale })
                if (input.IsScrubbing || !input.TryCommitText()) input.CancelEdit();
        }
        scale.ValuePreviewed += (_, _) =>
        {
            global.Scale = scale.Value;
            overlay?.ApplyGlobal(global);
        };
        scale.ValueCommitted += (_, _) =>
        {
            if (!SaveScale(m.Id, scope, scale.Value))
            { error.Text = Loc.T("크기를 저장하지 못했습니다."); error.Visibility = Visibility.Visible; }
            else error.Visibility = Visibility.Collapsed;
        };
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
                { error.Text = Loc.T("위치를 저장하지 못했습니다."); error.Visibility = Visibility.Visible; }
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
