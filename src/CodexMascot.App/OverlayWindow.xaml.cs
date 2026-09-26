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
    private bool _videoSound;
    private bool _boostVideo, _videoReady;
    private readonly SoundPlayerService _videoAudio = new();
    private MascotState _state;
    private StateConfiguration? _stateConfiguration;
    private Point _press;
    private bool _placementDragging;
    private Point _placementStart;
    public event EventHandler? Clicked;
    public event EventHandler? PositionSaved;
    public event EventHandler? PlacementDragStarted;
    public event EventHandler<Point>? PlacementPositionChanged;
    public string? LastImageError { get; private set; }
    public string? LastAudioError { get; private set; }
    public event EventHandler<string>? AudioError;
    internal bool HasBoostAudio => _videoAudio.IsPrepared;
    internal MascotState DisplayedState => _state;
    internal bool IsPresenting => IsVisible && !_hiding;
    public bool PlacementMode { get; set; }
    private bool HoldUntilClick => _state == MascotState.NeedsAttention ||
        (_state == MascotState.Completed && _global.KeepCompletedVisibleUntilClick);
    private bool ClickThrough => _global.ClickThrough && !PlacementMode && !HoldUntilClick;
    public OverlayWindow()
    {
        InitializeComponent();
        LocationChanged += (_, _) =>
        {
            if (_placementDragging && PlacementMode && !_closed)
                PlacementPositionChanged?.Invoke(this, DesktopPosition);
        };
        _videoAudio.Feedback += (_, message) =>
        {
            if (message.Contains("재생 시작")) return;
            LastAudioError = message; AudioError?.Invoke(this, message);
        };
        MascotVideo.MediaOpened += (_, _) =>
        {
            if (_closed || _hiding || MascotVideo.Source is null) return;
            _videoReady = true;
            if (_boostVideo) { MascotVideo.Pause(); MascotVideo.Position = TimeSpan.Zero; StartVideoAudio(); MascotVideo.Play(); }
        };
        MascotVideo.MediaEnded += (_, _) =>
        {
            _videoAudio.Stop();
            if (_loop && !_hiding && !_closed)
            { MascotVideo.Position = TimeSpan.Zero; StartVideoAudio(); MascotVideo.Play(); }
        };
        MascotVideo.MediaFailed += (_, e) =>
        {
            if (_closed || _hiding || MascotVideo.Source is null) return;
            ShowMediaError("영상을 재생할 수 없습니다. 파일 또는 Windows 코덱을 확인하세요: " + e.ErrorException.Message);
        };
        _timer.Tick += (_, _) =>
        {
            if (_frames.Count == 0) return;
            if (_frame == _frames.Count - 1 && !_loop) { _timer.Stop(); return; }
            _frame = (_frame + 1) % _frames.Count;
            MascotImage.Source = _frames[_frame].Bitmap;
            _timer.Interval = FrameDelay(_frames[_frame].DelayMs);
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
            if (!BeginPlacementDrag()) return;
            _pressed = false; ReleaseMouseCapture();
            try { DragMove(); } catch (InvalidOperationException) { }
            finally { CompletePlacementDrag(); }
        };
        MouseLeftButtonUp += (_, e) =>
        {
            var clicked = _pressed && !_dragged && !ClickThrough && !PlacementMode && !IsButton(e.OriginalSource);
            _pressed = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            _dragged = false;
            if (clicked) { HideMascot(); Clicked?.Invoke(this, EventArgs.Empty); }
        };
        Closed += (_, _) => { _closed = true; _videoAudio.Dispose(); MascotVideo.Close(); _timer.Stop(); _display?.Cancel(); _display?.Dispose(); };
    }
    public void ApplyGlobal(GlobalConfiguration config)
    {
        var lifetimeChanged = _keepCompletedVisible != config.KeepCompletedVisibleUntilClick;
        _keepCompletedVisible = config.KeepCompletedVisibleUntilClick;
        _global = config;
        UpdateVideoVolume();
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
    public void ShowState(MascotState state, StateConfiguration config, string? imagePath, bool videoSound = true)
    {
        if (_closed) return;
        _state = state; _stateConfiguration = config; _hiding = false;
        _videoAudio.Stop(); _videoReady = false; _boostVideo = config.Volume > 1;
        MascotVideo.Close(); MascotVideo.Source = null; MascotVideo.Visibility = Visibility.Collapsed;
        _videoSound = videoSound && !PlacementMode;
        _timer.Stop(); _frames = Array.Empty<MascotFrame>(); _frame = 0; _loop = config.Loop;
        LastImageError = LastAudioError = null;
        try
        {
            if (imagePath is null) throw new FileNotFoundException("이미지 파일이 없습니다.");
            if (imagePath.StartsWith("builtin:", StringComparison.Ordinal))
            {
                MascotImage.Source = DemoMascotArtwork.Create(imagePath[8..]);
                MascotImage.Visibility = Visibility.Visible; FallbackCard.Visibility = Visibility.Collapsed;
            }
            else if (MascotMedia.IsVideo(imagePath))
            {
                if (!File.Exists(imagePath)) throw new FileNotFoundException("영상 파일이 없습니다.");
                MascotImage.Visibility = Visibility.Collapsed; FallbackCard.Visibility = Visibility.Collapsed;
                MascotVideo.Visibility = Visibility.Visible;
                UpdateVideoVolume();
                MascotVideo.Source = new Uri(Path.GetFullPath(imagePath));
                MascotVideo.SpeedRatio = Math.Clamp(config.PlaybackSpeed, .25, 3);
                MascotVideo.Play();
            }
            else
            {
            _frames = MascotImageLoader.Load(imagePath, config);
            MascotImage.Source = _frames[0].Bitmap;
            MascotImage.Visibility = Visibility.Visible; FallbackCard.Visibility = Visibility.Collapsed;
            if (_frames.Count > 1) { _timer.Interval = FrameDelay(_frames[0].DelayMs); _timer.Start(); }
            }
        }
        catch (Exception ex)
        {
            ShowMediaError(ex.Message);
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
        _hiding = true; _videoReady = false; _videoAudio.Stop(); MascotVideo.Stop(); MascotVideo.Close(); MascotVideo.Source = null; _display?.Cancel(); Fade(0, 250);
    }
    internal bool BeginPlacementDrag()
    {
        // A drag is not a click, even when movement is locked in normal playback.
        _dragged = true;
        if (!PlacementMode || _closed) return false;
        PlacementDragStarted?.Invoke(this, EventArgs.Empty);
        _placementStart = DesktopPosition; _placementDragging = true;
        return true;
    }
    internal Point DesktopPosition
    {
        get
        {
            var handle = new WindowInteropHelper(this).Handle;
            return handle != IntPtr.Zero && GetWindowRect(handle, out var rect) ? new Point(rect.Left, rect.Top) : new Point();
        }
    }
    internal void CompletePlacementDrag()
    {
        if (!_placementDragging) return;
        _placementDragging = false;
        if (_closed || !PlacementMode) return;
        var point = DesktopPosition;
        PlacementPositionChanged?.Invoke(this, point);
        // Native DragMove restores the start position on Escape. Do not persist it
        // as a new custom position or add a history entry in that case.
        if (point == _placementStart) return;
        _global.Position = "custom"; _global.CustomLeft = point.X; _global.CustomTop = point.Y;
        _global.MonitorDevice = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).DeviceName;
        PositionSaved?.Invoke(this, EventArgs.Empty);
    }
    private TimeSpan FrameDelay(int ms) => TimeSpan.FromMilliseconds(Math.Max(10, ms / Math.Clamp(_stateConfiguration?.PlaybackSpeed ?? 1, .25, 3)));
    private void UpdateVideoVolume()
    {
        MascotVideo.IsMuted = _boostVideo || !_videoSound || !_global.SoundEnabled;
        MascotVideo.Volume = Math.Clamp(_global.MasterVolume * (_stateConfiguration?.Volume ?? 1), 0, 1);
        _videoAudio.SetVolume(VideoGain);
    }
    private double VideoGain => _videoSound && _global.SoundEnabled ? _global.MasterVolume * (_stateConfiguration?.Volume ?? 1) : 0;
    private void StartVideoAudio()
    {
        if (!_boostVideo || !_videoReady || !MascotVideo.HasAudio || MascotVideo.Source is null || _hiding || _closed) return;
        _videoAudio.Play(MascotVideo.Source.LocalPath, VideoGain, _stateConfiguration?.PlaybackSpeed ?? 1);
    }
    private void ShowMediaError(string message)
    {
        LastImageError = message;
        _videoReady = false; _videoAudio.Stop();
        MascotVideo.Close(); MascotVideo.Source = null; MascotVideo.Visibility = Visibility.Collapsed;
        MascotImage.Visibility = Visibility.Collapsed; FallbackCard.Visibility = Visibility.Visible;
        FallbackEmoji.Text = _state switch { MascotState.Running => "⚙", MascotState.Completed => "★", MascotState.Failed => "!", MascotState.NeedsAttention => "?", MascotState.Interrupted => "Ⅱ", _ => "●" };
        FallbackText.Text = MainWindow.StateName(_state);
    }
    private void Fade(double opacity, int ms)
    {
        var generation = ++_generation;
        if (!IsVisible) return;
        var fade = new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(ms));
        fade.Completed += (_, _) => { if (!_closed && generation == _generation && opacity == 0) { Hide(); _timer.Stop(); } };
        BeginAnimation(OpacityProperty, fade);
    }
    private Forms.Screen GetScreen() => GetScreen(_global);
    private static Forms.Screen GetScreen(GlobalConfiguration config) => config.Position == "custom" && config.CustomLeft is not null && config.CustomTop is not null
        ? Forms.Screen.FromPoint(new System.Drawing.Point((int)config.CustomLeft.Value, (int)config.CustomTop.Value))
        : Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == config.MonitorDevice)
        ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    private void PositionWindow()
    {
        var bounds = PlacementBounds(_global);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0010 | 0x0004);
    }
    internal static System.Drawing.Rectangle PlacementBounds(GlobalConfiguration config)
    {
        var area = GetScreen(config).WorkingArea;
        uint dpi = 96;
        var point = new NativePoint { X = area.Left + 1, Y = area.Top + 1 };
        try { if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out var dx, out _) == 0) dpi = dx; }
        catch (DllNotFoundException) { }
        var w = Math.Min(area.Width, (int)(260 * Math.Clamp(config.Scale, .4, 3) * dpi / 96));
        var h = Math.Min(area.Height, (int)(280 * Math.Clamp(config.Scale, .4, 3) * dpi / 96));
        var margin = (int)(24 * dpi / 96);
        var (x, y) = config.Position switch
        {
            "top-left" => (area.Left + margin, area.Top + margin),
            "top-right" => (area.Right - w - margin, area.Top + margin),
            "bottom-left" => (area.Left + margin, area.Bottom - h - margin),
            "center" => (area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2),
            "custom" => ((int)(config.CustomLeft ?? area.Left), (int)(config.CustomTop ?? area.Top)),
            _ => (area.Right - w - margin, area.Bottom - h - margin)
        };
        x = Math.Clamp(x, area.Left, area.Right - w); y = Math.Clamp(y, area.Top, area.Bottom - h);
        return new System.Drawing.Rectangle(x, y, w, h);
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
