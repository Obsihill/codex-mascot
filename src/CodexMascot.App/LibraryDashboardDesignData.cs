using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexMascot.App;

// Referenced exclusively through d:DataContext. No files, timers or app services.
public sealed class LibraryDashboardDesignData
{
    public sealed record PreviewCard(string Name, ImageSource Thumbnail);
    public IReadOnlyList<PreviewCard> Installed { get; }
    public IReadOnlyList<PreviewCard> Selected { get; }
    public ImageSource Preview { get; }

    public LibraryDashboardDesignData()
    {
        var original = Image("original.png");
        Preview = Image("mascat.png");
        Installed = new[] { new PreviewCard(Loc.T("기존 마스코트"), original), new PreviewCard("MasCat", Preview) };
        Selected = new[] { new PreviewCard("MasCat", Preview) };
    }

    private static ImageSource Image(string file)
    {
        var image = BitmapFrame.Create(new Uri("pack://application:,,,/AgentMascot;component/Design/" + file));
        image.Freeze(); return image;
    }
}
