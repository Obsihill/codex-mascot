using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMascot.App;

internal static class SettingsTransactionTests
{
    internal static void Run(Action<bool, string> check, string dir)
    {
        var manager = new CustomizationManager();
        var originalFile = File.ReadAllText(AppPaths.ConfigFile);
        var language = Loc.Language;
        var dark = PencilPalette.Current.IsDark;
        SettingsWindow? window = null;
        var calls = new List<bool>();
        try
        {
            var global = manager.Configuration.Global;
            var monitor = manager.Configuration.Monitor;
            var top = global.AlwaysOnTop;
            var startup = global.StartWithWindows;
            var home = monitor.CodexHome;
            window = Open();
            global.AlwaysOnTop = !top; global.StartWithWindows = !startup; monitor.CodexHome = "draft-only";
            Loc.Configure(language == "ko" ? "en" : "ko"); Pump();
            manager.Save();
            check(File.ReadAllText(AppPaths.ConfigFile) == originalFile && calls.Count == 0, "draft Save does not write disk or Windows startup");
            window.Close(); Pump();
            check(ReferenceEquals(global, manager.Configuration.Global) && ReferenceEquals(monitor, manager.Configuration.Monitor), "Cancel preserves live configuration references");
            check(global.AlwaysOnTop == top && global.StartWithWindows == startup && monitor.CodexHome == home && Loc.Language == language, $"titlebar close restores preferences, connection and language; actual {global.AlwaysOnTop}/{global.StartWithWindows}/{monitor.CodexHome}/{Loc.Language}, expected {top}/{startup}/{home}/{language}");
            check(calls.Count == 0 && File.ReadAllText(AppPaths.ConfigFile) == originalFile, "Cancel leaves startup and saved configuration untouched");

            window = Open();
            foreach (var isDark in new[] { false, true })
            {
                AppTheme.Apply(isDark); Pump();
                var ok = Children(window).OfType<Button>().Single(b => b.Name == "ConfirmSettings");
                check(ReferenceEquals(ok.Background, PencilPalette.Current.Button) && ReferenceEquals(ok.Foreground, PencilPalette.Current.OnButton), "OK uses registration button colors in both themes");
            }
            global.StartWithWindows = !startup; global.AlwaysOnTop = !top;
            var saves = 0; window.Saved += (_, _) => saves++;
            window.Accept(); Pump();
            check(window.Accepted && !manager.IsEditing && saves == 1 && calls.SequenceEqual(new[] { !startup }), "OK commits once and applies Windows startup exactly once");
            check(new CustomizationManager().Configuration.Global.AlwaysOnTop == !top, "OK persists preview preferences");
            check(new CustomizationManager().Configuration.Global.Language == Loc.Language, "OK stores the actual displayed language");

            var committed = File.ReadAllText(AppPaths.ConfigFile);
            calls.Clear();
            window = Open(); global.StartWithWindows = startup;
            window.Accepting += () => throw new IOException("Simulated hook write failure");
            window.Accept(); Pump();
            check(window.IsVisible && !window.Accepted && manager.IsEditing, "failed commit keeps the draft open for retry or cancellation");
            check(File.ReadAllText(AppPaths.ConfigFile) == committed && calls.SequenceEqual(new[] { startup, !startup }), "failed commit restores saved file and external startup setting");
            window.Close(); Pump();
            check(global.StartWithWindows == !startup, "Cancel after failed commit restores the last accepted preferences");
            check(StartupRegistration.IsEnabledFor("\"C:\\Apps\\AgentMascot.exe\" --tray", "C:\\Apps\\AgentMascot.exe"), "installer startup command matches the app preference");
            check(!StartupRegistration.IsEnabledFor(null, "C:\\Apps\\AgentMascot.exe") && !StartupRegistration.IsEnabledFor("\"C:\\Other\\AgentMascot.exe\" --tray", "C:\\Apps\\AgentMascot.exe"), "disabled or another installation is not reported as enabled");
            var beforeProjects = File.ReadAllText(AppPaths.ConfigFile);
            var project = Path.Combine(dir, "sample-project");
            var excluded = Path.Combine(dir, "excluded-project");
            var selector = new ProjectSelectionWindow(manager) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                selector.Show(); selector.AddProject(project, true); selector.AutoInclude.IsChecked = false;
                selector.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(File.ReadAllText(AppPaths.ConfigFile) == beforeProjects, "project selection cancellation preserves settings");
            }
            finally { selector.Close(); }
            selector = new ProjectSelectionWindow(manager) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                selector.Show(); selector.AddProject(project, true); selector.AddProject(excluded, false);
                selector.AutoInclude.IsChecked = false; selector.RecentLimit.Value = 20;
                selector.AddChat(new() { Agent = CodexMascot.Core.AgentKind.Codex, Id = "normal-chat", ProjectPath = project, Title = "Normal chat" });
                selector.AddChat(new() { Agent = CodexMascot.Core.AgentKind.Codex, Id = "test-chat", ProjectPath = project, Title = "Test chat" });
                selector.AddChat(new() { Agent = CodexMascot.Core.AgentKind.Claude, Id = "test-chat", ProjectPath = project, Title = "Claude chat" });
                selector.ChatChoices["Codex:normal-chat"].IsChecked = true;
                selector.ChatChoices["Claude:test-chat"].IsChecked = true;
                selector.ProjectGroups[project].IsExpanded = true;
                check(selector.ChatChoices["Codex:test-chat"].IsChecked == false, "choosing siblings does not enable excluded test chat");
                selector.UpdateLayout();
                var visibleProjectText = Children(selector).OfType<TextBlock>().Select(t => t.Text).ToArray();
                check(visibleProjectText.Contains("sample-project") && visibleProjectText.Contains(project) && visibleProjectText.Contains("excluded-project") && !visibleProjectText.Any(t => t.Contains("System.Windows.Controls.StackPanel")), "project rows actually render project names and full paths instead of control type names");
                var screenshots = Environment.GetEnvironmentVariable("MASCOT_THEME_SCREENSHOT_DIR");
                if (!string.IsNullOrWhiteSpace(screenshots))
                    foreach (var isDark in new[] { true, false })
                    { AppTheme.Apply(isDark); LibraryFeatureTests.Capture(selector, Path.Combine(screenshots, "project-selection-" + (isDark ? "dark" : "light") + ".png")); }
                selector.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var saved = new CustomizationManager().Configuration.Monitor;
                check(selector.Accepted && !saved.AutoIncludeNewProjects && saved.RecentSessionLimit == 20, "project selection persists automatic monitoring and recent cap");
                check(CodexMascot.Core.ProjectWatchPolicy.Allows(saved, project) && !CodexMascot.Core.ProjectWatchPolicy.Allows(saved, excluded), "project checkboxes persist include and exclude choices");
                check(!CodexMascot.Core.ProjectWatchPolicy.Allows(saved, Path.Combine(dir, "future-project")), "new projects stay excluded when automatic monitoring is disabled");
                check(CodexMascot.Core.ChatWatchPolicy.Allows(saved, CodexMascot.Core.AgentKind.Codex, "normal-chat", project) && !CodexMascot.Core.ChatWatchPolicy.Allows(saved, CodexMascot.Core.AgentKind.Codex, "test-chat", project), "same-folder individual chat choices survive reload");
                check(CodexMascot.Core.ChatWatchPolicy.Allows(saved, CodexMascot.Core.AgentKind.Claude, "test-chat", project), "same chat ID in another agent stays independent");
                check(!CodexMascot.Core.ChatWatchPolicy.Allows(saved, CodexMascot.Core.AgentKind.Codex, "new-chat", project), "new chat in existing folder stays excluded with auto off");
            }
            finally { selector.Close(); }
            foreach (var kind in new[] { CodexMascot.Core.AgentKind.Codex, CodexMascot.Core.AgentKind.Claude })
            {
                var hookHome = Path.Combine(dir, "automatic-hooks-" + kind); Directory.CreateDirectory(hookHome);
                HookIntegration.EnsureInstalled(hookHome, kind);
                check(HookIntegration.IsInstalled(hookHome, kind), "automatic hook setup registers " + kind);
                var configPath = Path.Combine(hookHome, CodexMascot.Core.AgentAdapters.For(kind).HookConfigFileName);
                var bytes = File.ReadAllText(configPath); var writeTime = File.GetLastWriteTimeUtc(configPath);
                HookIntegration.EnsureInstalled(hookHome, kind);
                check(File.ReadAllText(configPath) == bytes && File.GetLastWriteTimeUtc(configPath) == writeTime && !Directory.EnumerateFiles(hookHome, "*.mascot-backup-*").Any(), "healthy automatic hook setup is idempotent: " + kind);
            }
        }
        finally
        {
            window?.Close(); Loc.Configure(language); AppTheme.Apply(dark);
            File.WriteAllText(AppPaths.ConfigFile, originalFile);
        }
        SettingsWindow Open()
        {
            var result = new SettingsWindow(manager, applyStartup: calls.Add) { ShowActivated = false, ShowInTaskbar = false };
            result.Show(); result.UpdateLayout(); return result;
        }
        void Pump() { window?.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window?.UpdateLayout(); }
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
}
