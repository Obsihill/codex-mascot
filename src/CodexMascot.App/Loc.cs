using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Markup;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CodexMascot.App;

// Korean is the source language. Only application-owned strings are localized;
// paths, prompts, task IDs and user-provided mascot names are never rewritten.
public static class Loc
{
    private static readonly Dictionary<string, string> English = Load();
    private static readonly Dictionary<string, string> Keys = English.Keys.ToDictionary(Key, s => s);
    private static readonly ConditionalWeakTable<string, Translation> OwnedText = new();
    public static LocaleState Current { get; } = new();
    public static event EventHandler? Changed;
    public static string Language { get; private set; } = Resolve("system");
    public static string Normalize(string? preference) => preference is "ko" or "en" ? preference : "system";
    public static string Resolve(string? preference, CultureInfo? culture = null) => Normalize(preference) switch
    {
        "ko" => "ko", "en" => "en",
        _ => (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "ko" ? "ko" : "en"
    };
    public static void Configure(string? preference)
    {
        var next = Resolve(preference);
        if (next == Language) return;
        if (Application.Current is { } app)
            foreach (Window window in app.Windows) BindTree(window, new HashSet<DependencyObject>());
        Language = next;
        Current.Notify();
        Changed?.Invoke(null, EventArgs.Empty);
    }
    internal static void ConfigureFromFile(string path)
    {
        var preference = "system";
        try
        {
            if (File.Exists(path))
                preference = JsonSerializer.Deserialize<CodexMascot.Core.MascotConfiguration>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Global?.Language ?? "system";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { /* CustomizationManager reports/backs up invalid settings after startup. */ }
        Configure(preference);
    }
    private static string Render(string text) => Language == "en" && English.TryGetValue(text, out var translation) ? translation : text;
    public static string T(string text) => Track(new Translation(text, null));
    public static string F(string text, params object?[] args) => Track(new Translation(text, args));
    private static string Track(Translation translation)
    {
        // A unique instance lets us distinguish our captions from identical user text.
        var result = new string(translation.ToString().AsSpan());
        if (result.Length > 0) OwnedText.Add(result, translation);
        return result;
    }
    private sealed record Translation(string Source, object?[]? Arguments)
    {
        public override string ToString() => Arguments is null ? Render(Source) : string.Format(CultureInfo.CurrentCulture, Render(Source), Arguments);
    }
    private sealed class TranslationConverter(Translation translation) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => translation.ToString();
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    private static void BindTree(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root)) return;
        var values = root.GetLocalValueEnumerator();
        var pending = new List<(DependencyProperty Property, Translation Text)>();
        while (values.MoveNext())
        {
            var value = values.Current;
            // Selection contains the same string objects as the visible items.
            // Translating selection state would clear it before its list rebuilds
            // and accidentally invoke preference-change handlers.
            if (root is System.Windows.Controls.Primitives.Selector &&
                (value.Property == System.Windows.Controls.Primitives.Selector.SelectedItemProperty ||
                 value.Property == System.Windows.Controls.Primitives.Selector.SelectedValueProperty ||
                 value.Property == System.Windows.Controls.ComboBox.TextProperty)) continue;
            if (!value.Property.ReadOnly && value.Value is string text && OwnedText.TryGetValue(text, out var owned))
                pending.Add((value.Property, owned));
        }
        foreach (var (property, text) in pending)
            BindingOperations.SetBinding(root, property, new Binding(nameof(LocaleState.Revision)) { Source = Current, Mode = BindingMode.OneWay, Converter = new TranslationConverter(text) });
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) BindTree(child, visited);
        if (root is Visual || root is System.Windows.Media.Media3D.Visual3D)
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) BindTree(VisualTreeHelper.GetChild(root, i), visited);
    }
    internal static string Key(string text) => "L" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
    internal static string FromKey(string key) => T(Keys.TryGetValue(key, out var text) ? text : throw new InvalidOperationException("Unknown localization key: " + key));
    internal static IReadOnlyDictionary<string, string> Catalog => English;
    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream("CodexMascot.App.Localization.en.json")
            ?? throw new InvalidOperationException("Missing English localization resource.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    // Core event records retain their stable source strings. Translate their
    // known messages only at the presentation boundary, preserving payloads.
    internal static string CoreMessage(string? text)
    {
        if (text is null) return "";
        var exact = T(text);
        if (Language != "en" || exact != text) return exact;
        var recent = System.Text.RegularExpressions.Regex.Match(text, @"^(.*) 기록 감시 중 · 목록은 최근 (\d+)개 기록 기준$");
        if (recent.Success) return F("{0} 기록 감시 중 · 목록은 최근 {1}개 기록 기준", recent.Groups[1].Value, recent.Groups[2].Value);
        foreach (var prefix in new[] { "읽기 오류: ", "Claude에서 확인해 주세요", "Claude가 질문을 표시했습니다: ", "Codex에서 승인해 주세요: " })
            if (text.StartsWith(prefix, StringComparison.Ordinal)) return T(prefix) + text[prefix.Length..];
        foreach (var suffix in new[] { ": 읽을 수 있는 로컬 작업 기록이 없습니다.", " 기록 감시 중 · 목록은 최근 100개 기록 기준" })
            if (text.EndsWith(suffix, StringComparison.Ordinal)) return text[..^suffix.Length] + T(suffix);
        return text;
    }
}

public sealed class LocaleState : INotifyPropertyChanged
{
    public int Revision { get; private set; }
    public string this[string key] => Loc.Catalog.ContainsKey(key) ? Loc.T(key) : Loc.FromKey(key);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Notify() { Revision++; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Binding.StringFormat is not a dependency property.
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget { TargetObject: Binding }) return Loc.FromKey(Key);
        return new Binding("[" + Key + "]") { Source = Loc.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
    }
}
