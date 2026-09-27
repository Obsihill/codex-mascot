namespace CodexMascot.App;
public static class AppPaths
{
    public static string BaseDirectory { get; } = FindWritableRoot();
    public static string AssetsDirectory => Path.Combine(BaseDirectory, "assets");
    public static string ImagesDirectory => Path.Combine(AssetsDirectory, "images");
    public static string SoundsDirectory => Path.Combine(AssetsDirectory, "sounds");
    public static string ConfigDirectory => Path.Combine(BaseDirectory, "config");
    public static string LibraryDirectory => Path.Combine(BaseDirectory, "library");
    public static string ConfigFile => Path.Combine(ConfigDirectory, "mascot.json");
    private static string FindWritableRoot()
    {
        var marker = Path.Combine(AppContext.BaseDirectory, ".mascot-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return AppContext.BaseDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMascot", "data"); }
    }
    public static void EnsureFolders()
    {
        Directory.CreateDirectory(ImagesDirectory); Directory.CreateDirectory(SoundsDirectory); Directory.CreateDirectory(ConfigDirectory);
        if (!BaseDirectory.Equals(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var source = Path.Combine(AppContext.BaseDirectory, "assets");
            if (Directory.Exists(source)) foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(AssetsDirectory, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target)) File.Copy(file, target);
            }
        }
    }
}
