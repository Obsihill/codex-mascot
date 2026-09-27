using System.Windows;
using System.Windows.Media;

namespace CodexMascot.App;

// Code-native vector samples: no external assets or downloads are required.
internal static class DemoMascotArtwork
{
    public static ImageSource Create(string key)
    {
        var parts = key.Split('/');
        key = parts[0];
        var state = parts.Length > 1 ? parts[1] : "cover";
        var group = new DrawingGroup();
        using (var d = group.Open())
        {
            var ink = new SolidColorBrush(Color.FromRgb(42, 53, 66));
            var colors = new Dictionary<string, string> { ["bot"] = "#8DDCC8", ["cat"] = "#F4B89C", ["ghost"] = "#C5B4EB", ["star"] = "#F1D778", ["sprout"] = "#ACD38C", ["blob"] = "#9DC6EE" };
            var fill = (Brush)new BrushConverter().ConvertFromString(colors.GetValueOrDefault(key, "#8DDCC8"))!;
            d.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 200, 180));
            d.DrawEllipse(new SolidColorBrush(Color.FromArgb(24, 45, 55, 70)), null, new Point(100, 159), 53, 8);
            if (key == "cat")
            {
                d.DrawGeometry(fill, null, Geometry.Parse("M48,65 L47,24 L83,51 Z M119,50 L154,24 L153,69 Z"));
                d.DrawEllipse(fill, null, new Point(100, 99), 62, 58);
            }
            else if (key == "ghost") d.DrawGeometry(fill, null, Geometry.Parse("M43,145 L43,80 C43,10 157,10 157,80 L157,146 L136,130 L118,149 L98,134 L78,150 L61,134 Z"));
            else if (key == "star") d.DrawGeometry(fill, null, Geometry.Parse("M100,18 L123,64 L175,72 L138,108 L147,160 L100,135 L52,160 L62,108 L24,72 L77,64 Z"));
            else if (key == "sprout")
            {
                d.DrawEllipse(fill, null, new Point(100, 110), 57, 48);
                d.DrawGeometry(new SolidColorBrush(Color.FromRgb(81, 149, 93)), null, Geometry.Parse("M100,67 C40,62 48,4 99,51 C117,6 167,29 100,67 Z"));
            }
            else if (key == "blob") d.DrawGeometry(fill, null, Geometry.Parse("M40,130 C20,109 50,86 55,64 C65,22 124,29 145,62 C169,95 179,117 155,143 C128,171 56,166 40,130 Z"));
            else
            {
                d.DrawRoundedRectangle(fill, null, new Rect(40, 48, 120, 103), 30, 30);
                d.DrawLine(new Pen(fill, 8), new Point(100, 48), new Point(100, 30));
                d.DrawEllipse(fill, null, new Point(100, 25), 9, 9);
                d.DrawRoundedRectangle(fill, null, new Rect(26, 83, 12, 37), 6, 6);
                d.DrawRoundedRectangle(fill, null, new Rect(162, 83, 12, 37), 6, 6);
            }
            var face = new Pen(ink, 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (state is "idle" or "completed")
                d.DrawGeometry(null, face, Geometry.Parse(state == "idle" ? "M71,92 L84,92 M116,92 L129,92" : "M71,94 Q78,80 85,94 M116,94 Q123,80 130,94"));
            else
            {
                d.DrawEllipse(ink, null, new Point(78, 91), 5, 8);
                d.DrawEllipse(ink, null, new Point(123, 91), 5, 8);
            }
            if (state == "needsAttention") d.DrawEllipse(null, face, new Point(100, 115), 7, 9);
            else d.DrawGeometry(null, face, Geometry.Parse(state == "failed" ? "M88,121 Q100,107 113,121" : state is "idle" or "interrupted" ? "M92,115 L109,115" : "M88,111 Q100,124 113,111"));
            d.DrawEllipse(new SolidColorBrush(Color.FromArgb(85, 239, 120, 121)), null, new Point(62, 110), 9, 5);
            d.DrawEllipse(new SolidColorBrush(Color.FromArgb(85, 239, 120, 121)), null, new Point(139, 110), 9, 5);
            if (state != "cover")
            {
                var accent = (Brush)new BrushConverter().ConvertFromString(state switch { "needsAttention" => "#DCA328", "failed" => "#DC6878", "completed" => "#399E78", "running" => "#5299D1", _ => "#9B8CBC" })!;
                d.DrawEllipse(accent, null, new Point(154, 38), 21, 21);
                var symbol = state switch
                {
                    "completed" => "M143,38 L151,46 L166,30",
                    "failed" => "M146,30 L162,46 M162,30 L146,46",
                    "needsAttention" => "M154,27 L154,39 M154,46 L154,48",
                    "interrupted" => "M149,29 L149,47 M159,29 L159,47",
                    "running" => "M145,30 L162,38 L145,46 Z",
                    _ => "M146,30 L162,30 L146,46 L162,46"
                };
                d.DrawGeometry(null, new Pen(Brushes.White, 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse(symbol));
            }
        }
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
}
