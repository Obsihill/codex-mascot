using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexMascot.Core;
namespace CodexMascot.App;
public static class InputRequestWindow
{
    public static object? Ask(Window owner, JsonElement parameters)
    {
        if (!parameters.TryGetProperty("questions", out var questions)) return null;
        var window = new Window { Title = "Codex 질문에 답변", Owner = owner, Width = 560, Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(17, 25, 37)) };
        var panel = new StackPanel { Margin = new Thickness(18) };
        var inputs = new Dictionary<string, TextBox>();
        foreach (var q in questions.EnumerateArray())
        {
            var id = q.GetStringOrNull("id");
            if (id is null) continue;
            panel.Children.Add(new TextBlock { Text = q.GetStringOrNull("question"), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) });
            if (q.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                panel.Children.Add(new TextBlock { Text = string.Join(" / ", options.EnumerateArray().Select(o => o.GetStringOrNull("label"))), Foreground = Brushes.LightBlue, TextWrapping = TextWrapping.Wrap });
            var input = new TextBox(); inputs[id] = input; panel.Children.Add(input);
        }
        var submit = new Button { Content = "답변 보내기" };
        submit.Click += (_, _) => { window.DialogResult = true; };
        panel.Children.Add(submit);
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return window.ShowDialog() == true ? new { answers = inputs.ToDictionary(p => p.Key, p => new { answers = new[] { p.Value.Text } }) } : null;
    }
}
