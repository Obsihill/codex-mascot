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
    private readonly PencilBorder _surface;
    private readonly PencilIcon _icon = new() { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly TextBlock _display = new() { FontFamily = PencilFonts.Numbers, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false };
    private readonly TextBox _editor = new() { Visibility = Visibility.Collapsed, TextAlignment = TextAlignment.Right, Cursor = Cursors.IBeam,
        FontFamily = PencilFonts.Numbers, FontSize = 13, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center };
    private bool _pointer, _dragging, _editing, _startMixed, _invalid;
    private double _startX, _lastX, _startValue, _accumulator;
    private double _fillFraction = double.NaN;
    private Brush _fillBrush = PencilPalette.Surface;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericDragInput), new PropertyMetadata(0d, RenderChanged));
    public static readonly DependencyProperty IsMixedProperty = DependencyProperty.Register(nameof(IsMixed), typeof(bool), typeof(NumericDragInput), new PropertyMetadata(false, RenderChanged));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(NumericDragInput), new PropertyMetadata("", RenderChanged));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(PencilIconKind), typeof(NumericDragInput), new PropertyMetadata(PencilIconKind.None, RenderChanged));
    public static readonly DependencyProperty ShowValueFillProperty = DependencyProperty.Register(nameof(ShowValueFill), typeof(bool), typeof(NumericDragInput), new PropertyMetadata(false, RenderChanged));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericDragInput), new PropertyMetadata(double.MinValue, RenderChanged));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericDragInput), new PropertyMetadata(double.MaxValue, RenderChanged));
    public PencilIconKind Icon { get => (PencilIconKind)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool IsMixed { get => (bool)GetValue(IsMixedProperty); set => SetValue(IsMixedProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool ShowValueFill { get => (bool)GetValue(ShowValueFillProperty); set => SetValue(ShowValueFillProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double DisplayScale { get; set; } = 1;
    public int DecimalPlaces { get; set; }
    // Expressed in display units, e.g. 0.5 percent or 1 desktop pixel per DIP.
    public double UnitsPerPixel { get; set; } = 1;
    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(NumericDragInput), new PropertyMetadata("", RenderChanged));
    public string Suffix { get => (string)GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }
    public event EventHandler? ValuePreviewed;
    public event EventHandler? ValueCommitted;
    internal bool IsEditing => _editing;
    internal bool IsScrubbing => _pointer;
    internal string DisplayText => _display.Text;
    internal TextBox Editor => _editor;
    internal double ValueFillFraction => _fillFraction;

    public NumericDragInput()
    {
        Focusable = true; Cursor = Cursors.SizeWE; MinHeight = 40;
        var ink = PencilPalette.Ink;
        _label.Foreground = _display.Foreground = _editor.Foreground = _icon.Foreground = ink; _editor.Margin = new Thickness(0);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _label.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(_display, 1); Grid.SetColumn(_editor, 1);
        var labelPanel = new StackPanel { Orientation = Orientation.Horizontal };
        labelPanel.Children.Add(_icon); labelPanel.Children.Add(_label);
        grid.Children.Add(labelPanel); grid.Children.Add(_display); grid.Children.Add(_editor);
        _surface = new PencilBorder { Child = grid, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(1) };
        Content = _surface;
        _editor.LostKeyboardFocus += (_, _) => { if (_editing && !TryCommitText()) CancelEdit(); };
        MouseEnter += (_, _) => Render(); MouseLeave += (_, _) => Render();
        IsKeyboardFocusWithinChanged += (_, _) => Render();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CancelEdit(); Opacity = IsEnabled ? 1 : .45; };
        Unloaded += (_, _) => CancelEdit(); Loaded += (_, _) => Render();
        Render();
        System.ComponentModel.PropertyChangedEventManager.AddHandler(PencilPalette.Current, ThemeChanged, string.Empty);
    }
    private void ThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _fillFraction = double.NaN;
        _label.Foreground = _display.Foreground = _editor.Foreground = _icon.Foreground = PencilPalette.Ink;
        Render();
    }
    private static void RenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((NumericDragInput)d).Render();
    private string Number(double value) => (value * DisplayScale).ToString("F" + DecimalPlaces, CultureInfo.CurrentCulture);
    private void Render()
    {
        if (_surface is null) return;
        _label.Text = Label; _display.Text = IsMixed ? "??" : Number(Value) + Suffix;
        _icon.Kind = Icon; _icon.Visibility = Icon == PencilIconKind.None ? Visibility.Collapsed : Visibility.Visible;
        _surface.Background = ValueBackground();
        _surface.BorderBrush = _invalid ? PencilPalette.Danger : IsEnabled && IsMouseOver ? PencilPalette.Emphasis : IsKeyboardFocusWithin ? PencilPalette.Accent : PencilPalette.Line;
        ToolTip = _invalid ? Loc.T("올바른 숫자를 입력하세요.") : null;
        System.Windows.Automation.AutomationProperties.SetName(this, Label);
    }
    private Brush ValueBackground()
    {
        var range = Maximum - Minimum;
        var fraction = ShowValueFill && !IsMixed && double.IsFinite(Value) && double.IsFinite(range) && range > 0
            ? Math.Clamp((Value - Minimum) / range, 0, 1) : 0;
        // Reuse the brush on hover/focus. Only the outline should change then.
        if (_fillFraction == fraction) return _fillBrush;
        _fillFraction = fraction;
        if (fraction == 0) return _fillBrush = PencilPalette.Surface;
        if (fraction == 1) return _fillBrush = PencilPalette.ValueFill;
        // Coincident stops make a crisp rectangle, not a fading gradient.
        // Relative coordinates automatically track the field's width on resize.
        var brush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        brush.GradientStops.Add(new GradientStop(PencilPalette.ValueFill.Color, 0));
        brush.GradientStops.Add(new GradientStop(PencilPalette.ValueFill.Color, fraction));
        brush.GradientStops.Add(new GradientStop(PencilPalette.Surface.Color, fraction));
        brush.GradientStops.Add(new GradientStop(PencilPalette.Surface.Color, 1));
        brush.Freeze();
        return _fillBrush = brush;
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
