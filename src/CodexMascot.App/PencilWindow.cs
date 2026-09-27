using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace CodexMascot.App;

// Opt-in only: transparent mascot/drag-preview windows keep their own chrome.
public static class PencilWindow
{
    private static readonly DependencyProperty AppliedProperty = DependencyProperty.RegisterAttached("Applied", typeof(bool), typeof(PencilWindow), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey FramePaddingPropertyKey = DependencyProperty.RegisterAttachedReadOnly("FramePadding", typeof(Thickness), typeof(PencilWindow), new PropertyMetadata(new Thickness(0)));
    public static readonly DependencyProperty FramePaddingProperty = FramePaddingPropertyKey.DependencyProperty;
    public static Thickness GetFramePadding(DependencyObject target) => (Thickness)target.GetValue(FramePaddingProperty);

    internal static void Apply(Window window)
    {
        if ((bool)window.GetValue(AppliedProperty)) return;
        window.SetValue(AppliedProperty, true);
        window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(AppBrand.IconUri);
        window.SetResourceReference(FrameworkElement.StyleProperty, "PencilWindowStyle");
        var chrome = new WindowChrome { CaptionHeight = 40, GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false };
        BindingOperations.SetBinding(chrome, WindowChrome.ResizeBorderThicknessProperty, new Binding(nameof(Window.ResizeMode)) { Source = window, Converter = new PencilResizeBorderConverter() });
        WindowChrome.SetWindowChrome(window, chrome);

        // Maximized HWND bounds include an invisible sizing frame. Measure the
        // actual overhang on this monitor, rather than using primary-screen metrics.
        var pending = false;
        var closed = false;
        window.SourceInitialized += (_, _) => QueueFrameUpdate();
        window.StateChanged += (_, _) => QueueFrameUpdate();
        window.SizeChanged += (_, _) => QueueFrameUpdate();
        window.LocationChanged += (_, _) => QueueFrameUpdate();
        window.DpiChanged += (_, _) => QueueFrameUpdate();
        window.Closed += (_, _) => closed = true;
        QueueFrameUpdate();

        void QueueFrameUpdate()
        {
            if (pending || closed) return;
            pending = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                pending = false;
                if (closed) return;
                var padding = new Thickness(0);
                var hwnd = new WindowInteropHelper(window).Handle;
                if (window.WindowState == WindowState.Maximized && hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var bounds))
                {
                    var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
                    var dpi = VisualTreeHelper.GetDpi(window);
                    padding = CalculateFramePadding(new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
                        new Rect(area.Left, area.Top, area.Width, area.Height), dpi.DpiScaleX, dpi.DpiScaleY);
                }
                window.SetValue(FramePaddingPropertyKey, padding);
            }));
        }

        bool Resizable() => window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        Bind(SystemCommands.MinimizeWindowCommand, () => SystemCommands.MinimizeWindow(window), () => window.ResizeMode != ResizeMode.NoResize && window.WindowState != WindowState.Minimized);
        Bind(SystemCommands.MaximizeWindowCommand, () => SystemCommands.MaximizeWindow(window), () => Resizable() && window.WindowState != WindowState.Maximized);
        Bind(SystemCommands.RestoreWindowCommand, () => SystemCommands.RestoreWindow(window), () => window.WindowState != WindowState.Normal);
        // Go through normal Closing, including MainWindow's hide-to-tray policy.
        Bind(SystemCommands.CloseWindowCommand, () => SystemCommands.CloseWindow(window), () => true);

        void Bind(RoutedCommand command, Action execute, Func<bool> canExecute)
            => window.CommandBindings.Add(new CommandBinding(command,
                (_, e) => { execute(); e.Handled = true; },
                (_, e) => { e.CanExecute = canExecute(); e.Handled = true; }));
    }

    internal static Thickness CalculateFramePadding(Rect bounds, Rect workArea, double dpiX, double dpiY)
        => new(Math.Max(0, workArea.Left - bounds.Left) / dpiX, Math.Max(0, workArea.Top - bounds.Top) / dpiY,
            Math.Max(0, bounds.Right - workArea.Right) / dpiX, Math.Max(0, bounds.Bottom - workArea.Bottom) / dpiY);

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    private sealed class PencilResizeBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
