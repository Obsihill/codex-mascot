namespace CodexMascot.App;

public static class CodexExecutable
{
    public static string Resolve(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured)) throw new FileNotFoundException("설정한 Codex 실행 파일을 찾을 수 없습니다.", configured);
            return Path.GetFullPath(configured);
        }
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(desktop))
        {
            var found = new DirectoryInfo(desktop).EnumerateFiles("codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (found is not null) return found.FullName;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var file = Path.Combine(dir.Trim('"'), "codex.exe");
            if (File.Exists(file)) return file;
        }
        throw new FileNotFoundException("Codex를 찾을 수 없습니다. 데스크톱/CLI를 설치하거나 'EXE 선택'을 눌러 주세요. 기록 감시는 계속 사용할 수 있습니다.");
    }
}
