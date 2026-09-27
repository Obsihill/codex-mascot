using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using CodexMascot.App;

internal static class PencilWindowTests
{
    public static void Run(Action<bool, string> check)
    {
        var context = SynchronizationContext.Current;
        var window = new Window
        {
            Title = "창 버튼 테스트", Width = 640, Height = 360,
            Background = PencilPalette.Paper, Content = new TextBox { Text = "창 내용", Margin = new Thickness(20) },
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AgentMascot;component/PencilTheme.xaml", UriKind.Relative) });
        try
        {
            check(PencilWindow.CalculateFramePadding(new Rect(-1928, 336, 1936, 1048), new Rect(-1920, 344, 1920, 1032), 1, 1) == new Thickness(8), "maximized insets support monitors at negative desktop coordinates");
            check(PencilWindow.CalculateFramePadding(new Rect(-12, -12, 2584, 1464), new Rect(0, 0, 2560, 1440), 1.5, 1.5) == new Thickness(8), "maximized insets convert physical pixels at 150 percent DPI");
            check(PencilWindow.CalculateFramePadding(new Rect(40, 30, 600, 300), new Rect(0, 0, 1920, 1080), 1, 1) == new Thickness(0), "windows contained in the work area need no inset");
            PencilWindow.Apply(window);
            var bindings = window.CommandBindings.Count;
            PencilWindow.Apply(window);
            check(window.CommandBindings.Count == bindings && bindings == 4, "window chrome application is idempotent");
            window.Show(); Pump();
            var chrome = WindowChrome.GetWindowChrome(window);
            check(chrome.CaptionHeight == 40 && chrome.GlassFrameThickness == new Thickness(0) && !chrome.UseAeroCaptionButtons, "custom caption replaces native icons but retains WindowChrome");
            check(window.WindowStyle == WindowStyle.SingleBorderWindow && chrome.ResizeBorderThickness.Left > 0, "native window style and resize border are retained");
            var minimize = Caption(window, "Minimize");
            var maximize = Caption(window, "Maximize");
            var close = Caption(window, "Close");
            foreach (var (button, kind, label) in new[] { (minimize, PencilIconKind.Minimize, "최소화"), (maximize, PencilIconKind.Maximize, "최대화"), (close, PencilIconKind.Close, "닫기") })
            {
                check(Pencil.GetIcon(button) == kind && AutomationProperties.GetName(button) == label && (string)button.ToolTip == label, "caption icon and accessible label: " + label);
                check(WindowChrome.GetIsHitTestVisibleInChrome(button) && button.IsEnabled, "caption button is interactive: " + label);
                PencilThemeTests.CheckHover(check, button, (PencilBorder)button.Template.FindName("Box", button), "caption " + label);
            }
            var title = (TextBlock)window.Template.FindName("WindowTitle", window);
            window.Title = "변경된 제목";
            check(title.Text == window.Title && title.FontFamily == PencilFonts.Handwriting, "caption binds the window title with the embedded handwriting font");
            check(HitTest(window, new Point(180, 20)) == 2, "empty caption remains a native draggable caption");
            check(HitTest(window, close.TranslatePoint(new Point(20, 14), window)) == 1, "close button is client-interactive, not a drag area");
            check(HitTest(window, new Point(180, 150)) == 1, "window content remains interactive");
            check(HitTest(window, new Point(1, 160)) == 10, "left edge remains a native resize border");

            var normalSize = new Size(window.ActualWidth, window.ActualHeight);
            Execute(maximize);
            check(window.WindowState == WindowState.Maximized && Pencil.GetIcon(maximize) == PencilIconKind.Restore && maximize.Command == SystemCommands.RestoreWindowCommand, "maximize switches to restore icon and command");
            check(AutomationProperties.GetName(maximize) == "이전 크기로", "restore button updates accessibility text");
            var hwnd = new WindowInteropHelper(window).Handle;
            var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
            var start = close.PointToScreen(new Point());
            var end = close.PointToScreen(new Point(close.ActualWidth, close.ActualHeight));
            var content = (FrameworkElement)window.Template.FindName("WindowContent", window);
            var contentEnd = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight));
            check(start.X >= area.Left - 1 && start.Y >= area.Top - 1 && end.X <= area.Right + 1 && end.Y <= area.Bottom + 1 && contentEnd.Y <= area.Bottom + 1, $"maximized caption and content stay inside monitor work area: area={area}, close={start}..{end}, contentEnd={contentEnd}, frame={SystemParameters.WindowResizeBorderThickness}");
            Execute(maximize);
            check(window.WindowState == WindowState.Normal && Pencil.GetIcon(maximize) == PencilIconKind.Maximize && Math.Abs(window.ActualWidth - normalSize.Width) < 1 && Math.Abs(window.ActualHeight - normalSize.Height) < 1, "restore returns the original dimensions and maximize icon");
            Execute(minimize);
            check(window.WindowState == WindowState.Minimized, "caption minimize uses native minimize");
            SystemCommands.RestoreWindow(window); Pump();
            check(window.WindowState == WindowState.Normal, "minimized window can be restored");
            // Exercise native title double-click without moving the user's pointer.
            SendMessage(hwnd, 0x00A3, new IntPtr(2), ScreenPoint(window, new Point(180, 20)));
            Pump();
            check(window.WindowState == WindowState.Maximized, "native title double-click still maximizes");
            Execute(maximize);

            window.ResizeMode = ResizeMode.NoResize; Pump();
            check(minimize.Visibility == Visibility.Collapsed && maximize.Visibility == Visibility.Collapsed && close.IsVisible && chrome.ResizeBorderThickness == new Thickness(0), "fixed dialogs show only close and have no resize border");
            check(!SystemCommands.MaximizeWindowCommand.CanExecute(null, window) && !SystemCommands.MinimizeWindowCommand.CanExecute(null, window), "fixed dialogs reject minimize and maximize commands");
            window.ResizeMode = ResizeMode.CanMinimize; Pump();
            check(minimize.IsVisible && maximize.Visibility == Visibility.Collapsed && chrome.ResizeBorderThickness == new Thickness(0), "minimizable fixed window exposes only minimize and close");
            window.ResizeMode = ResizeMode.CanResizeWithGrip; Pump();
            check(minimize.IsVisible && maximize.IsVisible && chrome.ResizeBorderThickness.Left > 0, "resizable mode restores caption buttons and resize border");
            var cancelClose = true;
            var closingCount = 0;
            window.Closing += (_, e) => { closingCount++; e.Cancel = cancelClose; };
            Execute(close);
            check(window.IsVisible && closingCount == 1, "caption close honors cancelled Closing handlers");
            cancelClose = false;
            Execute(close);
            check(!window.IsVisible && closingCount == 2, "caption close closes a regular window normally");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(context); }
    }

    internal static Button Caption(Window window, string name) => (Button)window.Template.FindName("Caption" + name, window);
    internal static void Execute(Button button)
    {
        ((RoutedCommand)button.Command).Execute(button.CommandParameter, button);
        Pump();
    }
    private static int HitTest(Window window, Point point) => (int)SendMessage(new WindowInteropHelper(window).Handle, 0x0084, IntPtr.Zero, ScreenPoint(window, point));
    private static IntPtr ScreenPoint(Window window, Point point)
    {
        var screen = window.PointToScreen(point);
        return new IntPtr(unchecked((int)((uint)(ushort)(int)screen.X | ((uint)(ushort)(int)screen.Y << 16))));
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
