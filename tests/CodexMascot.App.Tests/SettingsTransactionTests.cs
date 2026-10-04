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
                var footer = ((Grid)window.Content).Children.OfType<DockPanel>().Single();
                check(ReferenceEquals(ok.Background, PencilPalette.Current.Button) && ReferenceEquals(ok.Foreground, PencilPalette.Current.OnButton) &&
                      !ok.IsDefault && ((Button)footer.Children[0]).Name == "ConfirmSettings" &&
                      ((Button)footer.Children[1]).Name == "CancelSettings",
                    "OK uses registration colors, sits right of Cancel and is handled by animated Enter in both themes");
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
            check(new CodexMascot.Core.MonitorConfiguration().RecentSessionLimit == 100,
                "new installations default to 100 recent records per agent");
            var beforeAutoInclude = monitor.AutoIncludeNewChats;
            var beforeRecentLimit = monitor.RecentSessionLimit;
            var project = Path.Combine(dir, "sample-project");
            var excluded = Path.Combine(dir, "excluded-project");
            var selector = new ProjectSelectionWindow(manager) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                selector.Show(); selector.AddProject(project, true); selector.AutoInclude.IsChecked = false;
                var pickerRoot = (DockPanel)selector.Content;
                var pickerFooter = (DockPanel)pickerRoot.Children[0];
                var pickerButtons = (StackPanel)pickerFooter.Children[0];
                var pickerOptions = (StackPanel)pickerRoot.Children[1];
                var pickerSummary = (Grid)pickerOptions.Children[^1];
                var pickerActions = (StackPanel)pickerSummary.Children[1];
                check(selector.Title == "감시 프로젝트 선택" && ReferenceEquals(pickerFooter.Children[1], selector.AutoInclude) &&
                      selector.AutoInclude.HorizontalAlignment == HorizontalAlignment.Left &&
                      DockPanel.GetDock(pickerButtons) == Dock.Right &&
                      pickerButtons.Children.IndexOf(selector.Cancel) == 0 &&
                      pickerButtons.Children.IndexOf(selector.Confirm) == 1 && !selector.Confirm.IsDefault,
                    "picker title and bottom-left auto-monitor checkbox sit beside right-aligned confirmation buttons");
                check(ReferenceEquals(pickerSummary.Children[0], pickerSummary.Children.OfType<TextBlock>().Single()) &&
                      Grid.GetColumn(pickerActions) == 1 && pickerActions.HorizontalAlignment == HorizontalAlignment.Right &&
                      ReferenceEquals(pickerActions.Children[0], selector.MonitorAll) && ReferenceEquals(pickerActions.Children[1], selector.ExcludeAll) &&
                      Pencil.GetIcon(selector.MonitorAll) == PencilIconKind.MonitorAll && Pencil.GetIcon(selector.ExcludeAll) == PencilIconKind.ExcludeAll &&
                      selector.MonitorAll.Content?.ToString() == "" && selector.ExcludeAll.Content?.ToString() == "" &&
                      selector.MonitorAll.ToolTip is ToolTip { Content: "모두 감시 · 표시된 항목 전부 켜기" } &&
                      selector.ExcludeAll.ToolTip is ToolTip { Content: "모두 제외 · 표시된 항목 전부 끄기" } &&
                      ToolTipService.GetInitialShowDelay(selector.MonitorAll) == 200 &&
                      ToolTipService.GetInitialShowDelay(selector.ExcludeAll) == 200,
                    "icon-only all and exclude controls sit at the far right and explain their actions on hover");
                foreach (var darkTheme in new[] { true, false })
                {
                    AppTheme.Apply(darkTheme);
                    foreach (var button in new[] { selector.MonitorAll, selector.ExcludeAll })
                    {
                        var tip = (ToolTip)button.ToolTip;
                        tip.ApplyTemplate(); tip.Measure(new Size(500, 100)); tip.Arrange(new Rect(tip.DesiredSize));
                        check(ReferenceEquals(tip.Background, PencilPalette.Surface) && ReferenceEquals(tip.Foreground, PencilPalette.Ink) &&
                              tip.FontSize == 12 && tip.DesiredSize.Width < 280 && tip.DesiredSize.Height < 40 &&
                              tip.Template.FindName("TipBorder", tip) is PencilBorder,
                            "compact icon tooltip has legible theme colors and a small box in " + (darkTheme ? "dark" : "light") + " mode");
                        var tooltipScreenshots = Environment.GetEnvironmentVariable("MASCOT_THEME_SCREENSHOT_DIR");
                        if (!string.IsNullOrWhiteSpace(tooltipScreenshots))
                            LibraryFeatureTests.Capture(tip, Path.Combine(tooltipScreenshots,
                                "project-tooltip-" + (darkTheme ? "dark" : "light") + "-" +
                                (ReferenceEquals(button, selector.MonitorAll) ? "monitor" : "exclude") + ".png"));
                    }
                }
                selector.AddChat(new() { Agent = CodexMascot.Core.AgentKind.Codex, Id = "preview-chat", ProjectPath = project, Title = "Preview" });
                selector.ExcludeAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(selector.ProjectChoices[project].IsChecked == false && selector.ChatChoices["Codex:preview-chat"].IsChecked == false,
                    "exclude-all icon turns off visible projects and chats");
                selector.MonitorAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(selector.ProjectChoices[project].IsChecked == true && selector.ChatChoices["Codex:preview-chat"].IsChecked == true,
                    "monitor-all icon turns on visible projects and chats");
                selector.ChatChoices["Codex:preview-chat"].IsChecked = true;
                selector.RecentLimit.Value = 12;
                selector.RecentLimit.CommitValue(12);
                check(manager.IsEditing && !manager.Configuration.Monitor.AutoIncludeNewChats &&
                      manager.Configuration.Monitor.RecentSessionLimit == 12 &&
                      CodexMascot.Core.ChatWatchPolicy.Allows(manager.Configuration.Monitor, CodexMascot.Core.AgentKind.Codex, "preview-chat", project) &&
                      !CodexMascot.Core.ChatWatchPolicy.Allows(manager.Configuration.Monitor, CodexMascot.Core.AgentKind.Codex, "future-chat", project) &&
                      File.ReadAllText(AppPaths.ConfigFile) == beforeProjects,
                    "project choices and recent limit affect live monitoring before Confirm without writing disk");
                selector.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!manager.IsEditing && manager.Configuration.Monitor.AutoIncludeNewChats == beforeAutoInclude &&
                      manager.Configuration.Monitor.RecentSessionLimit == beforeRecentLimit &&
                      !manager.Configuration.Monitor.Chats.Any(c => c.Id == "preview-chat") &&
                      File.ReadAllText(AppPaths.ConfigFile) == beforeProjects, "project selection cancellation restores live and saved settings");
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
                check(manager.Configuration.Monitor.Chats.Any(c => c.Agent == CodexMascot.Core.AgentKind.Claude && c.Id == "test-chat" && c.Enabled) &&
                      !manager.Configuration.Monitor.Chats.First(c => c.Agent == CodexMascot.Core.AgentKind.Codex && c.Id == "test-chat").Enabled,
                    "individual chat checkboxes apply to the live filter immediately");
                selector.ProjectGroups[project].IsExpanded = true;
                check(selector.ChatChoices["Codex:test-chat"].IsChecked == false, "choosing siblings does not enable excluded test chat");
                selector.UpdateLayout();
                var projectHeader = (Grid)selector.ProjectGroups[project].Header;
                var folderCheck = selector.ProjectChoices[project];
                check(ReferenceEquals(projectHeader.Children[1], folderCheck) && Grid.GetColumn(folderCheck) == 1 &&
                      Pencil.GetIcon(folderCheck) == PencilIconKind.Folder && folderCheck.Content?.ToString() == "" &&
                      folderCheck.ToolTip is ToolTip { Content: string folderHelp } && folderHelp.Contains("이 폴더의 채팅을 모두 감시하거나 제외합니다.") &&
                      ToolTipService.GetInitialShowDelay(folderCheck) == 200 &&
                      !((StackPanel)selector.ProjectGroups[project].Content).Children.Contains(folderCheck),
                    "folder-wide watch is an explained icon at the project header's right edge, not a chat row");
                var folderTip = (ToolTip)folderCheck.ToolTip;
                foreach (var darkTheme in new[] { true, false })
                {
                    AppTheme.Apply(darkTheme);
                    folderTip.ApplyTemplate(); folderTip.Measure(new Size(500, 100)); folderTip.Arrange(new Rect(folderTip.DesiredSize));
                    check(ReferenceEquals(folderTip.Background, PencilPalette.Surface) &&
                          ReferenceEquals(folderTip.Foreground, PencilPalette.Ink) && folderTip.FontSize == 12,
                        "folder icon tooltip follows the " + (darkTheme ? "dark" : "light") + " theme");
                    var tipShots = Environment.GetEnvironmentVariable("MASCOT_THEME_SCREENSHOT_DIR");
                    if (!string.IsNullOrWhiteSpace(tipShots)) LibraryFeatureTests.Capture(folderTip,
                        Path.Combine(tipShots, "folder-watch-tooltip-" + (darkTheme ? "dark" : "light") + ".png"));
                }
                var clickHeaderIcon = new RoutedEventArgs(Button.ClickEvent);
                folderCheck.RaiseEvent(clickHeaderIcon);
                check(clickHeaderIcon.Handled && selector.ProjectGroups[project].IsExpanded,
                    "clicking the folder-wide watch icon does not collapse the project");
                var chatRow = selector.ChatChoices["Codex:normal-chat"];
                var chatLabel = (Grid)chatRow.Content;
                check(chatRow.ToolTip is null && chatRow.Margin.Top == 1 && chatRow.Padding.Top == 0 &&
                      Grid.GetColumn(chatLabel.Children[1]) == 1 &&
                      ((TextBlock)chatLabel.Children[0]).Text == "Normal chat" &&
                      ((TextBlock)chatLabel.Children[1]).Text == "Codex · normal-chat",
                    "chat rows have no tooltip, keep their ID to the right of the title, and use compact spacing");
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
            // Saved choices outside the current scan remain stored, but do not
            // clutter the picker or get reset when the visible subset is saved.
            selector = new ProjectSelectionWindow(manager) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                selector.Show();
                check(selector.ProjectChoices.Count == 0 && selector.ChatChoices.Count == 0, "picker omits saved chats outside the current monitoring range");
                selector.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var preserved = new CustomizationManager().Configuration.Monitor;
                check(CodexMascot.Core.ChatWatchPolicy.Allows(preserved, CodexMascot.Core.AgentKind.Codex, "normal-chat", project) &&
                      !CodexMascot.Core.ChatWatchPolicy.Allows(preserved, CodexMascot.Core.AgentKind.Codex, "test-chat", project) &&
                      !CodexMascot.Core.ProjectWatchPolicy.Allows(preserved, excluded), "saving a filtered picker retains hidden include and exclude choices");
            }
            finally { selector.Close(); }
            var beforeRescan = File.ReadAllText(AppPaths.ConfigFile);
            selector = new ProjectSelectionWindow(manager, limit => Task.FromResult<IReadOnlyList<CodexMascot.Core.ChatWatchEntry>>(
                new[]
                {
                    new CodexMascot.Core.ChatWatchEntry { Agent = CodexMascot.Core.AgentKind.Codex, Id = "rescan-one", ProjectPath = project, Title = "First" },
                    new CodexMascot.Core.ChatWatchEntry { Agent = CodexMascot.Core.AgentKind.Codex, Id = "rescan-two", ProjectPath = excluded, Title = "Second" }
                }.Take(limit).ToArray())) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                selector.Show();
                WaitFor(() => selector.ChatChoices.Count == 2);
                check(selector.ChatChoices.Count == 2 && selector.ProjectChoices.Count == 2, "initial project scan shows chats and folders within the recent limit");
                selector.ProjectChoices[excluded].IsChecked = true;
                selector.ChatChoices["Codex:rescan-two"].IsChecked = false;
                selector.RecentLimit.CommitValue(1);
                WaitFor(() => selector.ChatChoices.Count == 1);
                check(selector.ChatChoices.Count == 1 && selector.ProjectChoices.Count == 1 &&
                      !selector.ChatChoices.ContainsKey("Codex:rescan-two") && !selector.ProjectChoices.ContainsKey(excluded) &&
                      manager.Configuration.Monitor.Chats.Any(c => c.Id == "rescan-two" && !c.Enabled),
                    "reducing recent limit removes out-of-range rows but preserves their draft choices");
                selector.RecentLimit.CommitValue(2);
                WaitFor(() => selector.ChatChoices.Count == 2);
                check(selector.ChatChoices.Count == 2 && selector.ProjectChoices.Count == 2 &&
                      selector.ChatChoices["Codex:rescan-two"].IsChecked == false &&
                      File.ReadAllText(AppPaths.ConfigFile) == beforeRescan,
                    "increasing recent limit restores rows and unsaved selections without writing settings");
                selector.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!manager.Configuration.Monitor.Chats.Any(c => c.Id is "rescan-one" or "rescan-two") &&
                      File.ReadAllText(AppPaths.ConfigFile) == beforeRescan,
                    "cancel after live rescans rolls back all newly discovered choices");
            }
            finally { selector.Close(); }
            selector = new ProjectSelectionWindow(manager) { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                selector.Show();
                var longId = "01234567-89ab-cdef-0123-456789abcdef";
                for (var i = 0; i < 24; i++)
                    selector.AddChat(new() { Agent = CodexMascot.Core.AgentKind.Codex,
                        Id = longId + i, ProjectPath = project, Title = "Chat " + i });
                selector.ProjectGroups[project].IsExpanded = true;
                var scroll = ((DockPanel)selector.Content).Children.OfType<ScrollViewer>().Single();
                foreach (var width in new[] { 760d, 580d })
                {
                    selector.Width = width;
                    selector.UpdateLayout();
                    var header = (Grid)selector.ProjectGroups[project].Header;
                    var identity = (TextBlock)((Grid)selector.ChatChoices["Codex:" + longId + 0].Content).Children[1];
                    var headerRight = header.TransformToAncestor(scroll).Transform(new Point(header.ActualWidth, 0)).X;
                    var identityRight = identity.TransformToAncestor(scroll).Transform(new Point(identity.ActualWidth, 0)).X;
                    check(scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible &&
                          headerRight <= scroll.ViewportWidth - 2 && identityRight <= scroll.ViewportWidth - 2,
                        $"project icon and chat ID stay clear of scrollbar at {width}px: header={headerRight:0.0}, ID={identityRight:0.0}, viewport={scroll.ViewportWidth:0.0}");
                    var layoutScreenshots = Environment.GetEnvironmentVariable("MASCOT_THEME_SCREENSHOT_DIR");
                    if (!string.IsNullOrWhiteSpace(layoutScreenshots)) LibraryFeatureTests.Capture(selector,
                        Path.Combine(layoutScreenshots, "project-selection-scroll-" + (int)width + ".png"));
                }
                selector.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
        void WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame);
            }
        }
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
