using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CodexMascot.App;

public enum PencilIconKind { None, Volume, Speed, Play, Stop, Settings, Duration, Position, Loop, Add, Remove, Delete, Reset, Back, Folder, ChevronDown }

public static class PencilFonts
{
    // Embedded, not dependent on machine-installed fonts or network access.
    public static FontFamily Handwriting { get; } = new(new Uri("pack://application:,,,/CodexMascot.App;component/"), "./Fonts/#Nanum Pen Script");
    public static FontFamily Numbers { get; } = new("Segoe UI");
}

public static class Pencil
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(PencilIconKind), typeof(Pencil), new PropertyMetadata(PencilIconKind.None));
    public static PencilIconKind GetIcon(DependencyObject element) => (PencilIconKind)element.GetValue(IconProperty);
    public static void SetIcon(DependencyObject element, PencilIconKind value) => element.SetValue(IconProperty, value);
}

public static class PencilPalette
{
    private static SolidColorBrush Brush(string color) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; brush.Freeze(); return brush; }
    public static SolidColorBrush Ink { get; } = Brush("#373A35");
    public static SolidColorBrush Muted { get; } = Brush("#696A60");
    public static SolidColorBrush Line { get; } = Brush("#88887A");
    public static SolidColorBrush Emphasis { get; } = Brush("#252A22");
    public static SolidColorBrush Surface { get; } = Brush("#FCFAF4");
    public static SolidColorBrush Inset { get; } = Brush("#F0EEE5");
    public static SolidColorBrush Accent { get; } = Brush("#587461");
    public static SolidColorBrush Selected { get; } = Brush("#E7ECDF");
    public static SolidColorBrush Danger { get; } = Brush("#97605A");
    public static SolidColorBrush Button { get; } = Brush("#626A58");
    public static SolidColorBrush OnButton { get; } = Brush("#FFFEF8");
    public static SolidColorBrush OrangeSurface { get; } = Brush("#F3D8B5");
    public static SolidColorBrush OrangeInk { get; } = Brush("#82491F");
    public static SolidColorBrush OrangeLine { get; } = Brush("#B76A31");
    public static DrawingBrush Paper { get; } = CreatePaper();
    private static DrawingBrush CreatePaper()
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Brush("#F4F0E5"), null, new Rect(0, 0, 64, 64));
            var grain = Brush("#12706448"); var random = new Random(711);
            for (var i = 0; i < 85; i++) dc.DrawEllipse(grain, null, new Point(random.NextDouble() * 64, random.NextDouble() * 64), .28, .42);
        }
        drawing.Freeze();
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 64, 64), Stretch = Stretch.None };
        brush.Freeze(); return brush;
    }
}

// Retains Border's layout/hit testing; only its paint is replaced. Geometry is
// deterministic and cached per size, so pointer hover never makes the line jitter.
public sealed class PencilBorder : Border
{
    private Size _cachedSize;
    private Geometry? _outline;
    public static readonly DependencyProperty HatchProperty = DependencyProperty.Register(nameof(Hatch), typeof(bool), typeof(PencilBorder), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HatchBrushProperty = DependencyProperty.Register(nameof(HatchBrush), typeof(Brush), typeof(PencilBorder), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Hatch { get => (bool)GetValue(HatchProperty); set => SetValue(HatchProperty, value); }
    public Brush? HatchBrush { get => (Brush?)GetValue(HatchBrushProperty); set => SetValue(HatchBrushProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width < 3 || bounds.Height < 3) return;
        dc.DrawRoundedRectangle(Background, null, bounds, 3, 3);
        if (Hatch && (HatchBrush ?? BorderBrush) is { } hatchBrush)
        {
            dc.PushClip(new RectangleGeometry(new Rect(2, 2, bounds.Width - 4, bounds.Height - 4)));
            dc.PushOpacity(.09);
            var hatch = new Pen(hatchBrush, .55);
            for (double y = 7; y < bounds.Height + 8; y += 5) dc.DrawLine(hatch, new Point(3, y), new Point(bounds.Width - 3, y - 7));
            dc.Pop(); dc.Pop();
        }
        if (BorderBrush is null || BorderThickness == new Thickness(0)) return;
        if (_outline is null || _cachedSize != RenderSize)
        {
            _cachedSize = RenderSize;
            _outline = PencilGeometry.Rectangle(RenderSize);
        }
        dc.PushOpacity(.8);
        dc.DrawGeometry(null, new Pen(BorderBrush, .75) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, _outline);
        dc.Pop();
    }
}

public sealed class PencilIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(PencilIconKind), typeof(PencilIcon), new FrameworkPropertyMetadata(PencilIconKind.None, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(PencilIcon), new FrameworkPropertyMetadata(PencilPalette.Ink, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public PencilIconKind Kind { get => (PencilIconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public PencilIcon() { IsHitTestVisible = false; Focusable = false; }
    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(20, availableSize.Width), Math.Min(20, availableSize.Height));
    protected override void OnRender(DrawingContext dc)
    {
        if (Kind == PencilIconKind.None || ActualWidth <= 0 || ActualHeight <= 0) return;
        var size = Math.Min(ActualWidth, ActualHeight);
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 32, size / 32));
        var geometry = PencilGeometry.Icon(Kind);
        dc.DrawGeometry(null, new Pen(Foreground, 1.45) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
        dc.PushOpacity(.26); dc.PushTransform(new TranslateTransform(.38, -.3));
        dc.DrawGeometry(null, new Pen(Foreground, .75) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }
}

internal static class PencilGeometry
{
    private static readonly IReadOnlyDictionary<PencilIconKind, Geometry> Icons = Enum.GetValues<PencilIconKind>().ToDictionary(k => k, BuildIcon);
    internal static Geometry Icon(PencilIconKind kind) => Icons.GetValueOrDefault(kind, Geometry.Empty);
    internal static Geometry Rectangle(Size size)
    {
        var result = new StreamGeometry(); var random = new Random(37);
        using (var context = result.Open())
        {
            var p = new[] { new Point(2, 2), new Point(size.Width - 2, 2), new Point(size.Width - 2, size.Height - 2), new Point(2, size.Height - 2), new Point(2, 2) };
            for (var pass = 0; pass < 2; pass++)
            {
                context.BeginFigure(p[0], false, false);
                for (var side = 0; side < 4; side++)
                {
                    var delta = p[side + 1] - p[side]; var count = Math.Max(2, (int)(delta.Length / 24));
                    for (var i = 1; i <= count; i++)
                    {
                        var fraction = i / (double)count; var q = p[side] + delta * fraction;
                        q.Offset((random.NextDouble() - .5) * 1.15, (random.NextDouble() - .5) * 1.15);
                        context.LineTo(q, true, false);
                    }
                }
            }
        }
        result.Freeze(); return result;
    }
    private static Geometry BuildIcon(PencilIconKind kind)
    {
        var path = kind switch
        {
            PencilIconKind.Volume => "M4,12 L10,12 17,6 17,26 10,20 4,20 Z M21,11 C25,13 25,19 21,21 M25,7 C32,12 32,20 25,25",
            PencilIconKind.Speed => "M4,26 A12,14 0 0 1 28,26 M16,5 L16,9 M7,12 L10,14 M25,12 L22,14 M16,23 L24,13 M18,24 A2,2 0 1 1 14,24 A2,2 0 1 1 18,24",
            PencilIconKind.Play => "M29,16 A13,13 0 1 1 3,16 A13,13 0 1 1 29,16 M12,9 L23,16 12,23 Z M14,12 L19,16 14,20 M16,14 L16,18",
            PencilIconKind.Stop => "M29,16 A13,13 0 1 1 3,16 A13,13 0 1 1 29,16 M11,11 L21,11 21,21 11,21 Z M12,14 L20,12 M12,17 L20,15 M12,20 L20,18",
            PencilIconKind.Settings => "M12,3 L20,3 20,7 24,9 28,8 31,15 27,17 26,21 28,24 22,29 19,26 14,26 11,29 5,24 7,20 5,16 2,15 5,8 9,9 12,7 Z M22,16 A6,6 0 1 1 10,16 A6,6 0 1 1 22,16",
            PencilIconKind.Duration => "M29,16 A13,13 0 1 1 3,16 A13,13 0 1 1 29,16 M16,7 L16,16 23,21",
            PencilIconKind.Position => "M16,3 L16,29 M3,16 L29,16 M11,8 L16,3 21,8 M11,24 L16,29 21,24 M8,11 L3,16 8,21 M24,11 L29,16 24,21",
            PencilIconKind.Loop => "M5,14 C5,4 20,1 27,10 M21,5 L27,10 21,12 M27,18 C27,28 12,31 5,22 M11,20 L5,22 11,27",
            PencilIconKind.Add => "M16,5 L16,27 M5,16 L27,16",
            PencilIconKind.Remove => "M5,16 L27,16",
            PencilIconKind.Delete => "M8,10 L9,28 24,28 25,10 M5,9 L28,9 M12,8 L12,4 21,4 21,8 M14,14 L14,24 M20,14 L20,24",
            PencilIconKind.Reset => "M7,8 C15,-1 30,7 28,20 C27,32 8,33 4,21 M7,3 L7,10 14,10",
            PencilIconKind.Back => "M27,16 L5,16 M13,8 L5,16 13,24",
            PencilIconKind.Folder => "M3,9 L3,26 29,26 29,10 17,10 14,6 3,6 Z",
            PencilIconKind.ChevronDown => "M8,12 L16,20 24,12",
            _ => ""
        };
        if (path.Length == 0) return Geometry.Empty;
        var flat = Geometry.Parse(path).GetFlattenedPathGeometry(.18, ToleranceType.Absolute);
        var drawing = new StreamGeometry(); var random = new Random(143 + (int)kind);
        using (var dc = drawing.Open())
        {
            Point Rough(Point p) => new(p.X + (random.NextDouble() - .5) * .3, p.Y + (random.NextDouble() - .5) * .3);
            foreach (var figure in flat.Figures)
            {
                dc.BeginFigure(Rough(figure.StartPoint), false, figure.IsClosed);
                foreach (var segment in figure.Segments)
                    if (segment is PolyLineSegment poly) foreach (var point in poly.Points) dc.LineTo(Rough(point), true, false);
                    else if (segment is LineSegment line) dc.LineTo(Rough(line.Point), true, false);
            }
        }
        drawing.Freeze(); return drawing;
    }
}
