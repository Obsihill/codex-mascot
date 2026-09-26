namespace CodexMascot.App;

internal static class MascotMedia
{
    public static bool IsVideo(string? path) => Path.GetExtension(path ?? "").ToLowerInvariant()
        is ".mp4" or ".m4v" or ".wmv" or ".avi" or ".mov";
}
