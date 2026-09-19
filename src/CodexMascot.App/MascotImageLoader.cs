using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexMascot.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;

namespace CodexMascot.App;

public sealed record MascotFrame(BitmapSource Bitmap, int DelayMs);
public static class MascotImageLoader
{
    public static IReadOnlyList<MascotFrame> Load(string path, StateConfiguration config)
    {
        if (new FileInfo(path).Length > 50 * 1024 * 1024) throw new InvalidDataException("이미지 파일은 50MB 이하로 선택해 주세요.");
        var info = SixLabors.ImageSharp.Image.Identify(path);
        if ((long)info.Width * info.Height * Math.Max(1, info.FrameMetadataCollection.Count) > 48_000_000)
            throw new InvalidDataException("이미지가 너무 큽니다. 프레임 수 또는 해상도를 줄여 주세요.");
        using var image = SixLabors.ImageSharp.Image.Load<Bgra32>(path);
        if ((long)image.Width * image.Height * image.Frames.Count > 48_000_000)
            throw new InvalidDataException("이미지가 너무 큽니다. 프레임 수 또는 해상도를 줄여 주세요.");
        var result = new List<MascotFrame>();
        foreach (var frame in image.Frames)
        {
            var pixels = new byte[checked(frame.Width * frame.Height * 4)];
            frame.CopyPixelDataTo(pixels);
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, pixels, frame.Width * 4);
            bitmap.Freeze();
            var delay = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".gif" => frame.Metadata.GetGifMetadata().FrameDelay * 10,
                ".webp" => (int)frame.Metadata.GetWebpMetadata().FrameDelay,
                _ => config.FrameDurationMs
            };
            if (image.Frames.Count == 1 && (config.SpriteColumns > 1 || config.SpriteRows > 1))
            {
                var columns = Math.Clamp(config.SpriteColumns, 1, 32);
                var rows = Math.Clamp(config.SpriteRows, 1, 32);
                var width = frame.Width / columns;
                var height = frame.Height / rows;
                if (width == 0 || height == 0) throw new InvalidDataException("스프라이트 칸 크기를 확인하세요.");
                for (var y = 0; y < rows; y++) for (var x = 0; x < columns; x++)
                {
                    var crop = new CroppedBitmap(bitmap, new System.Windows.Int32Rect(x * width, y * height, width, height));
                    crop.Freeze();
                    result.Add(new(crop, Math.Max(20, config.FrameDurationMs)));
                }
            }
            else result.Add(new(bitmap, Math.Max(20, delay)));
        }
        return result;
    }
}
