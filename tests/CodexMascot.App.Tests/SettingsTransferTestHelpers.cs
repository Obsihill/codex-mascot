using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class SettingsTransferTestHelpers
{
    internal static void QueueResponse(Action<bool, string> check, string title, bool approve)
    {
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.IsVisible && w.Title == title);
            var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
            var warning = panel.Children.OfType<TextBlock>().Single();
            check(warning.Text.Length > 20 && warning.TextWrapping == TextWrapping.Wrap,
                title + " explains the settings change before it is saved");
            var actions = panel.Children.OfType<StackPanel>().Single();
            check(actions.Children.OfType<Button>().Single(b => b.Name == "CancelSettingsChange").IsDefault &&
                  !actions.Children.OfType<Button>().Single(b => b.Name == "ConfirmSettingsChange").IsDefault,
                title + " defaults to cancellation");
            var button = actions.Children.OfType<Button>().Single(b =>
                b.Name == (approve ? "ConfirmSettingsChange" : "CancelSettingsChange"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
    }
}
