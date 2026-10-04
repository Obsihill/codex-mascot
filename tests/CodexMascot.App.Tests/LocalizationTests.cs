using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMascot.App;
using CodexMascot.Core;

internal static class LocalizationTests
{
    internal static void Run(Action<bool, string> check, string dir)
    {
        check(Loc.Resolve("system", new CultureInfo("ko-KR")) == "ko", "Korean Windows display language selects Korean");
        check(Loc.Resolve("system", new CultureInfo("en-GB")) == "en", "English Windows display language selects English");
        check(Loc.Resolve("system", new CultureInfo("fr-FR")) == "en", "unsupported display languages fall back to English");
        check(Loc.Resolve("ko", new CultureInfo("en-US")) == "ko" && Loc.Resolve("en", new CultureInfo("ko-KR")) == "en", "explicit preference overrides the system language");
        check(Loc.Normalize("invalid") == "system" && new GlobalConfiguration().Language == "system", "old and invalid preferences use system language");
        var manager = new CustomizationManager();
        var originalConfig = File.ReadAllText(AppPaths.ConfigFile);
        var originalLanguage = Loc.Language;
        var originalDark = PencilPalette.Current.IsDark;
        Window? host = null;
        SettingsWindow? settings = null;
        MascotPackageEditor? editor = null;
        LibraryDashboard? dashboard = null;
        try
        {
            var preferenceFile = Path.Combine(dir, "language-preference.json");
            File.WriteAllText(preferenceFile, "{\"global\":{\"language\":\"en\"}}");
            Loc.ConfigureFromFile(preferenceFile);
            check(Loc.Language == "en", "startup reads explicit language before loading XAML resources");
            File.WriteAllText(preferenceFile, "{\"global\":{\"language\":\"ko\"}}");
            Loc.ConfigureFromFile(preferenceFile);
            check(Loc.Language == "ko", "startup honors a Korean override");
            File.WriteAllText(preferenceFile, "invalid json");
            Loc.ConfigureFromFile(preferenceFile);
            check(Loc.Language == Loc.Resolve("system"), "invalid preference file safely falls back to system language");
            Loc.Configure("en");
            foreach (var pair in Loc.Catalog)
            {
                check(!string.IsNullOrEmpty(pair.Value) && !Regex.IsMatch(pair.Value, "[가-힣]"), "English translation exists: " + pair.Key);
                check(Loc.FromKey(Loc.Key(pair.Key)) == pair.Value, "XAML localization key resolves: " + pair.Key);
                check(Regex.Matches(pair.Key, @"\{\d+\}").Select(m => m.Value).Order().SequenceEqual(
                    Regex.Matches(pair.Value, @"\{\d+\}").Select(m => m.Value).Order()), "translation preserves placeholders: " + pair.Key);
            }
            check(Loc.F("{0} 추가", "내 고양이") == "Add 내 고양이", "formatting preserves user-provided names");
            check(Loc.T(" · 위치/크기 변경") == " · Move & resize" &&
                  Loc.T("위치와 크기를 저장하지 못했습니다.") == "Could not save the position and size.",
                "position dialog's newer actions and errors have English translations");
            check(Loc.CoreMessage("Codex 기록 감시 중 · 목록은 최근 100개 기록 기준") == "Codex logs monitored · Showing the latest 100 logs", "core health messages translate without changing event records");
            check(MainWindow.StateName(MascotState.NeedsAttention) == "Needs attention", "notification state is localized");
            var typeface = new Typeface(PencilFonts.Handwriting, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            check(typeface.TryGetGlyphTypeface(out var glyphs) && Enumerable.Range(32, 95).All(c => glyphs.CharacterToGlyphMap.ContainsKey(c)), "embedded handwriting font covers printable English characters");

            var store = TestLibrary.Create(Path.Combine(dir, "english-library.json"));
            var names = new[] { "Base mascot", "Mint bot", "Apricot cat", "Lilac ghost", "Lemon star", "Sprout", "Blue jelly" };
            for (var i = 0; i < store.Library.Installed.Count; i++) store.Library.Installed[i].Name = names[i];
            foreach (var entry in store.Library.Selected) entry.Name = "Base mascot";
            var snapshot = store.Snapshot();
            dashboard = new LibraryDashboard(); dashboard.Initialize(store, manager);
            host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowActivated = false, ShowInTaskbar = false };
            PencilWindow.Apply(host); host.Show(); Pump(host);
            check(((Button)dashboard.FindName("RegisterButton")).Content?.ToString() == "Add mascot", "dashboard XAML uses English");
            check(((NumericDragInput)dashboard.FindName("SpeedInput")).Label == "Speed", "numeric field labels use English");
            check(((CheckBox)dashboard.FindName("TaskbarCheck")).Content?.ToString() == "Behind taskbar", "dynamic checkbox captions use English");
            settings = new SettingsWindow(manager) { ShowActivated = false }; settings.Show(); Pump(settings);
            check(settings.Title == "Settings" && ((TabItem)settings.Pages.Items[0]).Header.ToString() == "General", "settings title and tabs use English");
            check(PencilWindowTests.Caption(settings, "Close").ToolTip?.ToString() == "Close", "caption resources follow the selected language");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_THEME_SCREENSHOT_DIR");
            foreach (var dark in new[] { false, true })
            {
                AppTheme.Apply(dark); Pump(host); Pump(settings);
                Capture(host, "app-english-" + (dark ? "dark" : "light"));
                Capture(settings, "settings-english-" + (dark ? "dark" : "light"));
            }
            ((Button)dashboard.FindName("StateSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(host);
            Capture(host, "app-english-state");
            check(store.Snapshot() == snapshot, "changing display language and scope preserves mascot data");
            settings.Pages.SelectedIndex = settings.Pages.Items.Count - 1; Pump(settings);
            foreach (var border in Descendants<PencilBorder>(settings).Where(b => b.Name is "SectionHeader" or "SectionBody"))
                check(ReferenceEquals(border.Background, border.Name == "SectionHeader" ? PencilPalette.Paper : PencilPalette.Surface),
                    "previously inactive language page uses the current dark palette");
            var picker = Descendants<ComboBox>(settings).Single(c => c.Name == "LanguagePicker");
            var unchangedFile = File.ReadAllText(AppPaths.ConfigFile);
            check(picker.Items.Count == 2 && picker.SelectedIndex == 1, "language settings show the effective language without a system-default option");
            picker.SelectedIndex = 0; Pump(settings); Pump(host);
            check(Loc.Language == "ko" && settings.Title == "설정", "language selection immediately updates the same settings window; actual=" + Loc.Language + "/" + settings.Title + "; UI=" + string.Join(" | ", Descendants<TextBlock>(settings).Select(t => t.Text)));
            check(((Button)dashboard.FindName("RegisterButton")).Content?.ToString() == "마스코트 등록", "existing main window updates immediately to Korean");
            check(((NumericDragInput)dashboard.FindName("SpeedInput")).Label == "재생 속도", "existing numeric labels update immediately");
            check(File.ReadAllText(AppPaths.ConfigFile) == unchangedFile, "language preview does not persist before confirmation");
            picker = Descendants<ComboBox>(settings).Single(c => c.Name == "LanguagePicker");
            picker.SelectedIndex = 1; Pump(settings); Pump(host);
            check(Loc.Language == "en" && ((Button)dashboard.FindName("RegisterButton")).Content?.ToString() == "Add mascot", "same window switches back to English without reopening");
            Capture(settings, "settings-language");
            check(store.Snapshot() == snapshot, "live language changes preserve mascot names and preferences");
            editor = new MascotPackageEditor(manager, null) { ShowActivated = false }; editor.Show(); Pump(editor);
            check(editor.Title == "Add mascot", "registration dialog is localized");
            check(editor.FontFamily == PencilFonts.Handwriting && editor.WindowStyle == WindowStyle.None &&
                  Descendants<Button>(editor).Single(b => b.Name == "ApplyMascotButton").Content?.ToString() == "Apply" &&
                  Descendants<Button>(editor).Single(b => b.Name == "CloseMascotButton").Content?.ToString() == "Close" &&
                  Loc.T("변경 중인 작업이 있습니다. 닫으시겠습니까?") == "You have unsaved work. Close without applying it?" &&
                  Loc.T("네") == "Yes" && Loc.T("아니요") == "No",
                "titleless registration and discard choices use the embedded font and English translations");
            var registrationTabs = Descendants<TabControl>(editor).Single();
            check(ReferenceEquals(registrationTabs.Background, PencilPalette.Surface), "registration tab content uses the dark surface");
            Capture(editor, "register-english");
            registrationTabs.SelectedIndex = 1; Pump(editor); Capture(editor, "register-english-manual");
            Loc.Configure("ko");
            check(Loc.T("설정") == "설정" && Loc.F("{0} 추가", "English name") == "English name 추가", "Korean captions and user data survive switching back");
            void Capture(FrameworkElement element, string name)
            {
                if (string.IsNullOrWhiteSpace(screenshot)) return;
                Directory.CreateDirectory(screenshot);
                LibraryFeatureTests.Capture(element, Path.Combine(screenshot, name + ".png"));
            }
        }
        finally
        {
            editor?.Close(); settings?.Close(); dashboard?.Shutdown(); host?.Close();
            Loc.Configure(originalLanguage); AppTheme.Apply(originalDark);
            File.WriteAllText(AppPaths.ConfigFile, originalConfig);
        }
    }
    private static void Pump(Window window)
    { window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
}
