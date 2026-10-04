using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexMascot.App;

// Keyboard submission is deliberately separate from a mouse click: let the
// pressed state render briefly before the dialog commits and closes.
internal sealed class PencilEnterFeedback : IDisposable
{
    private readonly Window _window;
    private readonly Button _button;
    private readonly Func<bool>? _canSubmit;
    private DispatcherTimer? _timer;
    private Brush? _background;
    private Brush? _borderBrush;
    private Transform? _transform;
    private Point _transformOrigin;
    private bool _disposed;

    private PencilEnterFeedback(Window window, Button button, Func<bool>? canSubmit)
    {
        _window = window; _button = button; _canSubmit = canSubmit;
        window.PreviewKeyDown += OnPreviewKeyDown;
        window.Closed += OnClosed;
    }

    internal static PencilEnterFeedback Attach(Window window, Button button, Func<bool>? canSubmit = null)
        => new(window, button, canSubmit);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Return or Key.Enter) || Keyboard.Modifiers != ModifierKeys.None) return;
        if (Keyboard.FocusedElement is Button focused && !ReferenceEquals(focused, _button)) return;
        if (_canSubmit?.Invoke() == false) return;
        e.Handled = true;
        Play();
    }

    internal void Play()
    {
        if (_disposed || _timer is not null || !_button.IsEnabled || !_window.IsVisible) return;
        _background = _button.Background; _borderBrush = _button.BorderBrush;
        _transform = _button.RenderTransform; _transformOrigin = _button.RenderTransformOrigin;
        _button.SetCurrentValue(Control.BackgroundProperty, PencilPalette.AccentButton);
        _button.SetCurrentValue(Control.BorderBrushProperty, PencilPalette.Emphasis);
        _button.RenderTransformOrigin = new Point(.5, .5);
        _button.RenderTransform = new ScaleTransform(.96, .96);
        _timer = new DispatcherTimer(DispatcherPriority.Normal, _window.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) =>
        {
            Restore();
            if (_window.IsVisible && _button.IsEnabled)
                _button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        _timer.Start();
    }

    private void Restore()
    {
        if (_timer is null) return;
        _timer.Stop(); _timer = null;
        _button.SetCurrentValue(Control.BackgroundProperty, _background);
        _button.SetCurrentValue(Control.BorderBrushProperty, _borderBrush);
        _button.RenderTransform = _transform;
        _button.RenderTransformOrigin = _transformOrigin;
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Restore();
        _window.PreviewKeyDown -= OnPreviewKeyDown;
        _window.Closed -= OnClosed;
    }
}
