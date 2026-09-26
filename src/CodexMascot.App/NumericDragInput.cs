using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexMascot.App;

// A whole-row scrub field. Value is previewed during a gesture but committed only
// on release, so a drag is one undo step and Escape never modifies persisted data.
public sealed class NumericDragInput : UserControl
{
    private readonly Border _surface;
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly TextBlock _display = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false };
    private readonly TextBox _editor = new() { Visibility = Visibility.Collapsed, TextAlignment = TextAlignment.Right, Cursor = Cursors.IBeam,
        Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center };
    private bool _pointer, _dragging, _editing, _startMixed, _invalid;
    private double _startX, _lastX, _startValue, _accumulator;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericDragInput), new PropertyMetadata(0d, RenderChanged));
    public static readonly DependencyProperty IsMixedProperty = DependencyProperty.Register(nameof(IsMixed), typeof(bool), typeof(NumericDragInput), new PropertyMetadata(false, RenderChanged));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(NumericDragInput), new PropertyMetadata("", RenderChanged));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool IsMixed { get => (bool)GetValue(IsMixedProperty); set => SetValue(IsMixedProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Minimum { get; set; } = double.MinValue;
    public double Maximum { get; set; } = double.MaxValue;
    public double DisplayScale { get; set; } = 1;
    public int DecimalPlaces { get; set; }
    // Expressed in display units, e.g. 0.5 percent or 1 desktop pixel per DIP.
    public double UnitsPerPixel { get; set; } = 1;
    public string Suffix { get; set; } = "";
    public event EventHandler? ValuePreviewed;
    public event EventHandler? ValueCommitted;
    internal bool IsEditing => _editing;
    internal bool IsScrubbing => _pointer;
    internal string DisplayText => _display.Text;
    internal TextBox Editor => _editor;

    public NumericDragInput()
    {
        Focusable = true; Cursor = Cursors.SizeWE; MinHeight = 40;
        var ink = new SolidColorBrush(Color.FromRgb(38, 50, 64));
        _label.Foreground = _display.Foreground = _editor.Foreground = ink; _editor.Margin = new Thickness(0);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _label.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(_display, 1); Grid.SetColumn(_editor, 1);
        grid.Children.Add(_label); grid.Children.Add(_display); grid.Children.Add(_editor);
        _surface = new Border { Child = grid, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
        Content = _surface;
        _editor.LostKeyboardFocus += (_, _) => { if (_editing && !TryCommitText()) CancelEdit(); };
        MouseEnter += (_, _) => Render(); MouseLeave += (_, _) => Render();
        IsKeyboardFocusWithinChanged += (_, _) => Render();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CancelEdit(); Opacity = IsEnabled ? 1 : .45; };
        Unloaded += (_, _) => CancelEdit(); Loaded += (_, _) => Render();
        Render();
    }
    private static void RenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((NumericDragInput)d).Render();
    private string Number(double value) => (value * DisplayScale).ToString("F" + DecimalPlaces, CultureInfo.CurrentCulture);
    private void Render()
    {
        if (_surface is null) return;
        _label.Text = Label; _display.Text = IsMixed ? "?" : Number(Value) + Suffix;
        _surface.Background = new SolidColorBrush(IsMouseOver && !_editing ? Color.FromRgb(239, 248, 245) : Colors.White);
        _surface.BorderBrush = _invalid ? Brushes.Firebrick : new SolidColorBrush(IsKeyboardFocusWithin || IsMouseOver ? Color.FromRgb(50, 140, 121) : Color.FromRgb(217, 223, 229));
        ToolTip = _invalid ? "올바른 숫자를 입력하세요." : null;
        System.Windows.Automation.AutomationProperties.SetName(this, Label);
    }
    private double Normalize(double value) => Math.Clamp(Math.Round(Math.Clamp(value, Minimum, Maximum) * DisplayScale, DecimalPlaces) / DisplayScale, Minimum, Maximum);
    private void RememberStart() { _startValue = Value; _startMixed = IsMixed; _invalid = false; }
    internal void BeginPointer(double x)
    {
        if (!IsEnabled) return;
        if (_editing && !TryCommitText()) return;
        RememberStart(); _pointer = true; _dragging = false; _startX = _lastX = x; _accumulator = Value * DisplayScale;
    }
    internal void MovePointer(double x, bool fine)
    {
        if (!_pointer) return;
        if (!_dragging && Math.Abs(x - _startX) < SystemParameters.MinimumHorizontalDragDistance) return;
        _dragging = true;
        _accumulator = Math.Clamp(_accumulator + (x - _lastX) * UnitsPerPixel * (fine ? .1 : 1), Minimum * DisplayScale, Maximum * DisplayScale);
        _lastX = x;
        Value = Normalize(_accumulator / DisplayScale); IsMixed = false;
        ValuePreviewed?.Invoke(this, EventArgs.Empty);
    }
    internal void EndPointer()
    {
        if (!_pointer) return;
        var wasDragging = _dragging; _pointer = _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (wasDragging) { if (_startMixed || Value != _startValue) ValueCommitted?.Invoke(this, EventArgs.Empty); }
        else BeginTextEdit();
    }
    internal void BeginTextEdit()
    {
        if (!IsEnabled || _editing) return;
        RememberStart(); _editing = true;
        _editor.Text = IsMixed ? "" : Number(Value);
        _display.Visibility = Visibility.Collapsed; _editor.Visibility = Visibility.Visible;
        _editor.Focus(); _editor.SelectAll(); Render();
    }
    internal bool TryCommitText()
    {
        if (!_editing) return true;
        var text = _editor.Text.Trim();
        if (!string.IsNullOrEmpty(Suffix) && text.EndsWith(Suffix, StringComparison.Ordinal)) text = text[..^Suffix.Length].Trim();
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
             !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) || !double.IsFinite(value))
        { _invalid = true; Render(); return false; }
        return CommitValue(value / DisplayScale);
    }
    internal bool CommitValue(double value)
    {
        if (!IsEnabled || !double.IsFinite(value)) return false;
        var before = _editing ? _startValue : Value; var mixed = _editing ? _startMixed : IsMixed;
        EndEditor(); Value = Normalize(value); IsMixed = false;
        ValuePreviewed?.Invoke(this, EventArgs.Empty);
        if (mixed || Value != before) ValueCommitted?.Invoke(this, EventArgs.Empty);
        return true;
    }
    private void EndEditor()
    {
        _editing = false; _invalid = false;
        _editor.Visibility = Visibility.Collapsed; _display.Visibility = Visibility.Visible; Render();
    }
    internal void CancelEdit()
    {
        if (!_pointer && !_editing) return;
        _pointer = _dragging = false; EndEditor();
        if (IsMouseCaptured) ReleaseMouseCapture();
        Value = _startValue; IsMixed = _startMixed; ValuePreviewed?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (_editing && _editor.IsMouseOver) return;
        if (_editing && !TryCommitText()) { e.Handled = true; return; }
        Focus(); BeginPointer(e.GetPosition(this).X); CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pointer && IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        { MovePointer(e.GetPosition(this).X, (Keyboard.Modifiers & ModifierKeys.Shift) != 0); e.Handled = true; }
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_pointer) { EndPointer(); e.Handled = true; }
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); if (_pointer) CancelEdit(); }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && (_pointer || _editing)) { CancelEdit(); Focus(); e.Handled = true; }
        else if (e.Key == Key.Enter)
        { if (_editing) { if (TryCommitText()) Focus(); } else BeginTextEdit(); e.Handled = true; }
        else if (!_editing && !_pointer && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var direction = e.Key is Key.Left or Key.Down ? -1 : 1;
            var step = Math.Max(Math.Pow(10, -DecimalPlaces), UnitsPerPixel * 2 * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? .1 : 1));
            CommitValue(Value + direction * step / DisplayScale); e.Handled = true;
        }
    }
}
