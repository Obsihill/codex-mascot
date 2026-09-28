using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexMascot.App;

public static class PencilPalette
{
    public static PencilThemeColors Current { get; } = new();
    public static SolidColorBrush Ink => Current.Ink;
    public static SolidColorBrush Muted => Current.Muted;
    public static SolidColorBrush Line => Current.Line;
    public static SolidColorBrush Emphasis => Current.Emphasis;
    public static SolidColorBrush Surface => Current.Surface;
    public static SolidColorBrush Inset => Current.Inset;
    public static SolidColorBrush ValueFill => Current.ValueFill;
    public static SolidColorBrush Accent => Current.Accent;
    public static SolidColorBrush AccentButton => Current.AccentButton;
    public static SolidColorBrush Selected => Current.Selected;
    public static SolidColorBrush Danger => Current.Danger;
    public static SolidColorBrush Button => Current.Button;
    public static SolidColorBrush OnButton => Current.OnButton;
    public static SolidColorBrush OrangeSurface => Current.OrangeSurface;
    public static SolidColorBrush OrangeInk => Current.OrangeInk;
    public static SolidColorBrush OrangeLine => Current.OrangeLine;
    public static SolidColorBrush Badge => Current.Badge;
    public static DrawingBrush Paper => Current.Paper;

    // Convert programmatically assigned palette brushes into live bindings.
    // Existing explicit bindings, custom artwork and non-palette colors are untouched.
    internal static void BindTree(DependencyObject root)
        => BindTree(root, new HashSet<DependencyObject>());
    private static void BindTree(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root)) return;
        BindLocal(root);
        // Inactive TabItem content is in the logical tree, but not the visual tree.
        // Bind it before switching palettes, even when it has never been displayed.
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            BindTree(child, visited);
        if (root is Visual || root is System.Windows.Media.Media3D.Visual3D)
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                BindTree(VisualTreeHelper.GetChild(root, i), visited);
    }
    internal static void BindLocal(DependencyObject root)
    {
        var values = root.GetLocalValueEnumerator();
        var replacements = new List<(DependencyProperty Property, string Name)>();
        while (values.MoveNext())
        {
            var entry = values.Current;
            if (!entry.Property.ReadOnly && entry.Value is Brush brush && Current.NameOf(brush) is { } name)
                replacements.Add((entry.Property, name));
        }
        foreach (var entry in replacements)
            BindingOperations.SetBinding(root, entry.Property, new Binding(entry.Name) { Source = Current });
    }
}

public sealed class PencilThemeColors : INotifyPropertyChanged
{
    private readonly Dictionary<string, Brush> _light = Create(false);
    private readonly Dictionary<string, Brush> _dark = Create(true);
    public bool IsDark { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public SolidColorBrush Ink => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Ink)];
    public SolidColorBrush Muted => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Muted)];
    public SolidColorBrush Line => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Line)];
    public SolidColorBrush Emphasis => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Emphasis)];
    public SolidColorBrush Surface => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Surface)];
    public SolidColorBrush Inset => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Inset)];
    public SolidColorBrush ValueFill => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(ValueFill)];
    public SolidColorBrush Accent => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Accent)];
    public SolidColorBrush AccentButton => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(AccentButton)];
    public SolidColorBrush Selected => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Selected)];
    public SolidColorBrush Danger => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Danger)];
    public SolidColorBrush Button => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Button)];
    public SolidColorBrush OnButton => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(OnButton)];
    public SolidColorBrush OrangeSurface => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(OrangeSurface)];
    public SolidColorBrush OrangeInk => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(OrangeInk)];
    public SolidColorBrush OrangeLine => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(OrangeLine)];
    public SolidColorBrush Badge => (SolidColorBrush)(IsDark ? _dark : _light)[nameof(Badge)];
    public DrawingBrush Paper => (DrawingBrush)(IsDark ? _dark : _light)[nameof(Paper)];
    private ImageSource? _lightLogo, _darkLogo;
    public ImageSource Logo => IsDark
        ? _darkLogo ??= BitmapFrame.Create(new Uri("pack://application:,,,/AgentMascot;component/Branding/mas-cat.png"))
        : _lightLogo ??= BitmapFrame.Create(new Uri("pack://application:,,,/AgentMascot;component/Branding/mas-cat-black.png"));
    internal void SetDark(bool dark)
    {
        if (IsDark == dark) return;
        IsDark = dark;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    internal string? NameOf(Brush brush) => _light.Concat(_dark).FirstOrDefault(p => ReferenceEquals(p.Value, brush)).Key;
    private static SolidColorBrush ColorBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze(); return brush;
    }
    private static Dictionary<string, Brush> Create(bool dark)
    {
        var brushes = new Dictionary<string, Brush>
        {
            [nameof(Ink)] = ColorBrush(dark ? "#EEEEEE" : "#373A35"),
            [nameof(Muted)] = ColorBrush(dark ? "#B8B8B8" : "#696A60"),
            [nameof(Line)] = ColorBrush(dark ? "#898989" : "#88887A"),
            [nameof(Emphasis)] = ColorBrush(dark ? "#FFFFFF" : "#252A22"),
            [nameof(Surface)] = ColorBrush(dark ? "#292929" : "#FCFAF4"),
            [nameof(Inset)] = ColorBrush(dark ? "#202020" : "#F0EEE5"),
            [nameof(ValueFill)] = ColorBrush(dark ? "#484848" : "#D8D5CC"),
            [nameof(Accent)] = ColorBrush(dark ? "#8DA596" : "#587461"),
            // Filled buttons need a darker green than accent text and focus outlines.
            [nameof(AccentButton)] = ColorBrush(dark ? "#3C5947" : "#587461"),
            [nameof(Selected)] = ColorBrush(dark ? "#3B3B3B" : "#E7ECDF"),
            [nameof(Danger)] = ColorBrush(dark ? "#F1A29A" : "#97605A"),
            [nameof(Button)] = ColorBrush(dark ? "#3B5042" : "#626A58"),
            [nameof(OnButton)] = ColorBrush(dark ? "#F1F1F1" : "#FFFEF8"),
            [nameof(OrangeSurface)] = ColorBrush(dark ? "#493522" : "#F3D8B5"),
            [nameof(OrangeInk)] = ColorBrush(dark ? "#FFD3A3" : "#82491F"),
            [nameof(OrangeLine)] = ColorBrush(dark ? "#D49964" : "#B76A31"),
            [nameof(Badge)] = ColorBrush(dark ? "#DC292929" : "#BFFFFFFF"),
        };
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(ColorBrush(dark ? "#1C1C1C" : "#F4F0E5"), null, new Rect(0, 0, 64, 64));
            var grain = ColorBrush(dark ? "#18D5D5D5" : "#12706448"); var random = new Random(711);
            for (var i = 0; i < 85; i++) dc.DrawEllipse(grain, null, new Point(random.NextDouble() * 64, random.NextDouble() * 64), .28, .42);
        }
        drawing.Freeze();
        var paper = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 64, 64), Stretch = Stretch.None };
        paper.Freeze(); brushes[nameof(Paper)] = paper;
        return brushes;
    }
}

