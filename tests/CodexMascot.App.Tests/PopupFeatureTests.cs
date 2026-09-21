using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMascot.App;
using CodexMascot.Core;

internal static class PopupFeatureTests
{
    public static void Run(Action<bool, string> check)
    {
        var migrated = JsonSerializer.Deserialize<MascotConfiguration>("""{"Global":{"ClickThrough":true},"States":{"completed":{"ShowDurationMs":4000}}}""")!;
        check(migrated.Global.KeepCompletedVisibleUntilClick && migrated.Global.BringCodexToFrontOnClick,
            "existing configuration gains enabled popup options");
        migrated.Global.KeepCompletedVisibleUntilClick = false;
        migrated.Global.BringCodexToFrontOnClick = false;
        var roundTrip = JsonSerializer.Deserialize<MascotConfiguration>(JsonSerializer.Serialize(migrated))!;
        check(!roundTrip.Global.KeepCompletedVisibleUntilClick && !roundTrip.Global.BringCodexToFrontOnClick,
            "disabled popup options survive JSON round trip");

        check(CodexDesktopActivator.IsDesktopExecutable(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.915_x64__publisher\app\ChatGPT.exe"), "Store Codex named ChatGPT.exe is recognized");
        check(CodexDesktopActivator.IsDesktopExecutable(@"C:\Apps\Codex\Codex.exe"), "Codex desktop executable is recognized");
        check(!CodexDesktopActivator.IsDesktopExecutable(@"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1_x64__publisher\app\ChatGPT.exe"), "ordinary ChatGPT is not activated");
        check(!CodexDesktopActivator.IsDesktopExecutable(@"C:\OpenAI\Codex\bin\hash\codex.exe"), "CLI is not activated");
        check(!CodexDesktopActivator.IsDesktopExecutable(@"C:\Apps\CodexMascot.App.exe"), "Mascot is not mistaken for Codex");

        var native = new FakeWindowActivation { Minimized = true, CanActivate = true };
        check(CodexDesktopActivator.ActivateWindow((IntPtr)123, native) == DesktopActivationResult.Activated && native.Calls.SequenceEqual(new[] { "restore", "foreground" }), "minimized desktop is restored before activation");
        native = new FakeWindowActivation { CanActivate = true };
        check(CodexDesktopActivator.ActivateWindow((IntPtr)123, native) == DesktopActivationResult.Activated && native.Calls.SequenceEqual(new[] { "foreground" }), "normal or maximized desktop keeps its geometry");
        native = new FakeWindowActivation();
        check(CodexDesktopActivator.ActivateWindow((IntPtr)123, native) == DesktopActivationResult.AttentionRequested && native.Calls.SequenceEqual(new[] { "foreground", "flash" }), "foreground denial requests taskbar attention");
        native = new FakeWindowActivation();
        check(CodexDesktopActivator.ActivateWindow(IntPtr.Zero, native) == DesktopActivationResult.NotFound && native.Calls.Count == 0, "absent Codex causes no native activation");

        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["TextBrush"] = Brushes.White;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var overlay = new OverlayWindow();
        try
        {
            var global = new GlobalConfiguration { ClickThrough = true, AlwaysOnTop = false };
            var state = new StateConfiguration { ShowDurationMs = 200 };
            overlay.ApplyGlobal(global);
            overlay.ShowState(MascotState.Completed, state, null);
            Pump(700);
            check(overlay.IsVisible && overlay.Opacity > .9, "completion survives configured duration and fade with fallback image");
            check(!IsClickThrough(overlay), "held completion remains clickable with click-through enabled");

            global.KeepCompletedVisibleUntilClick = false;
            overlay.ApplyGlobal(global);
            Pump(700);
            check(!overlay.IsVisible, "disabling hold starts the existing timeout immediately");

            overlay.ShowState(MascotState.NeedsAttention, state, null);
            Pump(700);
            check(overlay.IsVisible && overlay.Opacity > .9, "approval remains visible regardless of duration and completion preference");
            check(!IsClickThrough(overlay), "approval remains clickable with click-through enabled");
            RaiseMouse(overlay, UIElement.MouseLeftButtonDownEvent);
            RaiseMouse(overlay, UIElement.MouseLeftButtonUpEvent);
            Pump(350);
            check(!overlay.IsVisible, "click dismisses approval popup");

            overlay.ShowState(MascotState.Completed, state, null);
            global.KeepCompletedVisibleUntilClick = true;
            overlay.ApplyGlobal(global);
            Pump(700);
            check(overlay.IsVisible && overlay.Opacity > .9, "enabling hold cancels the already scheduled timeout");
            overlay.HideMascot();
            Pump(350);
            check(!overlay.IsVisible, "held completion can be explicitly hidden");

            overlay.ShowState(MascotState.Running, state, null);
            check(IsClickThrough(overlay), "non-completion keeps click-through preference");
            Pump(700);
            check(!overlay.IsVisible, "running popup still times out");

            overlay.ShowState(MascotState.Completed, state, null);
            overlay.ShowState(MascotState.Failed, state, null);
            Pump(700);
            check(!overlay.IsVisible, "replacement warning keeps its own lifetime");

            var clicked = 0;
            overlay.Clicked += (_, _) => clicked++;
            overlay.ShowState(MascotState.Completed, state, null);
            RaiseMouse(overlay, UIElement.MouseLeftButtonDownEvent);
            RaiseMouse(overlay, UIElement.MouseLeftButtonUpEvent);
            Pump(350);
            check(clicked == 1 && !overlay.IsVisible, "left click emits activation once and dismisses popup");
            RaiseMouse(overlay, UIElement.MouseLeftButtonUpEvent);
            check(clicked == 1, "mouse release without press cannot reopen desktop");
        }
        finally { overlay.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void RaiseMouse(UIElement element, RoutedEvent routedEvent)
        => element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routedEvent });
    private static bool IsClickThrough(Window window) => (GetWindowLong(new WindowInteropHelper(window).Handle, -20) & 0x20) != 0;
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private sealed class FakeWindowActivation : CodexDesktopActivator.IWindowActivation
    {
        public bool Minimized, CanActivate;
        public List<string> Calls { get; } = new();
        public bool IsMinimized(IntPtr window) => Minimized;
        public void Restore(IntPtr window) => Calls.Add("restore");
        public bool TryForeground(IntPtr window) { Calls.Add("foreground"); return CanActivate; }
        public void RequestAttention(IntPtr window) => Calls.Add("flash");
    }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
}
