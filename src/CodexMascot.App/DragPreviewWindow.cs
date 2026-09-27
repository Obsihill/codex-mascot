using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexMascot.App;

internal sealed class DragPreviewWindow : Window
{
    public DragPreviewWindow(ImageSource? image, string name)
    {
        Width = 146; Height = 120; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Opacity = .55;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false;
        var panel = new StackPanel();
        panel.Children.Add(new Image { Source = image, Height = 76, Stretch = Stretch.Uniform });
        panel.Children.Add(new TextBlock { Text = name, Foreground = Brushes.Black, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) });
        Content = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Child = panel };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x20 | 0x08000000 | 0x80); FollowCursor();
        };
    }
    internal static Point CursorPoint() { GetCursorPos(out var p); return new Point(p.X, p.Y); }
    internal void FollowCursor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var p = CursorPoint(); SetWindowPos(hwnd, IntPtr.Zero, (int)p.X + 14, (int)p.Y + 14, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
