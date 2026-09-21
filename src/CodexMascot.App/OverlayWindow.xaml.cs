using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CodexMascot.Core;
using Forms = System.Windows.Forms;
namespace CodexMascot.App;

public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private IReadOnlyList<MascotFrame> _frames = Array.Empty<MascotFrame>();
    private GlobalConfiguration _global = new();
    private CancellationTokenSource? _display;
    private int _frame, _generation;
    private bool _loop, _dragged, _closed, _pressed, _hiding;
    private bool _keepCompletedVisible = true;
    private MascotState _state;
    private StateConfiguration? _stateConfiguration;
    private Point _press;
    public event EventHandler? Clicked;
    public event EventHandler? PositionSaved;
    public string? LastImageError { get; private set; }
    internal MascotState DisplayedState => _state;
    internal bool IsPresenting => IsVisible && !_hiding;
    public bool PlacementMode { get; set; }
    private bool HoldUntilClick => _state == MascotState.NeedsAttention ||
        (_state == MascotState.Completed && _global.KeepCompletedVisibleUntilClick);
    private bool ClickThrough => _global.ClickThrough && !PlacementMode && !HoldUntilClick;
    public OverlayWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) =>
        {
            if (_frames.Count == 0) return;
            if (_frame == _frames.Count - 1 && !_loop) { _timer.Stop(); return; }
            _frame = (_frame + 1) % _frames.Count;
            MascotImage.Source = _frames[_frame].Bitmap;
            _timer.Interval = TimeSpan.FromMilliseconds(_frames[_frame].DelayMs);
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (ClickThrough || IsButton(e.OriginalSource)) return;
            _press = e.GetPosition(this); _dragged = false; _pressed = true; CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (!_pressed || !IsMouseCaptured || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _press.X) + Math.Abs(p.Y - _press.Y) < 8) return;
            _dragged = true; _pressed = false; ReleaseMouseCapture();
            try { DragMove(); } catch (InvalidOperationException) { }
            var handle = new WindowInteropHelper(this).Handle;
            GetWindowRect(handle, out var rect);
            _global.Position = "custom"; _global.CustomLeft = rect.Left; _global.CustomTop = rect.Top;
            _global.MonitorDevice = Forms.Screen.FromHandle(handle).DeviceName;
            PositionSaved?.Invoke(this, EventArgs.Empty);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            var clicked = _pressed && !_dragged && !ClickThrough && !PlacementMode && !IsButton(e.OriginalSource);
            _pressed = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            _dragged = false;
            if (clicked) { HideMascot(); Clicked?.Invoke(this, EventArgs.Empty); }
        };
        Closed += (_, _) => { _closed = true; _timer.Stop(); _display?.Cancel(); _display?.Dispose(); };
    }
    public void ApplyGlobal(GlobalConfiguration config)
    {
        var lifetimeChanged = _keepCompletedVisible != config.KeepCompletedVisibleUntilClick;
        _keepCompletedVisible = config.KeepCompletedVisibleUntilClick;
        _global = config;
        Width = 260 * Math.Clamp(config.Scale, .4, 3);
        Height = 280 * Math.Clamp(config.Scale, .4, 3);
        Topmost = config.AlwaysOnTop;
        if (IsVisible) { PositionWindow(); ApplyStyles(); }
        if (IsVisible && lifetimeChanged && _state == MascotState.Completed)
        {
            _hiding = false; Fade(1, 150); ScheduleAutoHide();
        }
    }
    private static bool IsButton(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase) return true;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
    public string DescribePosition()
    {
        var name = _global.Position switch { "top-left" => "왼쪽 위", "top-right" => "오른쪽 위", "bottom-left" => "왼쪽 아래", "center" => "중앙", "custom" => "직접 지정한 위치", _ => "오른쪽 아래" };
        return GetScreen().DeviceName + " · " + name;
    }
    public void ShowState(MascotState state, StateConfiguration config, string? imagePath)
    {
        if (_closed) return;
        _state = state; _stateConfiguration = config; _hiding = false;
        _timer.Stop(); _frames = Array.Empty<MascotFrame>(); _frame = 0; _loop = config.Loop;
        LastImageError = null;
        try
        {
            if (imagePath is null) throw new FileNotFoundException("이미지 파일이 없습니다.");
            _frames = MascotImageLoader.Load(imagePath, config);
            MascotImage.Source = _frames[0].Bitmap;
            MascotImage.Visibility = Visibility.Visible; FallbackCard.Visibility = Visibility.Collapsed;
            if (_frames.Count > 1) { _timer.Interval = TimeSpan.FromMilliseconds(_frames[0].DelayMs); _timer.Start(); }
        }
        catch (Exception ex)
        {
            LastImageError = ex.Message;
            MascotImage.Visibility = Visibility.Collapsed; FallbackCard.Visibility = Visibility.Visible;
            FallbackEmoji.Text = state switch { MascotState.Running => "⚙", MascotState.Completed => "★", MascotState.Failed => "!", MascotState.NeedsAttention => "?", MascotState.Interrupted => "Ⅱ", _ => "●" };
            FallbackText.Text = MainWindow.StateName(state);
        }
        if (!IsVisible) { Opacity = 0; Show(); }
        PositionWindow(); ApplyStyles(); Fade(1, 150);
        ScheduleAutoHide();
    }
    private void ScheduleAutoHide()
    {
        _display?.Cancel(); _display?.Dispose(); _display = new();
        if (!_hiding && !HoldUntilClick && _stateConfiguration?.ShowDurationMs > 0)
            _ = HideLater(Math.Clamp(_stateConfiguration.ShowDurationMs, 200, 600000), _display.Token);
    }
    private async Task HideLater(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); if (!ct.IsCancellationRequested) HideMascot(); }
        catch (OperationCanceledException) { }
    }
    public void HideMascot()
    {
        if (_closed) return;
        _hiding = true; _display?.Cancel(); Fade(0, 250);
    }
    private void Fade(double opacity, int ms)
    {
        var generation = ++_generation;
        if (!IsVisible) return;
        var fade = new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(ms));
        fade.Completed += (_, _) => { if (!_closed && generation == _generation && opacity == 0) { Hide(); _timer.Stop(); } };
        BeginAnimation(OpacityProperty, fade);
    }
    private Forms.Screen GetScreen() => Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _global.MonitorDevice)
        ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    private void PositionWindow()
    {
        var area = GetScreen().WorkingArea;
        var handle = new WindowInteropHelper(this).Handle;
        uint dpi = 96;
        var point = new NativePoint { X = area.Left + 1, Y = area.Top + 1 };
        try { if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out var dx, out _) == 0) dpi = dx; }
        catch (DllNotFoundException) { }
        var w = Math.Min(area.Width, (int)(Width * dpi / 96));
        var h = Math.Min(area.Height, (int)(Height * dpi / 96));
        var margin = (int)(24 * dpi / 96);
        var (x, y) = _global.Position switch
        {
            "top-left" => (area.Left + margin, area.Top + margin),
            "top-right" => (area.Right - w - margin, area.Top + margin),
            "bottom-left" => (area.Left + margin, area.Bottom - h - margin),
            "center" => (area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2),
            "custom" => ((int)(_global.CustomLeft ?? area.Left), (int)(_global.CustomTop ?? area.Top)),
            _ => (area.Right - w - margin, area.Bottom - h - margin)
        };
        x = Math.Clamp(x, area.Left, area.Right - w); y = Math.Clamp(y, area.Top, area.Bottom - h);
        SetWindowPos(handle, IntPtr.Zero, x, y, w, h, 0x0010 | 0x0004);
    }
    private void ApplyStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, -20) | 0x08000000 | 0x80;
        SetWindowLong(handle, -20, ClickThrough ? style | 0x20 : style & ~0x20);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
}
