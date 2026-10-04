using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMascot.App;

internal static class SettingsTransferTestHelpers
{
    internal static void QueueResponse(Action<bool, string> check, string title, bool approve)
    {
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.IsVisible && w.Title == title);
            var frame = (PencilBorder)dialog.Content;
            var panel = (StackPanel)((ScrollViewer)frame.Child).Content;
            var warning = panel.Children.OfType<TextBlock>().Single();
            check(warning.Text.Length > 20 && warning.TextWrapping == TextWrapping.Wrap,
                title + " explains the settings change before it is saved");
            check(dialog.WindowStyle == WindowStyle.None && dialog.FontFamily == PencilFonts.Handwriting &&
                  frame.BorderBrush == PencilPalette.Line,
                title + " uses a titleless, close-button-free handwritten dialog");
            var actions = panel.Children.OfType<StackPanel>().Single();
            var confirm = actions.Children.OfType<Button>().Single(b => b.Name == "ConfirmSettingsChange");
            check(actions.HorizontalAlignment == HorizontalAlignment.Right &&
                  actions.Children.OfType<Button>().Select(b => b.Name).SequenceEqual(new[] { "CancelSettingsChange", "ConfirmSettingsChange" }) &&
                  !actions.Children.OfType<Button>().Any(b => b.IsDefault) &&
                  confirm.Background == PencilPalette.Button &&
                  confirm.Foreground == PencilPalette.OnButton && confirm.BorderBrush == PencilPalette.Button,
                title + " places Cancel left of OK without an Enter default");
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog),
                Environment.TickCount, Key.Return) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            dialog.RaiseEvent(enter);
            check(enter.Handled && dialog.IsVisible, title + " ignores Enter without choosing either action");
            var button = actions.Children.OfType<Button>().Single(b =>
                b.Name == (approve ? "ConfirmSettingsChange" : "CancelSettingsChange"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
    }
}
