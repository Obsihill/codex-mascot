using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexMascot.App;

// Keep the mascot's existing topmost preference without changing Explorer's Z order.
// Mask only the visible taskbar rectangles, including secondary-monitor taskbars.
internal sealed class TaskbarOcclusion : IDisposable
{
    private readonly Window _window;
    private readonly FrameworkElement _content;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private Rect[] _previous = Array.Empty<Rect>();
    private Size _size;
    private bool _enabled = true;
    internal bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value) { _timer.Stop(); _content.Clip = null; _previous = Array.Empty<Rect>(); _size = default; }
            else if (_window.IsVisible) { _timer.Start(); Refresh(this, EventArgs.Empty); }
        }
    }

    internal TaskbarOcclusion(Window window, FrameworkElement content)
    {
        _window = window; _content = content;
        _timer.Tick += Refresh;
        window.IsVisibleChanged += VisibilityChanged;
        window.LocationChanged += Refresh;
        content.SizeChanged += SizeChanged;
    }

    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_window.IsVisible && Enabled) { _timer.Start(); Refresh(this, EventArgs.Empty); }
        else _timer.Stop();
    }
    private void SizeChanged(object sender, SizeChangedEventArgs e) => Refresh(sender, e);
    private void Refresh(object? sender, EventArgs e)
    {
        if (!Enabled || !_window.IsVisible || PresentationSource.FromVisual(_content) is null) return;
        var size = _content.RenderSize;
        var bounds = new Rect(size);
        var rectangles = new List<Rect>();
        foreach (var className in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            var handle = IntPtr.Zero;
            while ((handle = FindWindowEx(IntPtr.Zero, handle, className, null)) != IntPtr.Zero)
            {
                if (!IsWindowVisible(handle) || !GetWindowRect(handle, out var r) || r.Right <= r.Left || r.Bottom <= r.Top) continue;
                // PointFromScreen accounts for DPI and negative monitor coordinates.
                var local = new Rect(_content.PointFromScreen(new Point(r.Left, r.Top)),
                    _content.PointFromScreen(new Point(r.Right, r.Bottom)));
                local.Intersect(bounds);
                if (!local.IsEmpty && local.Width > 0 && local.Height > 0) rectangles.Add(local);
            }
        }
        if (_size == size && _previous.SequenceEqual(rectangles)) return;
        _size = size; _previous = rectangles.ToArray();
        _content.Clip = CreateClip(size, rectangles);
    }

    internal static Geometry? CreateClip(Size size, IEnumerable<Rect> taskbars)
    {
        var bounds = new Rect(size);
        Geometry? clip = null;
        foreach (var taskbar in taskbars)
        {
            var overlap = Rect.Intersect(bounds, taskbar);
            if (overlap.IsEmpty || overlap.Width <= 0 || overlap.Height <= 0) continue;
            clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                clip ?? new RectangleGeometry(bounds), new RectangleGeometry(overlap));
        }
        clip?.Freeze();
        return clip;
    }

    public void Dispose()
    {
        _timer.Stop(); _timer.Tick -= Refresh;
        _window.IsVisibleChanged -= VisibilityChanged;
        _window.LocationChanged -= Refresh;
        _content.SizeChanged -= SizeChanged;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
}
